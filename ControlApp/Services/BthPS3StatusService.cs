using Nefarius.DsHidMini.ControlApp.Models.Drivers;
using Nefarius.Utilities.Bluetooth;

using Wpf.Ui.Controls;

namespace Nefarius.DsHidMini.ControlApp.Services;

/// <summary>
///     Aggregates BthPS3 install, version, and configuration state for the ControlApp UI.
/// </summary>
public partial class BthPS3StatusService : ObservableObject
{
    [ObservableProperty]
    private bool _areSettingsIncorrect;

    [ObservableProperty]
    private bool _canRectifySettings;

    [ObservableProperty]
    private bool _hasProblem;

    [ObservableProperty]
    private Version? _installedVersion;

    [ObservableProperty]
    private string _installedVersionDisplay = "Unknown";

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private bool _isFilterAvailable;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isPsmPatchingEnabled;

    [ObservableProperty]
    private bool _isPsmStateKnown;

    [ObservableProperty]
    private bool _isRadioOperable;

    [ObservableProperty]
    private bool _isRawPdoDisabled;

    [ObservableProperty]
    private bool _isVersionSupported;

    [ObservableProperty]
    private string _psmPatchingDisplay = "Unknown";

    [ObservableProperty]
    private string _rawPdoDisplay = "Unknown";

    [ObservableProperty]
    private string _requiredVersionDisplay = BthPS3Setup.MinimumSupportedVersion.ToString();

    [ObservableProperty]
    private InfoBarSeverity _severity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _statusTitle = string.Empty;

    /// <summary>
    ///     Re-reads radio, install, version, PSM, and RawPDO state.
    /// </summary>
    public void Refresh()
    {
        IsElevated = SecurityUtil.IsElevated;

        try
        {
            IsRadioOperable = HostRadio.IsOperable;
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Failed to query Bluetooth radio state.");
            IsRadioOperable = false;
        }

        IsInstalled = BthPS3Setup.IsInstalled;
        InstalledVersion = BthPS3Setup.InstalledVersion;
        IsVersionSupported = BthPS3Setup.IsVersionSupported;

        try
        {
            IsFilterAvailable = BthPS3FilterDriver.IsFilterAvailable;
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Failed to query BthPS3 filter availability.");
            IsFilterAvailable = false;
        }

        IsPsmStateKnown = false;
        IsPsmPatchingEnabled = false;
        if (IsElevated && IsFilterAvailable)
        {
            try
            {
                IsPsmPatchingEnabled = BthPS3FilterDriver.IsFilterEnabled;
                IsPsmStateKnown = true;
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Failed to query BthPS3 PSM patching state.");
            }
        }

        try
        {
            IsRawPdoDisabled = !BthPS3ProfileDriver.RawPDO;
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Failed to query BthPS3 RawPDO setting.");
            IsRawPdoDisabled = true;
        }

        InstalledVersionDisplay = InstalledVersion?.ToString() ?? (IsInstalled ? "Unknown" : "Not installed");
        RequiredVersionDisplay = BthPS3Setup.MinimumSupportedVersion.ToString();
        PsmPatchingDisplay = !IsPsmStateKnown
            ? IsElevated ? "Unknown" : "Unknown (run as Administrator)"
            : IsPsmPatchingEnabled
                ? "Enabled"
                : "Disabled";
        RawPdoDisplay = IsRawPdoDisabled ? "Disabled" : "Enabled";

        AreSettingsIncorrect = IsInstalled && IsVersionSupported && IsFilterAvailable &&
                               (!IsRawPdoDisabled || (IsPsmStateKnown && !IsPsmPatchingEnabled));
        CanRectifySettings = IsElevated && AreSettingsIncorrect;

        ApplyStatus();
    }

    /// <summary>
    ///     Disables RawPDO and enables PSM patching, then refreshes status.
    /// </summary>
    public bool TryRectifySettings()
    {
        if (!IsElevated)
        {
            return false;
        }

        try
        {
            BthPS3ProfileDriver.RawPDO = false;
            BthPS3FilterDriver.IsFilterEnabled = true;
            Refresh();
            return !AreSettingsIncorrect;
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Failed to rectify BthPS3 settings.");
            Refresh();
            return false;
        }
    }

    private void ApplyStatus()
    {
        if (!IsRadioOperable)
        {
            SetStatus(
                false,
                InfoBarSeverity.Informational,
                "No Bluetooth radio detected",
                "Turn on Bluetooth (with the default Windows drivers) to use wireless controllers.");
            return;
        }

        if (!IsInstalled)
        {
            SetStatus(
                true,
                InfoBarSeverity.Error,
                "BthPS3 is not installed",
                "Install BthPS3 to use DualShock 3 controllers over Bluetooth.");
            return;
        }

        if (InstalledVersion is { Major: < 2 })
        {
            SetStatus(
                true,
                InfoBarSeverity.Error,
                "Incompatible BthPS3 version 1.x detected",
                $"DsHidMini requires BthPS3 {BthPS3Setup.MinimumSupportedVersion} or newer. Uninstall BthPS3 v1 and install a current release.");
            return;
        }

        if (InstalledVersion is { } outdated && outdated < BthPS3Setup.MinimumSupportedVersion)
        {
            SetStatus(
                true,
                InfoBarSeverity.Warning,
                $"BthPS3 {outdated} is outdated",
                $"Update BthPS3 to {BthPS3Setup.MinimumSupportedVersion} or newer.");
            return;
        }

        if (InstalledVersion is null)
        {
            SetStatus(
                true,
                InfoBarSeverity.Warning,
                "BthPS3 version could not be determined",
                $"Could not read the installed BthPS3 version. DsHidMini requires {BthPS3Setup.MinimumSupportedVersion} or newer.");
            return;
        }

        if (!IsFilterAvailable)
        {
            SetStatus(
                true,
                InfoBarSeverity.Warning,
                "PSM filter driver not loaded",
                "BthPS3 is installed but the PSM filter is not available. Bluetooth may be off, or BthPS3PSM failed to load.");
            return;
        }

        if (AreSettingsIncorrect)
        {
            SetStatus(
                true,
                InfoBarSeverity.Warning,
                "BthPS3 settings need correcting",
                IsElevated
                    ? "PSM patching is disabled and/or RAW PDO is enabled. Use Fix settings to apply the required values."
                    : "PSM patching is disabled and/or RAW PDO is enabled. Restart as Administrator to fix them.");
            return;
        }

        if (!IsElevated && !IsPsmStateKnown)
        {
            SetStatus(
                false,
                InfoBarSeverity.Informational,
                "BthPS3 is installed",
                "Run as Administrator to verify PSM patching and change BthPS3 settings.");
            return;
        }

        SetStatus(
            false,
            InfoBarSeverity.Success,
            "BthPS3 is installed and configured correctly",
            $"Version {InstalledVersion} meets the minimum requirement of {BthPS3Setup.MinimumSupportedVersion}.");
    }

    private void SetStatus(bool hasProblem, InfoBarSeverity severity, string title, string message)
    {
        HasProblem = hasProblem;
        Severity = severity;
        StatusTitle = title;
        StatusMessage = message;
    }
}
