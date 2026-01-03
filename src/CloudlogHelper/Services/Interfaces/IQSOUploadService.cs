using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Models;

namespace CloudlogHelper.Services.Interfaces;

public interface IQSOUploadService
{
    Task EnqueueQSOForUploadAsync(RecordedCallsignDetail rcd,CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}