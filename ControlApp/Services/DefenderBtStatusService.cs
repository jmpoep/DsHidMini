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
    private DeviceNotificationListener? _listener;

    /// <summary>
    ///     HID device path (symbolic link) of the currently detected Defender BT in DualShock 4 mode, if any.
    /// </summary>
    private string? _detectedDevicePath;

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

        Rescan();
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
    }

    /// <summary>
    ///     Sends the PS3 mode-switch probe to the currently detected device, if any.
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
        Application.Current?.Dispatcher.BeginInvoke(Rescan);
    }

    private void Rescan()
    {
        string? foundPath = null;

        int instance = 0;
        while (Devcon.FindByInterfaceGuid(
                   DefenderBtModeSwitcher.HidDeviceInterfaceGuid, out string? path, out string? _, instance++))
        {
            if (path is null || !DefenderBtModeSwitcher.IsDefenderBtInDs4Mode(path))
            {
                continue;
            }

            foundPath = path;
            break;
        }

        _detectedDevicePath = foundPath;
        IsDetected = foundPath is not null;

        if (IsDetected)
        {
            StatusTitle = "Retro Fighters Defender BT detected in DualShock 4 mode";
            StatusMessage =
                "Switch it to PS3 (DualShock 3) mode so DsHidMini can bind to it and Bluetooth pairing becomes available.";
            Severity = InfoBarSeverity.Informational;

            if (ApplicationConfiguration.Instance.AutoSwitchDefenderBtToPs3Mode)
            {
                Log.Logger.Information(
                    "AutoSwitchDefenderBtToPs3Mode is enabled, automatically switching detected device");
                TrySwitchToPs3Mode();
            }
        }
        else
        {
            StatusTitle = string.Empty;
            StatusMessage = string.Empty;
        }

        OnPropertyChanged(nameof(CanSwitch));
    }
}
