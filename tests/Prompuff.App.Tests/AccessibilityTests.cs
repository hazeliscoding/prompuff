using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Prompuff.App.Controls;
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

        // A card is read by its prompt's title, with the description as help, rather than everything printed on it.
        Assert.Contains("library cards: Button: UI Mockup Generator", Names);
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

    /// <summary>The editor's rating takes the keyboard, shows focus, and reads as "Usefulness" with its word as the value.</summary>
    [AvaloniaFact]
    public async Task The_rating_takes_the_keyboard_and_reads_out_its_word()
    {
        await using var app = await AppHarness.StartAsync();
        await app.ViewModel.Library.Items.First().OpenCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var editor = Assert.IsType<PromptEditorViewModel>(app.ViewModel.CurrentPage);
        var pips = app.Window.GetVisualDescendants().OfType<RatingPips>().Single(pips => pips.IsInteractive);

        Assert.True(pips.Focusable);
        pips.Focus(NavigationMethod.Tab);
        await app.SettleAsync(30);
        Assert.Contains(":focus-visible", pips.Classes);
        Assert.NotNull(pips.FocusBrush);

        async Task Press(PhysicalKey key)
        {
            app.Window.KeyPressQwerty(key, RawInputModifiers.None);
            await app.SettleAsync(30);
        }

        await Press(PhysicalKey.Digit3);
        Assert.Equal(3, editor.Rating);
        await Press(PhysicalKey.ArrowRight);
        Assert.Equal(4, editor.Rating);
        app.Screenshot("editor-rating-keyboard");

        var peer = ControlAutomationPeer.CreatePeerForElement(pips);
        Assert.Equal("Usefulness", peer.GetName());
        Assert.Equal(AutomationControlType.Slider, peer.GetAutomationControlType());
        var value = Assert.IsAssignableFrom<IValueProvider>(peer);
        Assert.Equal("Great", value.Value);
        value.SetValue("Meh");
        Assert.Equal(1, editor.Rating);

        await Press(PhysicalKey.ArrowLeft);
        Assert.Null(editor.Rating);
        Assert.Equal("Unrated", value.Value);
        await Press(PhysicalKey.End);
        Assert.Equal(5, editor.Rating);
        await Press(PhysicalKey.Delete);
        Assert.Null(editor.Rating);
    }

    /// <summary>
    /// Toasts and Quick save's error are live regions. Windows tells screen readers about one when its name changes, and only
    /// for an element already in the window, so each one stays there and takes the message as its name.
    /// </summary>
    [AvaloniaFact]
    public async Task Toasts_and_quick_save_errors_are_announced()
    {
        await using var app = await AppHarness.StartAsync();
        Control Named(string name) => app.Window.GetVisualDescendants().OfType<Control>().Single(control => control.Name == name);

        var toast = Named("ToastAnnouncer");
        Assert.True(toast.IsEffectivelyVisible, "The toast's announcer is in the window before any toast");
        var toastPeer = ControlAutomationPeer.CreatePeerForElement(toast);
        Assert.Equal(AutomationLiveSetting.Polite, toastPeer.GetLiveSetting());
        var renamed = 0;
        toastPeer.PropertyChanged += (_, e) => renamed += e.Property == AutomationElementIdentifiers.NameProperty ? 1 : 0;

        app.ViewModel.Toasts.Show("Moved to Recently deleted.", "UI Mockup Generator", isHappy: false);
        await app.SettleAsync();
        Assert.Equal("Moved to Recently deleted. UI Mockup Generator", toastPeer.GetName());
        Assert.True(renamed > 0, "Changing the name raises the event screen readers listen for");

        await app.ViewModel.QuickSave.OpenAsync();
        await app.SettleAsync();
        app.ViewModel.QuickSave.Text = string.Empty;
        var error = Named("QuickSaveErrorAnnouncer");
        Assert.True(error.IsEffectivelyVisible, "Quick save's announcer is in the window before any error");
        var errorPeer = ControlAutomationPeer.CreatePeerForElement(error);
        Assert.Equal(AutomationLiveSetting.Assertive, errorPeer.GetLiveSetting());
        await app.ViewModel.QuickSave.SaveCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.Equal("Paste or type the prompt first.", errorPeer.GetName());
    }

    /// <summary>A heart is read as what it will do, so a screen reader can tell a favorite from one that isn't.</summary>
    [AvaloniaFact]
    public async Task A_heart_says_whether_its_prompt_is_a_favorite()
    {
        await using var app = await AppHarness.StartAsync();
        var id = app.ViewModel.Library.Items.First().Id;
        // A favorite change refreshes the library, which makes new cards, so look the card up each time.
        PromptCardViewModel Card() => app.ViewModel.Library.Items.Single(card => card.Id == id);
        string Heart() => ControlAutomationPeer.CreatePeerForElement(app.Window.GetVisualDescendants().OfType<Button>()
            .First(button => button.DataContext == Card() && button.Command == Card().ToggleFavoriteCommand)).GetName();

        var was = Card().IsFavorite;
        Assert.Equal(was ? "Remove from favorites" : "Add to favorites", Heart());
        await Card().ToggleFavoriteCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.Equal(was ? "Add to favorites" : "Remove from favorites", Heart());

        await Card().OpenCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var editor = Assert.IsType<PromptEditorViewModel>(app.ViewModel.CurrentPage);
        var heart = app.Window.GetVisualDescendants().OfType<Button>().First(button => button.Command == editor.ToggleFavoriteCommand);
        Assert.Equal(was ? "Add to favorites (Ctrl D)" : "Remove from favorites (Ctrl D)", ControlAutomationPeer.CreatePeerForElement(heart).GetName());
    }

    /// <summary>Show details says when the details are open, in words and with its chevron.</summary>
    [AvaloniaFact]
    public async Task Show_details_says_when_the_details_are_open()
    {
        await using var app = await AppHarness.StartAsync();
        var showing = app.ViewModel.Dialogs.ShowErrorAsync("Couldn't import that file.", "Your library is safe.", "System.IO.IOException: locked");
        await app.SettleAsync();
        var dialog = app.ViewModel.Dialogs.Current!;
        var toggle = app.Window.GetVisualDescendants().OfType<Button>().Single(button => button.Command == dialog.ToggleDetailsCommand);
        string Name() => ControlAutomationPeer.CreatePeerForElement(toggle).GetName();

        Assert.Equal("Show details", Name());
        dialog.ToggleDetailsCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal("Hide details", Name());
        Assert.Contains(toggle.GetVisualDescendants().OfType<Icon>(), icon => icon.IsEffectivelyVisible && icon.Kind == "ChevronDown");
        app.Screenshot("dialog-details-open");

        app.ViewModel.Dialogs.CancelCurrent();
        await showing;
    }
}
