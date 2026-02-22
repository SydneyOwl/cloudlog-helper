using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using CloudlogHelper.Converters;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService;
using FastCloner.SourceGenerator.Shared;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Helpers;

namespace CloudlogHelper.Models;

/// <summary>
///     Application-wide settings
/// </summary>
[FastClonerClonable]
public class ApplicationSettings : ReactiveValidationObject
{
    /// <summary>
    ///     ProgramShutdownMode of this application.
    /// </summary>
    [Reactive]
    public ProgramShutdownMode ShutdownMode { get; set; } = ProgramShutdownMode.NotSpecified;

    /// <summary>
    ///     Some basic settings.
    /// </summary>
    [Reactive]
    public BasicSettings BasicSettings { get; set; } = new();

    /// <summary>
    ///     Cloudlog settings.
    /// </summary>
    public CloudlogSettings CloudlogSettings { get; set; } = new();
    
    /// <summary>
    ///     Log services like qrz and eqsl.cc
    /// </summary>
    public List<ThirdPartyLogService> LogServices { get; set; } = new();

    /// <summary>
    ///     Hamlib settings.
    /// </summary>
    public HamlibSettings HamlibSettings { get; set; } = new();

    /// <summary>
    ///     FLRig settings.
    /// </summary>
    public FLRigSettings FLRigSettings { get; set; } = new();
    
    /// <summary>
    ///     OmniRig settings.
    /// </summary>
    public OmniRigSettings OmniRigSettings { get; set; } = new();

    /// <summary>
    ///     UDP Settings.
    /// </summary>
    public UDPServerSettings UDPSettings { get; set; } = new();

    /// <summary>
    ///     QSA Settings
    /// </summary>
    public QsoSyncAssistantSettings QsoSyncAssistantSettings { get; set; } = new();
    
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(List<ThirdPartyLogService>))]
    public ApplicationSettings(){}
}