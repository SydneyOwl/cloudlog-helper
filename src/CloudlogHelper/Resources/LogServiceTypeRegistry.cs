using System;
using System.Linq;
using System.Reflection;
using CloudlogHelper.LogService;
using CloudlogHelper.LogService.Attributes;

namespace CloudlogHelper.Resources;

public static class LogServiceTypeRegistry
{
    private static readonly Lazy<Type[]> CachedTypes = new(DiscoverLogServiceTypes);

    public static Type[] ServiceTypes => CachedTypes.Value;

    public static ThirdPartyLogService[] CreateEmptyServices()
    {
        return ServiceTypes
            .Select(type => (ThirdPartyLogService)Activator.CreateInstance(type)!)
            .ToArray();
    }

    private static Type[] DiscoverLogServiceTypes()
    {
        var types = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(type => typeof(ThirdPartyLogService).IsAssignableFrom(type))
            .Where(type => !type.IsAbstract)
            .Where(type => type.GetCustomAttribute<LogServiceAttribute>() is not null)
            .ToArray();

        var duplicateName = types
            .GroupBy(type => type.Name)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateName is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate log service type name found: {duplicateName.Key}. This breaks settings polymorphism.");
        }

        var missingCtor = types.FirstOrDefault(type => type.GetConstructor(Type.EmptyTypes) is null);
        if (missingCtor is not null)
        {
            throw new TypeLoadException(
                $"Log service {missingCtor.FullName} must have a parameterless constructor.");
        }

        return types;
    }
}
