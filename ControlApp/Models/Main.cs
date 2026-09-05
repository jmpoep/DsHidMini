using System.Diagnostics;

namespace Nefarius.DsHidMini.ControlApp.Models;

public class Main
{
    private static void StartAsAdmin(string fileName, string arguments)
    {
        Process proc = new()
        {
            StartInfo =
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas"
            }
        };

        proc.Start();
    }

    public static void RestartAsAdmin()
    {
        if (SecurityUtil.IsElevated)
        {
            return;
        }

        Debug.WriteLine("restarting as admin");
        string token = Guid.NewGuid().ToString("N");
        using EventWaitHandle ready = SingleInstanceLifetime.CreateHandoffReadyEvent(token);
        RestartAsAdminFlow.Run(
            () => StartAsAdmin(
                Environment.ProcessPath!,
                SingleInstanceLifetime.HandoffArgumentPrefix + token),
            Nefarius.DsHidMini.ControlApp.App.ReleaseSingleInstanceOwnership,
            Nefarius.DsHidMini.ControlApp.App.ReacquireSingleInstanceOwnership,
            Nefarius.DsHidMini.ControlApp.App.RequestExit,
            () => ready.WaitOne(TimeSpan.FromSeconds(15)));
    }
}
