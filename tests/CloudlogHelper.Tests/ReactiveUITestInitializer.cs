using System.Runtime.CompilerServices;
using ReactiveUI.Builder;

namespace CloudlogHelper.Tests;

internal static class ReactiveUITestInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        var builder = RxAppBuilder.CreateReactiveUIBuilder();
        builder.WithCoreServices();
        builder.WithPlatformServices();
        builder.BuildApp();
    }
}
