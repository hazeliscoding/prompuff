using Microsoft.Extensions.Logging;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Domain.Entities;

namespace Prompuff.Application.Services;

/// <summary>Creates and edits workflows: named, ordered lists of prompts with a note on what each step hands on.</summary>
public sealed class WorkflowService(
    IWorkflowRepository workflows,
    IPromptTemplateService templates,
    TimeProvider time,
    ILogger<WorkflowService> logger)
{
    public Task<IReadOnlyList<WorkflowSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        workflows.ListAsync(cancellationToken);

    public Task<Workflow?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        workflows.GetAsync(id, cancellationToken);

    public async Task<Workflow> CreateAsync(
        string? name,
        string? description = null,
        IEnumerable<(Guid PromptId, string? Note)>? steps = null,
        CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Name = Workflow.NormalizeName(name),
            Description = Workflow.NormalizeDescription(description),
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var (promptId, note) in steps ?? [])
        {
            workflow.Steps.Add(new WorkflowStep(Guid.NewGuid(), promptId, WorkflowStep.NormalizeNote(note)));
        }

        await workflows.SaveAsync(workflow, cancellationToken);
        logger.LogInformation("Created workflow {WorkflowId} with {Count} steps", workflow.Id, workflow.Steps.Count);
        return workflow;
    }

    /// <summary>Saves the name and description. Returns the workflow as saved.</summary>
    public Task<Workflow> RenameAsync(Guid id, string? name, string? description, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, workflow =>
        {
            workflow.Name = Workflow.NormalizeName(name);
            workflow.Description = Workflow.NormalizeDescription(description);
        }, cancellationToken);

    public Task<Workflow> AddStepAsync(Guid id, Guid promptId, string? note = null, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, workflow => workflow.Steps.Add(new WorkflowStep(Guid.NewGuid(), promptId, WorkflowStep.NormalizeNote(note))), cancellationToken);

    public Task<Workflow> RemoveStepAsync(Guid id, Guid stepId, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, workflow => workflow.Steps.RemoveAll(step => step.Id == stepId), cancellationToken);

    /// <summary>Moves a step up (negative) or down (positive), stopping at either end.</summary>
    public Task<Workflow> MoveStepAsync(Guid id, Guid stepId, int offset, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, workflow =>
        {
            var index = workflow.Steps.FindIndex(step => step.Id == stepId);
            if (index < 0)
            {
                return;
            }

            var target = Math.Clamp(index + offset, 0, workflow.Steps.Count - 1);
            var step = workflow.Steps[index];
            workflow.Steps.RemoveAt(index);
            workflow.Steps.Insert(target, step);
        }, cancellationToken);

    public Task<Workflow> SetStepNoteAsync(Guid id, Guid stepId, string? note, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, workflow =>
        {
            var index = workflow.Steps.FindIndex(step => step.Id == stepId);
            if (index >= 0)
            {
                workflow.Steps[index] = workflow.Steps[index] with { Note = WorkflowStep.NormalizeNote(note) };
            }
        }, cancellationToken);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await workflows.DeleteAsync(id, cancellationToken);
        logger.LogInformation("Deleted workflow {WorkflowId}", id);
    }

    public Task<Dictionary<string, string>> LoadValuesAsync(Guid id, CancellationToken cancellationToken = default) =>
        workflows.LoadValuesAsync(id, cancellationToken);

    public Task SaveValuesAsync(Guid id, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default) =>
        workflows.SaveValuesAsync(id, values, cancellationToken);

    /// <summary>
    /// The variables across all steps, each listed once in order of first appearance, with the steps that use it. A
    /// variable that several steps share is filled in once.
    /// </summary>
    public IReadOnlyList<WorkflowVariable> CollectVariables(IReadOnlyList<string> stepBodies)
    {
        var steps = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var order = new List<string>();
        for (var i = 0; i < stepBodies.Count; i++)
        {
            foreach (var name in templates.ExtractVariables(stepBodies[i]))
            {
                if (!steps.TryGetValue(name, out var numbers))
                {
                    steps[name] = numbers = [];
                    order.Add(name);
                }

                if (!numbers.Contains(i + 1))
                {
                    numbers.Add(i + 1);
                }
            }
        }

        return order.Select(name => new WorkflowVariable(name, steps[name])).ToList();
    }

    private async Task<Workflow> ModifyAsync(Guid id, Action<Workflow> change, CancellationToken cancellationToken)
    {
        var workflow = await workflows.GetAsync(id, cancellationToken) ?? throw new LibraryException("That workflow no longer exists.");
        change(workflow);
        workflow.UpdatedAt = time.GetUtcNow();
        await workflows.SaveAsync(workflow, cancellationToken);
        return workflow;
    }
}
