using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Prompuff.App.ViewModels;
using Prompuff.Application.Settings;

namespace Prompuff.App.Tests;

public class AccessibilityTests
{
    private static string SampleWorkflow => Path.Combine(AppContext.BaseDirectory, "samples", "workflows", "angular-upgrade-start-to-finish.md");

    /// <summary>
    /// Every control a keyboard or screen reader user can reach on this screen, with what a screen reader would call it.
    /// A name that's empty or a type name ("Avalonia.Controls.StackPanel") is how they fail.
    /// </summary>
    private static readonly List<string> Names = [];

    private static List<string> Unnamed(AppHarness app, string screen)
    {
        var failures = new List<string>();
        foreach (var control in app.Window.GetVisualDescendants().OfType<Control>())
        {
            if (control is not (Button or TextBox or ComboBox or Slider or ListBox) || !control.IsEffectivelyVisible || control.Bounds.Width < 1)
            {
                continue;
            }

            if (control is TextBox { IsReadOnly: true } || control.FindAncestorOfType<ScrollBar>() is not null)
            {
                continue;
            }

            var name = ControlAutomationPeer.CreatePeerForElement(control).GetName();
            Names.Add($"{screen}: {control.GetType().Name}: {name}");
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("Avalonia.", StringComparison.Ordinal) || name.StartsWith("Prompuff.", StringComparison.Ordinal))
            {
                var where = control.Name is { Length: > 0 } id ? $"#{id}" : string.Join(" < ", control.GetVisualAncestors().OfType<Control>()
                    .Select(parent => parent.Name).Where(parentName => !string.IsNullOrEmpty(parentName)).Take(2));
                failures.Add($"{screen}: {control.GetType().Name} {(control.Classes.Count > 0 ? "." + string.Join(".", control.Classes) : string.Empty)} [{where}] named \"{name}\"");
            }
        }

        return failures;
    }

    [AvaloniaFact]
    public async Task Every_button_box_and_list_has_a_name_a_screen_reader_can_read()
    {
        var files = new FakeFilePicker { ImportPaths = [SampleWorkflow] };
        await using var app = await AppHarness.StartAsync(files: files);
        var failures = new List<string>();

        failures.AddRange(Unnamed(app, "library cards"));
        app.ViewModel.Library.IsListLayout = true;
        await app.SettleAsync();
        failures.AddRange(Unnamed(app, "library list"));
        app.ViewModel.Library.IsCardsLayout = true;

        await app.ViewModel.Library.Items.First().OpenCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var editor = Assert.IsType<PromptEditorViewModel>(app.ViewModel.CurrentPage);
        failures.AddRange(Unnamed(app, "editor"));
        foreach (var tab in new[] { EditorTab.Render, EditorTab.History })
        {
            editor.Tab = tab;
            await app.SettleAsync();
            failures.AddRange(Unnamed(app, $"editor {tab}"));
        }

        await app.ViewModel.Sidebar.OpenSettingsCommand.ExecuteAsync(null);
        foreach (var section in Enum.GetValues<SettingsSection>())
        {
            app.ViewModel.Settings.Select(section);
            await app.SettleAsync();
            failures.AddRange(Unnamed(app, $"settings {section}"));
        }

        await app.ViewModel.Settings.ImportCommand.ExecuteAsync(null);
        await app.SettleAsync();
        await app.ViewModel.Sidebar.OpenWorkflowsCommand.ExecuteAsync(null);
        await app.SettleAsync();
        failures.AddRange(Unnamed(app, "workflows"));
        await Assert.IsType<WorkflowsViewModel>(app.ViewModel.CurrentPage).Items.First().OpenCommand.ExecuteAsync(null);
        await app.SettleAsync();
        failures.AddRange(Unnamed(app, "workflow"));

        await app.ViewModel.QuickSave.OpenAsync();
        await app.SettleAsync();
        failures.AddRange(Unnamed(app, "quick save"));
        app.ViewModel.QuickSave.IsOpen = false;

        await app.ViewModel.Palette.OpenAsync();
        await app.SettleAsync();
        failures.AddRange(Unnamed(app, "command palette"));
        app.ViewModel.Palette.IsOpen = false;

        var asking = app.ViewModel.Dialogs.ConfirmAsync("Delete it?", "It moves to Recently deleted.", "Delete", isDanger: true);
        await app.SettleAsync();
        failures.AddRange(Unnamed(app, "dialog"));
        app.ViewModel.Dialogs.CancelCurrent();
        await asking;

        // With PROMPUFF_SCREENSHOTS set, every name is saved too, for reading through.
        if (Environment.GetEnvironmentVariable("PROMPUFF_SCREENSHOTS") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            File.WriteAllLines(Path.Combine(output, "accessible-names.txt"), Names.Distinct());
        }

        Assert.True(failures.Count == 0, $"{failures.Count} controls a screen reader can't name:\n" + string.Join("\n", failures.Distinct()));
    }
}
