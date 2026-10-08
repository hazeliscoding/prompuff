using Prompuff.Application.DTOs;
using Prompuff.Domain.Entities;

namespace Prompuff.Application.Interfaces;

public interface IWorkflowRepository
{
    /// <summary>Every workflow, most recently changed first.</summary>
    Task<IReadOnlyList<WorkflowSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<Workflow?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates the workflow and replaces its steps, in one transaction.</summary>
    Task SaveAsync(Workflow workflow, CancellationToken cancellationToken = default);

    /// <summary>Deletes the workflow, its steps and its remembered values. Its prompts stay in the library.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The values last filled in on the Run tab. Never exported or logged.</summary>
    Task<Dictionary<string, string>> LoadValuesAsync(Guid workflowId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the remembered values. Empty values aren't stored.</summary>
    Task SaveValuesAsync(Guid workflowId, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default);
}
