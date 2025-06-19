using System.Collections.Generic;
using CloudlogHelper.Models;

namespace CloudlogHelper.Messages;

public struct QsoUploadRequested
{
    public List<RecordedCallsignDetail> QsoData { get; set; }
}