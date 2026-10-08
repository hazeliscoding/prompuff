using System.IO.Compression;
using Prompuff.Application.DTOs;
using Prompuff.Application.Services;
using Prompuff.Domain.ValueObjects;

namespace Prompuff.Infrastructure.Tests;

public class MarkdownTransferServiceTests
{
    [Fact]
    public async Task Export_then_import_recreates_the_prompt()
    {
        await using var library = await TestLibrary.CreateAsync();
        var coding = await library.CollectionService.CreateAsync("Coding");
        var original = await library.PromptService.CreateAsync(
            new PromptContent("Angular Upgrade Planner", "Upgrade planning prompt", "Upgrade {{repo_name}}.\n\n# Steps\n\nInspect first.", "Phasing works."),
            new PromptMetadata(true, 5, coding.Id, ["angular", "migration"]));
        var file = library.TempFile("angular-upgrade-planner.md");

        await library.Transfer.ExportPromptAsync(original.Id, file);
        await library.PromptService.DeleteAsync(original.Id);
        await library.CollectionService.DeleteAsync(coding.Id);
        var result = await library.Transfer.ImportFilesAsync([file]);

        Assert.Empty(result.Failures);
        var imported = await library.Prompts.GetAsync(Assert.Single(result.ImportedPromptIds));
        Assert.NotNull(imported);
        Assert.Equal(original.Content, imported.Content);
        Assert.True(imported.IsFavorite);
        Assert.Equal(5, imported.Rating);
        Assert.Equal(["angular", "migration"], imported.Tags);
        Assert.Equal("Coding", (await library.Collections.GetAsync(imported.CollectionId!.Value))!.Name);
        Assert.Equal(original.CreatedAt.ToUnixTimeSeconds(), imported.CreatedAt.ToUnixTimeSeconds());
        Assert.Equal("Imported from Markdown", (await library.Prompts.GetVersionsAsync(imported.Id)).Single().Note);
    }

    [Fact]
    public async Task Import_reuses_an_existing_collection_regardless_of_case()
    {
        await using var library = await TestLibrary.CreateAsync();
        var coding = await library.CollectionService.CreateAsync("Coding");
        var file = library.TempFile("p.md");
        await File.WriteAllTextAsync(file, "---\ntitle: P\ncollection: coding\n---\nBody");

        var result = await library.Transfer.ImportFilesAsync([file]);

        Assert.Equal(coding.Id, (await library.Prompts.GetAsync(result.ImportedPromptIds[0]))!.CollectionId);
        Assert.Single(await library.Collections.ListAsync());
    }

    [Fact]
    public async Task A_bad_file_is_reported_and_leaves_the_library_unchanged()
    {
        await using var library = await TestLibrary.CreateAsync();
        var good = library.TempFile("good.md");
        var bad = library.TempFile("bad.md");
        var missing = library.TempFile("missing.md");
        await File.WriteAllTextAsync(good, "---\ntitle: Good\n---\nBody");
        await File.WriteAllTextAsync(bad, "---\ntitle: Bad\nBody with no closing fence");

        var result = await library.Transfer.ImportFilesAsync([good, bad, missing]);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal([bad, missing], result.Failures.Select(failure => failure.FilePath));
        Assert.Equal(1, (await library.Prompts.GetCountsAsync()).All);
    }

    [Fact]
    public async Task Exporting_several_prompts_gives_each_a_unique_file_in_one_zip()
    {
        await using var library = await TestLibrary.CreateAsync();
        var a = await library.PromptService.CreateAsync(new PromptContent("Same title", null, "a", null));
        var b = await library.PromptService.CreateAsync(new PromptContent("Same title", null, "b", null));
        var zip = library.TempFile("export.zip");

        var result = await library.Transfer.ExportArchiveAsync([a.Id, b.Id], zip);

        Assert.Equal(new ExportResult(2, zip), result);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(["same-title-2.md", "same-title.md"], archive.Entries.Select(entry => entry.FullName).Order());
    }

    [Fact]
    public async Task A_zip_moves_prompts_to_another_library()
    {
        await using var source = await TestLibrary.CreateAsync();
        var coding = await source.CollectionService.CreateAsync("Coding");
        var angular = await source.PromptService.CreateAsync(
            new PromptContent("Angular Upgrade Planner", "Upgrade planning prompt", "Upgrade {{repo_name}}.", "Phasing works."),
            new PromptMetadata(true, 5, coding.Id, ["angular", "migration"]));
        var readme = await source.PromptService.CreateAsync(new PromptContent("README Cleanup", null, "Rewrite the README.", null));
        var zip = source.TempFile("share.zip");
        await source.Transfer.ExportArchiveAsync([angular.Id, readme.Id], zip);

        await using var target = await TestLibrary.CreateAsync();
        var result = await target.Transfer.ImportFilesAsync([zip]);

        Assert.Empty(result.Failures);
        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(0, result.SkippedCount);
        var imported = (await target.Search.SearchAsync(new PromptQuery { Text = "angular" })).Single();
        var prompt = (await target.Prompts.GetAsync(imported.Id))!;
        Assert.Equal(angular.Content, prompt.Content);
        Assert.Equal(["angular", "migration"], prompt.Tags);
        Assert.True(prompt.IsFavorite);
        Assert.Equal("Coding", (await target.Collections.GetAsync(prompt.CollectionId!.Value))!.Name);
    }

