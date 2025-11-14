using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
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
public class OmniRigSettings : ReactiveValidationObject
{
    [Reactive]
    [JsonProperty]
    public string PollInterval { get; set; } = DefaultConfigs.RigDefaultPollingInterval.ToString();

    [Reactive] [JsonProperty] public bool PollAllowed { get; set; }

    [Reactive] [JsonProperty] public string SelectedRig { get; set; } = DefaultConfigs.OmniRigAvailableRig.First();

    [Reactive] [JsonProperty] public string SyncRigInfoAddress { get; set; } = string.Empty;
    
    
    private bool _isConfChanged;

    private CompositeDisposable _disposable = new();
    
    public IObservable<bool> IsOmniRigValid => this.WhenAnyValue(
        x => x.PollInterval,
        x => x.PollAllowed,
        (a,b) => !IsOmniRigHasErrors()
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
        this.ValidationRule(x => x.PollInterval,
            SettingsValidation.CheckInt,
            TranslationHelper.GetString(LangKeys.pollintervalreq)
        );
        this.WhenAnyValue(    
            x => x.SelectedRig,
            x => x.PollAllowed,
             x=> x.PollInterval,
            x => x.SyncRigInfoAddress
        ).Skip(1).Subscribe(_ =>
        {
            // Console.WriteLine("Oh seems like sth changed..");
            _isConfChanged = true;
        }).DisposeWith(_disposable);;
    }


    private bool IsPropertyHasErrors(string propertyName)
    {
        return GetErrors(propertyName).Cast<string>().Any();
    }

    public bool IsOmniRigHasErrors()
    {
        return HasErrors;
    }

    protected bool Equals(OmniRigSettings other)
    {
        return PollInterval == other.PollInterval && PollAllowed == other.PollAllowed && SelectedRig == other.SelectedRig && SyncRigInfoAddress == other.SyncRigInfoAddress;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((OmniRigSettings)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(PollInterval, PollAllowed, SelectedRig, SyncRigInfoAddress);
    }
}