using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Models;

namespace CloudlogHelper.Services.Interfaces;

public interface IRigctldService
{
    void InitScheduler();
    bool IsRigctldClientRunning();

    Task<(bool, string)> StartOnetimeRigctldAsync(string args,
        int timeoutMilliseconds = 5000);

    Task<(bool, string)> RestartRigctldBackgroundProcessAsync(string args,
        bool ignoreIfRunning = false,
        int timeoutMilliseconds = 5000);

    Task<string> ExecuteCommandInScheduler(string host, int port, string cmd, bool highPriority);
    
    Task<RadioData> GetAllRigInfo(string ip, int port, bool reportRfPower, bool reportSplitInfo, CancellationToken token);

    List<RigInfo> ParseAllModelsFromRawOutput(string rawOutput);

    string GenerateRigctldCmdArgs(string radioId, string port, bool disablePtt = false,
        bool allowExternal = false);

    void TerminateBackgroundProcess();

    void TerminateOnetimeProcess();
}