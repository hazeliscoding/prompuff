namespace Prompuff.Application.DTOs;

public sealed record ImportFailure(string FilePath, string Reason, string? Details = null);

public sealed record ImportResult(IReadOnlyList<Guid> ImportedPromptIds, IReadOnlyList<ImportFailure> Failures)
{
    public int ImportedCount => ImportedPromptIds.Count;
}

public sealed record ExportResult(int ExportedCount, string FolderPath);
