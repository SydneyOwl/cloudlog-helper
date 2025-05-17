using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CloudlogHelper.Utils;

public struct WorkItem
{
    public Func<Task<string>> Work { get; }
    public TaskCompletionSource<string> TaskCompletionSource { get; }
    public WorkItem(Func<Task<string>> work, TaskCompletionSource<string> tcs)
    {
        Work = work;
        TaskCompletionSource = tcs;
    }
}

public class RigctldScheduler
{
    private readonly ConcurrentQueue<WorkItem> _highPriorityQueue = new();
    private readonly ConcurrentQueue<WorkItem> _lowPriorityQueue = new();
    
    private readonly SemaphoreSlim _workAvailable = new(0);
    
    private readonly CancellationTokenSource _cts = new();
    
    public RigctldScheduler()
    {
        // Console.WriteLine("Well now started..");
        Task.Run(() => ProcessRequestsAsync(_cts.Token));
    }
    
    public Task<string> EnqueueHighPriorityRequest(Func<Task<string>> work)
    {
        if (_cts.IsCancellationRequested) throw new OperationCanceledException();
        var tcs = new TaskCompletionSource<string>();
        _highPriorityQueue.Enqueue(new WorkItem(work, tcs));
        _workAvailable.Release();
        return tcs.Task;
    }
    
    public Task<string> EnqueueLowPriorityRequest(Func<Task<string>> work)
    {
        if (_cts.IsCancellationRequested) throw new OperationCanceledException();
        var tcs = new TaskCompletionSource<string>();
        _lowPriorityQueue.Enqueue(new WorkItem(work, tcs));
        _workAvailable.Release();
        return tcs.Task;
    }
    
    private async Task ProcessRequestsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _workAvailable.WaitAsync(ct);
                // Console.WriteLine("Okay new work for us now...");
                if (_highPriorityQueue.TryDequeue(out var highPriorityItem))
                {
                    try
                    {
                        var result = await highPriorityItem.Work();
                        highPriorityItem.TaskCompletionSource.TrySetResult(result);
                    }
                    catch (Exception ex)
                    {
                        highPriorityItem.TaskCompletionSource.TrySetException(ex);
                    }

                    continue;
                }

                if (_lowPriorityQueue.TryDequeue(out var lowPriorityItem))
                {
                    try
                    {
                        var result = await lowPriorityItem.Work();
                        lowPriorityItem.TaskCompletionSource.TrySetResult(result);
                    }
                    catch (Exception ex)
                    {
                        lowPriorityItem.TaskCompletionSource.TrySetException(ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        // cancel all unprocessed requests
        while (!_highPriorityQueue.IsEmpty)
        {
            _highPriorityQueue.TryDequeue(out var item);
            item.TaskCompletionSource.TrySetCanceled();
        }
        while (!_lowPriorityQueue.IsEmpty)
        {
            _lowPriorityQueue.TryDequeue(out var item);
            item.TaskCompletionSource.TrySetCanceled();
        }
    }
    
    public void Stop()
    {
        // Console.WriteLine("well now stopped...");
        _cts.Cancel();
    }
}