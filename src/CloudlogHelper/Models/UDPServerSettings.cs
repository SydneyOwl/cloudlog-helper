using System;
using System.Linq;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using Newtonsoft.Json;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;

/// <summary>
///     UDP settings.
/// </summary>
public class UDPServerSettings : ReactiveValidationObject
{
    [Reactive] [JsonProperty] public bool EnableUDPServer { get; set; } = true;
    [Reactive] [JsonProperty] public bool EnableConnectionFromOutside { get; set; }
    [Reactive] [JsonProperty] public string UDPPort { get; set; } = DefaultConfigs.UDPServerDefaultPort.ToString();

    [Reactive] [JsonProperty] public string RetryCount { get; set; } = "3";

    [Reactive] [JsonProperty] public bool ForwardMessage { get; set; }
    [Reactive] [JsonProperty] public string ForwardAddress { get; set; }


    [Reactive] [JsonProperty] public bool ForwardMessageToHttp { get; set; }
    [Reactive] [JsonProperty] public string ForwardHttpAddress { get; set; }

    // === Notification groupbox

    /// <summary>
    ///     Push notification when a qso is uploaded, either successfully or unsuccessfully.
    /// </summary>
    [Reactive]
    [JsonProperty]
    public bool PushNotificationOnQSOUploaded { get; set; } = true;

    /// <summary>
    ///     Push notification when a qso is made. This is read directly from wsjtx message.
    /// </summary>
    [Reactive]
    [JsonProperty]
    public bool PushNotificationOnQSOMade { get; set; }

    public void ApplyValidationRules()
    {
        this.ClearValidationRules();
        this.ValidationRule(
            x => x.UDPPort,
            st =>
            {
                if (!int.TryParse(st, out var res)) return false;
                return res is <= 65535 and >= 0;
            },
            TranslationHelper.GetString(LangKeys.invalidport)
        );
        this.ValidationRule(
            x => x.RetryCount,
            st =>
            {
                if (!int.TryParse(st, out var res)) return false;
                return !string.IsNullOrEmpty(st) && res >= 0;
            },
            TranslationHelper.GetString(LangKeys.retrycountreq)
        );
    }


    private bool IsPropertyHasErrors(string propertyName)
    {
        return GetErrors(propertyName).Cast<string>().Any();
    }

    public bool IsUDPConfigHasErrors()
    {
        return IsPropertyHasErrors(nameof(UDPPort)) || IsPropertyHasErrors(nameof(RetryCount));
    }


    protected bool Equals(UDPServerSettings other)
    {
        return EnableUDPServer == other.EnableUDPServer &&
               EnableConnectionFromOutside == other.EnableConnectionFromOutside && UDPPort == other.UDPPort &&
               RetryCount == other.RetryCount && ForwardMessage == other.ForwardMessage &&
               ForwardAddress == other.ForwardAddress && ForwardMessageToHttp == other.ForwardMessageToHttp &&
               ForwardHttpAddress == other.ForwardHttpAddress &&
               PushNotificationOnQSOUploaded == other.PushNotificationOnQSOUploaded &&
               PushNotificationOnQSOMade == other.PushNotificationOnQSOMade;
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
        var hashCode = new HashCode();
        hashCode.Add(EnableUDPServer);
        hashCode.Add(EnableConnectionFromOutside);
        hashCode.Add(UDPPort);
        hashCode.Add(RetryCount);
        hashCode.Add(ForwardMessage);
        hashCode.Add(ForwardAddress);
        hashCode.Add(ForwardMessageToHttp);
        hashCode.Add(ForwardHttpAddress);
        hashCode.Add(PushNotificationOnQSOUploaded);
        hashCode.Add(PushNotificationOnQSOMade);
        return hashCode.ToHashCode();
    }
}