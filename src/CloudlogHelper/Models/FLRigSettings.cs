using System;
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

[JsonObject(MemberSerialization.OptIn)]
public class FLRigSettings : ReactiveValidationObject
{
    [Reactive]
    [JsonProperty]
    public string PollInterval { get; set; } = DefaultConfigs.RigDefaultPollingInterval.ToString();

    [Reactive] [JsonProperty] public bool PollAllowed { get; set; }

    [Reactive] [JsonProperty] public bool ReportRFPower { get; set; }

    [Reactive] [JsonProperty] public bool ReportSplitInfo { get; set; }

    [Reactive] [JsonProperty] public string FLRigHost { get; set; } = DefaultConfigs.FLRigDefaultHost;

    [Reactive] [JsonProperty] public string FLRigPort { get; set; } = DefaultConfigs.FLRigDefaultPort;

    [Reactive] [JsonProperty] public string SyncRigInfoAddress { get; set; } = string.Empty;

    public IObservable<bool> IsFLRigValid => this.WhenAnyValue(
        x => x.PollInterval,
        x => x.FLRigHost,
        x => x.FLRigPort,
        (a, b, c) =>
            !IsFLRigHasErrors()
    );

    public void ApplyValidationRules()
    {
        this.ClearValidationRules();
        this.ValidationRule(x => x.FLRigPort,
            SettingsValidation.CheckHttpPort,
            TranslationHelper.GetString(LangKeys.invalidport)
        );
        this.ValidationRule(x => x.FLRigHost,
            SettingsValidation.CheckHttpIp!,
            TranslationHelper.GetString(LangKeys.invalidaddr)
        );
        this.ValidationRule(x => x.PollInterval,
            SettingsValidation.CheckInt,
            TranslationHelper.GetString(LangKeys.pollintervalreq)
        );
    }


    private bool IsPropertyHasErrors(string propertyName)
    {
        return GetErrors(propertyName).Cast<string>().Any();
    }

    public bool IsFLRigHasErrors()
    {
        return HasErrors;
    }

    protected bool Equals(FLRigSettings other)
    {
        return PollInterval == other.PollInterval &&
               PollAllowed == other.PollAllowed &&
               ReportRFPower == other.ReportRFPower &&
               ReportSplitInfo == other.ReportSplitInfo &&
               FLRigHost == other.FLRigHost &&
               FLRigPort == other.FLRigPort &&
               SyncRigInfoAddress == other.SyncRigInfoAddress;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((FLRigSettings)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(PollInterval, PollAllowed, ReportRFPower, ReportSplitInfo, FLRigHost, FLRigPort,
            SyncRigInfoAddress);
    }
}