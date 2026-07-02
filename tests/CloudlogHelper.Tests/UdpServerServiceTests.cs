using CloudlogHelper.Models;
using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;

namespace CloudlogHelper.Tests;

public class UdpServerServiceTests
{
    [Theory]
    [InlineData(false, "2237", "(127.0.0.1:2237)")]
    [InlineData(true, "2237", "(0.0.0.0:2237)")]
    [InlineData(false, "", "(?)")]
    public void GetUdpBindingAddress_ReflectsCurrentSettings(
        bool allowOutsideConnection,
        string port,
        string expected)
    {
        var settings = new ApplicationSettings();
        settings.UDPSettings.EnableConnectionFromOutside = allowOutsideConnection;
        settings.UDPSettings.UDPPort = port;
        var service = new UdpServerService(new FakeApplicationSettingsService(settings));

        Assert.Equal(expected, service.GetUdpBindingAddress());
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData("not-a-number", 1)]
    [InlineData("", 1)]
    public void QSOUploadRetryCount_ReturnsConfiguredValue_OrDefault(string configuredValue, int expected)
    {
        var settings = new ApplicationSettings();
        settings.UDPSettings.RetryCount = configuredValue;
        var service = new UdpServerService(new FakeApplicationSettingsService(settings));

        Assert.Equal(expected, service.QSOUploadRetryCount());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsUdpServerEnabled_ReflectsCurrentSettings(bool enabled)
    {
        var settings = new ApplicationSettings();
        settings.UDPSettings.EnableUDPServer = enabled;
        var service = new UdpServerService(new FakeApplicationSettingsService(settings));

        Assert.Equal(enabled, service.IsUdpServerEnabled());
    }

    [Fact]
    public void NotificationFlags_ReflectCurrentSettings()
    {
        var settings = new ApplicationSettings();
        settings.UDPSettings.PushNotificationOnQSOMade = true;
        settings.UDPSettings.PushNotificationOnQSOUploaded = false;
        var service = new UdpServerService(new FakeApplicationSettingsService(settings));

        Assert.True(service.IsNotifyOnQsoMade());
        Assert.False(service.IsNotifyOnQsoUploaded());
    }

    [Fact]
    public async Task InitializeAsync_WhenUdpServerDisabled_DoesNotStartServerOrLogErrors()
    {
        var settings = new ApplicationSettings();
        settings.UDPSettings.EnableUDPServer = false;
        var logs = new List<Microsoft.Extensions.Logging.LogLevel>();
        using var service = new UdpServerService(new FakeApplicationSettingsService(settings));

        await service.InitializeAsync(_ => Task.CompletedTask, (level, _) => logs.Add(level));

        Assert.False(service.IsUdpServerRunning());
        Assert.DoesNotContain(Microsoft.Extensions.Logging.LogLevel.Error, logs);
    }

    [Fact]
    public void GetUdpBindingAddress_ReturnsQuestionMark_WhenSettingsProviderThrows()
    {
        var service = new UdpServerService(new ThrowingApplicationSettingsService());

        Assert.Equal("?", service.GetUdpBindingAddress());
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

    private sealed class ThrowingApplicationSettingsService : IApplicationSettingsService
    {
        public void ApplySettings(object owner, List<LogSystemConfig>? rawConfigs = null) =>
            throw new NotSupportedException();

        public void RestoreSettings(object owner) => throw new NotSupportedException();

        public ApplicationSettings GetCurrentSettings() => throw new InvalidOperationException("boom");

        public ApplicationSettings GetCurrentDraftSettingsSnapshot() => throw new NotSupportedException();

        public bool TryGetDraftSettings(object owner, out ApplicationSettings? draftSettings)
        {
            throw new NotSupportedException();
        }
    }
}
