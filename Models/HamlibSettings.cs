using System;
using System.Linq;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using CloudlogHelper.Validation;
using Force.DeepCloner;
using Newtonsoft.Json;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;

[JsonObject(MemberSerialization.OptIn)]
public class HamlibSettings : ReactiveValidationObject
{
    public HamlibSettings()
    {
        this.ValidationRule(x => x.SelectedRigInfo,
            st => st?.Id is not null,
            TranslationHelper.GetString(LangKeys.notnull)
        );
        this.ValidationRule(x => x.SelectedPort,
            SettingsValidation.CheckStringNotNull,
            TranslationHelper.GetString(LangKeys.notnull)
        );
        this.ValidationRule(x => x.ExternalRigctldHostAddress,
            IPAddrUtil.CheckAddress!,
            TranslationHelper.GetString(LangKeys.invalidaddr)
        );
        this.ValidationRule(x => x.PollInterval,
            SettingsValidation.CheckInt,
            TranslationHelper.GetString(LangKeys.pollintervalreq)
        );
    }

    [Reactive] [JsonProperty] public RigInfo? SelectedRigInfo { get; set; } = new();
    [Reactive] [JsonProperty] public string SelectedPort { get; set; } = string.Empty;

    [Reactive]
    [JsonProperty]
    public string PollInterval { get; set; } = DefaultConfigs.RigctldDefaultPollingInterval.ToString();

    [Reactive] [JsonProperty] public bool PollAllowed { get; set; }

    [Reactive] [JsonProperty] public bool ReportRFPower { get; set; }

    [Reactive] [JsonProperty] public bool ReportSplitInfo { get; set; }

    [Reactive] [JsonProperty] public bool UseRigAdvanced { get; set; }

    [Reactive] [JsonProperty] public bool DisablePTT { get; set; }

    [Reactive] [JsonProperty] public bool AllowExternalControl { get; set; }

    [Reactive] [JsonProperty] public string OverrideCommandlineArg { get; set; } = string.Empty;

    [Reactive] [JsonProperty] public bool UseExternalRigctld { get; set; }

    [Reactive] [JsonProperty] public string SyncRigInfoAddress { get; set; } = string.Empty;

    [Reactive]
    [JsonProperty]
    public string ExternalRigctldHostAddress { get; set; } = DefaultConfigs.RigctldExternalHost;

    public IObservable<bool> IsHamlibValid => this.WhenAnyValue(
        x => x.SelectedRigInfo,
        x => x.SelectedPort,
        x => x.PollInterval,
        x => x.ExternalRigctldHostAddress,
        x => x.UseExternalRigctld,
        x => x.OverrideCommandlineArg,
        x => x.UseRigAdvanced,
        (a, b, c, e, f, g, h) =>
            !IsHamlibHasErrors()
    );

    public bool RestartHamlibNeeded(HamlibSettings oldSettings)
    {
        if (SelectedRigInfo is null) return true;
        return !SelectedRigInfo.Equals(oldSettings.SelectedRigInfo) || SelectedPort != oldSettings.SelectedPort ||
               PollAllowed != oldSettings.PollAllowed ||
               UseRigAdvanced != oldSettings.UseRigAdvanced || DisablePTT != oldSettings.DisablePTT ||
               AllowExternalControl != oldSettings.AllowExternalControl ||
               OverrideCommandlineArg != oldSettings.OverrideCommandlineArg ||
               UseExternalRigctld != oldSettings.UseExternalRigctld ||
               ExternalRigctldHostAddress != oldSettings.ExternalRigctldHostAddress;
    }

    public void ApplySettingsChange(HamlibSettings settings)
    {
        SelectedRigInfo = settings.SelectedRigInfo?.DeepClone();
        SelectedPort = settings.SelectedPort;
        PollInterval = settings.PollInterval;
        PollAllowed = settings.PollAllowed;
        ReportRFPower = settings.ReportRFPower;
        ReportSplitInfo = settings.ReportSplitInfo;
        UseRigAdvanced = settings.UseRigAdvanced;
        DisablePTT = settings.DisablePTT;
        AllowExternalControl = settings.AllowExternalControl;
        OverrideCommandlineArg = settings.OverrideCommandlineArg;
        UseExternalRigctld = settings.UseExternalRigctld;
        ExternalRigctldHostAddress = settings.ExternalRigctldHostAddress;
        SyncRigInfoAddress = settings.SyncRigInfoAddress;
    }

    private bool IsPropertyHasErrors(string propertyName)
    {
        return GetErrors(propertyName).Cast<string>().Any();
    }

    public bool IsHamlibHasErrors()
    {
        if (UseRigAdvanced)
            if (!string.IsNullOrEmpty(OverrideCommandlineArg))
                if (SelectedRigInfo?.Id is null)
                    return true;

        if (!SettingsValidation.CheckInt(PollInterval)) return true;

        if (!UseExternalRigctld)
            return SelectedRigInfo?.Id is null || !SettingsValidation.CheckStringNotNull(SelectedPort);

        return !IPAddrUtil.CheckAddress(ExternalRigctldHostAddress);
    }


    public HamlibSettings GetReference()
    {
        return this;
    }

    protected bool Equals(HamlibSettings other)
    {
        return SelectedRigInfo != null &&
               SelectedRigInfo.Equals(other.SelectedRigInfo) && SelectedPort == other.SelectedPort &&
               PollInterval == other.PollInterval && PollAllowed == other.PollAllowed &&
               ReportRFPower == other.ReportRFPower && ReportSplitInfo == other.ReportSplitInfo &&
               UseRigAdvanced == other.UseRigAdvanced && DisablePTT == other.DisablePTT &&
               AllowExternalControl == other.AllowExternalControl &&
               OverrideCommandlineArg == other.OverrideCommandlineArg &&
               UseExternalRigctld == other.UseExternalRigctld &&
               ExternalRigctldHostAddress == other.ExternalRigctldHostAddress &&
               SyncRigInfoAddress == other.SyncRigInfoAddress;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((HamlibSettings)obj);
    }

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(SelectedRigInfo);
        hashCode.Add(SelectedPort);
        hashCode.Add(PollInterval);
        hashCode.Add(PollAllowed);
        hashCode.Add(ReportRFPower);
        hashCode.Add(ReportSplitInfo);
        hashCode.Add(UseRigAdvanced);
        hashCode.Add(DisablePTT);
        hashCode.Add(AllowExternalControl);
        hashCode.Add(OverrideCommandlineArg);
        hashCode.Add(UseExternalRigctld);
        hashCode.Add(ExternalRigctldHostAddress);
        hashCode.Add(SyncRigInfoAddress);
        return hashCode.ToHashCode();
    }
}