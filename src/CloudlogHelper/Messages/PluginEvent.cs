using Google.Protobuf;

namespace CloudlogHelper.Messages;

public struct PluginEvent
{
    public IMessage? Message { get; set; }
}