namespace Prompuff.Application.DTOs;

public sealed record ImportFailure(string FilePath, string Reason, string? Details = null);

/// <param name="SkippedCount">Prompts left out because the library already has one with the same title and body.</param>
/// <param name="OtherFiles">Files in an imported folder that aren't Markdown, relative to the folder, such as images.</param>
public sealed record ImportResult(
    IReadOnlyList<Guid> ImportedPromptIds,
    IReadOnlyList<ImportFailure> Failures,
    int SkippedCount = 0,
    IReadOnlyList<string>? OtherFiles = null)
{
    public int ImportedCount => ImportedPromptIds.Count;
    public IReadOnlyList<string> SkippedFiles => OtherFiles ?? [];
}

public sealed record ExportResult(int ExportedCount, string FilePath);
