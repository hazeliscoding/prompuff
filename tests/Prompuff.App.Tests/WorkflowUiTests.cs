using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Prompuff.App.ViewModels;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;
using Prompuff.Domain.ValueObjects;

namespace Prompuff.App.Tests;

public class WorkflowUiTests
{
    private static string SampleWorkflow => Path.Combine(AppContext.BaseDirectory, "samples", "workflows", "angular-upgrade-start-to-finish.md");

    [AvaloniaFact]
    public async Task The_Angular_upgrade_workflow_is_built_filled_once_and_copied_step_by_step()
    {
        await using var app = await AppHarness.StartAsync();
        var prompts = app.Get<PromptService>();
        await prompts.CreateAsync(new PromptContent("Angular Upgrade Phase Runner", null,
            "Upgrade {{repo_name}} to Angular {{target_version}} with {{package_manager}}. The plan:\n\n{{upgrade_plan}}\n\nCarry out phase {{phase}} only.", null));
        await prompts.CreateAsync(new PromptContent("Angular Upgrade Checker", null,
            "Check the Angular {{target_version}} upgrade of {{repo_name}} after this phase:\n\n{{phase_report}}", null));

        // Build it.
        await app.ViewModel.Sidebar.OpenWorkflowsCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var list = Assert.IsType<WorkflowsViewModel>(app.ViewModel.CurrentPage);
        Assert.True(list.IsEmpty);
        Assert.True(app.ViewModel.Sidebar.IsWorkflowsActive);
        app.Screenshot("workflows-empty");

        var creating = list.NewWorkflowCommand.ExecuteAsync(null);
        await app.SettleAsync();
        app.ViewModel.Dialogs.Current!.InputText = "Angular upgrade, start to finish";
        app.ViewModel.Dialogs.Current.ConfirmCommand.Execute(null);
        await creating;
        await app.SettleAsync();
        var workflow = Assert.IsType<WorkflowViewModel>(app.ViewModel.CurrentPage);
        Assert.Equal("Angular upgrade, start to finish", workflow.Name);
        Assert.False(workflow.HasSteps);

        foreach (var title in new[] { "Angular Upgrade Planner", "Angular Upgrade Phase Runner", "Angular Upgrade Checker" })
        {
            workflow.PickerQuery = title;
            await workflow.PickerResults.First(pick => pick.Title == title).AddCommand.ExecuteAsync(null);
        }

        workflow.Description = "Plan the upgrade, carry it out one phase at a time, then check it.";
        workflow.Steps[0].Note = "The phased plan, into {{upgrade_plan}}.";
        workflow.Steps[1].Note = "The commits and open decisions, into {{phase_report}}.";
        Assert.True(await workflow.FlushAsync());
        await app.SettleAsync();
        Assert.Equal(["Angular Upgrade Planner", "Angular Upgrade Phase Runner", "Angular Upgrade Checker"], workflow.Steps.Select(step => step.Title));
        app.Screenshot("workflow-steps");

        // Fill in once: the shared variables appear a single time, marked with the steps that use them.
        workflow.Tab = WorkflowTab.Run;
        await app.SettleAsync();
        Assert.Equal(["repo_name", "target_version", "package_manager", "tone", "upgrade_plan", "phase", "phase_report"], workflow.Variables.Select(variable => variable.Name));
        var repo = workflow.Variables.Single(variable => variable.Name == "repo_name");
        Assert.Equal("Steps 1, 2 and 3", repo.UsedIn);
        repo.Value = "acme-web";
        workflow.Variables.Single(variable => variable.Name == "target_version").Value = "22";
        workflow.Variables.Single(variable => variable.Name == "package_manager").Value = "pnpm";
        workflow.Variables.Single(variable => variable.Name == "tone").Value = "direct";
        await app.SettleAsync();
        Assert.Equal("Step 1 of 3 · 0 copied", workflow.ProgressLabel);
        Assert.Equal("4 of 4 filled", workflow.FillLabel);
        app.Screenshot("workflow-run-step-1");

        // Copy step by step.
        var clipboard = app.Get<IClipboardService>();
        await workflow.CopyStepCommand.ExecuteAsync(null);
        Assert.StartsWith("You are a senior Angular engineer planning an upgrade of acme-web to Angular 22.", await clipboard.GetTextAsync());
        Assert.Equal(2, workflow.CurrentStep!.Number);
        Assert.True(workflow.Steps[0].IsCopied);
        Assert.Equal("Hands off: The phased plan, into {{upgrade_plan}}.", app.ViewModel.Toasts.Current?.Subtitle);

        workflow.Variables.Single(variable => variable.Name == "upgrade_plan").Value = "1. Pre-flight\n2. Bump to 21\n3. Bump to 22";
        workflow.Variables.Single(variable => variable.Name == "phase").Value = "1";
        await app.SettleAsync();
        app.Screenshot("workflow-run-step-2");
        await workflow.CopyStepCommand.ExecuteAsync(null);
        var second = await clipboard.GetTextAsync();
        Assert.Contains("Upgrade acme-web to Angular 22 with pnpm.", second);
        Assert.Contains("2. Bump to 21", second);

        // Ctrl+Enter copies the last step.
        workflow.Variables.Single(variable => variable.Name == "phase_report").Value = "Bumped tooling; kept zone.js.";
        await app.SettleAsync();
        var command = Platform.Shortcuts.CommandModifier == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;
        app.Window.KeyPressQwerty(PhysicalKey.Enter, command);
        await app.SettleAsync();
        Assert.Equal("Check the Angular 22 upgrade of acme-web after this phase:\n\nBumped tooling; kept zone.js.", await clipboard.GetTextAsync());
        Assert.Equal("Step 3 of 3 · 3 copied", workflow.ProgressLabel);
        app.Screenshot("workflow-run-done");

        // The values are remembered for next time.
        await app.ViewModel.Sidebar.OpenWorkflowsCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.Equal("3 steps", list.Items.Single().StepCountLabel);
        app.Screenshot("workflows-list");
        await list.Items.Single().OpenCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var reopened = Assert.IsType<WorkflowViewModel>(app.ViewModel.CurrentPage);
        Assert.Equal("acme-web", reopened.Variables.Single(variable => variable.Name == "repo_name").Value);
        Assert.Equal("The phased plan, into {{upgrade_plan}}.", reopened.Steps[0].Note);
    }

