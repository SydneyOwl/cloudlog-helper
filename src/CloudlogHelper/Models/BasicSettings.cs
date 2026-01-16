using CloudlogHelper.Enums;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;

public class BasicSettings : ReactiveValidationObject
{
    [Reactive] public string? MyMaidenheadGrid { get; set; }
    [Reactive] public bool DisableAllCharts { get; set; }

    /// <summary>
    ///     Default language of this application.
    /// </summary>
    [Reactive]
    public SupportedLanguage LanguageType { get; set; } = SupportedLanguage.NotSpecified;
}