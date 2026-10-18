using System;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using System.Threading.Tasks;
using AutoMapper;
using Google.Protobuf;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using DynamicData;
using Microsoft.Extensions.Logging;
using QSOTestValues = WsjtxUtilsPatch.WsjtxMessages.Messages;
using ReactiveUI;

namespace CloudlogHelper.Tests;

public class PluginServiceTests
{
    [Fact]
    public async Task EnabledPlugin_ConstructionSucceeds_AndServesNoPluginsWithoutPipes()
    {
        // The constructor wires three MessageBus listeners and, when plugins are enabled, kicks
        // off the named-pipe server, which cannot bind in a test host. What is observable here:
        // construction must not throw, and nothing is ever reported as connected.
        using var service = CreateService(enablePlugin: true);
        await WaitForIdleAsync(service);

        Assert.Empty(service.GetConnectedPluginsAsync().Result);
        Assert.Equal(0u, (await service.GetServerInfoAsync(CancellationToken.None)).ConnectedPluginCount);
    }

    [Fact]
    public async Task DisabledPlugin_ConstructionSucceeds_AndServesNoPlugins()
    {
        using var service = CreateService(enablePlugin: false);
        await WaitForIdleAsync(service);

        Assert.Empty(service.GetConnectedPluginsAsync().Result);
        Assert.Equal(0u, (await service.GetServerInfoAsync(CancellationToken.None)).ConnectedPluginCount);
    }

    [Fact]
    public async Task BroadcastMessage_WithoutRunningService_IsIgnored()
    {
        using var service = CreateService(enablePlugin: false);

        // The UDP path calls this for every decode; it must be a no-op rather than a throw.
        await service.BroadcastMessageAsync(NewDecodeMessage(), CancellationToken.None);

        Assert.Empty(service.GetConnectedPluginsAsync().Result);
    }

    [Fact]
    public async Task BroadcastMessage_NullMessage_IsIgnored()
    {
        using var service = CreateService(enablePlugin: false);

        await service.BroadcastMessageAsync(null, CancellationToken.None);

        Assert.Empty(service.GetConnectedPluginsAsync().Result);
    }

    [Fact]
    public async Task SettingsSnapshot_ReportsCurrentSettingsToPlugins()
    {
        var settings = new ApplicationSettings();
        settings.BasicSettings.InstanceName = "snapshot-instance";
        settings.BasicSettings.EnablePlugin = false;
        settings.BasicSettings.DisableAllCharts = true;
        settings.BasicSettings.MyMaidenheadGrid = "OM89";
        settings.UDPSettings.EnableUDPServer = true;
        settings.CloudlogSettings.AutoQSOUploadEnabled = true;

        using var service = CreateService(enablePlugin: false, settings);

        var snapshot = await service.GetRuntimeSnapshotAsync(CancellationToken.None);

        Assert.Equal("snapshot-instance", snapshot.SettingsSnapshot.InstanceName);
        Assert.True(snapshot.SettingsSnapshot.EnablePlugin is false);
        Assert.True(snapshot.SettingsSnapshot.DisableAllCharts);
        Assert.Equal("OM89", snapshot.SettingsSnapshot.MyMaidenheadGrid);
        Assert.True(snapshot.SettingsSnapshot.AutoQsoUploadEnabled);
        Assert.True(snapshot.SettingsSnapshot.EnableUdpServer);
    }

    [Fact]
    public async Task ServerInfo_ReportsInstanceVersionAndKeepalive()
    {
        var settings = new ApplicationSettings();
        settings.BasicSettings.InstanceName = "info-instance";

        using var service = CreateService(enablePlugin: false, settings);

        var info = await service.GetServerInfoAsync(CancellationToken.None);

        Assert.Equal("info-instance", info.ClhInstanceId);
        Assert.Equal(VersionInfo.Version, info.ClhVersion);
        Assert.Equal((uint)DefaultConfigs.PluginKeepaliveTimeoutSec, info.KeepaliveTimeoutSec);
        Assert.Equal(0u, info.ConnectedPluginCount);
    }

