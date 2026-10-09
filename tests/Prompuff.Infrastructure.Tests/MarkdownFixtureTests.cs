using Prompuff.Application.Services;
using Prompuff.Domain.ValueObjects;
using Prompuff.Tests;

namespace Prompuff.Infrastructure.Tests;

/// <summary>
/// Markdown exported from 1.0 imports into every later version. The files in <c>Fixtures/markdown-1.0.0/</c> are what
/// 1.0 wrote (see <see cref="MarkdownFixtures"/>), and these tests import them the way the app does.
/// </summary>
public class MarkdownFixtureTests
{
    public static TheoryData<string> PromptFiles => new(MarkdownFixtures.Prompts.Select(prompt => prompt.File));

    [Fact]
    public void The_folder_holds_each_prompt_the_workflow_and_the_zip()
    {
        var folder = MarkdownFixtures.PathFor(string.Empty);
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            MarkdownFixtures.Prompts.Select(prompt => prompt.File).Append(MarkdownFixtures.Workflow.File).Append(MarkdownFixtures.ZipName).Order(StringComparer.Ordinal),
            files);
    }

    [Theory]
    [MemberData(nameof(PromptFiles))]
    public async Task Each_prompt_file_imports_with_everything_it_holds(string file)
    {
        await using var library = await TestLibrary.CreateAsync();

        var result = await library.Transfer.ImportFilesAsync([MarkdownFixtures.PathFor(file)]);

        Assert.Empty(result.Failures);
        await AssertImportedAsync(library, MarkdownFixtures.Prompts.Single(prompt => prompt.File == file), Assert.Single(result.ImportedPromptIds));
    }

    [Fact]
    public async Task The_workflow_file_imports_with_its_steps_as_prompts()
    {
        var expected = MarkdownFixtures.Workflow;
        await using var library = await TestLibrary.CreateAsync();

        var result = await library.Transfer.ImportFilesAsync([MarkdownFixtures.PathFor(expected.File)]);

        // On its own, each step becomes a prompt with its title and text; the planner, used twice, becomes one.
        Assert.Empty(result.Failures);
        Assert.Equal(2, result.ImportedCount);
        var workflow = await library.WorkflowService.GetAsync(Assert.Single(result.ImportedWorkflowIds));
        Assert.NotNull(workflow);
        Assert.Equal((expected.Name, expected.Description), (workflow.Name, workflow.Description));
        Assert.Equal(expected.Steps.Select(step => step.Note), workflow.Steps.Select(step => step.Note));
        foreach (var (step, stored) in expected.Steps.Zip(workflow.Steps))
        {
            var prompt = await library.Prompts.GetAsync(stored.PromptId);
            Assert.NotNull(prompt);
            Assert.Equal((step.PromptTitle, MarkdownFixtures.Prompt(step.PromptTitle).Body), (prompt.Title, prompt.Body));
        }
    }

    [Fact]
    public async Task The_zip_imports_every_prompt_and_its_workflow_uses_them()
    {
        await using var library = await TestLibrary.CreateAsync();

        var result = await library.Transfer.ImportFilesAsync([MarkdownFixtures.PathFor(MarkdownFixtures.ZipName)]);

        Assert.Empty(result.Failures);
        Assert.Equal(MarkdownFixtures.Prompts.Count, result.ImportedCount);
        var byTitle = new Dictionary<string, Guid>();
        foreach (var id in result.ImportedPromptIds)
        {
            var title = (await library.Prompts.GetAsync(id))!.Title;
            await AssertImportedAsync(library, MarkdownFixtures.Prompt(title), id);
            byTitle[title] = id;
        }

        var workflow = await library.WorkflowService.GetAsync(Assert.Single(result.ImportedWorkflowIds));
        Assert.NotNull(workflow);
        Assert.Equal(MarkdownFixtures.Workflow.Steps.Select(step => byTitle[step.PromptTitle]), workflow.Steps.Select(step => step.PromptId));
    }

    /// <summary>
    /// Writes the files with this version's export and pins them. It's skipped unless
    /// <c>PROMPUFF_WRITE_MARKDOWN_FIXTURES</c> is set, and refuses to overwrite a pinned file, so it only ever ran for 1.0.
    /// A later release whose Markdown carries something new adds its own folder beside this one.
    /// </summary>
    [Fact]
    public async Task Write_fixtures()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROMPUFF_WRITE_MARKDOWN_FIXTURES")))
        {
            Assert.Skip("Set PROMPUFF_WRITE_MARKDOWN_FIXTURES to write the Markdown fixtures.");
        }

        var folder = MarkdownFixtures.SourceFolder;
        var files = MarkdownFixtures.Prompts.Select(prompt => prompt.File).Append(MarkdownFixtures.Workflow.File).Append(MarkdownFixtures.ZipName)
            .Select(file => Path.Combine(folder, file))
            .ToList();
        files.ForEach(PinnedFixtures.EnsureUnpinned);
        Directory.CreateDirectory(Path.Combine(folder, "workflows"));

        await using var library = await TestLibrary.CreateAsync();
        var ids = new Dictionary<string, Guid>();
        foreach (var fixture in MarkdownFixtures.Prompts)
        {
            Assert.Equal(fixture.File, library.Transfer.SuggestFileName(fixture.Title));
            Guid? collectionId = fixture.Collection is { } name ? (await library.CollectionService.GetOrCreateAsync(name)).Id : null;
            var prompt = await library.PromptService.CreateAsync(
                new PromptContent(fixture.Title, fixture.Description, fixture.Body, fixture.Notes),
                new PromptMetadata(fixture.IsFavorite, fixture.Rating, collectionId, fixture.Tags),
                "First version",
                fixture.CreatedAt,
                fixture.UpdatedAt);
            ids[fixture.Title] = prompt.Id;
            await library.Transfer.ExportPromptAsync(prompt.Id, Path.Combine(folder, fixture.File));
        }

        var expected = MarkdownFixtures.Workflow;
        Assert.Equal(Path.GetFileName(expected.File), library.Transfer.SuggestFileName(expected.Name));
        var workflow = await library.WorkflowService.CreateAsync(
            expected.Name, expected.Description, expected.Steps.Select(step => (ids[step.PromptTitle], step.Note)));
        await library.Transfer.ExportWorkflowAsync(workflow.Id, Path.Combine(folder, expected.File));
        await library.Transfer.ExportArchiveAsync([.. ids.Values], Path.Combine(folder, MarkdownFixtures.ZipName), [workflow.Id]);

        files.ForEach(PinnedFixtures.Pin);
    }

    private static async Task AssertImportedAsync(TestLibrary library, MarkdownFixturePrompt expected, Guid id)
    {
        var prompt = await library.Prompts.GetAsync(id);
        Assert.NotNull(prompt);
        Assert.Equal((expected.Title, expected.Description, expected.Body, expected.Notes), (prompt.Title, prompt.Description, prompt.Body, prompt.Notes));
        Assert.Equal((expected.IsFavorite, expected.Rating), (prompt.IsFavorite, prompt.Rating));
        Assert.Equal(expected.Tags, prompt.Tags);
        Assert.Equal(expected.Collection, prompt.CollectionId is { } collection ? (await library.Collections.GetAsync(collection))?.Name : null);
        Assert.Equal((expected.CreatedAt, expected.UpdatedAt), (prompt.CreatedAt, prompt.UpdatedAt));
        Assert.Equal("Imported from Markdown", Assert.Single(await library.Prompts.GetVersionsAsync(id)).Note);
    }
}
