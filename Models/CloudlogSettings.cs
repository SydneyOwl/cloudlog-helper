using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CloudlogHelper.Validation;
using DynamicData;
using Newtonsoft.Json;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;

/// <summary>
///     Settings of cloudlog.
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class CloudlogSettings : ReactiveValidationObject
{
    public CloudlogSettings()
    {
        
    }

    public void ApplyValidationRules()
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

    [Reactive] [JsonProperty] public string CloudlogUrl { get; set; } = string.Empty;
    [Reactive] [JsonProperty] public string CloudlogApiKey { get; set; } = string.Empty;
    [Reactive] [JsonProperty] public StationInfo? CloudlogStationInfo { get; set; }

    [Reactive] [JsonProperty] public bool AutoQSOUploadEnabled { get; set; } = true;

    [Reactive]
    [JsonProperty]
    public ObservableCollection<StationInfo> AvailableCloudlogStationInfo { get; set; } = new();

    public IObservable<bool> IsCloudlogValid => this.WhenAnyValue(
        x => x.CloudlogUrl,
        x => x.CloudlogApiKey,
        x => x.CloudlogStationInfo,
        (url, key, id) => !IsCloudlogHasErrors()
    );
    

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
               AutoQSOUploadEnabled == other.AutoQSOUploadEnabled;
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
        return HashCode.Combine(CloudlogUrl, CloudlogApiKey, CloudlogStationInfo, AutoQSOUploadEnabled);
    }
}