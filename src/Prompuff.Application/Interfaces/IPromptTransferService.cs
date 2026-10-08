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

    /// <summary>
    /// Imports every Markdown file in a folder and its subfolders, such as an Obsidian vault. Folders and files whose
    /// names start with a dot (<c>.obsidian</c>, <c>.git</c>) are passed over, and other files are listed as skipped.
    /// </summary>
    Task<ImportResult> ImportFolderAsync(string folderPath, CancellationToken cancellationToken = default);
}
