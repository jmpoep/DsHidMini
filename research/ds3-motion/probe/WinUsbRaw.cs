// Minimal direct WinUSB P/Invoke backend. Used instead of Nefarius.Drivers.WinUSB because that
// library's USBDevice constructor insists on reading string descriptors, which an original
// SIXAXIS does not have (it STALLs the language-ID request -> "device not functioning").
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Ds3MotionProbe;

public sealed unsafe class WinUsbRaw : IDisposable
{
    private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80, FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint PIPE_TRANSFER_TIMEOUT = 0x03;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WINUSB_SETUP_PACKET
    {
        public byte RequestType, Request;
        public ushort Value, Index, Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct USB_INTERFACE_DESCRIPTOR
    {
        public byte bLength, bDescriptorType, bInterfaceNumber, bAlternateSetting, bNumEndpoints, bInterfaceClass, bInterfaceSubClass, bInterfaceProtocol, iInterface;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINUSB_PIPE_INFORMATION
    {
        public int PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct USB_DEVICE_DESCRIPTOR
    {
        public byte bLength, bDescriptorType;
        public ushort bcdUSB;
        public byte bDeviceClass, bDeviceSubClass, bDeviceProtocol, bMaxPacketSize0;
        public ushort idVendor, idProduct, bcdDevice;
        public byte iManufacturer, iProduct, iSerialNumber, bNumConfigurations;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, nint lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, nint hTemplateFile);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Initialize(SafeFileHandle DeviceHandle, out nint InterfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Free(nint InterfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_GetDescriptor(nint InterfaceHandle, byte DescriptorType, byte Index, ushort LanguageID, byte* Buffer, uint BufferLength, out uint LengthTransferred);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_QueryInterfaceSettings(nint InterfaceHandle, byte AlternateInterfaceNumber, out USB_INTERFACE_DESCRIPTOR UsbAltInterfaceDescriptor);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_QueryPipe(nint InterfaceHandle, byte AlternateInterfaceNumber, byte PipeIndex, out WINUSB_PIPE_INFORMATION PipeInformation);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_SetPipePolicy(nint InterfaceHandle, byte PipeID, uint PolicyType, uint ValueLength, void* Value);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_SetPowerPolicy(nint InterfaceHandle, uint PolicyType, uint ValueLength, void* Value);

    private const uint AUTO_SUSPEND = 0x81;

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_ResetPipe(nint InterfaceHandle, byte PipeID);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_AbortPipe(nint InterfaceHandle, byte PipeID);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_SetCurrentAlternateSetting(nint InterfaceHandle, byte SettingNumber);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_ControlTransfer(nint InterfaceHandle, WINUSB_SETUP_PACKET SetupPacket, byte* Buffer, uint BufferLength, nint LengthTransferred, NativeOverlapped* Overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_ReadPipe(nint InterfaceHandle, byte PipeID, byte* Buffer, uint BufferLength, nint LengthTransferred, NativeOverlapped* Overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_WritePipe(nint InterfaceHandle, byte PipeID, byte* Buffer, uint BufferLength, nint LengthTransferred, NativeOverlapped* Overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateEventW(nint lpEventAttributes, bool bManualReset, bool bInitialState, nint lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(SafeFileHandle hFile, NativeOverlapped* lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);

    private const int ERROR_IO_PENDING = 997;

    private delegate bool OverlappedCall(NativeOverlapped* overlapped);

    /// <summary>Runs an overlapped WinUSB call to completion and returns the transferred length.</summary>
    private uint Complete(OverlappedCall call, string what)
    {
        var ov = new NativeOverlapped { EventHandle = _event };
        bool ok = call(&ov);
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            if (err != ERROR_IO_PENDING) throw new Win32Exception(err, $"{what}: error {err} ({new Win32Exception(err).Message})");
        }

        if (!GetOverlappedResult(_file, &ov, out uint transferred, true))
        {
            int err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err, $"{what}: error {err} ({new Win32Exception(err).Message})");
        }

        return transferred;
    }

    private readonly SafeFileHandle _file;
    private readonly nint _itf;
    private readonly nint _event;

    public USB_DEVICE_DESCRIPTOR Descriptor { get; }
    public List<WINUSB_PIPE_INFORMATION> Pipes { get; } = [];
    public byte InterfaceClass { get; }

    public WinUsbRaw(string devicePath)
    {
        _file = CreateFileW(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED, 0);
        if (_file.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateFile");
        if (!WinUsb_Initialize(_file, out _itf)) throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_Initialize");
        _event = CreateEventW(0, true, false, 0);
        if (_event == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateEvent");

        // keep the pad awake: WinUSB selectively suspends an idle device after ~5 s by default
        byte off = 0;
        WinUsb_SetPowerPolicy(_itf, AUTO_SUSPEND, 1, &off);

        USB_DEVICE_DESCRIPTOR dd;
        if (!WinUsb_GetDescriptor(_itf, 0x01, 0, 0, (byte*)&dd, (uint)sizeof(USB_DEVICE_DESCRIPTOR), out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_GetDescriptor(device)");
        Descriptor = dd;

        if (WinUsb_QueryInterfaceSettings(_itf, 0, out USB_INTERFACE_DESCRIPTOR id))
        {
            InterfaceClass = id.bInterfaceClass;
            for (byte i = 0; i < id.bNumEndpoints; i++)
            {
                if (WinUsb_QueryPipe(_itf, 0, i, out WINUSB_PIPE_INFORMATION pi)) Pipes.Add(pi);
            }
        }
    }

    public string? TryGetString(byte index)
    {
        if (index == 0) return null;
        byte* buf = stackalloc byte[256];
        if (!WinUsb_GetDescriptor(_itf, 0x03, index, 0x0409, buf, 256, out uint len) || len < 2) return null;
        return new string((char*)(buf + 2), 0, (int)(len - 2) / 2).TrimEnd('\0');
    }

    /// <summary>Clears a halted endpoint. Returns null on success or the Win32 message on failure.</summary>
    public string? TryResetPipe(byte pipeId)
    {
        if (WinUsb_ResetPipe(_itf, pipeId)) return null;
        int e = Marshal.GetLastWin32Error();
        return $"error {e} ({new Win32Exception(e).Message})";
    }

    public string? TryAbortPipe(byte pipeId)
    {
        if (WinUsb_AbortPipe(_itf, pipeId)) return null;
        int e = Marshal.GetLastWin32Error();
        return $"error {e} ({new Win32Exception(e).Message})";
    }

    /// <summary>Re-selects alternate setting 0, which re-arms the interface's endpoints.</summary>
    public string? TrySelectAltSetting0()
    {
        if (WinUsb_SetCurrentAlternateSetting(_itf, 0)) return null;
        int e = Marshal.GetLastWin32Error();
        return $"error {e} ({new Win32Exception(e).Message})";
    }

    public void SetPipeTimeout(byte pipeId, uint ms)
    {
        uint v = ms;
        if (!WinUsb_SetPipePolicy(_itf, pipeId, PIPE_TRANSFER_TIMEOUT, 4, &v)) throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_SetPipePolicy");
    }

    public int ControlIn(byte requestType, byte request, ushort value, ushort index, Span<byte> buffer)
    {
        var sp = new WINUSB_SETUP_PACKET { RequestType = requestType, Request = request, Value = value, Index = index, Length = (ushort)buffer.Length };
        fixed (byte* p = buffer)
        {
            byte* pp = p;
            int len = buffer.Length;
            return (int)Complete(ov => WinUsb_ControlTransfer(_itf, sp, pp, (uint)len, 0, ov), $"ControlIn {request:X2}/{value:X4} len {len}");
        }
    }

    public void ControlOut(byte requestType, byte request, ushort value, ushort index, ReadOnlySpan<byte> buffer)
    {
        var sp = new WINUSB_SETUP_PACKET { RequestType = requestType, Request = request, Value = value, Index = index, Length = (ushort)buffer.Length };
        Span<byte> writable = buffer.Length <= 256 ? stackalloc byte[buffer.Length] : new byte[buffer.Length];
        buffer.CopyTo(writable);
        fixed (byte* p = writable)
        {
            byte* pp = p;
            int len = writable.Length;
            Complete(ov => WinUsb_ControlTransfer(_itf, sp, pp, (uint)len, 0, ov), $"ControlOut {request:X2}/{value:X4} len {len}");
        }
    }

    public int ReadPipe(byte pipeId, Span<byte> buffer)
    {
        fixed (byte* p = buffer)
        {
            byte* pp = p;
            int len = buffer.Length;
            return (int)Complete(ov => WinUsb_ReadPipe(_itf, pipeId, pp, (uint)len, 0, ov), $"ReadPipe 0x{pipeId:X2}");
        }
    }

    public void Dispose()
    {
        if (_itf != 0) WinUsb_Free(_itf);
        if (_event != 0) CloseHandle(_event);
        _file.Dispose();
    }
}
