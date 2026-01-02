using System;
using System.Net;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Validation;

/// <summary>
///     Some commonly used validations.
/// </summary>
public static class SettingsValidation
{
    public static string ValidateNotEmpty(string url)
    {
        if (string.IsNullOrEmpty(url)) return TranslationHelper.GetString(LangKeys.notnull);

        return string.Empty;
    }

    public static string ValidateStartsWithHttp(string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return TranslationHelper.GetString(LangKeys.startwithhttp);

        return string.Empty;
    }

    public static string ValidateNotEndsWithApiQso(string url)
    {
        if (url.EndsWith("/api/qso", StringComparison.OrdinalIgnoreCase))
            return TranslationHelper.GetString(LangKeys.onlymaindomain);

        return string.Empty;
    }

    public static string ValidateSpace(string url)
    {
        if (url.Trim() != url)
            return TranslationHelper.GetString(LangKeys.spacenotallowed);
        return string.Empty;
    }

    public static bool CheckStringNotNull(string? st)
    {
        return !string.IsNullOrEmpty(st);
    }

    public static bool CheckInt(string? st)
    {
        if (!int.TryParse(st, out var res)) return false;
        return !string.IsNullOrEmpty(st) && res >= 1;
    }

    public static bool CheckHttpPort(string? st)
    {
        if (!int.TryParse(st, out var res)) return false;
        return !string.IsNullOrEmpty(st) && res is >= 1 and <= 65535;
    }
    public static bool CheckHttpPort(int st)
    {
        return st is >= 1 and <= 65535;
    }

    public static bool CheckHttpIp(string? st)
    {
        return IPAddress.TryParse(st, out _);
    }
    
    public static bool CheckHost(string? st)
    {
        if (string.IsNullOrWhiteSpace(st))
        {
            return false;
        }

        return Uri.CheckHostName(st) switch
        {
            UriHostNameType.Unknown => false,
            UriHostNameType.Basic or UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6 => true,
            _ => false
        };
    }
}