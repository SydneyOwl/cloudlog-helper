using System.Collections.Generic;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService;
using Newtonsoft.Json;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;

/// <summary>
///     Application-wide settings
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class ApplicationSettings : ReactiveValidationObject
{
    /// <summary>
    /// Instance name of Cloudlog Helper. This name will be generated on application first start.
    /// </summary>
    [Reactive]
    [JsonProperty]
    public string InstanceName { get; set; } = string.Empty;
    
    /// <summary>
    ///     ProgramShutdownMode of this application.
    /// </summary>
    [Reactive]
    [JsonProperty]
    public ProgramShutdownMode ShutdownMode { get; set; } = ProgramShutdownMode.NotSpecified;

    /// <summary>
    ///     Some basic settings.
    /// </summary>
    [Reactive]
    [JsonProperty]
    public BasicSettings BasicSettings { get; set; } = new();

    /// <summary>
    ///     Cloudlog settings.
    /// </summary>
    [JsonProperty]
    public CloudlogSettings CloudlogSettings { get; set; } = new();


    /// <summary>
    ///     Log services like qrz and eqsl.cc
    /// </summary>
    [JsonProperty]
    public List<ThirdPartyLogService> LogServices { get; set; } = new();

    /// <summary>
    ///     Hamlib settings.
    /// </summary>
    [JsonProperty]
    public HamlibSettings HamlibSettings { get; set; } = new();

    /// <summary>
    ///     FLRig settings.
    /// </summary>
    [JsonProperty]
    public FLRigSettings FLRigSettings { get; set; } = new();
    
    /// <summary>
    ///     OmniRig settings.
    /// </summary>
    [JsonProperty]
    public OmniRigSettings OmniRigSettings { get; set; } = new();

    /// <summary>
    ///     UDP Settings.
    /// </summary>
    [JsonProperty]
    public UDPServerSettings UDPSettings { get; set; } = new();
    
    /// <summary>
    ///     CLH Server Settings.
    /// </summary>
    [JsonProperty]
    public CLHServerSettings CLHServerSettings { get; set; } = new();

    /// <summary>
    ///     QSA Settings
    /// </summary>
    [JsonProperty]
    public QsoSyncAssistantSettings QsoSyncAssistantSettings { get; set; } = new();
}