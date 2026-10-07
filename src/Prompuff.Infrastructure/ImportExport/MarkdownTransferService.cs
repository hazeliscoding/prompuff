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
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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

    public async Task<ExportResult> ExportPromptsAsync(IReadOnlyList<Guid> promptIds, string folderPath, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(folderPath);
            var exported = 0;
            foreach (var id in promptIds)
            {
                var prompt = await prompts.GetAsync(id, cancellationToken);
                if (prompt is null)
                {
                    continue;
                }

                var path = UniquePath(folderPath, SuggestFileName(prompt.Title));
                await File.WriteAllTextAsync(path, await ToMarkdownAsync(prompt, cancellationToken), Utf8NoBom, cancellationToken);
                exported++;
            }

            logger.LogInformation("Exported {Count} prompts", exported);
            return new ExportResult(exported, folderPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Export of {Count} prompts failed", promptIds.Count);
            throw new LibraryException("Prompuff couldn't write to that folder. Check that you can save there.", exception);
        }
    }

    public async Task<ImportResult> ImportFilesAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default)
    {
        var imported = new List<Guid>();
        var failures = new List<ImportFailure>();

        foreach (var path in filePaths)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    failures.Add(new ImportFailure(path, "The file doesn't exist anymore."));
                    continue;
                }

                if (info.Length > MaxImportBytes)
                {
                    failures.Add(new ImportFailure(path, "The file is too large to be a prompt (over 5 MB)."));
                    continue;
                }

                var text = await File.ReadAllTextAsync(path, cancellationToken);
                var parsed = MarkdownPromptFormat.Read(text, Path.GetFileNameWithoutExtension(path));
                imported.Add(await AddAsync(parsed, cancellationToken));
            }
            catch (MarkdownFormatException exception)
            {
                logger.LogWarning("Import skipped a file: {Reason}", exception.Message);
                failures.Add(new ImportFailure(path, exception.Message));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                logger.LogWarning(exception, "Import couldn't read a file");
                failures.Add(new ImportFailure(path, "Prompuff couldn't read the file.", exception.Message));
            }
        }

        logger.LogInformation("Imported {Imported} prompts, {Failed} files failed", imported.Count, failures.Count);
        return new ImportResult(imported, failures);
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
            cancellationToken);
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

    private static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(folder, $"{stem}-{n}{extension}");
        }

        return path;
    }
}
