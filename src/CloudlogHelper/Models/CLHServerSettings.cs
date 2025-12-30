using System;
using System.Collections.Generic;
using System.Linq;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
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
public class CLHServerSettings: ReactiveValidationObject
{
    [Reactive] [JsonProperty] public bool IsEnabled { get; set; }
    [Reactive] [JsonProperty] public string ServerHost { get; set; } = string.Empty;
    [Reactive] [JsonProperty] public int ServerPort { get; set; } = 7410;
    [Reactive] [JsonProperty] public string ServerKey { get; set; } = string.Empty;
    [Reactive] [JsonProperty] public bool UseTLS { get; set; } = true;

    
    public IObservable<bool> IsCLHServerValid => this.WhenAnyValue(
        x => x.ServerHost,
        x => x.ServerPort,
        (url, key) => !IsCLHServerHasErrors()
    );

    public void ReinitRules()
    {
        this.ClearValidationRules();

        // This makes sure only one err is displayed each time
        this.ValidationRule(x => x.ServerHost,
            SettingsValidation.CheckHttpIp,
            TranslationHelper.GetString(LangKeys.invalidaddr)
        );

        this.ValidationRule(
            x => x.ServerPort,
            SettingsValidation.CheckHttpPort,
            TranslationHelper.GetString(LangKeys.invalidport)
        );
    }


    public bool IsCLHServerHasErrors()
    {
        return !SettingsValidation.CheckHttpIp(ServerHost) ||
               !SettingsValidation.CheckHttpPort(ServerPort);
    }


    private bool IsPropertyHasErrors(string propertyName)
    {
        return GetErrors(propertyName).Cast<string>().Any();
    }

    protected bool Equals(CLHServerSettings other)
    {
        return IsEnabled == other.IsEnabled && ServerHost == other.ServerHost && ServerPort == other.ServerPort && ServerKey == other.ServerKey && UseTLS == other.UseTLS;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((CLHServerSettings)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(IsEnabled, ServerHost, ServerPort, ServerKey, UseTLS);
    }
}