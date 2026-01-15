using System;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;

namespace CloudlogHelper.Utils;
// https://stackoverflow.com/questions/75318571/serializing-deserializing-an-object-that-is-derived-from-java-lang-object-throws/75318807#75318807
public static class JsonExtensions
{
    public static Action<JsonTypeInfo> IgnorePropertiesDeclaredBy(Type declaringType)
        => (Action<JsonTypeInfo>) (typeInfo => 
        {
            if (typeInfo.Kind != JsonTypeInfoKind.Object || !declaringType.IsAssignableFrom(typeInfo.Type))
                return;
            foreach (var property in typeInfo.Properties)
            {
                if (property.GetDeclaringType() == declaringType)
                    property.ShouldSerialize = static (obj, value) => false;
            }
        });
    public static Action<JsonTypeInfo> IgnorePropertiesDeclaredBy<TDeclaringType>() => IgnorePropertiesDeclaredBy(typeof(TDeclaringType));
    public static Type? GetDeclaringType(this JsonPropertyInfo property) => (property.AttributeProvider as MemberInfo)?.DeclaringType;
}