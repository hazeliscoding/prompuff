using Microsoft.Extensions.Logging;
using Prompuff.Application;
using Prompuff.Application.DTOs;
using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.ImportExport;
using Prompuff.Infrastructure.Logging;

namespace Prompuff.Infrastructure.Tests;

public class WorkflowTests
{
    private static PromptContent Content(string title, string body) => new(title, null, body, null);

    [Fact]
    public async Task A_workflow_keeps_its_steps_in_order_with_their_notes()
    {
        await using var library = await TestLibrary.CreateAsync();
        var plan = await library.PromptService.CreateAsync(Content("Plan", "Plan {{repo}}."));
        var apply = await library.PromptService.CreateAsync(Content("Apply", "Apply {{plan}} to {{repo}}."));

        var workflow = await library.WorkflowService.CreateAsync("  Ship   it ", "Plan, then apply.", [(plan.Id, "The plan"), (apply.Id, null)]);
        var stored = await library.WorkflowService.GetAsync(workflow.Id);

        Assert.NotNull(stored);
        Assert.Equal("Ship it", stored.Name);
        Assert.Equal("Plan, then apply.", stored.Description);
        Assert.Equal([plan.Id, apply.Id], stored.Steps.Select(step => step.PromptId));
        Assert.Equal(["The plan", null], stored.Steps.Select(step => step.Note));

        await library.WorkflowService.MoveStepAsync(workflow.Id, stored.Steps[1].Id, -1);
        await library.WorkflowService.SetStepNoteAsync(workflow.Id, stored.Steps[0].Id, "  Hands back the plan\n");
        var moved = await library.WorkflowService.GetAsync(workflow.Id);
        Assert.Equal([apply.Id, plan.Id], moved!.Steps.Select(step => step.PromptId));
        Assert.Equal("Hands back the plan", moved.Steps[1].Note);

        var summary = Assert.Single(await library.WorkflowService.ListAsync());
        Assert.Equal(["Apply", "Plan"], summary.StepTitles);
    }

    [Fact]
    public async Task Removing_a_prompt_for_good_removes_its_steps_but_not_the_workflow()
    {
        await using var library = await TestLibrary.CreateAsync();
        var keep = await library.PromptService.CreateAsync(Content("Keep", "keep"));
        var gone = await library.PromptService.CreateAsync(Content("Gone", "gone"));
        var workflow = await library.WorkflowService.CreateAsync("Flow", steps: [(keep.Id, null), (gone.Id, null)]);
        await library.WorkflowService.SaveValuesAsync(workflow.Id, new Dictionary<string, string> { ["repo"] = "acme", ["empty"] = "" });

        await library.PromptService.DeleteAsync(gone.Id);
        Assert.Equal(2, (await library.WorkflowService.GetAsync(workflow.Id))!.Steps.Count);
        await library.PromptService.EmptyRecentlyDeletedAsync();

        Assert.Equal([keep.Id], (await library.WorkflowService.GetAsync(workflow.Id))!.Steps.Select(step => step.PromptId));
        Assert.Equal(new Dictionary<string, string> { ["repo"] = "acme" }, await library.WorkflowService.LoadValuesAsync(workflow.Id));

        await library.WorkflowService.DeleteAsync(workflow.Id);
        Assert.Null(await library.WorkflowService.GetAsync(workflow.Id));
        Assert.Empty(await library.WorkflowService.LoadValuesAsync(workflow.Id));
        Assert.NotNull(await library.Prompts.GetAsync(keep.Id));
    }

    [Fact]
    public async Task A_step_needs_a_prompt_that_exists()
    {
        await using var library = await TestLibrary.CreateAsync();

        await Assert.ThrowsAsync<LibraryException>(() => library.WorkflowService.CreateAsync("Flow", steps: [(Guid.NewGuid(), null)]));
    }

    [Fact]
    public void Variables_shared_by_several_steps_are_listed_once()
    {
        var service = new Application.Services.WorkflowService(null!, new Application.Services.PromptTemplateService(), TimeProvider.System, null!);

        var variables = service.CollectVariables(["Plan {{repo}} for {{version}}.", "Apply {{plan}} to {{repo}}.", "Check {{repo}} at {{version}}, {{repo}}."]);

        Assert.Equal(
            [new WorkflowVariable("repo", [1, 2, 3]), new WorkflowVariable("version", [1, 3]), new WorkflowVariable("plan", [2])],
            variables,
            (a, b) => a.Name == b.Name && a.Steps.SequenceEqual(b.Steps));
    }

