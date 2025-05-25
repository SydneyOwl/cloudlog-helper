using System;
using System.Threading.Tasks;
using CloudlogHelper.Resources;
using Flurl.Http;
using NLog;

namespace CloudlogHelper.Utils;

public class ClublogUtil
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public static async Task<string> TestClublogConnectionAsync(string callsign, string password, string email)
    {
        var result = await DefaultConfigs.ClublogTestEndpoint
            .AllowHttpStatus(200, 400, 500, 403)
            .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
            .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultRequestTimeout))
            .PostUrlEncodedAsync(new
            {
                email,
                password,
                call = callsign,
                type = "dxqsl",
                startyear = 1999,
                endyear = 1999
            });
        var responseText = await result.GetStringAsync();
        var code = result.StatusCode;
        ClassLogger.Debug($"Got response from `TestClublogConnectionAsync`: {code} {responseText}");
        if (code == 200) return string.Empty;
        return responseText;
    }

    public static async Task<string> UploadQSOToClublogAsync(string callsign, string password, string email,
        string adif)
    {
        var result = await DefaultConfigs.ClublogQsoUploadEndpoint
            .AllowHttpStatus(200, 400, 500, 403)
            .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
            .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultRequestTimeout))
            .PostUrlEncodedAsync(new
            {
                email,
                password,
                callsign,
                adif,
                api = DefaultConfigs.Clkk
            });

        var responseText = await result.GetStringAsync();
        var code = result.StatusCode;
        if (code == 200) return string.Empty;
        return responseText;
    }
}