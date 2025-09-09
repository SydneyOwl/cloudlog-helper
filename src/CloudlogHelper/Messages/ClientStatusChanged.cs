using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Messages;

public struct ClientStatusChanged
{
    public Status CurrStatus { get; set; }
}