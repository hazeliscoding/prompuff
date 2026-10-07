namespace Prompuff.Application.DTOs;

public sealed record ImportFailure(string FilePath, string Reason, string? Details = null);

/// <param name="SkippedCount">Prompts left out because the library already has one with the same title and body.</param>
public sealed record ImportResult(IReadOnlyList<Guid> ImportedPromptIds, IReadOnlyList<ImportFailure> Failures, int SkippedCount = 0)
{
    public int ImportedCount => ImportedPromptIds.Count;
}

public sealed record ExportResult(int ExportedCount, string FilePath);
