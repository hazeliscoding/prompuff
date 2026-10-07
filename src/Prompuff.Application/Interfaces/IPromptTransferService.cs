using Prompuff.Application.DTOs;

namespace Prompuff.Application.Interfaces;

/// <summary>Moves prompts in and out of the library as portable files.</summary>
public interface IPromptTransferService
{
    string SuggestFileName(string title);
    Task ExportPromptAsync(Guid promptId, string filePath, CancellationToken cancellationToken = default);
    Task<ExportResult> ExportPromptsAsync(IReadOnlyList<Guid> promptIds, string folderPath, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportFilesAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default);
}
