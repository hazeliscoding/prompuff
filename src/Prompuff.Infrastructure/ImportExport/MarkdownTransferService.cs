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
    WorkflowService workflows,
    ILogger<MarkdownTransferService> logger) : IPromptTransferService
{
    private const long MaxImportBytes = 5 * 1024 * 1024;
    private const int MaxArchiveEntries = 10_000;
    private const int MaxFolderDepth = 32;
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

    public async Task ExportWorkflowAsync(Guid workflowId, string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var workflow = await WorkflowMarkdownAsync(workflowId, cancellationToken) ?? throw new LibraryException("That workflow no longer exists.");
            await File.WriteAllTextAsync(filePath, workflow.Markdown, Utf8NoBom, cancellationToken);
            logger.LogInformation("Exported workflow {WorkflowId}", workflowId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Export failed for workflow {WorkflowId}", workflowId);
            throw new LibraryException("Prompuff couldn't write that file. Check that the folder exists and you can save there.", exception);
        }
    }

    /// <summary>The workflow as one document holding each step's prompt, or null when it no longer exists.</summary>
    private async Task<(string Name, string Markdown)?> WorkflowMarkdownAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        if (await workflows.GetAsync(workflowId, cancellationToken) is not { } workflow)
        {
            return null;
        }

        var steps = new List<MarkdownWorkflowStep>(workflow.Steps.Count);
        foreach (var step in workflow.Steps)
        {
            if (await prompts.GetAsync(step.PromptId, cancellationToken) is { } prompt)
            {
                steps.Add(new MarkdownWorkflowStep(prompt.Title, prompt.Body, step.Note));
            }
        }

        return (workflow.Name, MarkdownWorkflowFormat.Write(new MarkdownWorkflow(workflow.Name, workflow.Description, steps)));
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

    public async Task<ExportResult> ExportArchiveAsync(
        IReadOnlyList<Guid> promptIds,
        string zipPath,
        IReadOnlyList<Guid>? workflowIds = null,
        CancellationToken cancellationToken = default)
    {
        // Written beside the target first, so a failed export never leaves half a zip under the chosen name.
        var temporary = zipPath + ".tmp";
        try
        {
            var exported = 0;
            var exportedWorkflows = 0;
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

                foreach (var id in workflowIds ?? [])
                {
                    if (await WorkflowMarkdownAsync(id, cancellationToken) is not { } workflow)
                    {
                        continue;
                    }

                    var entry = archive.CreateEntry(UniqueName(names, SuggestFileName(workflow.Name), "workflows/"), CompressionLevel.Optimal);
                    await using var writer = new StreamWriter(await entry.OpenAsync(cancellationToken), Utf8NoBom);
                    await writer.WriteAsync(workflow.Markdown);
                    exportedWorkflows++;
                }
            }

            File.Move(temporary, zipPath, overwrite: true);
            logger.LogInformation("Exported {Count} prompts and {Workflows} workflows to a zip", exported, exportedWorkflows);
            return new ExportResult(exported, zipPath, exportedWorkflows);
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
            await ImportFileAsync(path, import, cancellationToken);
        }

        await ImportWorkflowsAsync(import, cancellationToken);
        return Finish(import);
    }

    public async Task<ImportResult> ImportFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var import = new ImportProgress();
        var root = new DirectoryInfo(folderPath);
        if (!root.Exists)
        {
            import.Failures.Add(new ImportFailure(folderPath, "The folder doesn't exist anymore."));
            return Finish(import);
        }

        var files = new List<string>();
        var pending = new Stack<(DirectoryInfo Folder, int Depth)>([(root, 0)]);
        while (pending.Count > 0)
        {
            var (folder, depth) = pending.Pop();
            List<FileSystemInfo> entries;
            try
            {
                entries = folder.EnumerateFileSystemInfos().ToList();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                logger.LogWarning(exception, "Folder import couldn't open a subfolder");
                import.Failures.Add(new ImportFailure(folder.FullName, "Prompuff couldn't open this folder.", exception.Message));
                continue;
            }

            foreach (var entry in entries)
            {
                // Dot folders hold app state (.obsidian, .git, .trash), and dot files are system clutter (.DS_Store).
                if (entry.Name.StartsWith('.') || entry.Name == "__MACOSX")
                {
                    continue;
                }

                if (entry is DirectoryInfo subfolder)
                {
                    // Links can point back up the tree, so they aren't followed.
                    if (subfolder.LinkTarget is null && depth < MaxFolderDepth)
                    {
                        pending.Push((subfolder, depth + 1));
                    }

                    continue;
                }

                if (files.Count + import.OtherFiles.Count >= MaxArchiveEntries)
                {
                    import.Failures.Add(new ImportFailure(folderPath, $"The folder holds more than {MaxArchiveEntries:N0} files, which is more than Prompuff imports at once."));
                    return Finish(import);
                }

                var relative = Path.GetRelativePath(root.FullName, entry.FullName);
                if (entry.LinkTarget is not null)
                {
                    // A link could lead anywhere on the machine, so only files that really live in the folder are read.
                    import.OtherFiles.Add(relative + " (a link)");
                }
                else if (MarkdownExtensions.Contains(entry.Extension))
                {
                    files.Add(entry.FullName);
                }
                else
                {
                    import.OtherFiles.Add(relative);
                }
            }
        }

        // Path order, so a folder imports the same way every time.
        foreach (var path in files.Order(StringComparer.OrdinalIgnoreCase))
        {
            await ImportFileAsync(path, import, cancellationToken);
        }

        await ImportWorkflowsAsync(import, cancellationToken);

        import.OtherFiles.Sort(StringComparer.OrdinalIgnoreCase);
        return Finish(import);
    }

    /// <param name="workflowPass">
    /// False on the first pass, which imports prompts and notes where the workflows are. True on the second, which
    /// reads those workflow files again. Only their paths wait in between, never their text, so a large import can't
    /// pile up in memory.
    /// </param>
    private async Task ImportFileAsync(string path, ImportProgress import, CancellationToken cancellationToken, bool workflowPass = false)
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
            else if (info.Length == 0)
            {
                // Also keeps pipes and device files, which report no length, from being opened at all.
                import.Failures.Add(new ImportFailure(path, "The file is empty."));
            }
            else
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (await ReadLimitedAsync(stream, cancellationToken) is not { } text)
                {
                    import.Failures.Add(new ImportFailure(path, TooLarge));
                }
                else if (!MarkdownWorkflowFormat.IsWorkflow(text))
                {
                    if (!workflowPass)
                    {
                        await ImportPromptAsync(path, text, Path.GetFileNameWithoutExtension(path), import, cancellationToken);
                    }
                }
                else if (workflowPass)
                {
                    await ImportWorkflowAsync(path, text, Path.GetFileNameWithoutExtension(path), import, cancellationToken);
                }
                else
                {
                    import.PendingWorkflowFiles.Add(path);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException or InvalidDataException)
        {
            logger.LogWarning(exception, "Import couldn't read a file");
            import.Failures.Add(new ImportFailure(path, "Prompuff couldn't read the file.", exception.Message));
        }
    }

    private ImportResult Finish(ImportProgress import)
    {
        logger.LogInformation(
            "Imported {Imported} prompts, skipped {Skipped} already in the library and {Other} other files, {Failed} files failed",
            import.Imported.Count, import.Skipped, import.OtherFiles.Count, import.Failures.Count);
        return new ImportResult(import.Imported, import.Failures, import.Skipped, import.OtherFiles, import.Workflows);
    }

    /// <summary>
    /// Imports the Markdown files in a zip, reading each entry in memory with the 5 MB limit. Folders such as
    /// <c>__MACOSX</c> and <c>.obsidian</c>, and files that aren't Markdown, are passed over. Prompts come first; the
    /// workflow entries are read again once they're in, so steps find the prompts from the same zip.
    /// </summary>
    private async Task ImportArchiveAsync(string zipPath, ImportProgress import, CancellationToken cancellationToken)
    {
        await using var archive = await ZipFile.OpenReadAsync(zipPath, cancellationToken);
        if (archive.Entries.Count > MaxArchiveEntries)
        {
            import.Failures.Add(new ImportFailure(zipPath, $"The zip holds more than {MaxArchiveEntries:N0} files, which is more than Prompuff imports at once."));
            return;
        }

        var workflowEntries = new List<ZipArchiveEntry>();
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

            await using var stream = await entry.OpenAsync(cancellationToken);
            if (await ReadLimitedAsync(stream, cancellationToken) is not { } text)
            {
                import.Failures.Add(new ImportFailure(entryPath, TooLarge));
            }
            else if (MarkdownWorkflowFormat.IsWorkflow(text))
            {
                workflowEntries.Add(entry);
            }
            else
            {
                await ImportPromptAsync(entryPath, text, Path.GetFileNameWithoutExtension(entry.Name), import, cancellationToken);
            }
        }

        foreach (var entry in workflowEntries)
        {
            await using var stream = await entry.OpenAsync(cancellationToken);
            if (await ReadLimitedAsync(stream, cancellationToken) is { } text)
            {
                await ImportWorkflowAsync(zipPath + "/" + entry.FullName, text, Path.GetFileNameWithoutExtension(entry.Name), import, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Reads text in memory, stopping as soon as it passes the 5 MB limit. Sizes reported by a zip's directory or by
    /// the file system can lie, so the read itself enforces the limit. Returns null when the text is too large.
    /// </summary>
    private static async Task<string?> ReadLimitedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while (buffer.Length <= MaxImportBytes && (read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length > MaxImportBytes)
        {
            return null;
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private async Task ImportPromptAsync(string path, string text, string fallbackTitle, ImportProgress import, CancellationToken cancellationToken)
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

    /// <summary>
    /// The second pass over files and folders: workflows wait until every prompt is in, so a step finds the prompt from
    /// the same export, with its tags, notes and collection, instead of creating a bare copy of it.
    /// </summary>
    private async Task ImportWorkflowsAsync(ImportProgress import, CancellationToken cancellationToken)
    {
        foreach (var path in import.PendingWorkflowFiles)
        {
            await ImportFileAsync(path, import, cancellationToken, workflowPass: true);
        }

        import.PendingWorkflowFiles.Clear();
    }

    /// <summary>
    /// A workflow document becomes a workflow. Each step uses the library's prompt with the same title and body, or a
    /// new prompt when there isn't one. Importing the same workflow twice is skipped, as a prompt would be.
    /// </summary>
    private async Task ImportWorkflowAsync(string path, string text, string fallbackTitle, ImportProgress import, CancellationToken cancellationToken)
    {
        MarkdownWorkflow parsed;
        try
        {
            parsed = MarkdownWorkflowFormat.Read(text, fallbackTitle);
        }
        catch (MarkdownFormatException exception)
        {
            logger.LogWarning("Import skipped a workflow: {Reason}", exception.Message);
            import.Failures.Add(new ImportFailure(path, exception.Message));
            return;
        }

        var name = Workflow.NormalizeName(parsed.Title);
        var steps = new List<(Guid PromptId, string? Note)>(parsed.Steps.Count);
        foreach (var step in parsed.Steps)
        {
            var id = await prompts.FindAsync(step.Title, step.Body, cancellationToken);
            if (id is null)
            {
                var created = await prompts.CreateAsync(
                    new PromptContent(step.Title, null, step.Body, null),
                    versionNote: $"Imported with the “{name}” workflow",
                    cancellationToken: cancellationToken);
                import.Imported.Add(created.Id);
                id = created.Id;
            }

            steps.Add((id.Value, step.Note));
        }

        foreach (var existing in await workflows.ListAsync(cancellationToken))
        {
            if (existing.Name == name && await workflows.GetAsync(existing.Id, cancellationToken) is { } candidate
                && candidate.Steps.Select(step => step.PromptId).SequenceEqual(steps.Select(step => step.PromptId)))
            {
                import.Skipped++;
                return;
            }
        }

        var workflow = await workflows.CreateAsync(name, parsed.Description, steps, cancellationToken);
        import.Workflows.Add(workflow.Id);
    }

    /// <param name="folder">A folder inside the zip, ending in "/", or empty for the top level.</param>
    private static string UniqueName(HashSet<string> taken, string fileName, string folder = "")
    {
        var name = folder + fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var n = 2; !taken.Add(name); n++)
        {
            name = $"{folder}{stem}-{n}{extension}";
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
        public List<Guid> Workflows { get; } = [];

        /// <summary>Workflow files found on the first pass, read again once every prompt is in.</summary>
        public List<string> PendingWorkflowFiles { get; } = [];
        public List<ImportFailure> Failures { get; } = [];
        public List<string> OtherFiles { get; } = [];
        public int Skipped { get; set; }
    }
}
