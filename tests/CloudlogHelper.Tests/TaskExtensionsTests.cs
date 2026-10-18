using System;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Tests;

public class TaskExtensionsTests
{
    [Fact]
    public void SafeFireAndForget_NeverRethrowsAFaultedTask()
    {
        // Load-bearing for every fire-and-forget call site (and what keeps the async-void
        // subscriptions from taking the process down): observing the fault must not surface it.
        var faulted = Task.FromException(new InvalidOperationException("boom"));

        Assert.Null(Record.Exception(() => faulted.SafeFireAndForget()));
    }

    [Fact]
    public void SafeFireAndForget_NeverRethrowsACancelledTask()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var canceled = Task.FromCanceled(cancelled.Token);

        Assert.Null(Record.Exception(() => canceled.SafeFireAndForget()));
    }

    [Fact]
    public void SafeFireAndForget_LeavesACompletedTaskAlone()
    {
        var completed = Task.CompletedTask;

        Assert.Null(Record.Exception(() => completed.SafeFireAndForget()));
    }
}
