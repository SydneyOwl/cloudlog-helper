using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.LogService;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Tests;

public class QSOUploadServiceTests
{
    [Fact]
    public async Task Upload_UsesRefreshedThirdPartyServices_AfterSettingsWindowClosed()
    {
        var settings = new ApplicationSettings();
        var disabledService = new FakeThirdPartyLogService { AutoQSOUploadEnabled = false };
        settings.LogServices.Add(disabledService);
        var enabledService = new FakeThirdPartyLogService { AutoQSOUploadEnabled = true };

        using var service = new QSOUploadService(
            new FakeApplicationSettingsService(settings),
            new FakeUdpServerService(),
            new FakeNotificationManager());
        await service.StartAsync();

        var beforeReload = CreateRecordedCallsignDetail("K1AAA");
        await service.EnqueueQSOForUploadAsync(beforeReload);
        await WaitForStatusAsync(beforeReload, UploadStatus.Ignored);
        Assert.Equal(0, enabledService.UploadCalls);

        // simulate a settings-window save that swapped in a different service list
        settings.LogServices = new List<ThirdPartyLogService> { enabledService };
        MessageBus.Current.SendMessage(new SettingsChanged { Part = ChangedPart.NothingJustClosed });

        var afterReload = CreateRecordedCallsignDetail("K1BBB");
        await service.EnqueueQSOForUploadAsync(afterReload);
        await WaitForStatusAsync(afterReload, UploadStatus.Success);
        Assert.Equal(1, enabledService.UploadCalls);

        await service.StopAsync();
    }

    [Fact]
    public async Task Upload_UsesRefreshedCloudlogSettings_AfterCloudlogSettingsChanged()
    {
        var settings = new ApplicationSettings();

        using var service = new QSOUploadService(
            new FakeApplicationSettingsService(settings),
            new FakeUdpServerService(),
            new FakeNotificationManager());
        await service.StartAsync();

        var beforeReload = CreateRecordedCallsignDetail("K1CCC");
        await service.EnqueueQSOForUploadAsync(beforeReload);
        await WaitForStatusAsync(beforeReload, UploadStatus.Ignored);

        // auto upload enabled with an unreachable endpoint: the upload is attempted
        // (and fails) instead of being ignored, proving the new snapshot is in effect
        settings.CloudlogSettings.AutoQSOUploadEnabled = true;
        settings.CloudlogSettings.CloudlogUrl = "http://127.0.0.1:1";
        settings.CloudlogSettings.CloudlogApiKey = "key";
        MessageBus.Current.SendMessage(new SettingsChanged { Part = ChangedPart.Cloudlog });

        var afterReload = CreateRecordedCallsignDetail("K1DDD");
        await service.EnqueueQSOForUploadAsync(afterReload);
        await WaitForStatusAsync(afterReload, UploadStatus.Fail);
        Assert.False(afterReload.UploadedServices.GetValueOrDefault("CloudlogService"));

        await service.StopAsync();
    }

    private static async Task WaitForStatusAsync(RecordedCallsignDetail rcd, UploadStatus expected,
        int timeoutMs = 30000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (rcd.UploadStatus != expected)
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException(
                    $"QSO {rcd.DXCall} did not reach {expected}; last status: {rcd.UploadStatus}, reason: {rcd.FailReason}");
            await Task.Delay(50);
        }
    }

    private static RecordedCallsignDetail CreateRecordedCallsignDetail(string dxCall)
    {
        return new RecordedCallsignDetail
        {
            DXCall = dxCall,
            DXGrid = "FN42",
            Mode = "FT8",
            ReportSent = "-10",
            ReportReceived = "-08",
            DateTimeOn = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            DateTimeOff = new DateTime(2026, 1, 2, 3, 5, 5, DateTimeKind.Utc),
            TXFrequencyInHz = 14_074_000,
            TXFrequencyInMeters = "20m",
            MyCall = "BG7AA",
            MyGrid = "OL63",
            Comments = "test"
        };
    }

    private sealed class FakeThirdPartyLogService : ThirdPartyLogService
    {
        public int UploadCalls { get; private set; }

        public override Task TestConnectionAsync(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        public override Task UploadQSOAsync(string? adif, CancellationToken token)
        {
            Assert.False(string.IsNullOrWhiteSpace(adif));
            UploadCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeApplicationSettingsService : IApplicationSettingsService
    {
        private readonly ApplicationSettings _settings;

        public FakeApplicationSettingsService(ApplicationSettings settings)
        {
            _settings = settings;
        }

        public void ApplySettings(object owner, List<LogSystemConfig>? rawConfigs = null)
        {
            throw new NotSupportedException();
        }

        public void RestoreSettings(object owner)
        {
            throw new NotSupportedException();
        }

        public ApplicationSettings GetCurrentSettings() => _settings;

        public ApplicationSettings GetCurrentDraftSettingsSnapshot() => _settings;

        public bool TryGetDraftSettings(object owner, out ApplicationSettings? draftSettings)
        {
            draftSettings = _settings;
            return true;
        }
    }

    private sealed class FakeUdpServerService : IUdpServerService
    {
        public string GetUdpBindingAddress() => "(127.0.0.1:0)";

        public bool IsUdpServerEnabled() => false;

        public bool IsUdpServerRunning() => false;

        public bool IsNotifyOnQsoMade() => false;

        public bool IsNotifyOnQsoUploaded() => false;

        public int QSOUploadRetryCount() => 1;

        public Task InitializeAsync(Func<WsjtxMessage, Task> handler, Action<LogLevel, string> logger)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeNotificationManager : INotificationManager
    {
        public Task Initialize() => Task.CompletedTask;

        public Task ShowNotificationAsync(string title, string message = "", string informativeText = "") =>
            Task.CompletedTask;
    }
}
