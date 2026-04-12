using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using System.Threading;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Resources.DXCC;
using CloudlogHelper.Resources.Language;
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
    private static readonly ResourceManager LanguageResourceManager = Language.ResourceManager;
    private static readonly ResourceManager DxccResourceManager = DXCC.ResourceManager;

    public static SupportedLanguageInfo[] GetSupportedLanguageInfos()
    {
        return Enum.GetValues(typeof(SupportedLanguage))
            .Cast<SupportedLanguage>()
            .Where(x => x is not SupportedLanguage.NotSpecified)
            .Select(x =>
            {
                var description = x.GetType().GetField(x.ToString())!.GetCustomAttribute<DescriptionAttribute>()
                    ?.Description;
                if (string.IsNullOrEmpty(description)) description = x.ToString();
                return new SupportedLanguageInfo
                {
                    LanguageName = description,
                    Language = x
                };
            })
            .ToArray();
    }
    
    
    /// <summary>
    ///  Cleans a string to contain only English letters, numbers, and underscores.
    ///  Multiple consecutive underscores are merged into a single one.
    ///  this is used for dxcc resx key
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public static string ParseToDXCCKey(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var result = Regex.Replace(input, @"[^a-zA-Z0-9_]", "_");
        result = Regex.Replace(result, @"_+", "_");
        result = result.Trim();
        
        // check if this translation exists
        // if not we use original key
        if (string.IsNullOrEmpty(DxccResourceManager.GetString(result, DXCC.Culture ?? CultureInfo.CurrentUICulture)))
        {
            return input;
        }
        
        return result;
    }

    /// <summary>
    ///     Gets the translated string for the specified key.
    /// </summary>
    /// <param name="key">The translation key to look up.</param>
    /// <returns>The translated string, or empty string if not found.</returns>
    public static string GetString(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        var culture = Language.Culture ?? CultureInfo.CurrentUICulture;
        return LanguageResourceManager.GetString(key, culture)
               ?? DxccResourceManager.GetString(key, DXCC.Culture ?? culture)
               ?? key;
    }

    public static string GetDxccString(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        return DxccResourceManager.GetString(key, DXCC.Culture ?? CultureInfo.CurrentUICulture) ?? key;
    }

    public static string GetDxccDisplayName(string? originalName)
    {
        if (string.IsNullOrWhiteSpace(originalName))
        {
            return GetString(Language.Unknown);
        }

        var key = ParseToDXCCKey(originalName);
        return key == originalName ? originalName : GetDxccString(key);
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
            
            if (lanName.Contains("zh"))
            {
                if (lanName is "zh-tw" or "zh-hk" or "zh-mo") return SupportedLanguage.TraditionalChinese;
                return SupportedLanguage.SimplifiedChinese;
            }
            
            if (lanName.Contains("ja")) return SupportedLanguage.Japanese;
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
            SupportedLanguage.SimplifiedChinese => new CultureInfo("zh-Hans"),
            SupportedLanguage.TraditionalChinese => new CultureInfo("zh-Hant"),
            SupportedLanguage.Japanese => new CultureInfo("ja-JP"),
            _ => new CultureInfo("en-US")
        };
    }

    public static void ApplyCulture(SupportedLanguage language)
    {
        var culture = GetCultureInfo(language);
        Thread.CurrentThread.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        Language.Culture = culture;
        DXCC.Culture = culture;
    }
}
