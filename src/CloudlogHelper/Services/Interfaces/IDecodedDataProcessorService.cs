using System.Net;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Services.Interfaces;

public interface IDecodedDataProcessorService
{
    /// <summary>
    ///     Queue a decoded message for batch processing.
    /// </summary>
    /// <param name="decode">The decoded message.</param>
    /// <param name="source">
    ///     UDP endpoint it arrived from. The chart's client key has to be derived by the same
    ///     function that produces the registry key, or a blank Id would key the chart differently
    ///     from the tab it is filtered by and that client's chart would stay empty.
    /// </param>
    void ProcessDecoded(Decode decode, EndPoint? source);

    void UpdateClientBand(string clientId, string band, EndPoint? source);
}