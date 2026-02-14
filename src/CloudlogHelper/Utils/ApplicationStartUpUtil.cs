using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CloudlogHelper.Utils;

public class ApplicationStartUpUtil
{    
    private const string AllowedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static void ResetApplication()
    {
        RestartApplicationWithArgs("--reinit-db --reinit-settings --reinit-hamlib");
    }
    
    public static string GenerateRandomInstanceName(int length)
    {
        var random = new Random();
        var result = new StringBuilder(length);

        for (var i = 0; i < length; i++)
        {
            var index = random.Next(AllowedChars.Length);
            result.Append(AllowedChars[index]);
        }

        return $"CLH-{result}";
    }
    
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
        if (OperatingSystem.IsWindows())
        {
            var winPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(winPath, "CloudlogHelper");
        }

        if (OperatingSystem.IsLinux())
        {
            var linuxPath = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ??
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

            return Path.Combine(linuxPath, "CloudlogHelper");
        }

        if (OperatingSystem.IsMacOS())
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

    public static Stream? GetSingleResourceStream(string name)
    {
        var assemblyNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();
        var path = assemblyNames.FirstOrDefault(x => x.Contains(name), null);
        return string.IsNullOrEmpty(path) ? 
            throw new FileNotFoundException("Resource not found: " + name) : 
            Assembly.GetExecutingAssembly().GetManifestResourceStream(path);
    }
}