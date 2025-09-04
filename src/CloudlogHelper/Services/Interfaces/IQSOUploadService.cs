using CloudlogHelper.Models;

namespace CloudlogHelper.Services.Interfaces;

public interface IQSOUploadService
{
    void EnqueueQSOForUpload(RecordedCallsignDetail rcd);
}