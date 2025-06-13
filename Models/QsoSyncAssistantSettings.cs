using System.Collections.Generic;
using System.Collections.ObjectModel;
using CloudlogHelper.Utils;
using CloudlogHelper.Validation;
using Newtonsoft.Json;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;

public class QsoSyncAssistantSettings : ReactiveValidationObject
{
    public QsoSyncAssistantSettings()
    {
        this.ValidationRule(x => x.CloudlogUserName,
            SettingsValidation.CheckStringNotNull,
            TranslationHelper.GetString("notnull")
        );

        this.ValidationRule(x => x.CloudlogPassword,
            SettingsValidation.CheckStringNotNull,
            TranslationHelper.GetString("notnull")
        );
    }

    public bool IsQsoSyncAssistantSettingsHasErrors()
    {
        return string.IsNullOrEmpty(CloudlogUserName) || string.IsNullOrEmpty(CloudlogPassword)
                                                      || LocalLogPath?.Count <= 0;
    }
    [Reactive] [JsonProperty] public bool ExecuteOnStart { get; set; }
    [Reactive] [JsonProperty] public string? CloudlogUserName { get; set; }
    [Reactive] [JsonProperty] public string? CloudlogPassword { get; set; }
    [Reactive] [JsonProperty] public ObservableCollection<string>? LocalLogPath { get; set; }
    [Reactive] [JsonProperty] public int CloudlogQSOSampleCount { get; set; } = 200;
    [Reactive] [JsonProperty] public int LocalQSOSampleCount { get; set; } = 200;
}