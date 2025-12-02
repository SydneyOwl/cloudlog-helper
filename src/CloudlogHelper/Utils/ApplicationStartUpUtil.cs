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

    public static Stream?[] GetResourceStream(string name)
    {
        if (name.StartsWith(".") || name.StartsWith("*.")) return GetResourcesWithExtension(name);

        return new[] { GetSingleResourceStream(name) };
    }

    public static Stream? GetSingleResourceStream(string name)
    {
        var assemblyNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();
        var path = assemblyNames.FirstOrDefault(x => x.Contains(name), null);
        return string.IsNullOrEmpty(path) ? 
            throw new FileNotFoundException("Resource not found: " + name) : 
            Assembly.GetExecutingAssembly().GetManifestResourceStream(path);
    }

    private static Stream?[] GetResourcesWithExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            throw new ArgumentException("Extension cannot be null or empty", nameof(extension));

        var normalizedExtension = extension.StartsWith(".") ? extension : "." + extension;
    
        var assembly = Assembly.GetExecutingAssembly();
        var allResources = assembly.GetManifestResourceNames();
    
        var target = allResources
            .Where(resourceName => resourceName.EndsWith(normalizedExtension, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        
        return target.Select(resourceName => assembly.GetManifestResourceStream(resourceName)).ToArray();
    }
}