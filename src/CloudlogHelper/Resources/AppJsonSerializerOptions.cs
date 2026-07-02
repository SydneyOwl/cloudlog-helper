using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CloudlogHelper.LogService;
using CloudlogHelper.Utils;
using ReactiveUI;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Resources;

public static class AppJsonSerializerOptions
{
    public static JsonSerializerOptions Settings { get; } = CreateSettingsOptions();

    private static JsonSerializerOptions CreateSettingsOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                ConfigureThirdPartyLogServicePolymorphism,
                JsonExtensions.IgnorePropertiesDeclaredBy<ReactiveValidationObject>(),
                JsonExtensions.IgnorePropertiesDeclaredBy<ReactiveObject>()
            }
        };

        return new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = resolver
        };
    }

    private static void ConfigureThirdPartyLogServicePolymorphism(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(ThirdPartyLogService)) return;

        typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "$type"
        };

        foreach (var serviceType in LogServiceTypeRegistry.ServiceTypes)
        {
            typeInfo.PolymorphismOptions.DerivedTypes.Add(
                new JsonDerivedType(serviceType, serviceType.Name));
        }
    }
}
