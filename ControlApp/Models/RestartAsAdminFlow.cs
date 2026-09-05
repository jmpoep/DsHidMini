namespace Nefarius.DsHidMini.ControlApp.Models;

/// <summary>
///     Restart-as-admin ordering: release the single-instance mutex, launch the
///     elevated process so it can become primary, then shut down the parent.
/// </summary>
internal static class RestartAsAdminFlow
{
    public static void Run(Action releaseOwnership, Action startElevated, Action requestExit)
    {
        releaseOwnership();
        startElevated();
        requestExit();
    }
}