    [AvaloniaFact]
    public async Task The_sample_workflow_imports_reusing_the_planner_and_exports_again()
    {
        var files = new FakeFilePicker { ImportPaths = [SampleWorkflow] };
        await using var app = await AppHarness.StartAsync(files: files);

        await app.ViewModel.Sidebar.OpenSettingsCommand.ExecuteAsync(null);
        await app.ViewModel.Settings.ImportCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.Equal(8, app.ViewModel.Sidebar.All.Count);
        Assert.Equal(1, app.ViewModel.Sidebar.WorkflowCount);
        Assert.Equal("1 workflow and 2 prompts added to your library.", app.ViewModel.Toasts.Current?.Subtitle);

        await app.ViewModel.Sidebar.OpenWorkflowsCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var list = Assert.IsType<WorkflowsViewModel>(app.ViewModel.CurrentPage);
        await list.Items.Single().OpenCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var workflow = Assert.IsType<WorkflowViewModel>(app.ViewModel.CurrentPage);
        Assert.Equal("Angular upgrade, start to finish", workflow.Name);
        Assert.Equal(["Angular Upgrade Planner", "Angular Upgrade Phase Runner", "Angular Upgrade Checker"], workflow.Steps.Select(step => step.Title));
        Assert.StartsWith("the phased plan", workflow.Steps[0].Note);

        // The planner is the library's own prompt, not a copy.
        var planner = app.ViewModel.Library.Items.Count == 0
            ? null
            : (await app.Get<IPromptSearch>().SearchAsync(new Application.DTOs.PromptQuery { Text = "Angular Upgrade Planner" })).Single(p => p.Title == "Angular Upgrade Planner");
        Assert.Equal(planner?.Id, workflow.Steps[0].PromptId);

        files.ExportPath = Path.Combine(app.Folder, "angular-upgrade.md");
        await workflow.ExportCommand.ExecuteAsync(null);
        var exported = await File.ReadAllTextAsync(files.ExportPath);
        Assert.StartsWith("---\ntype: workflow\ntitle: Angular upgrade, start to finish\n", exported);
        Assert.Contains("## Step 3: Angular Upgrade Checker", exported);

        // Importing it again finds nothing new.
        files.ImportPaths = [files.ExportPath];
        await app.ViewModel.Settings.ImportCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.Equal(1, app.ViewModel.Sidebar.WorkflowCount);
        Assert.Equal(8, app.ViewModel.Sidebar.All.Count);
    }

