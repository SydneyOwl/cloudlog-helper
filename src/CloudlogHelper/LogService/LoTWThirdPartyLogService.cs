using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Controls.Shapes;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService.Attributes;
using CloudlogHelper.Models;
using CloudlogHelper.Utils;
using NLog;
using Path = System.IO.Path;

namespace CloudlogHelper.LogService;

// https://lotw.arrl.org/lotw-help/cmdline/

[LogService("LoTW", Description = "LoTW Log Service")]
public class LoTWThirdPartyLogService : ThirdPartyLogService
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    
    [UserInput("TQSL Path", InputType = FieldType.FilePicker)]
    public string LotwFilePath { get; set; }
    
    
    [UserInput("Station Name", InputType = FieldType.ComboBox, SelectionsArrayName = nameof(Stations))]
    public string? StationName { get; set; }
    
    [UserInput("TQSL Password", InputType = FieldType.Password)]
    public string? TqslPassword { get; set; }

    public string?[]? Stations = Array.Empty<string>();
    
    public override async Task TestConnectionAsync(CancellationToken token)
    {
        await ProcessUtil.ExecFile(LotwFilePath, new []{"-q", "-v"}, (stdout, stderr) =>
        {
            ClassLogger.Debug($"Lotw stderr detected: {stderr}");
        }, token);
    }

    public override async Task UploadQSOAsync(string? adif, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(StationName)) throw new ArgumentException("Please select station name first!");

        var generateHeader = new StringBuilder(AdifUtil.GenerateHeader());
        generateHeader.AppendLine(adif);
        var tempFileName = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFileName, generateHeader.ToString(), token);

        var args = new List<string>();
        args.Add("-a");
        args.Add("all");
        args.Add($"-l");
        args.Add(StationName);
        if (!string.IsNullOrWhiteSpace(TqslPassword))
        {
            args.Add($"-p");
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
            ClassLogger.Debug($"Lotw stderr detected: {stderr}");
            ClassLogger.Debug($"Lotw stdout detected: {stdout}");
        }, token);
        
        if(result.Contains("Final Status: Success(0)"))return;
        throw new Exception($"Upload failed: {result}");
    }

    public override void PreInitSync()
    {
        // find station infos
        if (Stations is null || Stations.Length == 0)
        {
            var stationDataPath = string.Empty;
            var combine = string.Empty;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                combine = Path.Combine(Environment.GetEnvironmentVariable("APPDATA") ?? string.Empty,
                    "TrustedQSL",
                    "station_data");
                if (File.Exists(combine))
                {
                    stationDataPath = combine;
                }
                else
                {
                    combine = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? string.Empty,
                        "TrustedQSL",
                        "station_data");
                    if (File.Exists(combine))
                    {
                        stationDataPath = combine;
                    }
                }
            }
            else
            {
                combine = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? string.Empty,
                    ".tqsl",
                    "station_data");
                if (File.Exists(combine))
                {
                    stationDataPath = combine;
                }
            }
            
            // check if file valid
            if (!string.IsNullOrEmpty(stationDataPath))
            {
                var readAllText = File.ReadAllText(stationDataPath);
                var doc = XDocument.Parse(readAllText);

                var nameList = doc.Root?.Elements("StationData")
                    .Select(stationElement => stationElement.Attribute("name")?.Value)
                    .ToList();
                Stations = nameList?.ToArray();
                if (Stations is not null && Stations.Length > 0 && !string.IsNullOrWhiteSpace(Stations[0])) StationName = Stations[0];
            }
        }

        // find default tqsl path
        if (string.IsNullOrWhiteSpace(LotwFilePath))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var combine = Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? string.Empty,
                    "TrustedQSL",
                    "tqsl.exe");
                if (File.Exists(combine))
                {
                    LotwFilePath = combine;
                }
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (File.Exists("/usr/bin/tqsl")) LotwFilePath =  "/usr/bin/tqsl";
                if (File.Exists("/usr/local/bin/tqsl")) LotwFilePath =  "/usr/local/bin/tqsl";
            }
        }
    }
}