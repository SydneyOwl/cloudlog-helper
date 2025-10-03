﻿using System;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService.Attributes;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using Flurl.Http;

namespace CloudlogHelper.LogService;

[LogService("Clublog", Description = "Clublog Log Service")]
public class ClublogThirdPartyLogService : ThirdPartyLogService
{
    private const string ClublogQsoUploadEndpoint = "https://clublog.org/realtime.php";

    private const string ClublogTestEndpoint = "https://clublog.org/getadif.php";


    [UserInput("callsign", Description = "Enter your callsign registered at clublog.")]
    public string Callsign { get; set; }

    [UserInput("password", InputType = FieldType.Password)]
    public string Password { get; set; }

    [UserInput("email")] public string Email { get; set; }


    public override async Task TestConnectionAsync(CancellationToken token)
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
            }, cancellationToken: token);
        var responseText = await result.GetStringAsync();
        var code = result.StatusCode;
        if (code == 200) return;
        throw new Exception(string.IsNullOrWhiteSpace(responseText)
            ? $"Failed to test connection. Resp code: {code}. Check ur login info!"
            : responseText);
    }

    public override async Task UploadQSOAsync(string? adif, CancellationToken token)
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
                adif,
                api = DefaultConfigs.Clkk
            }, cancellationToken: token);

        var responseText = await result.GetStringAsync();
        var code = result.StatusCode;
        if (code == 200) return;
        throw new Exception(responseText);
    }
}