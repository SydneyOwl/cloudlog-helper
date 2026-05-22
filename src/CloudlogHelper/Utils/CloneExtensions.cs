using System.Diagnostics.CodeAnalysis;

namespace CloudlogHelper.Utils;

public static class CloneExtensions
{
    [return: NotNullIfNotNull(nameof(obj))]
    public static T? DeepClone<T>(this T? obj)
    {
        return FastCloner.FastCloner.DeepClone(obj);
    }
}
