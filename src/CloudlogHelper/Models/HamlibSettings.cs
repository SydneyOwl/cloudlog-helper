using System;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json.Serialization;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using CloudlogHelper.Validation;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;

public class HamlibSettings : ReactiveValidationObject
{
    [Reactive] public RigInfo? SelectedRigInfo { get; set; } = new();
    [Reactive] public string SelectedPort { get; set; } = string.Empty;

    [Reactive]
    public string PollInterval { get; set; } = DefaultConfigs.RigDefaultPollingInterval.ToString();

    [Reactive] public bool PollAllowed { get; set; }

    [Reactive] public bool ReportRFPower { get; set; }

    [Reactive] public bool ReportSplitInfo { get; set; }

    [Reactive] public bool UseRigAdvanced { get; set; }

    [Reactive] public bool DisablePTT { get; set; }

    [Reactive] public bool AllowExternalControl { get; set; }

    [Reactive] public string OverrideCommandlineArg { get; set; } = string.Empty;

    [Reactive] public bool UseExternalRigctld { get; set; }

    [Reactive]
    public string ExternalRigctldHostAddress { get; set; } = DefaultConfigs.RigctldExternalHost;

    private bool _isConfChanged;

    private CompositeDisposable _disposable = new();

    
    [JsonIgnore]
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

    public bool IsConfOnceChanged()
    {
        return _isConfChanged;
    }

    public void ReinitRules()
    {
        _disposable.Dispose();
        _disposable = new CompositeDisposable();

        _isConfChanged = false;
        
        this.ClearValidationRules();
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
        
        // skip 4: refer to CloudlogHelper.ViewModels.SettingsWindowViewModel._initializeHamlibAsync
        this.WhenAnyValue(     
            x => x.SelectedRigInfo,
            x => x.SelectedPort,
            x => x.PollInterval,
            x => x.PollAllowed,
            x => x.ReportRFPower,
            x => x.ReportSplitInfo
        ) // fixme some trickly solutions...
        .SkipUntil(Observable.Timer(TimeSpan.FromMilliseconds(500)))
        .Subscribe(tmp =>
        {
            _isConfChanged = true;
        }).DisposeWith(_disposable);
        
        this.WhenAnyValue(    
            x => x.UseRigAdvanced,
            x => x.DisablePTT,
            x => x.AllowExternalControl,
            x => x.OverrideCommandlineArg,
            x => x.UseExternalRigctld,
            x => x.ExternalRigctldHostAddress
        )// fixme some trickly solutions...
        .SkipUntil(Observable.Timer(TimeSpan.FromMilliseconds(500)))
        .Subscribe(_ =>
        {
            _isConfChanged = true;
        }).DisposeWith(_disposable);;
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
        return hashCode.ToHashCode();
    }
}