    [Fact]
    public async Task Prompts_already_in_the_library_are_skipped()
    {
        await using var library = await TestLibrary.CreateAsync();
        var kept = await library.PromptService.CreateAsync(new PromptContent("Kept", null, "Line one\r\nLine two\r\n", null));
        var edited = await library.PromptService.CreateAsync(new PromptContent("Edited", null, "first", null));
        var deleted = await library.PromptService.CreateAsync(new PromptContent("Deleted", null, "gone", null));
        var zip = library.TempFile("share.zip");
        await library.Transfer.ExportArchiveAsync([kept.Id, edited.Id, deleted.Id], zip);

        await library.PromptService.SaveContentAsync(edited.Id, new PromptContent("Edited", null, "second", null));
        await library.PromptService.DeleteAsync(deleted.Id);
        var result = await library.Transfer.ImportFilesAsync([zip, zip]);

        Assert.Empty(result.Failures);
        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(4, result.SkippedCount);
        Assert.Equal(["Deleted", "Edited", "Edited", "Kept"], (await library.Search.SearchAsync(new PromptQuery { Sort = PromptSort.Title })).Select(p => p.Title));
    }

    [Fact]
    public async Task Zip_entries_are_checked_and_extras_ignored()
    {
        await using var library = await TestLibrary.CreateAsync();
        var zip = library.TempFile("mixed.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            void Add(string name, string text)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(text);
            }

            Add("top.md", "---\ntitle: Top\n---\n\nBody");
            Add("folder/nested.md", "---\ntitle: Nested\n---\n\nBody");
            Add("folder/", "");
            Add("__MACOSX/._top.md", "junk");
            Add(".obsidian/workspace.md", "junk");
            Add("notes.pdf", "junk");
            Add("huge.md", new string('x', 5 * 1024 * 1024 + 1));
        }

        var result = await library.Transfer.ImportFilesAsync([zip]);

        Assert.Equal(2, result.ImportedCount);
        var failure = Assert.Single(result.Failures);
        Assert.EndsWith("huge.md", failure.FilePath);
        Assert.Contains("too large", failure.Reason);
        Assert.Equal(["Nested", "Top"], (await library.Search.SearchAsync(new PromptQuery { Sort = PromptSort.Title })).Select(p => p.Title));
    }

    [Fact]
    public async Task A_damaged_zip_is_reported_and_leaves_the_library_unchanged()
    {
        await using var library = await TestLibrary.CreateAsync();
        var zip = library.TempFile("damaged.zip");
        await File.WriteAllTextAsync(zip, "not really a zip");

        var result = await library.Transfer.ImportFilesAsync([zip]);

        Assert.Empty(result.ImportedPromptIds);
        Assert.Equal(zip, Assert.Single(result.Failures).FilePath);
        Assert.Empty(await library.Search.SearchAsync(PromptQuery.All));
    }

    [Fact]
    public async Task A_folder_imports_its_subfolders_and_skips_dot_folders()
    {
        await using var library = await TestLibrary.CreateAsync();
        var vault = Path.Combine(library.Folder, "vault");
        void Write(string relative, string text)
        {
            var path = Path.Combine(vault, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        Write("Top.md", "Top body");
        Write(Path.Combine("Coding", "Review.md"), "---\ntitle: Code review\ntags: [review]\n---\n\n# Prompt\n\nReview {{diff}}.");
        Write(Path.Combine("Coding", "Deeper", "Plan.markdown"), "Plan body");
        Write(Path.Combine("Coding", "diagram.png"), "not text");
        Write("notes.pdf", "not text");
        Write(Path.Combine(".obsidian", "workspace.md"), "Obsidian state");
        Write(Path.Combine(".git", "HEAD.md"), "Git state");
        Write(".DS_Store", "clutter");

        var result = await library.Transfer.ImportFolderAsync(vault);

        Assert.Empty(result.Failures);
        Assert.Equal(3, result.ImportedCount);
        Assert.Equal([Path.Combine("Coding", "diagram.png"), "notes.pdf"], result.SkippedFiles);
        var titles = (await library.Search.SearchAsync(new PromptQuery { Sort = PromptSort.Title })).Select(p => p.Title);
        Assert.Equal(["Code review", "Plan", "Top"], titles);

        // A second import finds nothing new.
        var again = await library.Transfer.ImportFolderAsync(vault);
        Assert.Equal(0, again.ImportedCount);
        Assert.Equal(3, again.SkippedCount);
    }

    [Fact]
    public async Task A_missing_folder_is_reported()
    {
        await using var library = await TestLibrary.CreateAsync();
        var missing = Path.Combine(library.Folder, "gone");

        var result = await library.Transfer.ImportFolderAsync(missing);

        Assert.Equal(missing, Assert.Single(result.Failures).FilePath);
    }

    [Theory]
    [InlineData("Angular Upgrade Planner", "angular-upgrade-planner.md")]
    [InlineData("  README: cleanup!! ", "readme-cleanup.md")]
    [InlineData("???", "prompt.md")]
    [InlineData("Ünïcödé", "n-c-d.md")]
    public void File_names_are_safe_on_every_platform(string title, string expected)
    {
        var service = new Prompuff.Infrastructure.ImportExport.MarkdownTransferService(null!, null!, null!, null!);

        Assert.Equal(expected, service.SuggestFileName(title));
    }
}