    [Fact]
    public async Task UdpSnapshot_ReflectsTheUdpServiceState()
    {
        using var service = new PluginService(
            CreateSettings(),
            new StubRigBackendManager(),
            new StubPluginUdpServerService(running: true),
            new StubWindowManagerService(),
            new StubInAppNotificationService(),
            new StubQsoQueueStore(),
            new StubQsoUploadService());

        var snapshot = await service.GetRuntimeSnapshotAsync(CancellationToken.None);

        Assert.True(snapshot.UdpSnapshot.ServerRunning);
    }

    [Fact]
    public async Task DisconnectPlugin_UnknownUuid_ReturnsFalse()
    {
        using var service = CreateService(enablePlugin: false);

        Assert.False(await service.DisconnectPluginAsync("no-such-uuid", "test", CancellationToken.None));
    }

    [Fact]
    public async Task DisconnectPlugin_BlankUuid_ReturnsFalse()
    {
        using var service = CreateService(enablePlugin: false);

        Assert.False(await service.DisconnectPluginAsync("   ", "test", CancellationToken.None));
    }

    private static PluginService CreateService(bool enablePlugin, ApplicationSettings? settings = null)
    {
        return new PluginService(CreateSettings(settings, enablePlugin), new StubRigBackendManager(),
            new StubPluginUdpServerService(), new StubWindowManagerService(),
            new StubInAppNotificationService(), new StubQsoQueueStore(), new StubQsoUploadService());
    }

    private static IApplicationSettingsService CreateSettings(ApplicationSettings? settings = null,
        bool enablePlugin = false)
    {
        var final = settings ?? new ApplicationSettings();
        final.BasicSettings.EnablePlugin = enablePlugin;
        return new FakeApplicationSettingsService(final);
    }

    private static IMessage? NewDecodeMessage() =>
        PbMsgConverter.ToPbWsjtxMessage(new QSOTestValues.Decode
        {
            Id = "unit-test",
            Mode = "FT8"
        });

    private static async Task WaitForIdleAsync(PluginService service)
    {
        // The constructor kicks off Task.Run-ing pipe work; give it a moment to settle so a
        // failure inside it surfaces here instead of as an unobserved task exception.
        for (var i = 0; i < 50; i++)
        {
            if (service.GetConnectedPluginsAsync().Result.Count == 0) await Task.Delay(20);
        }
    }

    private sealed class FakeApplicationSettingsService : IApplicationSettingsService
    {
        private readonly ApplicationSettings _settings;

        public FakeApplicationSettingsService(ApplicationSettings settings)
        {
            _settings = settings;
        }

        public void ApplySettings(object owner, List<LogSystemConfig>? rawConfigs = null) { }

        public void RestoreSettings(object owner) { }

        public ApplicationSettings GetCurrentSettings() => _settings;

        public ApplicationSettings GetCurrentDraftSettingsSnapshot() => _settings;

        public bool TryGetDraftSettings(object owner, out ApplicationSettings? draftSettings)
        {
            draftSettings = _settings;
            return true;
        }
    }

    private sealed class StubRigBackendManager : IRigBackendManager
    {
        public Task InitializeAsync() => Task.CompletedTask;
        public RigBackendServiceEnum GetServiceType() => RigBackendServiceEnum.Hamlib;
        public string GetServiceEndpointAddress() => "127.0.0.1:4532";
        public IRigService GetServiceByName(RigBackendServiceEnum backend) => throw new NotSupportedException();
        public bool IsServiceRunning() => false;
        public Task RestartService() => Task.CompletedTask;
        public Task StopService() => Task.CompletedTask;
        public Task StartService() => Task.CompletedTask;
        public Task<RigInfo[]> GetSupportedRigModels() => Task.FromResult(Array.Empty<RigInfo>());
        public Task<RadioData> GetAllRigInfo() => throw new NotSupportedException();
        public Task<string> GetServiceVersion() => Task.FromResult("stub");
        public Task<(string Version, List<RigInfo> Models)> GetHamlibCatalogAsync() =>
            Task.FromResult(("stub", new List<RigInfo>()));
        public bool GetPollingAllowed() => false;
        public int GetPollingInterval() => 0;
        public Task ExecuteTest(RigBackendServiceEnum backend, ApplicationSettings draftSettings,
            CancellationToken token) => Task.CompletedTask;
    }

