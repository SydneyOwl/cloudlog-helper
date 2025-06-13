using System;
using Newtonsoft.Json;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.Models;

public class ClublogSettings : ReactiveObject
{
    [Reactive] [JsonProperty] public string ClublogCallsign { get; set; } = string.Empty;
    [Reactive] [JsonProperty] public string ClublogPassword { get; set; } = string.Empty;
    [Reactive] [JsonProperty] public string ClublogEmail { get; set; } = string.Empty;

    public void ApplySettingsChange(ClublogSettings settings)
    {
        ClublogCallsign = settings.ClublogCallsign;
        ClublogPassword = settings.ClublogPassword;
        ClublogEmail = settings.ClublogEmail;
    }

    public ClublogSettings GetReference()
    {
        return this;
        // return JsonConvert.DeserializeObject<ClublogSettings>(JsonConvert.SerializeObject(this))!;
    }

    public bool IsClublogHasErrors()
    {
        return string.IsNullOrEmpty(ClublogCallsign) || string.IsNullOrEmpty(ClublogPassword) ||
               string.IsNullOrEmpty(ClublogEmail);
    }

    protected bool Equals(ClublogSettings other)
    {
        return ClublogCallsign == other.ClublogCallsign && ClublogPassword == other.ClublogPassword &&
               ClublogEmail == other.ClublogEmail;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((ClublogSettings)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ClublogCallsign, ClublogPassword, ClublogEmail);
    }
}