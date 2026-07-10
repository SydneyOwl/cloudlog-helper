using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using Flurl.Http;

namespace CloudlogHelper.LogService;

public abstract class ThirdPartyLogService
{
    /// <summary>
    ///     Determines whether to upload this qso automatically.
    /// </summary>
    public virtual bool AutoQSOUploadEnabled { get; set; } = false;

    /// <summary>
    ///     Determines whether to skip TLS certificate validation for this service.
    /// </summary>
    public virtual bool SkipTlsValidation { get; set; } = false;

    /// <summary>
    ///     Test connection of specified log service.
    /// </summary>
    public abstract Task TestConnectionAsync(CancellationToken token);

    /// <summary>
    ///     Upload qso to specified log system use customized logic.
    /// </summary>
    /// <param name="adif"></param>
    public abstract Task UploadQSOAsync(string? adif, CancellationToken token);

    public virtual Task UploadRigInfoAsync(RadioData rigData, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Preinit works. This will be called on application start.
    /// </summary>
    /// <returns></returns>
    public virtual Task PreInitAsync(CancellationToken token)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Creates a Flurl request configured with TLS validation settings.
    ///     When SkipTlsValidation is true, certificate errors are ignored.
    /// </summary>
    /// <param name="url">The endpoint URL.</param>
    /// <returns>A configured IFlurlRequest.</returns>
    protected IFlurlRequest CreateRequest(string url)
    {
        if (SkipTlsValidation)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            var client = new FlurlClient(new HttpClient(handler));
            return client.Request(url)
                .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
                .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultRequestTimeout));
        }

        return url
            .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
            .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultRequestTimeout));
    }
}
