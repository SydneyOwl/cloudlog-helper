using System.Threading;
using System.Threading.Tasks;
using Flurl.Http;

namespace CloudlogHelper.Utils;

public static class FlurlHttpExceptionExtensions
{
    /// <summary>
    ///     True when a Flurl call failed because the caller cancelled it (e.g. the page was closed).
    /// </summary>
    public static bool IsCancelledByUser(this FlurlHttpException ex, CancellationToken token) =>
        ex.InnerException is TaskCanceledException && token.IsCancellationRequested;
}
