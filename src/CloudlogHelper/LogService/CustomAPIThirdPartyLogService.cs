using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService.Attributes;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using Flurl.Http;

namespace CloudlogHelper.LogService;

[LogService("Custom API", Description = "Custom API Log Service")]
public class CustomAPIThirdPartyLogService : ThirdPartyLogService
{
    [UserInput("qsouploadendpoint", InputType = FieldType.Text, Description = "Endpoints for uploading QSO info. Spilt by Semicolon. See readme for more.")]
    public string QSOEndpoint { get; set; }
    
    [UserInput("riginfouploadendpoint", InputType = FieldType.Text, Description = "Endpoints for uploading RIG info. Spilt by Semicolon. See readme for more.")]
    public string RIGEndpoint { get; set; }
    
    [UserInput("uploadriginfo", InputType = FieldType.CheckBox)]
    public bool AllowUploadRigInfo { get; set; } = false;
    
    public override Task TestConnectionAsync(CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public override async Task UploadQSOAsync(string? adif, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(QSOEndpoint))return;

        var eps = QSOEndpoint.Split(";")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.WithHeader("Content-Type", "application/json")
                .PostJsonAsync(new
                {
                    adif = adif,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }, cancellationToken: token))
            .ToArray();
        
        await Task.WhenAll(eps);
    }

    public override async Task UploadRigInfoAsync(RadioData rigData, CancellationToken token)
    {
        if (!AllowUploadRigInfo)return;
        
        var payload = new RadioApiCallV2
        {
            Radio = rigData.RigName ?? "Unknown",
            Frequency = rigData.FrequencyTx,
            Mode = rigData.ModeTx,
            FrequencyRx = rigData.FrequencyRx,
            ModeRx = rigData.ModeRx,
            Power = rigData.Power
        };
        
        var eps = RIGEndpoint.Split(";")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x
                .WithHeader("Content-Type", "application/json")
                .PostStringAsync(JsonSerializer.Serialize(payload, SourceGenerationContext.Default.RadioApiCallV2), cancellationToken: token)
            )
            .ToArray();
        
        await Task.WhenAll(eps);
    }
}