    [Fact]
    public void The_Markdown_format_survives_prompts_with_code_blocks_headings_and_quotes()
    {
        var body = "Review this:\n\n```csharp\nvar x = 1;\n```\n\n## Step 9: not a step\n\n> not a note\n\n~~~\nstill the body\n~~~";
        var workflow = new MarkdownWorkflow("Review: start to finish", "Look, then fix.", [
            new MarkdownWorkflowStep("Reviewer", body, "The review.\n\nAs a list."),
            new MarkdownWorkflowStep("Fixer", "Fix {{review}}.", null),
        ]);

        var text = MarkdownWorkflowFormat.Write(workflow);
        Assert.Contains("````prompt", text);
        Assert.True(MarkdownWorkflowFormat.IsWorkflow(text));
        var read = MarkdownWorkflowFormat.Read(text, "fallback");

        Assert.Equal("Review: start to finish", read.Title);
        Assert.Equal("Look, then fix.", read.Description);
        Assert.Equal(2, read.Steps.Count);
        Assert.Equal(body, read.Steps[0].Body);
        Assert.Equal("The review.\n\nAs a list.", read.Steps[0].Note);
        Assert.Equal(new MarkdownWorkflowStep("Fixer", "Fix {{review}}.", null), read.Steps[1]);
    }

