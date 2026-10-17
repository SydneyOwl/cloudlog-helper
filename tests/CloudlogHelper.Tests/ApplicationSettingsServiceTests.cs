using AutoMapper;
using System.IO;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;
using ReactiveUI;

namespace CloudlogHelper.Tests;

public class ApplicationSettingsServiceTests
{
    // Fixed path, not a unique one per test: the tests in this class run sequentially, and this
    // way a test run leaves at most one file behind in temp instead of one per test.
    private static readonly string TestSettingsFile =
        Path.Combine(Path.GetTempPath(), "clh-test-settings-ApplicationSettingsService.json");
    [Fact]
    public void TryGetDraftSettings_AllowsOnlyOneOwner_UntilRestore()
    {
        var service = CreateService();
        var firstOwner = new object();
        var secondOwner = new object();

        Assert.True(service.TryGetDraftSettings(firstOwner, out var draft));
        Assert.NotNull(draft);
        Assert.False(service.TryGetDraftSettings(secondOwner, out _));

        service.RestoreSettings(firstOwner);

        Assert.True(service.TryGetDraftSettings(secondOwner, out _));
        service.RestoreSettings(secondOwner);
    }

    [Fact]
    public void TryGetDraftSettings_WhenRuleReinitThrows_HandsTheDraftBack()
    {
        // The semaphore is taken before ReinitRules runs, so a failure in there used to leave
        // the draft claimed forever and every settings window dead until restart.
        var service = CreateService();
        service.InjectMockSettings(BrokenSettings());

        Assert.Throws<NullReferenceException>(() => service.TryGetDraftSettings(new object(), out _));

        // If the claim had not been handed back this would still be refused.
        service.InjectMockSettings(new ApplicationSettings());

        Assert.True(service.TryGetDraftSettings(new object(), out var reclaimed));
        Assert.NotNull(reclaimed);
    }

    private static ApplicationSettings BrokenSettings()
    {
        var settings = new ApplicationSettings();
        // A partially deserialized config can leave a section null
        settings.CloudlogSettings = null!;
        return settings;
    }

    [Fact]
    public void RestoreSettings_DiscardsDraftChanges_AndReleasesDraftLock()
    {
        var service = CreateService();
        var owner = new object();

        Assert.True(service.TryGetDraftSettings(owner, out var draft));
        draft!.BasicSettings.InstanceName = "discard-me";

        service.RestoreSettings(owner);

        Assert.NotEqual("discard-me", service.GetCurrentSettings().BasicSettings.InstanceName);
        Assert.True(service.TryGetDraftSettings(new object(), out _));
    }

    [Fact]
    public void ApplySettings_WithWrongOwner_ThrowsAndKeepsOriginalOwnerLock()
    {
        var service = CreateService();
        var owner = new object();

        Assert.True(service.TryGetDraftSettings(owner, out _));

        Assert.Throws<SynchronizationLockException>(() => service.ApplySettings(new object()));
        Assert.False(service.TryGetDraftSettings(new object(), out _));

        service.RestoreSettings(owner);
    }

    [Fact]
    public void GetCurrentDraftSettingsSnapshot_ReturnsClone()
    {
        var service = CreateService();

        var snapshot = service.GetCurrentDraftSettingsSnapshot();
        snapshot.BasicSettings.InstanceName = "snapshot-only";

        Assert.NotEqual("snapshot-only", service.GetCurrentDraftSettingsSnapshot().BasicSettings.InstanceName);
        Assert.NotEqual("snapshot-only", service.GetCurrentSettings().BasicSettings.InstanceName);
    }

    [Fact]
    public void ApplySettings_PersistsDraftChanges_AndReleasesDraftLock()
    {
        var service = CreateService();
        var owner = new object();

        Assert.True(service.TryGetDraftSettings(owner, out var draft));
        draft!.BasicSettings.InstanceName = "focused-test-instance";
        draft.BasicSettings.DisableAllCharts = true;

        service.ApplySettings(owner);

        Assert.Equal("focused-test-instance", service.GetCurrentSettings().BasicSettings.InstanceName);
        Assert.True(service.GetCurrentSettings().BasicSettings.DisableAllCharts);
        Assert.True(service.TryGetDraftSettings(new object(), out _));
    }

    [Fact]
    public void ApplySettings_AppliesRawLogServiceConfigs()
    {
        var logSystemManager = new FakeLogSystemManager();
        var service = ApplicationSettingsService.GenerateApplicationSettingsService(
            logSystemManager,
            reinit: true,
            version: new Version(0, 0, 0),
            CreateMapper(),
            TestSettingsFile);
        var owner = new object();
        var rawConfigs = new List<LogSystemConfig> { new() };

        Assert.True(service.TryGetDraftSettings(owner, out _));
        service.ApplySettings(owner, rawConfigs);

        Assert.Equal(2, logSystemManager.ApplyLogServiceChangesCallCount);
        Assert.All(logSystemManager.AppliedRawConfigs, configs => Assert.Same(rawConfigs, configs));
    }

    [Fact]
    public void Mapper_CopiesLogServiceInstancesByReference()
    {
        var draft = new ApplicationSettings();
        var current = new ApplicationSettings();
        var draftService = new QRZThirdPartyLogService();
        draft.LogServices.Add(draftService);
        var currentBasicSettings = current.BasicSettings;
        draft.BasicSettings.InstanceName = "draft-instance";

        CreateMapper().Map(draft, current);

        // AutoMapper cannot construct the abstract element type, so it moves the very same
        // service object into the destination list. Nested members that *do* have a map
        // (BasicSettings, CloudlogSettings) are mapped in place instead.
        Assert.NotSame(draft.LogServices, current.LogServices);
        Assert.Same(draftService, current.LogServices[0]);
        Assert.Same(currentBasicSettings, current.BasicSettings);
        Assert.Equal("draft-instance", current.BasicSettings.InstanceName);
    }

