namespace Prompuff.Application.Interfaces;

public interface IFilePickerService
{
    /// <summary>Lets the user pick Markdown files or .zip exports to import. Returns an empty list when cancelled.</summary>
    Task<IReadOnlyList<string>> PickFilesToImportAsync();

    /// <summary>Lets the user pick a folder of Markdown files to import. Returns null when cancelled.</summary>
    Task<string?> PickFolderToImportAsync();

    /// <summary>Asks where to save one exported prompt. Returns null when cancelled.</summary>
    Task<string?> PickExportFileAsync(string suggestedFileName);

    /// <summary>Asks where to save a .zip of several prompts. Returns null when cancelled.</summary>
    Task<string?> PickArchiveExportFileAsync(string suggestedFileName);
}
