using Prompuff.Application;
using Prompuff.Application.DTOs;
using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.ImportExport;

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
    public void A_prompt_file_is_not_a_workflow() =>
        Assert.False(MarkdownWorkflowFormat.IsWorkflow("---\ntitle: Just a prompt\n---\n\n# Prompt\n\nHello"));

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
