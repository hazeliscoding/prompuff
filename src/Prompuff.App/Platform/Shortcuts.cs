using Avalonia.Input;

namespace Prompuff.App.Platform;

public enum ShortcutAction
{
    NewPrompt,
    Save,
    FocusSearch,
    CommandPalette,
    QuickSave,
    RenderOrCopy,
    CopyPrompt,
    ToggleFavorite,
    ShowHistory,
    Close,
}

public sealed record Shortcut(ShortcutAction Action, string Label, Key Key, bool Command = true, bool Shift = false)
{
    public KeyModifiers Modifiers =>
        (Command ? Shortcuts.CommandModifier : KeyModifiers.None) | (Shift ? KeyModifiers.Shift : KeyModifiers.None);

    public string Display => Shortcuts.Format(this);
}

/// <summary>
/// Every keyboard shortcut in one table. Shortcuts use the platform's command modifier (Ctrl on Windows and
/// Linux), so a macOS build gets Cmd without touching the bindings.
/// </summary>
public static class Shortcuts
{
    public static KeyModifiers CommandModifier { get; } = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    public static IReadOnlyList<Shortcut> All { get; } =
    [
        new(ShortcutAction.CommandPalette, "Command palette", Key.K),
        new(ShortcutAction.CommandPalette, "Command palette", Key.P, Shift: true),
        new(ShortcutAction.QuickSave, "Quick save from clipboard", Key.S, Shift: true),
        new(ShortcutAction.NewPrompt, "New prompt", Key.N),
        new(ShortcutAction.Save, "Save", Key.S),
        new(ShortcutAction.FocusSearch, "Search", Key.F),
        new(ShortcutAction.RenderOrCopy, "Render, then copy the rendered prompt", Key.Enter),
        new(ShortcutAction.CopyPrompt, "Copy the prompt template", Key.C, Shift: true),
        new(ShortcutAction.ToggleFavorite, "Toggle favorite", Key.D),
        new(ShortcutAction.ShowHistory, "Open history", Key.H),
        new(ShortcutAction.Close, "Close a dialog or the palette", Key.Escape, Command: false),
    ];

    /// <summary>Shortcuts as shown in Settings: one row per action.</summary>
    public static IReadOnlyList<(string Label, string Keys)> Reference { get; } = All
        .GroupBy(shortcut => shortcut.Action)
        .Select(group => (group.First().Label, string.Join("  or  ", group.Select(shortcut => shortcut.Display))))
        .ToList();

    public static ShortcutAction? Match(KeyEventArgs e)
    {
        var modifiers = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Shift | KeyModifiers.Alt);
        foreach (var shortcut in All)
        {
            if (shortcut.Key == e.Key && shortcut.Modifiers == modifiers)
            {
                return shortcut.Action;
            }
        }

        return null;
    }

    public static string Display(ShortcutAction action) => All.First(shortcut => shortcut.Action == action).Display;

    public static string Format(Shortcut shortcut)
    {
        var mac = OperatingSystem.IsMacOS();
        var parts = new List<string>();
        if (shortcut.Command)
        {
            parts.Add(mac ? "⌘" : "Ctrl");
        }

        if (shortcut.Shift)
        {
            parts.Add(mac ? "⇧" : "Shift");
        }

        parts.Add(shortcut.Key switch
        {
            Key.Enter => mac ? "↩" : "Enter",
            Key.Escape => "Esc",
            var key => key.ToString(),
        });
        return string.Join(mac ? string.Empty : " ", parts);
    }
}
