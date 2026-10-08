using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Prompuff.App.ViewModels;

namespace Prompuff.App.Platform;

/// <summary>
/// The macOS application menu: About and Settings on top. macOS adds Services, Hide and Quit (Cmd+Q) below them.
/// </summary>
internal static class MacAppMenu
{
    public static void Install(Avalonia.Application application, MainWindowViewModel viewModel)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        NativeMenu.SetMenu(application, new NativeMenu
        {
            new NativeMenuItem("About Prompuff") { Command = viewModel.OpenAboutCommand },
            new NativeMenuItemSeparator(),
            new NativeMenuItem("Settings…")
            {
                Command = viewModel.OpenSettingsCommand,
                Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta),
            },
        });
    }
}
