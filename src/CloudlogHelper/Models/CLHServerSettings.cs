using System;
using System.Linq;
using System.Text.Json.Serialization;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using CloudlogHelper.Validation;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;
/// <summary>
///     Settings of cloudlog.
/// </summary>
public class CLHServerSettings: ReactiveValidationObject
{
    [Reactive] public bool CLHServerEnabled { get; set; }
    [Reactive] public string ServerHost { get; set; } = string.Empty;
    [Reactive] public int ServerPort { get; set; } = 7410;
    [Reactive] public string ServerKey { get; set; } = string.Empty;
    [Reactive] public bool UseTLS { get; set; } = true;

    
    [JsonIgnore]
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
            SettingsValidation.CheckHost,
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
        return !SettingsValidation.CheckHost(ServerHost) ||
               !SettingsValidation.CheckHttpPort(ServerPort);
    }


    private bool IsPropertyHasErrors(string propertyName)
    {
        return GetErrors(propertyName).Cast<string>().Any();
    }

    protected bool Equals(CLHServerSettings other)
    {
        return CLHServerEnabled == other.CLHServerEnabled && ServerHost == other.ServerHost && ServerPort == other.ServerPort && ServerKey == other.ServerKey && UseTLS == other.UseTLS;
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
        return HashCode.Combine(CLHServerEnabled, ServerHost, ServerPort, ServerKey, UseTLS);
    }
}