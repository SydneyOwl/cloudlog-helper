using CloudlogHelper.Enums;

namespace CloudlogHelper.Models;

public struct SupportedLanguageInfo
{
    public string LanguageName { get; set; }
    public SupportedLanguage Language { get; set; }
}