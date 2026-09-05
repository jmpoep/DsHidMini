# EnumDescriptionTypeConverter

Namespace: Nefarius.DsHidMini.IPC.Util.Converters

```csharp
public class EnumDescriptionTypeConverter : System.ComponentModel.EnumConverter
```

Inheritance [Object](https://learn.microsoft.com/dotnet/api/system.object) → [TypeConverter](https://learn.microsoft.com/dotnet/api/system.componentmodel.typeconverter) → [EnumConverter](https://learn.microsoft.com/dotnet/api/system.componentmodel.enumconverter) → [EnumDescriptionTypeConverter](./nefarius.dshidmini.ipc.util.converters.enumdescriptiontypeconverter.md)<br>
Attributes [NullableContextAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullablecontextattribute), [NullableAttribute](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.nullableattribute)

## Constructors

### <a id="constructors-.ctor"/>**EnumDescriptionTypeConverter(Type)**

```csharp
public EnumDescriptionTypeConverter(Type type)
```

#### Parameters

`type` [Type](https://learn.microsoft.com/dotnet/api/system.type)<br>

## Methods

### <a id="methods-convertto"/>**ConvertTo(ITypeDescriptorContext, CultureInfo, Object, Type)**

```csharp
public object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
```

#### Parameters

`context` [ITypeDescriptorContext](https://learn.microsoft.com/dotnet/api/system.componentmodel.itypedescriptorcontext)<br>

`culture` [CultureInfo](https://learn.microsoft.com/dotnet/api/system.globalization.cultureinfo)<br>

`value` [Object](https://learn.microsoft.com/dotnet/api/system.object)<br>

`destinationType` [Type](https://learn.microsoft.com/dotnet/api/system.type)<br>

#### Returns

[Object](https://learn.microsoft.com/dotnet/api/system.object)
