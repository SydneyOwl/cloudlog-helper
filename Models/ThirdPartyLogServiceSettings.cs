using System;
using Newtonsoft.Json;
using ReactiveUI;

namespace CloudlogHelper.Models;

public class ThirdPartyLogServiceSettings
{
    [JsonProperty] public ClublogSettings ClublogSettings { get; set; } = new();
    [JsonProperty] public HamCQSettings HamCQSettings { get; set; } = new();
    [JsonProperty] public EqslSettings EqslSettings { get; set; } = new();
    
    public void ApplySettingsChange(ThirdPartyLogServiceSettings settings)
    {
        ClublogSettings.ApplySettingsChange(settings.ClublogSettings);
        HamCQSettings.ApplySettingsChange(settings.HamCQSettings);
        EqslSettings.ApplySettingsChange(settings.EqslSettings);
    }

    protected bool Equals(ThirdPartyLogServiceSettings other)
    {
        return ClublogSettings.Equals(other.ClublogSettings) && HamCQSettings.Equals(other.HamCQSettings) && EqslSettings.Equals(other.EqslSettings);
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((ThirdPartyLogServiceSettings)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ClublogSettings, HamCQSettings, EqslSettings);
    }
}