    [Fact]
    public void ApplySettings_ClonesLogServices_InsteadOfSharingDraftInstances()
    {
        var service = CreateService();
        var owner = new object();

        Assert.True(service.TryGetDraftSettings(owner, out var draft));
        var draftService = new QRZThirdPartyLogService { ApiKey = "draft-key" };
        draft!.LogServices.Add(draftService);
        var liveList = service.GetCurrentSettings().LogServices;

        service.ApplySettings(owner);

        var liveServices = service.GetCurrentSettings().LogServices;
        // The list object stays stable - rig reporting captured it at startup and never
        // re-reads it.
        Assert.Same(liveList, liveServices);
        // The live settings hold a clone, not the draft's instance: credentials edited in
        // the settings window (draft) must not tear in-flight uploads.
        Assert.Single(liveServices);
        Assert.IsType<QRZThirdPartyLogService>(liveServices[0]);
        Assert.NotSame(draftService, liveServices[0]);
        Assert.Equal("draft-key", ((QRZThirdPartyLogService)liveServices[0]).ApiKey);

        draftService.ApiKey = "edited-in-draft";
        Assert.Equal("draft-key", ((QRZThirdPartyLogService)liveServices[0]).ApiKey);
    }

    [Fact]
    public void Mapper_KeepsComplexSectionMembersIsolated()
    {
        // RigInfo and StationInfo have registered same-type maps, so the in-place map
        // copies their values instead of aliasing the draft's objects into the live
        // settings - which is what the assignable path does for unmapped members (see
        // Mapper_CopiesLogServiceInstancesByReference for the unmapped case).
        var draft = new ApplicationSettings();
        var current = new ApplicationSettings();
        draft.HamlibSettings.SelectedRigInfo = new RigInfo { Id = "2" };
        var draftStation = new StationInfo { StationId = "19" };
        draft.CloudlogSettings.AvailableCloudlogStationInfo.Add(draftStation);
        var currentRigInfo = current.HamlibSettings.SelectedRigInfo!;

        CreateMapper().Map(draft, current);

        // Complex scalar-carrying member: mapped in place, object identity preserved.
        Assert.Same(currentRigInfo, current.HamlibSettings.SelectedRigInfo);
        Assert.NotSame(draft.HamlibSettings.SelectedRigInfo, current.HamlibSettings.SelectedRigInfo);
        Assert.Equal("2", current.HamlibSettings.SelectedRigInfo.Id);

        // Collection of complex elements: rebuilt with new instances.
        Assert.NotSame(draftStation, current.CloudlogSettings.AvailableCloudlogStationInfo[0]);
        Assert.Equal("19", current.CloudlogSettings.AvailableCloudlogStationInfo[0].StationId);
    }

    [Fact]
    public void ApplySettings_PublishesBasicSettingsChanged_WhenBasicSettingsChanged()
    {
        var service = CreateService();
        var owner = new object();
        var received = new List<ChangedPart>();
        using var subscription = MessageBus.Current.Listen<SettingsChanged>()
            .Subscribe(message => received.Add(message.Part));

        Assert.True(service.TryGetDraftSettings(owner, out var draft));
        draft!.BasicSettings.InstanceName = "message-bus-focused-test";

        service.ApplySettings(owner);

        Assert.Contains(ChangedPart.BasicSettings, received);
    }

    private static ApplicationSettingsService CreateService()
    {
        // Without a path of its own, ApplySettings writes to the developer's real settings.json
        // and turns SkipWizard off - the wizard then comes back after every test run.
        return ApplicationSettingsService.GenerateApplicationSettingsService(
            new FakeLogSystemManager(),
            reinit: true,
            version: new Version(0, 0, 0),
            CreateMapper(),
            TestSettingsFile);
    }

    private static IMapper CreateMapper()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<HamlibSettings, HamlibSettings>();
            cfg.CreateMap<FLRigSettings, FLRigSettings>();
            cfg.CreateMap<OmniRigSettings, OmniRigSettings>();
            cfg.CreateMap<CloudlogSettings, CloudlogSettings>();
            cfg.CreateMap<UDPServerSettings, UDPServerSettings>();
            cfg.CreateMap<QsoSyncAssistantSettings, QsoSyncAssistantSettings>();
            cfg.CreateMap<BasicSettings, BasicSettings>();
            cfg.CreateMap<RigInfo, RigInfo>();
            cfg.CreateMap<StationInfo, StationInfo>();
            cfg.CreateMap<ApplicationSettings, ApplicationSettings>();
        });

        return config.CreateMapper();
    }

    private sealed class FakeLogSystemManager : ILogSystemManager
    {
        public int ApplyLogServiceChangesCallCount { get; private set; }
        public List<List<LogSystemConfig>> AppliedRawConfigs { get; } = new();

        public ThirdPartyLogService[] GetEmptySupportedLogServices() => Array.Empty<ThirdPartyLogService>();

        public Task PreInitLogSystem(IEnumerable<ThirdPartyLogService> ls) => Task.CompletedTask;

        public LogSystemConfig[] ExtractLogSystemConfigBatch(IEnumerable<ThirdPartyLogService> ls) =>
            Array.Empty<LogSystemConfig>();

        public void ApplyLogServiceChanges(List<ThirdPartyLogService> logServices, List<LogSystemConfig> rawConfigs)
        {
            ApplyLogServiceChangesCallCount++;
            AppliedRawConfigs.Add(rawConfigs);
        }
    }
}