    [Theory]
    [InlineData("---\ntype: workflow\ntitle: Empty\n---\n\n# Empty\n", "no steps")]
    [InlineData("---\ntype: workflow\n---\n\n## Step 1: Lost\n\nNo fence here.\n", "has no prompt")]
    [InlineData("---\ntype: workflow\n---\n\n## Step 1: Open\n\n```prompt\nnever closed\n", "never closed")]
    public void Broken_workflow_documents_say_what_is_wrong(string text, string message)
    {
        var error = Assert.Throws<MarkdownFormatException>(() => MarkdownWorkflowFormat.Read(text, "fallback"));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public async Task A_workflow_that_can_not_be_imported_keeps_its_step_titles_out_of_the_log()
    {
        await using var library = await TestLibrary.CreateAsync();
        var logs = Path.Combine(library.Folder, "logs");
        var provider = new FileLoggerProvider(logs);
        using (var factory = new LoggerFactory([provider]))
        {
            var transfer = new MarkdownTransferService(
                library.PromptService, library.CollectionService, library.Collections, library.WorkflowService, factory.CreateLogger<MarkdownTransferService>());
            var file = library.TempFile("lost.md");
            await File.WriteAllTextAsync(file, "---\ntype: workflow\n---\n\n## Step 1: Secret Planner\n\nNo fence here.\n");

            var result = await transfer.ImportFilesAsync([file]);

            // The person importing still sees which step it was.
            Assert.Contains("Secret Planner", Assert.Single(result.Failures).Reason);
        }

        provider.Dispose();
        var log = string.Concat(Directory.GetFiles(logs).Select(File.ReadAllText));
        Assert.Contains("Import skipped a workflow", log);
        Assert.DoesNotContain("Secret Planner", log);
    }

    [Fact]
    public void Long_crafted_lines_read_in_linear_time()
    {
        var padding = new string(' ', 200_000);
        var text = "---\ntype: workflow\n---\n\n## Step 1: " + padding + "x" + padding + "!\n\n```" + padding + "a" + padding + "b\n\n```prompt\nbody\n```\n\n## " + padding + "y" + padding + "\n\n```prompt\nsecond\n```\n";

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var read = MarkdownWorkflowFormat.Read(text, "fallback");

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Reading took {watch.Elapsed}.");
        Assert.Equal(2, read.Steps.Count);
        Assert.StartsWith("x", read.Steps[0].Title);
        Assert.Equal("y", read.Steps[1].Title);
    }

    [Fact]
    public void A_prompt_file_is_not_a_workflow() =>
        Assert.False(MarkdownWorkflowFormat.IsWorkflow("---\ntitle: Just a prompt\n---\n\n# Prompt\n\nHello"));

    [Fact]
    public async Task A_library_zip_carries_workflows_and_their_prompts_keep_their_details()
    {
        await using var library = await TestLibrary.CreateAsync();
        var coding = await library.CollectionService.CreateAsync("Coding");
        var plan = await library.PromptService.CreateAsync(
            new PromptContent("Plan", "Plans the work", "Plan {{repo}}.", "Small steps work."),
            new Application.Services.PromptMetadata(true, 5, coding.Id, ["planning"]));
        var apply = await library.PromptService.CreateAsync(Content("Apply", "Apply {{plan}}."));
        var first = await library.WorkflowService.CreateAsync("Ship it", steps: [(plan.Id, "The plan"), (apply.Id, null)]);
        var second = await library.WorkflowService.CreateAsync("Ship it", steps: [(apply.Id, null)]);
        var zip = library.TempFile("everything.zip");

        var exported = await library.Transfer.ExportArchiveAsync([plan.Id, apply.Id], zip, [first.Id, second.Id]);

        Assert.Equal(new ExportResult(2, zip, 2), exported);
        using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
        {
            Assert.Equal(["apply.md", "plan.md", "workflows/ship-it-2.md", "workflows/ship-it.md"], archive.Entries.Select(entry => entry.FullName).Order());
        }

        await using var other = await TestLibrary.CreateAsync();
        var result = await other.Transfer.ImportFilesAsync([zip]);

        Assert.Empty(result.Failures);
        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(2, result.ImportedWorkflowIds.Count);
        var importedPlan = await other.Prompts.GetAsync((await other.PromptService.FindAsync("Plan", "Plan {{repo}}."))!.Value);
        Assert.Equal("Small steps work.", importedPlan!.Notes);
        Assert.Equal(["planning"], importedPlan.Tags);
        Assert.True(importedPlan.IsFavorite);
        var workflows = new List<Domain.Entities.Workflow>();
        foreach (var id in result.ImportedWorkflowIds)
        {
            workflows.Add((await other.WorkflowService.GetAsync(id))!);
        }

        var twoSteps = Assert.Single(workflows, workflow => workflow.Steps.Count == 2);
        Assert.Equal("Ship it", twoSteps.Name);
        Assert.Equal(importedPlan.Id, twoSteps.Steps[0].PromptId);
    }

    [Fact]
    public async Task A_workflow_read_before_its_prompts_still_uses_them()
    {
        await using var library = await TestLibrary.CreateAsync();
        var zip = library.TempFile("workflow-first.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        {
            var workflow = MarkdownWorkflowFormat.Write(new MarkdownWorkflow("Flow", null, [new MarkdownWorkflowStep("Plan", "Plan {{repo}}.", null)]));
            await using (var writer = new StreamWriter(archive.CreateEntry("a-flow.md").Open()))
            {
                await writer.WriteAsync(workflow);
            }

            await using (var writer = new StreamWriter(archive.CreateEntry("plan.md").Open()))
            {
                await writer.WriteAsync("---\ntitle: Plan\ntags: [planning]\n---\n\n# Prompt\n\nPlan {{repo}}.\n\n# Notes\n\nKeep it short.\n");
            }
        }

        var result = await library.Transfer.ImportFilesAsync([zip]);

        Assert.Equal(1, result.ImportedCount);
        var prompt = await library.Prompts.GetAsync(Assert.Single(result.ImportedPromptIds));
        Assert.Equal(["planning"], prompt!.Tags);
        Assert.Equal("Keep it short.", prompt.Notes);
        var workflowSteps = (await library.WorkflowService.GetAsync(Assert.Single(result.ImportedWorkflowIds)))!.Steps;
        Assert.Equal(prompt.Id, Assert.Single(workflowSteps).PromptId);
    }

    [Fact]
    public async Task A_folder_imports_its_prompts_before_its_workflows()
    {
        await using var library = await TestLibrary.CreateAsync();
        var folder = Directory.CreateDirectory(Path.Combine(library.Folder, "export")).FullName;
        var workflow = MarkdownWorkflowFormat.Write(new MarkdownWorkflow("Flow", null, [new MarkdownWorkflowStep("Plan", "Plan {{repo}}.", null)]));
        await File.WriteAllTextAsync(Path.Combine(folder, "a-flow.md"), workflow);
        await File.WriteAllTextAsync(Path.Combine(folder, "plan.md"), "---\ntitle: Plan\ntags: [planning]\n---\n\n# Prompt\n\nPlan {{repo}}.\n");

        var result = await library.Transfer.ImportFolderAsync(folder);

        var prompt = await library.Prompts.GetAsync(Assert.Single(result.ImportedPromptIds));
        Assert.Equal(["planning"], prompt!.Tags);
        var steps = (await library.WorkflowService.GetAsync(Assert.Single(result.ImportedWorkflowIds)))!.Steps;
        Assert.Equal(prompt.Id, Assert.Single(steps).PromptId);
    }

    [Fact]
    public async Task Export_then_import_rebuilds_the_workflow_and_reuses_prompts_already_there()
    {
        await using var library = await TestLibrary.CreateAsync();
        var plan = await library.PromptService.CreateAsync(Content("Plan", "Plan {{repo}}."));
        var apply = await library.PromptService.CreateAsync(Content("Apply", "Apply {{plan}} to {{repo}}."));
        var workflow = await library.WorkflowService.CreateAsync("Ship it", "Plan, then apply.", [(plan.Id, "The plan, into {{plan}}"), (apply.Id, null)]);
        var file = library.TempFile("ship-it.md");
        await library.Transfer.ExportWorkflowAsync(workflow.Id, file);

        // Into the same library: the workflow is already there.
        var again = await library.Transfer.ImportFilesAsync([file]);
        Assert.Empty(again.ImportedWorkflowIds);
        Assert.Equal(1, again.SkippedCount);

        // Into a library that has only the first prompt: it's reused, and the second one is created.
        await using var other = await TestLibrary.CreateAsync();
        var existing = await other.PromptService.CreateAsync(Content("Plan", "Plan {{repo}}."));
        var result = await other.Transfer.ImportFilesAsync([file]);

        Assert.Empty(result.Failures);
        var imported = await other.WorkflowService.GetAsync(Assert.Single(result.ImportedWorkflowIds));
        Assert.Equal("Ship it", imported!.Name);
        Assert.Equal("Plan, then apply.", imported.Description);
        Assert.Equal(existing.Id, imported.Steps[0].PromptId);
        Assert.Equal("The plan, into {{plan}}", imported.Steps[0].Note);
        var created = await other.Prompts.GetAsync(Assert.Single(result.ImportedPromptIds));
        Assert.Equal("Apply", created!.Title);
        Assert.Equal(created.Id, imported.Steps[1].PromptId);
    }
}
