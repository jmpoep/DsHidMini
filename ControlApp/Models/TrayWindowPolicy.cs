namespace Nefarius.DsHidMini.ControlApp.Models;

/// <summary>
///     Pure policy for hide-to-tray vs real close. Kept separate so it can be unit-tested
///     without constructing a WPF window.
/// </summary>
internal static class TrayWindowPolicy
{
    public static bool ShouldHideInsteadOfClose(bool minimizeToTray, bool isExiting)
    {
        return minimizeToTray && !isExiting;
    }

    public static bool ShouldHideOnMinimize(bool minimizeToTray, WindowState newState)
    {
        return minimizeToTray && newState == WindowState.Minimized;
    }
}
