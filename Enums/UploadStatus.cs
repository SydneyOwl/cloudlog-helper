namespace CloudlogHelper.Enums;

public enum UploadStatus
{
    Uploading,
    Retrying,
    Pending,
    Success,
    Fail,
    Ignored // Auto upload qso is not enabled 
}