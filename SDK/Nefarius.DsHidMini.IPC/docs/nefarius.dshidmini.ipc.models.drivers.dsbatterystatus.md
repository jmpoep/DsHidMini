# DsBatteryStatus

Namespace: Nefarius.DsHidMini.IPC.Models.Drivers

Battery status values.

```csharp
public enum DsBatteryStatus
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [ValueType](https://learn.microsoft.com/dotnet/api/system.valuetype) → [Enum](https://learn.microsoft.com/dotnet/api/system.enum) → [DsBatteryStatus](./nefarius.dshidmini.ipc.models.drivers.dsbatterystatus.md)<br>
Implements [IComparable](https://learn.microsoft.com/dotnet/api/system.icomparable), [ISpanFormattable](https://learn.microsoft.com/dotnet/api/system.ispanformattable), [IFormattable](https://learn.microsoft.com/dotnet/api/system.iformattable), [IConvertible](https://learn.microsoft.com/dotnet/api/system.iconvertible)<br>
Attributes [TypeConverterAttribute](https://learn.microsoft.com/dotnet/api/system.componentmodel.typeconverterattribute)

## Fields

| Name | Value | Description |
| --- | --: | --- |
| Unknown | 0 | Unknown/not yet reported. |
| Dying | 1 | Dying. Battery is so low the device is barely being kept on. |
| Low | 2 | Low. Device should be charged soon. |
| Medium | 3 | Medium. Will last for a while but should be charged soon. |
| High | 4 | High. Will last for a few sessions. |
| Full | 5 | Full. Status right after successful charging. |
| Charging | 238 | Charging. The default state while wired until [DsBatteryStatus.Charged](./nefarius.dshidmini.ipc.models.drivers.dsbatterystatus.md#charged) is reached. |
| Charged | 239 | Charged. While wired synonymous to [DsBatteryStatus.Full](./nefarius.dshidmini.ipc.models.drivers.dsbatterystatus.md#full) while wireless. |
