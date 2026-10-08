using Microsoft.Data.Sqlite;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Domain.Entities;
using Prompuff.Infrastructure.Persistence;

namespace Prompuff.Infrastructure.Repositories;

public sealed class SqliteWorkflowRepository(SqliteDatabase database) : IWorkflowRepository
{
    public async Task<IReadOnlyList<WorkflowSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT w.Id, w.Name, w.Description, w.UpdatedAt, p.Title
            FROM Workflows w
            LEFT JOIN WorkflowSteps s ON s.WorkflowId = w.Id
            LEFT JOIN Prompts p ON p.Id = s.PromptId
            ORDER BY w.UpdatedAt DESC, w.Id, s.Position;
            """;

        var summaries = new List<WorkflowSummary>();
        Guid? currentId = null;
        List<string> titles = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.ReadId(0);
            if (id != currentId)
            {
                titles = [];
                summaries.Add(new WorkflowSummary(id, reader.GetString(1), reader.ReadTextOrNull(2), titles, reader.ReadTime(3)));
                currentId = id;
            }

            if (reader.ReadTextOrNull(4) is { } title)
            {
                titles.Add(title);
            }
        }

        return summaries;
    }

    public async Task<Workflow?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        Workflow workflow;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, Description, CreatedAt, UpdatedAt FROM Workflows WHERE Id = $id;";
            command.With("$id", SqlValues.Id(id));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            workflow = new Workflow
            {
                Id = reader.ReadId(0),
                Name = reader.GetString(1),
                Description = reader.ReadTextOrNull(2),
                CreatedAt = reader.ReadTime(3),
                UpdatedAt = reader.ReadTime(4),
            };
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, PromptId, Note FROM WorkflowSteps WHERE WorkflowId = $id ORDER BY Position;";
            command.With("$id", SqlValues.Id(id));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                workflow.Steps.Add(new WorkflowStep(reader.ReadId(0), reader.ReadId(1), reader.ReadTextOrNull(2)));
            }
        }

        return workflow;
    }

    public async Task SaveAsync(Workflow workflow, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Workflows (Id, Name, Description, CreatedAt, UpdatedAt)
                VALUES ($id, $name, $description, $created, $updated)
                ON CONFLICT (Id) DO UPDATE SET Name = $name, Description = $description, UpdatedAt = $updated;
                DELETE FROM WorkflowSteps WHERE WorkflowId = $id;
                """;
            command.With("$id", SqlValues.Id(workflow.Id))
                .With("$name", workflow.Name)
                .With("$description", SqlValues.TextOrNull(workflow.Description))
                .With("$created", SqlValues.Time(workflow.CreatedAt))
                .With("$updated", SqlValues.Time(workflow.UpdatedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var position = 0; position < workflow.Steps.Count; position++)
        {
            var step = workflow.Steps[position];
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO WorkflowSteps (Id, WorkflowId, Position, PromptId, Note)
                VALUES ($id, $workflow, $position, $prompt, $note);
                """;
            command.With("$id", SqlValues.Id(step.Id))
                .With("$workflow", SqlValues.Id(workflow.Id))
                .With("$position", position)
                .With("$prompt", SqlValues.Id(step.PromptId))
                .With("$note", SqlValues.TextOrNull(step.Note));
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                throw new Application.LibraryException("One of those prompts no longer exists.", exception);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Workflows WHERE Id = $id;";
        command.With("$id", SqlValues.Id(id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Dictionary<string, string>> LoadValuesAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, Value FROM WorkflowValues WHERE WorkflowId = $id;";
        command.With("$id", SqlValues.Id(workflowId));

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        return values;
    }

    public async Task SaveValuesAsync(Guid workflowId, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM WorkflowValues WHERE WorkflowId = $id;";
            clear.With("$id", SqlValues.Id(workflowId));
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var (name, value) in values)
        {
            if (value.Length == 0)
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO WorkflowValues (WorkflowId, Name, Value) VALUES ($id, $name, $value);";
            command.With("$id", SqlValues.Id(workflowId)).With("$name", name).With("$value", value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
