namespace Prompuff.Application.DTOs;

/// <param name="StepTitles">The prompt titles of the steps, in order, for a preview such as "Plan → Apply → Verify".</param>
public sealed record WorkflowSummary(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<string> StepTitles,
    DateTimeOffset UpdatedAt)
{
    public int StepCount => StepTitles.Count;
}

/// <summary>A variable shared across a workflow's steps.</summary>
/// <param name="Steps">The step numbers that use it, counting from 1.</param>
public sealed record WorkflowVariable(string Name, IReadOnlyList<int> Steps);
