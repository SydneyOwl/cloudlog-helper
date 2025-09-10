using Newtonsoft.Json;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;

public class BasicSettings : ReactiveValidationObject
{
    [Reactive] [JsonProperty] public string? MyMaidenheadGrid { get; set; }
}