using System;
using System.Linq;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using Newtonsoft.Json;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;

/// <summary>
///     UDP settings.
/// </summary>
public class UDPServerSettings : ReactiveValidationObject
{
    public UDPServerSettings()
    {
        this.ValidationRule(
            x => x.UDPPort,
            st =>
            {
                if (!int.TryParse(st, out var res)) return false;
                return res is <= 65535 and >= 0;
            },
            TranslationHelper.GetString("invalidport")
        );
        this.ValidationRule(
            x => x.RetryCount,
            st =>
            {
                if (!int.TryParse(st, out var res)) return false;
                return !string.IsNullOrEmpty(st) && res >= 0;
            },
            TranslationHelper.GetString("retrycountreq")
        );
    }

    [Reactive] [JsonProperty] public bool EnableUDPServer { get; set; } = true;
    [Reactive] [JsonProperty] public bool EnableConnectionFromOutside { get; set; }
    [Reactive] [JsonProperty] public string UDPPort { get; set; } = DefaultConfigs.UDPServerDefaultPort.ToString();


    [Reactive] [JsonProperty] public bool AutoUploadQSO { get; set; } = true;

    [Reactive] [JsonProperty] public string RetryCount { get; set; } = "3";

    public IObservable<bool> IsUDPConfigValid => this.WhenAnyValue(
        x => x.UDPPort,
        x => x.RetryCount,
        (a, b) =>
            !IsUDPConfigHasErrors()
    );

    private bool IsPropertyHasErrors(string propertyName)
    {
        return GetErrors(propertyName).Cast<string>().Any();
    }

    public bool IsUDPConfigHasErrors()
    {
        return IsPropertyHasErrors(nameof(UDPPort)) || IsPropertyHasErrors(nameof(RetryCount));
    }

    public UDPServerSettings DeepClone()
    {
        return JsonConvert.DeserializeObject<UDPServerSettings>(JsonConvert.SerializeObject(this))!;
    }

    protected bool Equals(UDPServerSettings other)
    {
        return EnableUDPServer == other.EnableUDPServer &&
               EnableConnectionFromOutside == other.EnableConnectionFromOutside && UDPPort == other.UDPPort &&
               AutoUploadQSO == other.AutoUploadQSO && RetryCount == other.RetryCount;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((UDPServerSettings)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(EnableUDPServer, EnableConnectionFromOutside, UDPPort, AutoUploadQSO, RetryCount);
    }
}