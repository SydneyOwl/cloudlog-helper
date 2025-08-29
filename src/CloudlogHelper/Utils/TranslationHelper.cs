using System;
using System.Globalization;
using System.Threading;
using Avalonia.Markup.Xaml.MarkupExtensions;
using CloudlogHelper.Enums;
using NLog;

namespace CloudlogHelper.Utils;

/// <summary>
///     Provides helper methods for translation and language support.
/// </summary>
public static class TranslationHelper
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     Gets the translated string for the specified key.
    /// </summary>
    /// <param name="key">The translation key to look up.</param>
    /// <returns>The translated string, or empty string if not found.</returns>
    public static string GetString(string key)
    {
        return I18NExtension.Translate(key) ?? string.Empty;
    }

    /// <summary>
    ///     Detects the system's default language and maps it to a supported language.
    /// </summary>
    /// <returns>The detected <see cref="SupportedLanguage" /> (defaults to English if not matched).</returns>
    public static SupportedLanguage DetectDefaultLanguage()
    {
        try
        {
            var lanName = Thread.CurrentThread.CurrentCulture.Name.ToLower();
            if (lanName.Contains("en")) return SupportedLanguage.English;
            if (lanName.Contains("zh")) return SupportedLanguage.SimplifiedChinese;
        }
        catch (Exception ex)
        {
            ClassLogger.Warn(ex, "Failed to detect default language. Using english instead.");
        }

        return SupportedLanguage.English;
    }

    /// <summary>
    ///     Gets the CultureInfo object corresponding to the specified supported language.
    /// </summary>
    /// <param name="language">The language to get CultureInfo for.</param>
    /// <returns>CultureInfo object for the specified language (defaults to en-US if not matched).</returns>
    public static CultureInfo GetCultureInfo(SupportedLanguage language)
    {
        return language switch
        {
            SupportedLanguage.English => new CultureInfo("en-US"),
            SupportedLanguage.SimplifiedChinese => new CultureInfo("zh-CN"),
            _ => new CultureInfo("en-US")
        };
    }
}