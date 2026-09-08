using System.Runtime.InteropServices;

namespace Nefarius.DsHidMini.ControlApp.Models;

/// <summary>
///     Connected-monitor working areas in WPF device-independent pixels.
/// </summary>
internal static class DisplayWorkAreas
{
    public static IReadOnlyList<Rect> GetDipWorkingAreas()
    {
        List<Rect> areas = [];
        try
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr monitor, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
                {
                    NativeMethods.MONITORINFO info = new()
                    {
                        cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
                    };
                    if (!NativeMethods.GetMonitorInfo(monitor, ref info))
                    {
                        return true;
                    }

                    uint dpiX = 96;
                    uint dpiY = 96;
                    if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MdtEffectiveDpi, out uint x,
                            out uint y) == 0)
                    {
                        dpiX = x == 0 ? 96 : x;
                        dpiY = y == 0 ? 96 : y;
                    }

                    NativeMethods.RECT work = info.rcWork;
                    areas.Add(new Rect(
                        work.left * 96.0 / dpiX,
                        work.top * 96.0 / dpiY,
                        (work.right - work.left) * 96.0 / dpiX,
                        (work.bottom - work.top) * 96.0 / dpiY));
                    return true;
                }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Failed to enumerate display monitors.");
        }

        if (areas.Count == 0)
        {
            areas.Add(SystemParameters.WorkArea);
        }

        return areas;
    }

    private static class NativeMethods
    {
        public const int MdtEffectiveDpi = 0;

        public delegate bool MonitorEnumProc(
            IntPtr hMonitor,
            IntPtr hdcMonitor,
            ref RECT lprcMonitor,
            IntPtr dwData);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(
            IntPtr hdc,
            IntPtr lprcClip,
            MonitorEnumProc lpfnEnum,
            IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(
            IntPtr hMonitor,
            int dpiType,
            out uint dpiX,
            out uint dpiY);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }
    }
}
