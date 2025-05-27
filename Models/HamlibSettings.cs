using System;
using System.Collections.Generic;
using System.Linq;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
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
        this.ValidationRule(x => x.SelectedRadio,
            st => !string.IsNullOrEmpty(st),
            TranslationHelper.GetString("notnull")
        );
        this.ValidationRule(x => x.SelectedPort,
            st => !string.IsNullOrEmpty(st),
            TranslationHelper.GetString("notnull")
        );
        this.ValidationRule(x => x.ExternalRigctldHostAddress,
            IPAddrUtil.CheckAddress!,
            TranslationHelper.GetString("invalidaddr")
        );
        this.ValidationRule(x => x.DebugServerAddress,
            IPAddrUtil.CheckAddress!,
            TranslationHelper.GetString("invalidaddr")
        );
        this.ValidationRule(x => x.PollInterval,
            st =>
            {
                if (!int.TryParse(st, out var res)) return false;
                return !string.IsNullOrEmpty(st) && res >= 1;
            },
            TranslationHelper.GetString("pollintervalreq")
        );
    }

    [Reactive] [JsonProperty] public string SelectedRadio { get; set; } = string.Empty;
    [Reactive] [JsonProperty] public string SelectedPort { get; set; } = string.Empty;

    [Reactive] [JsonProperty] public Dictionary<string, string> KnownModels { get; set; } = new();

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
    [Reactive] [JsonProperty] public bool AllowDebugServer { get; set; }

    [Reactive]
    [JsonProperty]
    public string DebugServerAddress { get; set; } = DefaultConfigs.DebugServerDefaultBindingAddress;

    [Reactive]
    [JsonProperty]
    public string ExternalRigctldHostAddress { get; set; } = DefaultConfigs.RigctldExternalHost;

    public IObservable<bool> IsHamlibValid => this.WhenAnyValue(
        x => x.SelectedRadio,
        x => x.SelectedPort,
        x => x.PollInterval,
        x => x.ExternalRigctldHostAddress,
        x => x.UseExternalRigctld,
        x => x.DebugServerAddress,
        x => x.OverrideCommandlineArg,
        x => x.UseRigAdvanced,
        x => x.AllowDebugServer,
        (a, b, c, d, e, f, g, h, i) =>
            !IsHamlibHasErrors()
    );

    public bool RestartHamlibNeeded(HamlibSettings oldSettings)
    {
        return SelectedRadio != oldSettings.SelectedRadio || SelectedPort != oldSettings.SelectedPort ||
               PollAllowed != oldSettings.PollAllowed ||
               UseRigAdvanced != oldSettings.UseRigAdvanced || DisablePTT != oldSettings.DisablePTT ||
               AllowExternalControl != oldSettings.AllowExternalControl ||
               OverrideCommandlineArg != oldSettings.OverrideCommandlineArg ||
               UseExternalRigctld != oldSettings.UseExternalRigctld || AllowDebugServer != oldSettings.AllowDebugServer ||
               DebugServerAddress != oldSettings.DebugServerAddress ||
               ExternalRigctldHostAddress != oldSettings.ExternalRigctldHostAddress;
    }

    private bool IsPropertyHasErrors(string propertyName)
    {
        return GetErrors(propertyName).Cast<string>().Any();
    }

    public bool IsHamlibOK()
    {
        return !IsHamlibHasErrors();
    }

    public bool IsHamlibHasErrors()
    {
        if (UseRigAdvanced)
        {
            if (!string.IsNullOrEmpty(OverrideCommandlineArg))
                if (IsPropertyHasErrors(nameof(SelectedRadio)))
                    return true;

            if (AllowDebugServer)
                if (IsPropertyHasErrors(nameof(DebugServerAddress)))
                    return true;
        }

        if (IsPropertyHasErrors(nameof(PollInterval))) return true;

        if (!UseExternalRigctld)
            return IsPropertyHasErrors(nameof(SelectedRadio)) || IsPropertyHasErrors(nameof(SelectedPort));

        return IsPropertyHasErrors(nameof(ExternalRigctldHostAddress));

        return false;
        // return (UseRigAdvanced && (string.IsNullOrEmpty(OverrideCommandlineArg) || IsPropertyHasErrors(nameof(SelectedRadio)))) || 
        //        ((IsPropertyHasErrors(nameof(SelectedRadio)) || IsPropertyHasErrors(nameof(SelectedPort)) || IsPropertyHasErrors(nameof(PollInterval))) && !UseExternalRigctld && !UseRigAdvanced) 
        //        || (IsPropertyHasErrors(nameof(ExternalRigctldHostAddress)) && UseExternalRigctld) || IsPropertyHasErrors(nameof(SelectedRadio));
    }


    public HamlibSettings DeepClone()
    {
        return JsonConvert.DeserializeObject<HamlibSettings>(JsonConvert.SerializeObject(this))!;
    }

    protected bool Equals(HamlibSettings other)
    {
        return SelectedRadio == other.SelectedRadio && SelectedPort == other.SelectedPort &&
               PollInterval == other.PollInterval && PollAllowed == other.PollAllowed &&
               ReportRFPower == other.ReportRFPower && ReportSplitInfo == other.ReportSplitInfo &&
               UseRigAdvanced == other.UseRigAdvanced && DisablePTT == other.DisablePTT &&
               AllowExternalControl == other.AllowExternalControl &&
               OverrideCommandlineArg == other.OverrideCommandlineArg &&
               UseExternalRigctld == other.UseExternalRigctld && AllowDebugServer == other.AllowDebugServer &&
               DebugServerAddress == other.DebugServerAddress &&
               ExternalRigctldHostAddress == other.ExternalRigctldHostAddress;
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
        hashCode.Add(SelectedRadio);
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
        hashCode.Add(AllowDebugServer);
        hashCode.Add(DebugServerAddress);
        hashCode.Add(ExternalRigctldHostAddress);
        return hashCode.ToHashCode();
    }
}