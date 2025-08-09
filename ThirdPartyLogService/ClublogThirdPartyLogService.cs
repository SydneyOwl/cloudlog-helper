using System;
using System.Threading.Tasks;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.ThirdPartyLogService.Attributes;
using Flurl.Http;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ThirdPartyLogService;

[LogService("Clublog", Description = "Clublog Log Service")]
public class ClublogThirdPartyLogService : ThirdPartyLogService
{
    private const string ClublogQsoUploadEndpoint = "https://clublog.org/realtime.php";

    private const string ClublogTestEndpoint = "https://clublog.org/getadif.php";

    
    [UserInput("Callsign", Description = "Enter your callsign registered at clublog.")]
    public string Callsign { get; set; } 
    
    [UserInput("Password", InputType = FieldType.Password)]
    public string Password { get; set; } 
    
    [UserInput("Email")]
    public string Email { get; set; } 
    
    
    public override async Task TestConnectionAsync()
    {
        var result = await ClublogTestEndpoint
            .AllowHttpStatus(200, 400, 500, 403)
            .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
            .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultRequestTimeout))
            .PostUrlEncodedAsync(new
            {
                email = Email,
                password = Password,
                call = Callsign,
                type = "dxqsl",
                startyear = 1999,
                endyear = 1999
            });
        var responseText = await result.GetStringAsync();
        var code = result.StatusCode;
        if (code == 200)return;
        throw new Exception(string.IsNullOrWhiteSpace(responseText) 
            ? $"Failed to test connection. Resp code: {code}" 
            : responseText);
    }

    public override async Task UploadQSOAsync(string? adif)
    {
        var result = await ClublogQsoUploadEndpoint
            .AllowHttpStatus(200, 400, 500, 403)
            .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
            .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultRequestTimeout))
            .PostUrlEncodedAsync(new
            {
                email = Email,
                password = Password,
                callsign = Callsign,
                adif = adif,
                api = DefaultConfigs.Clkk
            });

        var responseText = await result.GetStringAsync();
        var code = result.StatusCode;
        if (code == 200)return;
        throw new Exception(responseText);
    }
}