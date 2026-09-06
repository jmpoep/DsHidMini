using Nefarius.DsHidMini.ControlApp.Models;
using Nefarius.DsHidMini.ControlApp.Models.Util;
using Nefarius.Utilities.DeviceManagement.PnP;

using Wpf.Ui.Controls;

namespace Nefarius.DsHidMini.ControlApp.Services;

/// <summary>
///     Watches for a Retro Fighters Defender Bluetooth Edition controller enumerated in its default
///     DualShock 4 USB identity and offers a one-click way to switch it into its DualShock 3 identity, which
///     DsHidMini can bind to. See issue #282 and <c>docs/PS3_USB_STARTUP.md</c>.
/// </summary>
public partial class DefenderBtStatusService : ObservableObject, IDisposable
{
    /// <summary>
    ///     How long to wait after the last HID arrival/removal notification before actually rescanning, so a
    ///     burst of notifications (e.g. whole-bus re-enumeration) coalesces into a single scan.
    /// </summary>
    private static readonly TimeSpan RescanDebounce = TimeSpan.FromMilliseconds(250);

    private DeviceNotificationListener? _listener;

    /// <summary>
    ///     HID device path (symbolic link) of the currently detected Defender BT in DualShock 4 mode, if any.
    /// </summary>
    private string? _detectedDevicePath;

    /// <summary>
    ///     Cancellation source for the pending debounced rescan, if any. Re-created (cancelling the previous
    ///     one) every time a new notification arrives so only the latest one actually runs.
    /// </summary>
    private CancellationTokenSource? _debounceCts;

    /// <summary>
    ///     Incremented for every queued scan; used to discard results from a stale/out-of-order scan that
    ///     completed after a newer one.
    /// </summary>
    private int _scanGeneration;

    [ObservableProperty]
    private bool _isDetected;

    [ObservableProperty]
    private bool _isSwitching;

    [ObservableProperty]
    private InfoBarSeverity _severity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _statusTitle = string.Empty;

    /// <summary>
    ///     True if a switch attempt can currently be made.
    /// </summary>
    public bool CanSwitch => IsDetected && !IsSwitching;

    public void Dispose()
    {
        StopListening();
    }

    /// <summary>
    ///     Starts watching for HID device arrivals/removals and performs an initial scan.
    /// </summary>
    public void StartListening()
    {
        if (_listener != null)
        {
            return;
        }

        Log.Logger.Information("Starting detection of Defender BT (DualShock 4 mode) devices");

        _listener = new DeviceNotificationListener();
        _listener.DeviceArrived += OnListenerDevicesArrivedOrRemoved;
        _listener.DeviceRemoved += OnListenerDevicesArrivedOrRemoved;
        _listener.StartListen(DefenderBtModeSwitcher.HidDeviceInterfaceGuid);

        QueueRescan();
    }

    /// <summary>
    ///     Stops watching for HID device arrivals/removals.
    /// </summary>
    public void StopListening()
    {
        Log.Logger.Information("Stopping detection of Defender BT (DualShock 4 mode) devices");
        _listener?.StopListen();
        _listener?.Dispose();
        _listener = null;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }

    /// <summary>
    ///     Sends the PS3 mode-switch probe to the currently detected device, if any. Intended to be called from
    ///     the UI thread (e.g. a button command), since it toggles <see cref="IsSwitching" />.
    /// </summary>
    public bool TrySwitchToPs3Mode()
    {
        if (_detectedDevicePath is null)
        {
            return false;
        }

        IsSwitching = true;

        try
        {
            DefenderBtModeSwitchResult result =
                DefenderBtModeSwitcher.TrySwitchToPs3Mode(_detectedDevicePath);

            Log.Logger.Information(
                "Defender BT PS3 mode-switch attempt for {DevicePath} resulted in {Result}",
                _detectedDevicePath, result);

            return result == DefenderBtModeSwitchResult.Sent;
        }
        finally
        {
            IsSwitching = false;
        }
    }

    private void OnListenerDevicesArrivedOrRemoved(DeviceEventArgs e)
    {
        QueueRescan();
    }

    /// <summary>
    ///     Coalesces bursts of HID arrival/removal notifications into a single debounced rescan. The actual HID
    ///     enumeration and <c>CreateFile</c>/<c>HidD_GetAttributes</c> work happens on a thread-pool thread;
    ///     only the resulting observable state update is marshaled back onto the WPF dispatcher.
    /// </summary>
    private void QueueRescan()
    {
        CancellationTokenSource cts = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _debounceCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        int generation = Interlocked.Increment(ref _scanGeneration);
        CancellationToken token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(RescanDebounce, token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            RunScan(generation);
        }, token);
    }

    /// <summary>
    ///     Runs on a thread-pool thread: enumerates HID devices, opens/queries each candidate, and - if a match
    ///     is found and auto-switch is enabled - sends the mode-switch probe, all off the UI thread. Only the
    ///     final observable state update is marshaled back to the dispatcher, guarded against staleness by
    ///     <paramref name="generation" />.
    /// </summary>
    private void RunScan(int generation)
    {
        string? foundPath = FindDefenderBtCandidatePath();

        if (foundPath is not null && ApplicationConfiguration.Instance.AutoSwitchDefenderBtToPs3Mode)
        {
            // A newer scan superseded this one; do not act on a stale result.
            if (generation != Volatile.Read(ref _scanGeneration))
            {
                return;
            }

            Log.Logger.Information(
                "AutoSwitchDefenderBtToPs3Mode is enabled, automatically switching detected device");
            DefenderBtModeSwitcher.TrySwitchToPs3Mode(foundPath);
        }

        Application.Current?.Dispatcher.BeginInvoke(() => ApplyScanResult(generation, foundPath));
    }

    private static string? FindDefenderBtCandidatePath()
    {
        int instance = 0;
        while (Devcon.FindByInterfaceGuid(
                   DefenderBtModeSwitcher.HidDeviceInterfaceGuid, out string? path, out string? _, instance++))
        {
            if (path is not null && DefenderBtModeSwitcher.IsDefenderBtInDs4Mode(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    ///     Runs on the UI thread. Applies the result of a background scan to observable state, unless a newer
    ///     scan has since been queued or completed.
    /// </summary>
    private void ApplyScanResult(int generation, string? foundPath)
    {
        if (generation != _scanGeneration)
        {
            return;
        }

        _detectedDevicePath = foundPath;
        IsDetected = foundPath is not null;

        if (IsDetected)
        {
            StatusTitle = "Retro Fighters Defender BT detected in DualShock 4 mode";
            StatusMessage =
                "Switch it to PS3 (DualShock 3) mode so DsHidMini can bind to it and Bluetooth pairing becomes available.";
            Severity = InfoBarSeverity.Informational;
        }
        else
        {
            StatusTitle = string.Empty;
            StatusMessage = string.Empty;
        }

        OnPropertyChanged(nameof(CanSwitch));
    }
}
