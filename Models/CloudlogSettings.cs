using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CloudlogHelper.Validation;
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
    [Reactive] [JsonProperty] public string CloudlogStationId { get; set; } = string.Empty;

    [Reactive]
    [JsonProperty]
    public ObservableCollection<StationInfo> AvailableCloudlogStationInfo { get; set; } = new();

    public IObservable<bool> IsCloudlogValid => this.WhenAnyValue(
        x => x.CloudlogUrl,
        x => x.CloudlogApiKey,
        x => x.CloudlogStationId,
        (url, key, id) => !IsCloudlogHasErrors()
    );

    public bool IsCloudlogHasErrors()
    {
        return IsPropertyHasErrors(nameof(CloudlogUrl)) || IsPropertyHasErrors(nameof(CloudlogApiKey)) ||
               IsPropertyHasErrors(nameof(CloudlogStationId));
    }

    private bool IsPropertyHasErrors(string propertyName)
    {
        return GetErrors(propertyName).Cast<string>().Any();
    }

    public CloudlogSettings DeepClone()
    {
        return JsonConvert.DeserializeObject<CloudlogSettings>(JsonConvert.SerializeObject(this))!;
    }

    protected bool Equals(CloudlogSettings other)
    {
        return CloudlogUrl == other.CloudlogUrl && CloudlogApiKey == other.CloudlogApiKey &&
               CloudlogStationId == other.CloudlogStationId;
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
        return HashCode.Combine(CloudlogUrl, CloudlogApiKey, CloudlogStationId);
    }
}