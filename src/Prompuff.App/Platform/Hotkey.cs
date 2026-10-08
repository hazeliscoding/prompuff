using Avalonia.Input;

namespace Prompuff.App.Platform;

/// <summary>
/// A system-wide key combination, stored in settings as text such as <c>Ctrl+Alt+P</c>. <c>Meta</c> is ⌘ on macOS
/// and the Windows key elsewhere. It needs Ctrl, Alt or Meta, and a letter, digit, F1–F12 or Space, which every
/// platform's hotkey API can register.
/// </summary>
public sealed record Hotkey(KeyModifiers Modifiers, Key Key)
{
    public const string DefaultText = "Ctrl+Alt+P";

    public static Hotkey Default { get; } = new(KeyModifiers.Control | KeyModifiers.Alt, Key.P);

    /// <summary>Reads a stored hotkey. Returns null for empty text (turned off) or anything it can't use.</summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var modifiers = KeyModifiers.None;
        Key? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= KeyModifiers.Control;
                    break;
                case "alt" or "option":
                    modifiers |= KeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= KeyModifiers.Shift;
                    break;
                case "meta" or "cmd" or "win" or "super":
                    modifiers |= KeyModifiers.Meta;
                    break;
                default:
                    if (key is not null || !TryParseKey(part, out var parsed))
                    {
                        return null;
                    }

                    key = parsed;
                    break;
            }
        }

        var hotkey = key is { } value ? new Hotkey(modifiers, value) : null;
        return hotkey is { IsValid: true } ? hotkey : null;
    }

    /// <summary>A hotkey needs Ctrl, Alt or Meta, so it can't swallow ordinary typing.</summary>
    public bool IsValid =>
        (Modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != KeyModifiers.None && IsSupportedKey(Key);

    public static bool IsSupportedKey(Key key) =>
        key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.F1 and <= Key.F12 or Key.Space;

    /// <summary>True for keys that only modify, which a recorder waits past.</summary>
    public static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System;

    /// <summary>The name used in settings and on X11: "P", "7", "F5" or "Space".</summary>
    public string KeyName => KeyNameOf(Key);

    /// <summary>How it's stored: "Ctrl+Alt+P".</summary>
    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(KeyModifiers.Meta))
        {
            parts.Add("Meta");
        }

        parts.Add(KeyName);
        return string.Join('+', parts);
    }

    /// <summary>How it's shown: "Ctrl+Alt+P", "Win+Shift+P", or "⌃⌥P" on macOS.</summary>
    public string Display
    {
        get
        {
            if (OperatingSystem.IsMacOS())
            {
                return (Modifiers.HasFlag(KeyModifiers.Control) ? "⌃" : "")
                       + (Modifiers.HasFlag(KeyModifiers.Alt) ? "⌥" : "")
                       + (Modifiers.HasFlag(KeyModifiers.Shift) ? "⇧" : "")
                       + (Modifiers.HasFlag(KeyModifiers.Meta) ? "⌘" : "")
                       + KeyName;
            }

            var meta = OperatingSystem.IsWindows() ? "Win" : "Super";
            return ToString().Replace("Meta", meta, StringComparison.Ordinal);
        }
    }

    private static string KeyNameOf(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        Key.Space => "Space",
        _ => key.ToString(),
    };

    private static bool TryParseKey(string text, out Key key)
    {
        if (text.Length == 1 && char.IsAsciiDigit(text[0]))
        {
            key = Key.D0 + (text[0] - '0');
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out key) && IsSupportedKey(key);
    }
}
