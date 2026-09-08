namespace Nefarius.DsHidMini.ControlApp.Models;

/// <summary>
///     Saved window rectangle and state. Geometry is in WPF device-independent pixels.
/// </summary>
internal readonly record struct WindowPlacementSnapshot(
    double Left,
    double Top,
    double Width,
    double Height,
    WindowState State,
    bool HasPosition);

/// <summary>
///     Pure placement rules so restore can be tested without a live window or display.
/// </summary>
internal static class WindowPlacementPolicy
{
    public const double DefaultWidth = 1100;
    public const double DefaultHeight = 650;
    public const double MinWidth = 1100;
    public const double MinHeight = 650;

    private const double TitleBarProbeX = 40;
    private const double TitleBarProbeY = 16;
    private const double MinOverlapWidth = 80;
    private const double MinOverlapHeight = 40;

    public static void Write(ApplicationConfiguration config, WindowState state, Rect normalBounds)
    {
        config.WindowLeft = normalBounds.Left;
        config.WindowTop = normalBounds.Top;
        config.WindowWidth = normalBounds.Width;
        config.WindowHeight = normalBounds.Height;
        config.WindowState = state == WindowState.Minimized ? WindowState.Normal : state;
    }

    public static bool TryRead(ApplicationConfiguration config, out WindowPlacementSnapshot snapshot)
    {
        if (config.WindowWidth is not { } width ||
            config.WindowHeight is not { } height ||
            double.IsNaN(width) ||
            double.IsNaN(height) ||
            width <= 0 ||
            height <= 0)
        {
            snapshot = default;
            return false;
        }

        bool hasPosition = config.WindowLeft is { } left &&
                           config.WindowTop is { } top &&
                           !double.IsNaN(left) &&
                           !double.IsNaN(top);

        snapshot = new WindowPlacementSnapshot(
            config.WindowLeft ?? 0,
            config.WindowTop ?? 0,
            width,
            height,
            config.WindowState,
            hasPosition);
        return true;
    }

    public static WindowPlacementSnapshot Resolve(
        WindowPlacementSnapshot saved,
        IReadOnlyList<Rect> monitorWorkAreas,
        Rect fallbackWorkArea)
    {
        IReadOnlyList<Rect> monitors = monitorWorkAreas.Count > 0
            ? monitorWorkAreas
            : [fallbackWorkArea];

        double width = Math.Max(MinWidth, saved.Width);
        double height = Math.Max(MinHeight, saved.Height);

        if (saved.HasPosition)
        {
            Rect window = new(saved.Left, saved.Top, width, height);
            if (IsVisibleOnAMonitor(window, monitors))
            {
                return saved with { Width = width, Height = height };
            }
        }

        Rect host = fallbackWorkArea.Width > 0 && fallbackWorkArea.Height > 0
            ? fallbackWorkArea
            : monitors[0];
        width = ClampDimension(width, MinWidth, host.Width);
        height = ClampDimension(height, MinHeight, host.Height);
        Rect centered = CenterIn(host, width, height);
        return new WindowPlacementSnapshot(
            centered.Left,
            centered.Top,
            centered.Width,
            centered.Height,
            saved.State,
            true);
    }

    public static bool IsVisibleOnAMonitor(Rect window, IReadOnlyList<Rect> monitorWorkAreas)
    {
        if (monitorWorkAreas.Count == 0 || window.Width <= 0 || window.Height <= 0)
        {
            return false;
        }

        Point titleBarProbe = new(
            window.Left + Math.Min(TitleBarProbeX, window.Width / 2),
            window.Top + Math.Min(TitleBarProbeY, window.Height / 2));

        foreach (Rect monitor in monitorWorkAreas)
        {
            if (monitor.Contains(titleBarProbe))
            {
                return true;
            }
        }

        foreach (Rect monitor in monitorWorkAreas)
        {
            Rect overlap = Rect.Intersect(window, monitor);
            if (!overlap.IsEmpty &&
                overlap.Width >= MinOverlapWidth &&
                overlap.Height >= MinOverlapHeight)
            {
                return true;
            }
        }

        return false;
    }

    private static double ClampDimension(double value, double min, double hostSize)
    {
        double clamped = Math.Max(min, value);
        if (hostSize >= min)
        {
            clamped = Math.Min(clamped, hostSize);
        }

        return clamped;
    }

    private static Rect CenterIn(Rect host, double width, double height)
    {
        double left = host.Left + Math.Max(0, (host.Width - width) / 2);
        double top = host.Top + Math.Max(0, (host.Height - height) / 2);
        return new Rect(left, top, width, height);
    }
}
