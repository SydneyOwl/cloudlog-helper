using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CloudlogHelper.Utils;

public class ApplicationStartUpUtil
{
    public static void RestartApplicationWithArgs(params string[] args)
    {
        var executablePath = Process.GetCurrentProcess().MainModule!.FileName;
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = string.Join(" ", args),
            UseShellExecute = true
        };
        Process.Start(startInfo);
        Environment.Exit(0);
    }
    
    public static string GetConfigDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var winPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(winPath, "CloudlogHelper");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var linuxPath = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? 
                   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            
            return Path.Combine(linuxPath, "CloudlogHelper");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var macPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Preferences"
            );
            return Path.Combine(macPath, "CloudlogHelper");
        }
        throw new PlatformNotSupportedException("Unsupported OS");
    }

}