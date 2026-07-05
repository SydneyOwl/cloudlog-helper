using AutoMapper;
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
            CreateMapper());
        var owner = new object();
        var rawConfigs = new List<LogSystemConfig> { new() };

        Assert.True(service.TryGetDraftSettings(owner, out _));
        service.ApplySettings(owner, rawConfigs);

        Assert.Equal(2, logSystemManager.ApplyLogServiceChangesCallCount);
        Assert.All(logSystemManager.AppliedRawConfigs, configs => Assert.Same(rawConfigs, configs));
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
        return ApplicationSettingsService.GenerateApplicationSettingsService(
            new FakeLogSystemManager(),
            reinit: true,
            version: new Version(0, 0, 0),
            CreateMapper());
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
