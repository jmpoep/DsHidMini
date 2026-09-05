namespace Nefarius.DsHidMini.ControlApp.Models;

/// <summary>
///     Restart-as-admin handoff: start the elevated successor while this process
///     still owns the single-instance mutex, wait until it has a handle, then
///     release so only that successor can become primary. On launch or wait
///     failure, reacquire (or keep) ownership and do not shut down.
/// </summary>
internal static class RestartAsAdminFlow
{
    public static bool Run(
        Action startElevated,
        Action releaseOwnership,
        Action reacquireOwnership,
        Action requestExit,
        Func<bool> waitForSuccessorReady)
    {
        try
        {
            startElevated();
        }
        catch
        {
            reacquireOwnership();
            return false;
        }

        if (!waitForSuccessorReady())
        {
            reacquireOwnership();
            return false;
        }

        releaseOwnership();
        requestExit();
        return true;
    }
}
