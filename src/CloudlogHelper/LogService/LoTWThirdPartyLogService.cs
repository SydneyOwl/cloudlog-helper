using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService.Attributes;

namespace CloudlogHelper.LogService;

// https://lotw.arrl.org/lotw-help/cmdline/

[LogService("LoTW", Description = "LoTW Log Service")]
public class LoTWThirdPartyLogService : ThirdPartyLogService
{
    [UserInput("LoTW.exe", InputType = FieldType.FilePicker)]
    public string lotwFilePath { get; set; }
    public override Task TestConnectionAsync(CancellationToken token)
    {
        throw new System.NotImplementedException();
    }

    public override Task UploadQSOAsync(string? adif, CancellationToken token)
    {
        throw new System.NotImplementedException();
    }
}