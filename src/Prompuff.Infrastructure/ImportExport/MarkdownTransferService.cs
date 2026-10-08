using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using Prompuff.Application;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;
using Prompuff.Domain.Entities;
using Prompuff.Domain.ValueObjects;

namespace Prompuff.Infrastructure.ImportExport;

/// <summary>Exports prompts to Markdown files and imports them back. Each imported file is its own unit:
/// a file that can't be read is reported and leaves the library unchanged.</summary>
public sealed class MarkdownTransferService(
    PromptService prompts,
    CollectionService collections,
    ICollectionRepository collectionRepository,
    ILogger<MarkdownTransferService> logger) : IPromptTransferService
{
    private const long MaxImportBytes = 5 * 1024 * 1024;
    private const int MaxArchiveEntries = 10_000;
    private const string TooLarge = "The file is too large to be a prompt (over 5 MB).";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly HashSet<string> MarkdownExtensions = new([".md", ".markdown", ".txt"], StringComparer.OrdinalIgnoreCase);

    public string SuggestFileName(string title)
    {
        var slug = new StringBuilder();
        foreach (var ch in title.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                slug.Append(ch);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var name = slug.ToString().Trim('-');
        if (name.Length > 60)
        {
            name = name[..60].TrimEnd('-');
        }

        return (name.Length == 0 ? "prompt" : name) + ".md";
    }

    public async Task ExportPromptAsync(Guid promptId, string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = await prompts.GetAsync(promptId, cancellationToken) ?? throw new LibraryException("That prompt no longer exists.");
            var markdown = await ToMarkdownAsync(prompt, cancellationToken);
            await File.WriteAllTextAsync(filePath, markdown, Utf8NoBom, cancellationToken);
            logger.LogInformation("Exported prompt {PromptId}", promptId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Export failed for prompt {PromptId}", promptId);
            throw new LibraryException("Prompuff couldn't write that file. Check that the folder exists and you can save there.", exception);
        }
    }

    public async Task<ExportResult> ExportArchiveAsync(IReadOnlyList<Guid> promptIds, string zipPath, CancellationToken cancellationToken = default)
    {
        // Written beside the target first, so a failed export never leaves half a zip under the chosen name.
        var temporary = zipPath + ".tmp";
        try
        {
            var exported = 0;
            await using (var file = File.Create(temporary))
            await using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var id in promptIds)
                {
                    var prompt = await prompts.GetAsync(id, cancellationToken);
                    if (prompt is null)
                    {
                        continue;
                    }

                    var entry = archive.CreateEntry(UniqueName(names, SuggestFileName(prompt.Title)), CompressionLevel.Optimal);
                    await using var writer = new StreamWriter(await entry.OpenAsync(cancellationToken), Utf8NoBom);
                    await writer.WriteAsync(await ToMarkdownAsync(prompt, cancellationToken));
                    exported++;
                }
            }

            File.Move(temporary, zipPath, overwrite: true);
            logger.LogInformation("Exported {Count} prompts to a zip", exported);
            return new ExportResult(exported, zipPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Export of {Count} prompts failed", promptIds.Count);
            TryDelete(temporary);
            throw new LibraryException("Prompuff couldn't save that file. Check that you can save there.", exception);
        }
    }

    public async Task<ImportResult> ImportFilesAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default)
    {
        var import = new ImportProgress();
        foreach (var path in filePaths)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    import.Failures.Add(new ImportFailure(path, "The file doesn't exist anymore."));
                }
                else if (string.Equals(info.Extension, ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    await ImportArchiveAsync(path, import, cancellationToken);
                }
                else if (info.Length > MaxImportBytes)
                {
                    import.Failures.Add(new ImportFailure(path, TooLarge));
                }
                else
                {
                    var text = await File.ReadAllTextAsync(path, cancellationToken);
                    await ImportTextAsync(path, text, Path.GetFileNameWithoutExtension(path), import, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException or InvalidDataException)
            {
                logger.LogWarning(exception, "Import couldn't read a file");
                import.Failures.Add(new ImportFailure(path, "Prompuff couldn't read the file.", exception.Message));
            }
        }

        logger.LogInformation(
            "Imported {Imported} prompts, skipped {Skipped} already in the library, {Failed} files failed",
            import.Imported.Count, import.Skipped, import.Failures.Count);
        return new ImportResult(import.Imported, import.Failures, import.Skipped);
    }

    /// <summary>
    /// Imports the Markdown files in a zip, reading each entry in memory with the 5 MB limit. Folders such as
    /// <c>__MACOSX</c> and <c>.obsidian</c>, and files that aren't Markdown, are passed over.
    /// </summary>
    private async Task ImportArchiveAsync(string zipPath, ImportProgress import, CancellationToken cancellationToken)
    {
        await using var archive = await ZipFile.OpenReadAsync(zipPath, cancellationToken);
        if (archive.Entries.Count > MaxArchiveEntries)
        {
            import.Failures.Add(new ImportFailure(zipPath, $"The zip holds more than {MaxArchiveEntries:N0} files, which is more than Prompuff imports at once."));
            return;
        }

        foreach (var entry in archive.Entries)
        {
            var segments = entry.FullName.Split('/', '\\');
            if (entry.Name.Length == 0
                || segments.Any(segment => segment.StartsWith('.') || segment == "__MACOSX")
                || !MarkdownExtensions.Contains(Path.GetExtension(entry.Name)))
            {
                continue;
            }

            var entryPath = zipPath + "/" + entry.FullName;
            if (entry.Length > MaxImportBytes)
            {
                import.Failures.Add(new ImportFailure(entryPath, TooLarge));
                continue;
            }

            // The size in the zip's directory can lie, so the read itself stops past the limit.
            await using var stream = await entry.OpenAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0 && buffer.Length <= MaxImportBytes)
            {
                buffer.Write(chunk, 0, read);
            }

            if (buffer.Length > MaxImportBytes)
            {
                import.Failures.Add(new ImportFailure(entryPath, TooLarge));
                continue;
            }

            buffer.Position = 0;
            using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = await reader.ReadToEndAsync(cancellationToken);
            await ImportTextAsync(entryPath, text, Path.GetFileNameWithoutExtension(entry.Name), import, cancellationToken);
        }
    }

    private async Task ImportTextAsync(string path, string text, string fallbackTitle, ImportProgress import, CancellationToken cancellationToken)
    {
        MarkdownPrompt parsed;
        try
        {
            parsed = MarkdownPromptFormat.Read(text, fallbackTitle);
        }
        catch (MarkdownFormatException exception)
        {
            logger.LogWarning("Import skipped a file: {Reason}", exception.Message);
            import.Failures.Add(new ImportFailure(path, exception.Message));
            return;
        }

        if (await prompts.HasPromptAsync(parsed.Title, parsed.Body, cancellationToken))
        {
            import.Skipped++;
            return;
        }

        import.Imported.Add(await AddAsync(parsed, cancellationToken));
    }

    private async Task<Guid> AddAsync(MarkdownPrompt parsed, CancellationToken cancellationToken)
    {
        Guid? collectionId = null;
        if (Collection.NormalizeName(parsed.Collection) is not null)
        {
            collectionId = (await collections.GetOrCreateAsync(parsed.Collection!, cancellationToken)).Id;
        }

        var content = new PromptContent(parsed.Title, parsed.Description, parsed.Body, parsed.Notes);
        var metadata = new PromptMetadata(parsed.IsFavorite, parsed.Rating, collectionId, TagName.NormalizeAll(parsed.Tags));
        var prompt = await prompts.CreateAsync(
            content,
            metadata,
            "Imported from Markdown",
            parsed.CreatedAt,
            parsed.UpdatedAt,
            cancellationToken: cancellationToken);
        return prompt.Id;
    }

    private async Task<string> ToMarkdownAsync(Prompt prompt, CancellationToken cancellationToken)
    {
        string? collectionName = null;
        if (prompt.CollectionId is { } collectionId)
        {
            collectionName = (await collectionRepository.GetAsync(collectionId, cancellationToken))?.Name;
        }

        return MarkdownPromptFormat.Write(new MarkdownPrompt
        {
            Title = prompt.Title,
            Description = prompt.Description,
            Body = prompt.Body,
            Notes = prompt.Notes,
            Tags = prompt.Tags,
            Collection = collectionName,
            IsFavorite = prompt.IsFavorite,
            Rating = prompt.Rating,
            CreatedAt = prompt.CreatedAt,
            UpdatedAt = prompt.UpdatedAt,
        });
    }

    private static string UniqueName(HashSet<string> taken, string fileName)
    {
        var name = fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var n = 2; !taken.Add(name); n++)
        {
            name = $"{stem}-{n}{extension}";
        }

        return name;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover .tmp file is harmless.
        }
    }

    private sealed class ImportProgress
    {
        public List<Guid> Imported { get; } = [];
        public List<ImportFailure> Failures { get; } = [];
        public int Skipped { get; set; }
    }
}
