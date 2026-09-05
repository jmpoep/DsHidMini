# DsHidDeviceMode

Namespace: Nefarius.DsHidMini.IPC.Models.Drivers

HID device emulation modes.

```csharp
public enum DsHidDeviceMode
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [Enum](https://learn.microsoft.com/dotnet/api/system.enum) → [DsHidDeviceMode](./nefarius.dshidmini.ipc.models.drivers.dshiddevicemode.md)<br>
Implements [IComparable](https://learn.microsoft.com/dotnet/api/system.icomparable), [ISpanFormattable](https://learn.microsoft.com/dotnet/api/system.ispanformattable), [IFormattable](https://learn.microsoft.com/dotnet/api/system.iformattable), [IConvertible](https://learn.microsoft.com/dotnet/api/system.iconvertible)<br>
Attributes [TypeConverterAttribute](https://learn.microsoft.com/dotnet/api/system.componentmodel.typeconverterattribute)

## Fields

| Name | Value | Description |
| --- | --: | --- |
| SDF | 1 | Single Device with Force Feedback mode. |
| GPJ | 2 | Gamepad plus Joystick mode. |
| SXS | 3 | SIXAXIS.SYS mode. |
| DS4W | 4 | DS4Windows DualShock 4 emulation mode. |
| XInput | 5 | Xbox One Controller mode. |
