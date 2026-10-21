namespace Prompuff.App.Platform;

/// <summary>
/// The app Quick insert was opened from. It's remembered before Prompuff comes forward, gets focus back once the
/// result is copied, and can have its paste key pressed. Wayland lets apps do neither, so there the result is only
/// copied.
/// </summary>
public interface IInsertTarget
{
    /// <summary>False where Prompuff can't hand focus to another app or press keys in it, such as under Wayland.</summary>
    bool IsSupported { get; }

    /// <summary>True when Prompuff may press keys in other apps. macOS asks for Accessibility access first.</summary>
    bool CanPaste { get; }

    /// <summary>
    /// Remembers the app in front. Called before Prompuff comes forward. When Prompuff itself is in front, the app
    /// remembered before stays.
    /// </summary>
    void Remember(IntPtr ownWindow);

    /// <summary>Gives focus back to the remembered app, once the keys pressed in Prompuff are let go. True once it has it.</summary>
    Task<bool> ReturnAsync();

    /// <summary>Presses the paste key in the app that has focus.</summary>
    Task<bool> PasteAsync();

    /// <summary>macOS shows its Accessibility prompt. Nothing to ask for elsewhere.</summary>
    void RequestPasteAccess();
}

public static class InsertTargets
{
    /// <summary>The implementation for this platform and session.</summary>
    public static IInsertTarget Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsInsertTarget();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacInsertTarget();
        }

        if (OperatingSystem.IsLinux() && !GlobalHotkeys.IsWaylandSession() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            return new X11InsertTarget();
        }

        return new UnsupportedInsertTarget();
    }

    /// <summary>Checks a condition every few milliseconds until it holds or the time runs out.</summary>
    internal static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 1000)
    {
        for (var waited = 0; waited < timeoutMilliseconds; waited += 15)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(15);
        }

        return condition();
    }
}

/// <summary>Where Prompuff can't reach other apps, such as under Wayland: Quick insert only copies.</summary>
public sealed class UnsupportedInsertTarget : IInsertTarget
{
    public bool IsSupported => false;
    public bool CanPaste => false;

    public void Remember(IntPtr ownWindow)
    {
    }

    public Task<bool> ReturnAsync() => Task.FromResult(false);

    public Task<bool> PasteAsync() => Task.FromResult(false);

    public void RequestPasteAccess()
    {
    }
}
