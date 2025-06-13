using System;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Validation;

/// <summary>
///     Some commonly used validations.
/// </summary>
public static class SettingsValidation
{
    public static string ValidateNotEmpty(string url)
    {
        if (string.IsNullOrEmpty(url)) return TranslationHelper.GetString("notnull");

        return string.Empty;
    }

    public static string ValidateStartsWithHttp(string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return TranslationHelper.GetString("startwithhttp");

        return string.Empty;
    }

    public static string ValidateNotEndsWithApiQso(string url)
    {
        if (url.EndsWith("/api/qso", StringComparison.OrdinalIgnoreCase))
            return TranslationHelper.GetString("onlymaindomain");

        return string.Empty;
    }

    public static string ValidateSpace(string url)
    {
        if (url.Trim() != url)
            return TranslationHelper.GetString("spacenotallowed");
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
}