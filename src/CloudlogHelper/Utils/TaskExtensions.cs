using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NLog;

namespace CloudlogHelper.Utils;

public static class TaskExtensions
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     Fire-and-forget with observation: unlike dropping the Task, exceptions are
    ///     logged instead of ending up in UnobservedTaskException (or crashing on
    ///     async void). Use only where awaiting is not an option.
    /// </summary>
    public static void SafeFireAndForget(this Task task, [CallerMemberName] string origin = "")
    {
        task.ContinueWith(t =>
        {
            if (t.IsFaulted) ClassLogger.Error(t.Exception, $"Fire-and-forget task failed: {origin}");
        }, TaskScheduler.Default);
    }
}
