using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Shapes;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService.Attributes;
using Path = System.IO.Path;

namespace CloudlogHelper.LogService;

// https://lotw.arrl.org/lotw-help/cmdline/

[LogService("LoTW", Description = "LoTW Log Service")]
public class LoTWThirdPartyLogService : ThirdPartyLogService
{
    [UserInput("TQSL Path", InputType = FieldType.FilePicker)]
    public string LotwFilePath { get; set; }
    
    public override Task TestConnectionAsync(CancellationToken token)
    {
        throw new System.NotImplementedException();
    }

    public override Task UploadQSOAsync(string? adif, CancellationToken token)
    {
        throw new System.NotImplementedException();
    }

    public async override Task PreInitAsync()
    {
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