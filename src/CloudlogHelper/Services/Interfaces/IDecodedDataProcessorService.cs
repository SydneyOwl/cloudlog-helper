using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Services.Interfaces;

public interface IDecodedDataProcessorService
{
    void ProcessDecoded(Decode decode);
}