using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService.Attributes;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using Flurl.Http;

namespace CloudlogHelper.LogService;

[LogService("HRDLOG", Description = "HRDLOG Log Service")]
public class HRDLogThirdPartyLogService : ThirdPartyLogService
{
    private const string HRDLOGUploadEndpoint = "http://robot.hrdlog.net/NewEntry.aspx";
    private const string HRDLOGOnAirEndpoint = "http://robot.hrdlog.net/OnAir.aspx";

    [UserInput("callsign")] public string Callsign { get; set; }

    [UserInput("Code", Description = "Upload code received via email after registration to HRDLOG.net")]
    public string Code { get; set; }

    [UserInput("uploadriginfo", InputType = FieldType.CheckBox)]
    public bool AllowUploadRigInfo { get; set; } = false;

    public override async Task TestConnectionAsync(CancellationToken token)
    {
        var result = await HRDLOGUploadEndpoint
            .PostUrlEncodedAsync(new
            {
                Callsign,
                Code,
                ADIFData = string.Empty
            }, cancellationToken: token);
        var responseText = await result.GetStringAsync();

        var xDocument = XDocument.Parse(responseText);
        XNamespace ns = "http://xml.hrdlog.com";

        var errorElement = xDocument.Descendants(ns + "error").FirstOrDefault()!.Value.Replace("\n", "").Trim();
        if (errorElement.Contains("A record should contain at least")) return;
        throw new Exception(errorElement);
    }

    public override async Task UploadQSOAsync(string? adif, CancellationToken token)
    {
        var result = await HRDLOGUploadEndpoint
            .PostUrlEncodedAsync(new
            {
                Callsign,
                Code,
                App = DefaultConfigs.DefaultApplicationName,
                ADIFData = adif
            }, cancellationToken: token);
        var responseText = await result.GetStringAsync();

        var xDocument = XDocument.Parse(responseText);
        XNamespace ns = "http://xml.hrdlog.com";

        var errorElement = xDocument.Descendants(ns + "error").FirstOrDefault();
        var insertElement = xDocument.Descendants(ns + "insert").FirstOrDefault();

        if (errorElement?.Value is not null)
            throw new Exception($"Error uploading qso: {errorElement.Value.Replace("\n", "").Trim()}");

        if (insertElement?.Value is not null)
            if (int.TryParse(insertElement.Value, out var res))
            {
                if (res == 1) return;
                if (res == 0) throw new Exception("Duplicate QSO!");
            }

        throw new Exception($"Error parsing result: {responseText.Trim()}");
    }

    public override async Task UploadRigInfoAsync(RadioData rigData, CancellationToken token)
    {
        if (!AllowUploadRigInfo) return;

        var result = await HRDLOGOnAirEndpoint
            .PostUrlEncodedAsync(new
            {
                Frequency = rigData.FrequencyTx,
                Mode = rigData.ModeTx,
                Radio = rigData.RigName,
                Callsign,
                Code,
                App = DefaultConfigs.DefaultApplicationName
            }, cancellationToken: token);
        var responseText = await result.GetStringAsync();

        var xDocument = XDocument.Parse(responseText);
        XNamespace ns = "http://xml.hrdlog.com";

        var errorElement = xDocument.Descendants(ns + "error").FirstOrDefault();
        var insertElement = xDocument.Descendants(ns + "insert").FirstOrDefault();

        if (errorElement?.Value is not null)
            throw new Exception($"Error uploading qso: {errorElement.Value.Replace("\n", "").Trim()}");

        if (insertElement?.Value is not null && insertElement.Value.Trim() == "OK") return;

        throw new Exception($"Error parsing result: {responseText.Trim()}");
    }
}