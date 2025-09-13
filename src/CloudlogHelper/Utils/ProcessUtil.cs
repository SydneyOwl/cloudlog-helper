using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CloudlogHelper.Utils;

public class ProcessUtil
{
    public static async Task ExecFile(
        string binaryFile,
        string[] options,
        Action<string, string> callback,
        CancellationToken token = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = binaryFile,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var option in options)
        {
            startInfo.ArgumentList.Add(option);
        }

        using var process = new Process();
        process.StartInfo = startInfo;
        process.Start();
            
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
            
        await process.WaitForExitAsync(token);

        callback(stdout, stderr);
    }
}