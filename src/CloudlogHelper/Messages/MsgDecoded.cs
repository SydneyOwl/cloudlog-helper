using System;
using System.Collections.Generic;
using CloudlogHelper.Models;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Messages;

public struct MsgDecoded
{
    public Decode DecodedData { get; set; }
}