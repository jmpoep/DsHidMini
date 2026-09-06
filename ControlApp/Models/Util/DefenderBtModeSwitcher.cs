using System.Runtime.InteropServices;

using Windows.Win32;
using Windows.Win32.Devices.HumanInterfaceDevice;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

using Microsoft.Win32.SafeHandles;

namespace Nefarius.DsHidMini.ControlApp.Models.Util;

/// <summary>
///     Outcome of an attempt to switch a Retro Fighters Defender Bluetooth Edition out of its default
///     DualShock 4 USB identity into its DualShock 3 identity, which DsHidMini can bind to.
/// </summary>
public enum DefenderBtModeSwitchResult
{
    /// <summary>
    ///     The supplied device path does not point to a Defender BT in DualShock 4 mode.
    /// </summary>
    NotADefenderBt,

    /// <summary>
    ///     The probe report was sent successfully; the device is expected to detach and re-enumerate as a
    ///     DualShock 3 (<c>USB\VID_054C&amp;PID_0268</c>) shortly after.
    /// </summary>
    Sent,

    /// <summary>
    ///     A matching device was found but sending the probe report failed.
    /// </summary>
    Failed
}

/// <summary>
///     Detects a Retro Fighters Defender Bluetooth Edition controller enumerated in its default DualShock 4
///     USB identity (<c>USB\VID_054C&amp;PID_05C4</c>) and replays the same HID Feature report a real PS3
///     sends it to make it detach and re-enumerate as a DualShock 3 (<c>USB\VID_054C&amp;PID_0268</c>), which
///     DsHidMini already binds to via <c>driver/dshidmini.inf</c>.
/// </summary>
/// <remarks>
///     See issue #282 and <c>docs/PS3_USB_STARTUP.md</c> ("Retro Fighters Defender") for how this sequence was
///     derived from real PS3-to-Defender-BT USB captures. The report is a verbatim replay of what a genuine PS3
///     periodically sends a real DualShock 4 as well (harmless no-op there), so sending it to a device that
///     merely looks like a Defender BT but is not carries no risk beyond it being ignored.
/// </remarks>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public static class DefenderBtModeSwitcher
{
    /// <summary>
    ///     Sony's USB Vendor ID, shared by the genuine DualShock 3/4 and the Defender BT's DualShock 4 identity.
    /// </summary>
    public const ushort SonyVendorId = 0x054C;

    /// <summary>
    ///     Product ID the Defender BT (and a genuine DualShock 4) enumerates as by default.
    /// </summary>
    public const ushort DualShock4ProductId = 0x05C4;

    /// <summary>
    ///     Product ID the Defender BT (and a genuine DualShock 3) enumerates as after the probe below succeeds.
    /// </summary>
    public const ushort DualShock3ProductId = 0x0268;

    /// <summary>
    ///     The 17-byte <c>SET_REPORT Feature 0x14</c> payload a real PS3 sends every ~1 second while a
    ///     DualShock-4-identity device is attached. On a Defender BT this makes it detach and re-enumerate as a
    ///     DualShock 3; on a genuine DualShock 4 it is a harmless no-op (confirmed from capture).
    /// </summary>
    private static readonly byte[] Ps3ModeProbeReport =
    {
        0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    /// <summary>
    ///     The HID device interface class GUID, used to enumerate/listen for HID device arrivals.
    /// </summary>
    public static Guid HidDeviceInterfaceGuid
    {
        get
        {
            PInvoke.HidD_GetHidGuid(out Guid guid);
            return guid;
        }
    }

    /// <summary>
    ///     True if <paramref name="devicePath" /> points to a HID device currently reporting Sony's Defender-BT
    ///     DualShock 4 identity (VID 0x054C, PID 0x05C4).
    /// </summary>
    public static bool IsDefenderBtInDs4Mode(string devicePath)
    {
        using SafeFileHandle handle = OpenDevice(devicePath);
        return !handle.IsInvalid && TryGetAttributes(handle, out HIDD_ATTRIBUTES attributes) &&
               attributes.VendorID == SonyVendorId && attributes.ProductID == DualShock4ProductId;
    }

    /// <summary>
    ///     Sends the PS3 mode-switch probe to <paramref name="devicePath" /> if (and only if) it currently
    ///     reports the Defender-BT DualShock 4 identity.
    /// </summary>
    public static unsafe DefenderBtModeSwitchResult TrySwitchToPs3Mode(string devicePath)
    {
        using SafeFileHandle handle = OpenDevice(devicePath);

        if (handle.IsInvalid || !TryGetAttributes(handle, out HIDD_ATTRIBUTES attributes) ||
            attributes.VendorID != SonyVendorId || attributes.ProductID != DualShock4ProductId)
        {
            return DefenderBtModeSwitchResult.NotADefenderBt;
        }

        Log.Logger.Information(
            "Sending PS3 mode-switch probe to Defender BT candidate {DevicePath}", devicePath);

        HANDLE rawHandle = new(handle.DangerousGetHandle());

        fixed (byte* buffer = Ps3ModeProbeReport)
        {
            BOOLEAN ok = PInvoke.HidD_SetFeature(rawHandle, buffer, (uint)Ps3ModeProbeReport.Length);

            if (!(bool)ok)
            {
                Log.Logger.Warning(
                    "HidD_SetFeature failed for Defender BT candidate {DevicePath}, Win32 error {Error}",
                    devicePath, Marshal.GetLastWin32Error());
                return DefenderBtModeSwitchResult.Failed;
            }
        }

        return DefenderBtModeSwitchResult.Sent;
    }

    private static SafeFileHandle OpenDevice(string devicePath)
    {
        return PInvoke.CreateFile(
            devicePath,
            (uint)(FILE_ACCESS_RIGHTS.FILE_GENERIC_READ | FILE_ACCESS_RIGHTS.FILE_GENERIC_WRITE),
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL,
            null
        );
    }

    private static bool TryGetAttributes(SafeFileHandle handle, out HIDD_ATTRIBUTES attributes)
    {
        return PInvoke.HidD_GetAttributes(handle, out attributes);
    }
}
