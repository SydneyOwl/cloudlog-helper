using System.Collections.ObjectModel;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using CloudlogHelper.Validation;
using Newtonsoft.Json;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;

public class QsoSyncAssistantSettings : ReactiveValidationObject
{
    public QsoSyncAssistantSettings()
    {
        
    }

    public void ApplyValidationRules()
    {
        this.ClearValidationRules();
        
        this.ValidationRule(x => x.CloudlogUserName,
            SettingsValidation.CheckStringNotNull,
            TranslationHelper.GetString(LangKeys.notnull)
        );

        this.ValidationRule(x => x.CloudlogPassword,
            SettingsValidation.CheckStringNotNull,
            TranslationHelper.GetString(LangKeys.notnull)
        );
    }

    [Reactive] [JsonProperty] public bool ExecuteOnStart { get; set; }
    [Reactive] [JsonProperty] public string? CloudlogUserName { get; set; }
    [Reactive] [JsonProperty] public string? CloudlogPassword { get; set; }
    [Reactive] [JsonProperty] public ObservableCollection<string>? LocalLogPath { get; set; }
    [Reactive] [JsonProperty] public int CloudlogQSODayRange { get; set; } = 120;
    [Reactive] [JsonProperty] public int LocalQSOSampleCount { get; set; } = 50;

    public bool IsQsoSyncAssistantSettingsHasErrors()
    {
        return string.IsNullOrEmpty(CloudlogUserName) || string.IsNullOrEmpty(CloudlogPassword)
                                                      || LocalLogPath?.Count <= 0;
    }
}