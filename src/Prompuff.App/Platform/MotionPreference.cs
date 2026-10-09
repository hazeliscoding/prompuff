using System.Runtime.InteropServices;

namespace Prompuff.App.Platform;

/// <summary>Whether the system asks apps to keep motion down. Avalonia doesn't report it, so Prompuff reads it where it can.</summary>
public interface IMotionPreference
{
    /// <summary>
    /// True when the system asks for less motion. Only Windows says so in a way Prompuff can read: its "Animation
    /// effects" setting. Elsewhere this is false, and the Reduce motion switch in Settings decides.
    /// </summary>
    bool SystemPrefersReducedMotion();
}

public sealed class SystemMotionPreference : IMotionPreference
{
    private const uint SpiGetClientAreaAnimation = 0x1042;

    public bool SystemPrefersReducedMotion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        // Settings › Accessibility › Visual effects › Animation effects. When the call fails, motion stays on.
        return SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var animations, 0) && !animations;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint winIni);
}
