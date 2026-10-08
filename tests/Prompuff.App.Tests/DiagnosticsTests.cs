using Avalonia.Headless.XUnit;
using Microsoft.Extensions.Logging;
using Prompuff.App.ViewModels;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;
using Prompuff.Domain.Entities;

namespace Prompuff.App.Tests;

public class DiagnosticsTests
{
    [AvaloniaFact]
    public async Task Copy_diagnostic_info_copies_a_report_with_no_prompt_text()
    {
        await using var app = await AppHarness.StartAsync();
        var transfer = app.Get<IPromptTransferService>();
        var workflowFile = Path.Combine(AppContext.BaseDirectory, "samples", "workflows", "angular-upgrade-start-to-finish.md");
        var workflowId = Assert.Single((await transfer.ImportFilesAsync([workflowFile])).ImportedWorkflowIds);
        var workflow = (await app.Get<WorkflowService>().GetAsync(workflowId))!;
        var prompts = new List<Prompt>();
        foreach (var summary in await app.Get<IPromptSearch>().SearchAsync(PromptQuery.All))
        {
            prompts.Add((await app.Get<IPromptRepository>().GetAsync(summary.Id))!);
        }

        // Everything from the samples that must never reach a bug report: six prompts, and two only the workflow has.
        Assert.Equal(8, prompts.Count);
        var titles = prompts.Select(prompt => prompt.Title).Append(workflow.Name).ToList();
        var texts = prompts.SelectMany(prompt => new[] { prompt.Description, prompt.Body, prompt.Notes })
            .Append(workflow.Description)
            .Concat(workflow.Steps.Select(step => step.Note));

        // Lines a careless change might one day log, written through the app's own file logger.
        var planner = prompts.Single(prompt => prompt.Title == "Angular Upgrade Planner");
        var leaky = app.Get<ILoggerFactory>().CreateLogger("Leaky");
        leaky.LogWarning("Import skipped a workflow: Step 1 (“{Title}”) has no prompt in a ```prompt block.", planner.Title);
        leaky.LogInformation("Opened {Title}", prompts.First(prompt => prompt != planner).Title);
        leaky.LogInformation("Ran {Workflow}", workflow.Name);
        leaky.LogError("Couldn't render {Body}", planner.Body);
        leaky.LogError(new InvalidOperationException(planner.Notes), "Something failed");
        leaky.LogWarning(new FileNotFoundException($"Could not find file '/home/puff/{transfer.SuggestFileName(prompts[^1].Title)}'."), "Import couldn't read a file");

        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = app.ViewModel.Settings;
        settings.Select(SettingsSection.About);
        await app.SettleAsync();
        await settings.CopyDiagnosticInfoCommand.ExecuteAsync(null);
        await app.SettleAsync();

        var report = await app.Get<IClipboardService>().GetTextAsync();
        Assert.NotNull(report);
        if (Environment.GetEnvironmentVariable("PROMPUFF_SCREENSHOTS") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "diagnostic-info.txt"), report);
        }

        foreach (var heading in new[] { "# Prompuff diagnostic info", "## App", "## Folders", "## Library", "## Settings", "## Recent log" })
        {
            Assert.Contains("\n" + heading + "\n", "\n" + report);
        }

        Assert.Contains("- Prompts: 8, and 0 in Recently deleted\n", report);
        Assert.Contains("- Workflows: 1\n", report);
        Assert.Contains($"- Data: {app.Folder}", report);
        Assert.Contains("Leaky: Import skipped a workflow: Step 1 (“…”) has no prompt in a ```prompt block.", report);
        Assert.Contains("Leaky: Something failed", report);
        Assert.Contains("lines that might hold prompt text.", report);

        foreach (var title in titles)
        {
            Assert.DoesNotContain(title, report, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var line in texts.SelectMany(text => (text ?? string.Empty).Split('\n')).Select(line => line.Trim()).Where(line => line.Length >= 20))
        {
            Assert.DoesNotContain(line, report);
        }

        Assert.Equal("Copied.", app.ViewModel.Toasts.Current?.Title);
        app.Screenshot("settings-about-diagnostics");
    }
}