    [AvaloniaFact]
    public async Task A_note_typed_just_before_moving_a_step_is_kept()
    {
        await using var app = await AppHarness.StartAsync();
        var items = app.ViewModel.Library.Items;
        var workflow = await app.Get<WorkflowService>().CreateAsync("Two steps", steps: [(items[0].Id, null), (items[1].Id, null)]);
        await app.Get<Navigator>().OpenWorkflowAsync(workflow.Id);
        await app.SettleAsync();
        var page = Assert.IsType<WorkflowViewModel>(app.ViewModel.CurrentPage);

        page.Steps[0].Note = "Typed a moment ago";
        await page.Steps[0].MoveDownCommand.ExecuteAsync(null);
        await app.SettleAsync(800);

        Assert.Equal([items[1].Id, items[0].Id], page.Steps.Select(step => step.PromptId));
        Assert.Equal("Typed a moment ago", page.Steps[1].Note);
        Assert.Equal("Typed a moment ago", (await app.Get<WorkflowService>().GetAsync(workflow.Id))!.Steps[1].Note);
    }

    [AvaloniaFact]
    public async Task The_sample_workflow_renders_in_the_light_theme()
    {
        var files = new FakeFilePicker { ImportPaths = [SampleWorkflow] };
        await using var app = await AppHarness.StartAsync(theme: Application.Settings.ThemePreference.Light, files: files);
        await app.ViewModel.Settings.ImportCommand.ExecuteAsync(null);
        await app.ViewModel.Sidebar.OpenWorkflowsCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var list = Assert.IsType<WorkflowsViewModel>(app.ViewModel.CurrentPage);
        app.Screenshot("workflows-list-light");

        await list.Items.Single().OpenCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var workflow = Assert.IsType<WorkflowViewModel>(app.ViewModel.CurrentPage);
        app.Screenshot("workflow-steps-light");
        workflow.Tab = WorkflowTab.Run;
        workflow.Variables[0].Value = "acme-web";
        await workflow.CopyStepCommand.ExecuteAsync(null);
        await app.SettleAsync();
        app.Screenshot("workflow-run-light");
        Assert.Equal(2, workflow.CurrentStep?.Number);
    }

    [AvaloniaFact]
    public async Task Deleting_a_workflow_keeps_its_prompts()
    {
        await using var app = await AppHarness.StartAsync();
        var planner = app.ViewModel.Library.Items.Single(item => item.Title == "Angular Upgrade Planner");
        var workflow = await app.Get<WorkflowService>().CreateAsync("Just planning", steps: [(planner.Id, null)]);
        await app.Get<Navigator>().OpenWorkflowAsync(workflow.Id);
        await app.SettleAsync();
        var page = Assert.IsType<WorkflowViewModel>(app.ViewModel.CurrentPage);

        var deleting = page.DeleteCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.Equal("Delete “Just planning”?", app.ViewModel.Dialogs.Current?.Title);
        app.ViewModel.Dialogs.Current!.ConfirmCommand.Execute(null);
        await deleting;
        await app.SettleAsync();

        Assert.IsType<WorkflowsViewModel>(app.ViewModel.CurrentPage);
        Assert.Equal(0, app.ViewModel.Sidebar.WorkflowCount);
        Assert.Equal(6, app.ViewModel.Sidebar.All.Count);
    }

    [AvaloniaFact]
    public async Task The_palette_finds_workflows_and_Esc_goes_back_to_the_list()
    {
        await using var app = await AppHarness.StartAsync();
        var planner = app.ViewModel.Library.Items.Single(item => item.Title == "Angular Upgrade Planner");
        await app.Get<WorkflowService>().CreateAsync("Angular upgrade, start to finish", steps: [(planner.Id, null)]);

        await app.ViewModel.Palette.OpenAsync();
        app.ViewModel.Palette.Query = "start to finish";
        await app.SettleAsync();
        var item = app.ViewModel.Palette.Items.First(entry => entry.Group == "Workflows");
        await app.ViewModel.Palette.RunCommand.ExecuteAsync(item);
        await app.SettleAsync();
        Assert.IsType<WorkflowViewModel>(app.ViewModel.CurrentPage);

        app.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await app.SettleAsync();
        Assert.IsType<WorkflowsViewModel>(app.ViewModel.CurrentPage);
    }
}
