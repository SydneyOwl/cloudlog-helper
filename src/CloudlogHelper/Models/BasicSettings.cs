using CloudlogHelper.Enums;
using Newtonsoft.Json;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;

public class BasicSettings : ReactiveValidationObject
{
    [Reactive] [JsonProperty] public string? MyMaidenheadGrid { get; set; }
    [Reactive] [JsonProperty] public bool DisableAllCharts { get; set; }

    /// <summary>
    ///     Default language of this application.
    /// </summary>
    [Reactive]
    [JsonProperty]
    public SupportedLanguage LanguageType { get; set; } = SupportedLanguage.NotSpecified;
}