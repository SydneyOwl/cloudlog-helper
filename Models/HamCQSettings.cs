using System;
using Newtonsoft.Json;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.Models;

public class HamCQSettings : ReactiveObject
{
    [Reactive] [JsonProperty] public string HamCQAPIKey { get; set; } = string.Empty;

    public HamCQSettings DeepClone()
    {
        return JsonConvert.DeserializeObject<HamCQSettings>(JsonConvert.SerializeObject(this))!;
    }

    public bool IsHamCQHasErrors()
    {
        return string.IsNullOrEmpty(HamCQAPIKey);
    }

    protected bool Equals(HamCQSettings other)
    {
        return HamCQAPIKey == other.HamCQAPIKey;
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
        return HamCQAPIKey.GetHashCode();
    }
}