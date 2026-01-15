using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using CloudlogHelper.Validation;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;

public class QsoSyncAssistantSettings : ReactiveValidationObject
{
    [Reactive] public bool ExecuteOnStart { get; set; }
    [Reactive] public string? CloudlogUserName { get; set; }
    [Reactive] public string? CloudlogPassword { get; set; }
    [Reactive] public ObservableCollection<string>? LocalLogPath { get; set; }
    [Reactive] public int CloudlogQSODayRange { get; set; } = 120;
    [Reactive] public int LocalQSOSampleCount { get; set; } = 50;

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ObservableCollection<string>))]
    public QsoSyncAssistantSettings()
    {
        
    }

    public void ReinitRules()
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

    public bool IsQsoSyncAssistantSettingsHasErrors()
    {
        return string.IsNullOrEmpty(CloudlogUserName) || string.IsNullOrEmpty(CloudlogPassword)
                                                      || LocalLogPath?.Count <= 0;
    }
}