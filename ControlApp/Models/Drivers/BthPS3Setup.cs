using System.Diagnostics;
using System.IO;

using Microsoft.Win32;

namespace Nefarius.DsHidMini.ControlApp.Models.Drivers;

[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static class BthPS3Setup
{
    public static readonly Version MinimumSupportedVersion = new(2, 0, 144);

    private const string ServiceKeyPath = @"SYSTEM\CurrentControlSet\Services\BthPS3";

    private const string SetupKeyPath =
        @"Software\Nefarius Software Solutions e.U.\Nefarius BthPS3 Bluetooth Drivers";

    private static string ProfileDriverPath =>
        Path.Combine(Environment.SystemDirectory, "drivers", "BthPS3.sys");

    private static string FilterDriverPath =>
        Path.Combine(Environment.SystemDirectory, "drivers", "BthPS3PSM.sys");

    /// <summary>
    ///     True if the BthPS3 profile service or driver file is present.
    /// </summary>
    public static bool IsInstalled
    {
        get
        {
            try
            {
                if (File.Exists(ProfileDriverPath))
                {
                    return true;
                }

                using RegistryKey? key = RegistryHelpers.GetRegistryKey(ServiceKeyPath);
                return key is not null;
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Failed to determine whether BthPS3 is installed.");
                return false;
            }
        }
    }

    /// <summary>
    ///     File version of BthPS3.sys, falling back to the installer registry value.
    /// </summary>
    public static Version? ProfileDriverVersion =>
        ReadFileVersion(ProfileDriverPath) ?? ReadSetupRegistryVersion("DriverVersion");

    /// <summary>
    ///     File version of BthPS3PSM.sys, falling back to the installer registry value.
    /// </summary>
    public static Version? FilterDriverVersion =>
        ReadFileVersion(FilterDriverPath) ?? ReadSetupRegistryVersion("FilterVersion");

    /// <summary>
    ///     BthPS3 setup package version from the Nefarius installer registry key.
    /// </summary>
    public static Version? SetupVersion => ReadSetupRegistryVersion("Version");

    /// <summary>
    ///     Best available installed version: profile driver, then setup package, then filter driver.
    /// </summary>
    public static Version? InstalledVersion =>
        ProfileDriverVersion ?? SetupVersion ?? FilterDriverVersion;

    /// <summary>
    ///     True when the resolved installed version meets the DsHidMini minimum.
    /// </summary>
    public static bool IsVersionSupported =>
        InstalledVersion is { } version && version >= MinimumSupportedVersion;

    private static Version? ReadFileVersion(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            if (info.FileMajorPart == 0 && info.FileMinorPart == 0 && info.FileBuildPart == 0)
            {
                return null;
            }

            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Failed to read file version from {Path}.", path);
            return null;
        }
    }

    private static Version? ReadSetupRegistryVersion(string valueName)
    {
        try
        {
            using RegistryKey? key = RegistryHelpers.GetRegistryKey(SetupKeyPath);
            object? value = key?.GetValue(valueName);
            if (value is null)
            {
                return null;
            }

            return Version.TryParse(value.ToString(), out Version? version) ? version : null;
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Failed to read BthPS3 setup registry value {ValueName}.", valueName);
            return null;
        }
    }
}
