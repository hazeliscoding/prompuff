using Prompuff.Application.DTOs;

namespace Prompuff.Application.Interfaces;

/// <summary>Moves prompts in and out of the library as portable files.</summary>
public interface IPromptTransferService
{
    string SuggestFileName(string title);
    Task ExportPromptAsync(Guid promptId, string filePath, CancellationToken cancellationToken = default);
    /// <summary>Saves the prompts as one .zip of Markdown files, for moving them to another machine or another person.</summary>
    Task<ExportResult> ExportArchiveAsync(IReadOnlyList<Guid> promptIds, string zipPath, CancellationToken cancellationToken = default);

    /// <summary>Imports .md files and .zip exports. Prompts the library already has (same title and body) are skipped.</summary>
    Task<ImportResult> ImportFilesAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default);
}
