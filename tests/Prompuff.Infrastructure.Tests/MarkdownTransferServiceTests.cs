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
    public async Task Exporting_several_prompts_gives_each_a_unique_file()
    {
        await using var library = await TestLibrary.CreateAsync();
        var a = await library.PromptService.CreateAsync(new PromptContent("Same title", null, "a", null));
        var b = await library.PromptService.CreateAsync(new PromptContent("Same title", null, "b", null));
        var folder = library.TempFile("export");

        var result = await library.Transfer.ExportPromptsAsync([a.Id, b.Id], folder);

        Assert.Equal(new ExportResult(2, folder), result);
        Assert.Equal(["same-title-2.md", "same-title.md"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
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
