using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService.Attributes;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using NLog;
using Path = System.IO.Path;

namespace CloudlogHelper.LogService;

// https://lotw.arrl.org/lotw-help/cmdline/

[LogService("LoTW", Description = "LoTW Log Service")]
public class LoTWThirdPartyLogService : ThirdPartyLogService
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public string?[]? Stations = Array.Empty<string>();

    [UserInput("tqslpath", InputType = FieldType.FilePicker)]
    public string LotwFilePath { get; set; }


    [UserInput("stationname", InputType = FieldType.ComboBox, SelectionsArrayName = nameof(Stations))]
    public string? StationName { get; set; }

    [UserInput("tqslpassword", InputType = FieldType.Password, IsRequired = false)]
    public string? TqslPassword { get; set; }

    public override async Task TestConnectionAsync(CancellationToken token)
    {
        await ProcessUtil.ExecFile(LotwFilePath, new[] { "-q", "-v" },
            (stdout, stderr) => { ClassLogger.Trace($"Lotw stderr detected: {stderr}"); }, token).ConfigureAwait(false);
    }

    public override async Task UploadQSOAsync(string? adif, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(StationName)) throw new ArgumentException("Please select station name first!");

        var adifWithHeader = new StringBuilder(AdifUtil.GenerateHeader());
        adifWithHeader.AppendLine(adif);
        var tempFileName = Path.Join(DefaultConfigs.DefaultTempFilePath, Guid.NewGuid().ToString().Replace("-", ""));
        await File.WriteAllTextAsync(tempFileName, adifWithHeader.ToString(), token).ConfigureAwait(false);

        var args = new List<string>();
        args.Add("-a");
        args.Add("all");
        args.Add("-l");
        args.Add(StationName);
        if (!string.IsNullOrWhiteSpace(TqslPassword))
        {
            args.Add("-p");
            args.Add(TqslPassword);
        }

        args.Add("-q");
        args.Add("-x");
        args.Add("-d");
        args.Add("-u");
        args.Add(tempFileName);

        var result = string.Empty;

        await ProcessUtil.ExecFile(LotwFilePath, args.ToArray(), (stdout, stderr) =>
        {
            result = stderr;
            ClassLogger.Trace($"Lotw stderr detected: {stderr}");
            ClassLogger.Trace($"Lotw stdout detected: {stdout}");
        }, token).ConfigureAwait(false);

        if (result.Contains("Final Status: Success(0)")) return;
        throw new Exception($"Upload failed: {result}");
    }

    public override async Task PreInitAsync(CancellationToken token)
    {
        var stationDataPath = string.Empty;
        var combineSTP = string.Empty;
        if (OperatingSystem.IsWindows())
        {
            combineSTP = Path.Combine(Environment.GetEnvironmentVariable("APPDATA") ?? string.Empty,
                "TrustedQSL",
                "station_data");
            if (File.Exists(combineSTP))
            {
                stationDataPath = combineSTP;
            }
            else
            {
                combineSTP = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? string.Empty,
                    "TrustedQSL",
                    "station_data");
                if (File.Exists(combineSTP)) stationDataPath = combineSTP;
            }
        }
        else
        {
            combineSTP = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? string.Empty,
                ".tqsl",
                "station_data");
            if (File.Exists(combineSTP)) stationDataPath = combineSTP;
        }

        // check if file valid
        if (!string.IsNullOrEmpty(stationDataPath))
        {
            var readAllText = await File.ReadAllTextAsync(stationDataPath, token);
            var doc = XDocument.Parse(readAllText);

            var nameList = doc.Root?.Elements("StationData")
                .Select(stationElement => stationElement.Attribute("name")?.Value)
                .ToList();
            Stations = nameList?.ToArray();
            if (string.IsNullOrWhiteSpace(StationName) &&
                Stations is not null && Stations.Length > 0 &&
                !string.IsNullOrWhiteSpace(Stations[0]))
            {
                StationName = Stations[0];
            }
        }

        // find default tqsl path
        if (string.IsNullOrWhiteSpace(LotwFilePath))
        {
            if (OperatingSystem.IsWindows())
            {
                var combine = Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? string.Empty,
                    "TrustedQSL",
                    "tqsl.exe");
                if (File.Exists(combine)) LotwFilePath = combine;
            }

            if (OperatingSystem.IsLinux())
            {
                if (File.Exists("/usr/bin/tqsl")) LotwFilePath = "/usr/bin/tqsl";
                if (File.Exists("/usr/local/bin/tqsl")) LotwFilePath = "/usr/local/bin/tqsl";
            }
        }
    }
}