    private sealed class StubPluginUdpServerService : IUdpServerService
    {
        private readonly bool _running;

        public StubPluginUdpServerService(bool running = false)
        {
            _running = running;
        }

        public string GetUdpBindingAddress() => "(127.0.0.1:2237)";
        public bool IsUdpServerEnabled() => true;
        public bool IsUdpServerRunning() => _running;
        public bool IsNotifyOnQsoMade() => false;
        public bool IsNotifyOnQsoUploaded() => false;
        public int QSOUploadRetryCount() => 1;
        public Task InitializeAsync(Func<QSOTestValues.WsjtxMessage, EndPoint, Task> handler,
            Action<Microsoft.Extensions.Logging.LogLevel, string> logger) => Task.CompletedTask;
    }

    private sealed class StubWindowManagerService : IWindowManagerService
    {
        public string Track(Avalonia.Controls.Window window, IDisposable? lifetime = null) => "stub";

        public Task<T?> CreateAndShowWindowByVm<T>(Type wType, Avalonia.Controls.Window? toplevel = null,
            bool dialog = true) => throw new NotSupportedException();

        public Task CreateAndShowWindowByVm(Type wType, Avalonia.Controls.Window? toplevel = null,
            bool dialog = true) => throw new NotSupportedException();

        public Task CreateAndShowWindowByVm(PluginControllableWindow wType, Avalonia.Controls.Window? toplevel = null,
            bool dialog = true) => throw new NotSupportedException();

        public T GetViewModelInstance<T>() => throw new NotSupportedException();
        public void CloseWindowBySeq(string seq) { }
        public Task LaunchBrowser(string uri, Avalonia.Controls.Window? topLevel = null) => Task.CompletedTask;
        public Task LaunchDir(string path, Avalonia.Controls.Window? topLevel = null) => Task.CompletedTask;

        public Task<IReadOnlyList<Avalonia.Platform.Storage.IStorageFile?>> OpenFilePickerAsync(
            Avalonia.Platform.Storage.FilePickerOpenOptions options, Avalonia.Controls.Window? topLevel = null) =>
            throw new NotSupportedException();

        public Avalonia.Controls.Window? GetToplevel(Type vmType) => null;

        public Task<Avalonia.Platform.Storage.IStorageFile?> OpenFileSaverAsync(
            Avalonia.Platform.Storage.FilePickerSaveOptions options, Avalonia.Controls.Window? topLevel = null) =>
            throw new NotSupportedException();
    }

    private sealed class StubInAppNotificationService : IInAppNotificationService
    {
        public Task SendInfoNotificationAsync(string message) => Task.CompletedTask;
        public Task SendSuccessNotificationAsync(string message) => Task.CompletedTask;
        public Task SendWarningNotificationAsync(string message) => Task.CompletedTask;
        public Task SendErrorNotificationAsync(string message) => Task.CompletedTask;
        public void SendInfoNotificationSync(string message) { }
        public void SendSuccessNotificationSync(string message) { }
        public void SendWarningNotificationSync(string message) { }
        public void SendErrorNotificationSync(string message) { }
    }

    private sealed class StubQsoQueueStore : IQsoQueueStore
    {
        private readonly List<RecordedCallsignDetail> _items = new();

        public IObservable<IChangeSet<RecordedCallsignDetail, string>> Connect() =>
            throw new NotSupportedException();

        public IEnumerable<RecordedCallsignDetail> Items => _items;

        public void Add(RecordedCallsignDetail qso) => _items.Add(qso);

        public void AddRange(IEnumerable<RecordedCallsignDetail> qsos) => _items.AddRange(qsos);

        public void Remove(RecordedCallsignDetail qso) => _items.Remove(qso);

        public void RemoveRange(IEnumerable<RecordedCallsignDetail> qsos)
        {
            foreach (var qso in qsos) _items.Remove(qso);
        }

        public void RemoveByUuids(IEnumerable<string> ids) { }
    }

    private sealed class StubQsoUploadService : IQSOUploadService
    {
        public Task StartAsync(CancellationToken token = default) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public Task EnqueueQSOForUploadAsync(RecordedCallsignDetail detail, CancellationToken token = default) =>
            Task.CompletedTask;
    }
}
