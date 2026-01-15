using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Serialization;
using CloudlogHelper.Validation;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;

/// <summary>
///     Settings of cloudlog.
/// </summary>
public class CloudlogSettings : ReactiveValidationObject
{
    [Reactive] public string CloudlogUrl { get; set; } = string.Empty;
    [Reactive] public string CloudlogApiKey { get; set; } = string.Empty;
    
    [Reactive] public string? CloudlogStationInfoId { get; set; }
    
    [JsonIgnore]
    public StationInfo? CloudlogStationInfo
    {
        get => AvailableCloudlogStationInfo.FirstOrDefault(s => s.StationId == CloudlogStationInfoId);
        set => CloudlogStationInfoId = value?.StationId;
    }

    [Reactive] public bool AutoQSOUploadEnabled { get; set; } = true;
    [Reactive] public bool AutoPollStationStatus { get; set; } = true;

    [Reactive]
    public ObservableCollection<StationInfo> AvailableCloudlogStationInfo { get; set; } = new();

    [JsonIgnore]
    public IObservable<bool> IsCloudlogValid => this.WhenAnyValue(
        x => x.CloudlogUrl,
        x => x.CloudlogApiKey,
        x => x.CloudlogStationInfo,
        (url, key, id) => !IsCloudlogHasErrors()
    );
   
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ObservableCollection<StationInfo>))]
    public CloudlogSettings(){}
    
    public void ReinitRules()
    {
        this.ClearValidationRules();
        var cloudlogRulesUrl =
            this.WhenAnyValue(
                x => x.CloudlogUrl,
                url =>
                {
                    var errs = new List<string>
                    {
                        SettingsValidation.ValidateNotEmpty(url),
                        SettingsValidation.ValidateStartsWithHttp(url),
                        SettingsValidation.ValidateNotEndsWithApiQso(url),
                        SettingsValidation.ValidateSpace(url)
                    };
                    return errs;
                });

        var cloudlogRulesKey =
            this.WhenAnyValue(
                x => x.CloudlogApiKey,
                key =>
                {
                    var errs = new List<string>
                    {
                        SettingsValidation.ValidateNotEmpty(key),
                        SettingsValidation.ValidateSpace(key)
                    };
                    return errs;
                });

        // This makes sure only one err is displayed each time
        this.ValidationRule(x => x.CloudlogUrl,
            cloudlogRulesUrl,
            b => b.All(string.IsNullOrEmpty),
            a => a.FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? string.Empty
        );

        this.ValidationRule(
            x => x.CloudlogApiKey,
            cloudlogRulesKey,
            b => b.All(string.IsNullOrEmpty),
            a => a.FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? string.Empty
        );
    }


    public bool IsCloudlogHasErrors(bool checkStationId = false)
    {
        return !string.IsNullOrEmpty(SettingsValidation.ValidateNotEmpty(CloudlogUrl)) ||
               !string.IsNullOrEmpty(SettingsValidation.ValidateStartsWithHttp(CloudlogUrl)) ||
               !string.IsNullOrEmpty(SettingsValidation.ValidateNotEndsWithApiQso(CloudlogUrl)) ||
               !string.IsNullOrEmpty(SettingsValidation.ValidateSpace(CloudlogUrl)) ||
               !string.IsNullOrEmpty(SettingsValidation.ValidateNotEmpty(CloudlogApiKey)) ||
               !string.IsNullOrEmpty(SettingsValidation.ValidateSpace(CloudlogApiKey)) ||
               (checkStationId && string.IsNullOrEmpty(CloudlogStationInfo?.StationId));
        // string.IsNullOrEmpty(CloudlogStationId);
        // return IsPropertyHasErrors(nameof(CloudlogUrl)) || IsPropertyHasErrors(nameof(CloudlogApiKey)) ||
        //        IsPropertyHasErrors(nameof(CloudlogStationId));
    }


    private bool IsPropertyHasErrors(string propertyName)
    {
        return GetErrors(propertyName).Cast<string>().Any();
    }

    protected bool Equals(CloudlogSettings other)
    {
        return CloudlogUrl == other.CloudlogUrl && CloudlogApiKey == other.CloudlogApiKey &&
               CloudlogStationInfo?.StationId == other.CloudlogStationInfo?.StationId &&
               AutoQSOUploadEnabled == other.AutoQSOUploadEnabled &&
               AutoPollStationStatus == other.AutoPollStationStatus;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((CloudlogSettings)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(CloudlogUrl, CloudlogApiKey, CloudlogStationInfo, AutoQSOUploadEnabled,
            AutoPollStationStatus);
    }
}