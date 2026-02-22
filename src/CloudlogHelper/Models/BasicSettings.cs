using System;
using CloudlogHelper.Enums;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;

public class BasicSettings : ReactiveValidationObject
{
    [Reactive] public string? MyMaidenheadGrid { get; set; }
    [Reactive] public bool DisableAllCharts { get; set; }
    [Reactive] public bool EnablePlugin { get; set; } = true;

    /// <summary>
    ///     Default language of this application.
    /// </summary>
    [Reactive]
    public SupportedLanguage LanguageType { get; set; } = SupportedLanguage.NotSpecified;
    
    /// <summary>
    /// Instance name of Cloudlog Helper. This name will be generated on application first start.
    /// </summary>
    [Reactive]
    public string InstanceName { get; set; } = string.Empty;

    protected bool Equals(BasicSettings other)
    {
        return MyMaidenheadGrid == other.MyMaidenheadGrid && DisableAllCharts == other.DisableAllCharts 
                                                          && EnablePlugin == other.EnablePlugin && LanguageType == other.LanguageType
                                                          && InstanceName == other.InstanceName;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((BasicSettings)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(MyMaidenheadGrid, DisableAllCharts, EnablePlugin, (int)LanguageType, InstanceName);
    }
}