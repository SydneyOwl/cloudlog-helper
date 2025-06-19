using System;
using Newtonsoft.Json;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.Models;

public class HamCQSettings : ReactiveObject
{
    [Reactive] [JsonProperty] public string HamCQAPIKey { get; set; } = string.Empty;
    [Reactive] [JsonProperty] public bool AutoQSOUploadEnabled { get; set; }

    public void ApplySettingsChange(HamCQSettings settings)
    {
        HamCQAPIKey = settings.HamCQAPIKey;
        AutoQSOUploadEnabled = settings.AutoQSOUploadEnabled;
    }

    public HamCQSettings GetReference()
    {
        return this;
        // return JsonConvert.DeserializeObject<HamCQSettings>(JsonConvert.SerializeObject(this))!;
    }

    public bool IsHamCQHasErrors()
    {
        return string.IsNullOrEmpty(HamCQAPIKey);
    }

    protected bool Equals(HamCQSettings other)
    {
        return HamCQAPIKey == other.HamCQAPIKey && AutoQSOUploadEnabled == other.AutoQSOUploadEnabled;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((HamCQSettings)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(HamCQAPIKey, AutoQSOUploadEnabled);
    }
}