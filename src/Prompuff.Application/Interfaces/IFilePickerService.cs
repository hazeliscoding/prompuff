namespace Prompuff.Application.Interfaces;

public interface IFilePickerService
{
    /// <summary>Lets the user pick Markdown files to import. Returns an empty list when cancelled.</summary>
    Task<IReadOnlyList<string>> PickFilesToImportAsync();

    /// <summary>Asks where to save one exported prompt. Returns null when cancelled.</summary>
    Task<string?> PickExportFileAsync(string suggestedFileName);

    /// <summary>Asks for a folder to export several prompts into. Returns null when cancelled.</summary>
    Task<string?> PickExportFolderAsync();
}
