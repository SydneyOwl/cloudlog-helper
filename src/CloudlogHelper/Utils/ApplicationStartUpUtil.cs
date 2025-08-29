using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CloudlogHelper.Utils;

public class ApplicationStartUpUtil
{
    public static void ResetApplication()
    {
        RestartApplicationWithArgs("--reinit-db --reinit-settings --reinit-hamlib");
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
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var winPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(winPath, "CloudlogHelper");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var linuxPath = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ??
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

            return Path.Combine(linuxPath, "CloudlogHelper");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
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

    public static Stream? GetResourceStream(string name)
    {
        var assemblyNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();
        var path = assemblyNames.FirstOrDefault(x => x.Contains(name), null);
        if (string.IsNullOrEmpty(path)) throw new FileNotFoundException("Resource not found: " + name);
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(path);
    }
}