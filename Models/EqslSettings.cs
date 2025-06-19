using System;
using Newtonsoft.Json;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.Models;

public class EqslSettings:ReactiveObject
{
    [Reactive] [JsonProperty] public string Username { get; set; } = string.Empty;
    [Reactive] [JsonProperty] public string Password { get; set; } = string.Empty;
    [Reactive] [JsonProperty] public string QthNickname { get; set; } = string.Empty;
    [Reactive] [JsonProperty] public bool AutoQSOUploadEnabled { get; set; }

    public void ApplySettingsChange(EqslSettings settings)
    {
        Username = settings.Username;
        Password = settings.Password;
        QthNickname = settings.QthNickname;
        AutoQSOUploadEnabled = settings.AutoQSOUploadEnabled;
    }

    public EqslSettings GetReference()
    {
        return this;
    }

    public bool IsEqslHasErrors()
    {
        return string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password) ;//||
               // string.IsNullOrEmpty(QthNickname);
    }

    protected bool Equals(EqslSettings other)
    {
        return Username == other.Username && Password == other.Password && QthNickname == other.QthNickname && AutoQSOUploadEnabled == other.AutoQSOUploadEnabled;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((EqslSettings)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Username, Password, QthNickname, AutoQSOUploadEnabled);
    }
}