using System;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace CloudlogHelper.Utils;

internal static class NativeMemoryTrimmer
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private static readonly SemaphoreSlim TrimLock = new(1, 1);

    // This is a bit hack...
    public static void TrimAfterWindowClosed(string reason)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;
        if (Environment.GetEnvironmentVariable("DISABLE_MALLOC_TRIM_AFTER_WINDOW_CLOSED") == "1") return;

        _ = Task.Run(async () =>
        {
            if (!await TrimLock.WaitAsync(0).ConfigureAwait(false)) return;

            try
            {
                await Task.Delay(500).ConfigureAwait(false);

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);

                var trimmed = false;
                try
                {
                    trimmed = malloc_trim(UIntPtr.Zero) != 0;
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
                {
                    ClassLogger.Trace(ex, "malloc_trim is unavailable on this Linux libc.");
                }

                ClassLogger.Trace($"Native memory trim requested after {reason}. malloc_trim={trimmed}.");
            }
            catch (Exception ex)
            {
                ClassLogger.Debug(ex, "Native memory trim failed.");
            }
            finally
            {
                TrimLock.Release();
            }
        });
    }

    [DllImport("libc", EntryPoint = "malloc_trim", SetLastError = false)]
    private static extern int malloc_trim(UIntPtr pad);
}
