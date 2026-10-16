using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using CloudlogHelper.Enums;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using WsjtxUtilsPatch.WsjtxMessages;
using WsjtxUtilsPatch.WsjtxMessages.Messages;
using WsjtxUtilsPatch.WsjtxUdpServer;
using ReactiveUI;

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
    public async Task InitializeAsync_CalledOnEveryActivation_RestartsTheSocketOnlyOnce()
    {
        // The view calls InitializeAsync from WhenActivated and the service is a singleton, so
        // a subscription per activation would turn one settings change into N socket rebuilds
        // racing for the same port. Every restart here fails on purpose, so the error count is
        // a direct read of how many restarts ran.
        var settings = new ApplicationSettings();
        settings.UDPSettings.EnableUDPServer = true;
        settings.UDPSettings.UDPPort = "not-a-port";
        var restartCount = 0;

        using var service = new UdpServerService(new FakeApplicationSettingsService(settings));
        await service.InitializeAsync(_ => Task.CompletedTask, Count);
        await service.InitializeAsync(_ => Task.CompletedTask, Count);
        await service.InitializeAsync(_ => Task.CompletedTask, Count);
        var afterInitialization = restartCount;

        MessageBus.Current.SendMessage(new SettingsChanged { Part = ChangedPart.UDPServer });
        await Task.Delay(1200);

        Assert.Equal(afterInitialization + 1, restartCount);

        void Count(Microsoft.Extensions.Logging.LogLevel level, string message)
        {
            if (level == Microsoft.Extensions.Logging.LogLevel.Error) Interlocked.Increment(ref restartCount);
        }
    }

    [Fact]
    public async Task ReceiveLoop_KeepsReadingWhileAHandlerHangs()
    {
        // Pins the library contract the whole UDP path leans on: the receive loop dispatches
        // handlers without awaiting them. A later package update that awaits them would
        // serialize decodes and turn every per-message await into a fixed cost per datagram.
        const int hangMs = 1500;
        const int datagrams = 3;
        var port = GetFreeUdpPort();
        var hangingTasks = new ConcurrentBag<Task>();
        var entered = 0;

        Task Handler(WsjtxMessage message)
        {
            Interlocked.Increment(ref entered);
            var task = Task.Delay(hangMs);
            hangingTasks.Add(task);
            return task;
        }

        var udpHandler = DefaultUDPMessageHandler.GenerateDefaultUDPMessageHandlerWithCallback(Handler,
            _ => Task.CompletedTask);
        using var server = new WsjtxUdpServer(udpHandler, IPAddress.Loopback, port);
        server.Start(new CancellationTokenSource());
        using var client = new UdpClient();
        var endPoint = new IPEndPoint(IPAddress.Loopback, port);

        var elapsed = Stopwatch.StartNew();
        for (var i = 0; i < datagrams; i++)
        {
            var buffer = new byte[1500];
            var written = new Heartbeat("contract-probe", "1.0", "1").WriteMessageTo(buffer);
            await client.SendAsync(buffer, written, endPoint);
        }

        while (Volatile.Read(ref entered) < datagrams && elapsed.ElapsedMilliseconds < 600)
            await Task.Delay(20);

        Assert.Equal(datagrams, Volatile.Read(ref entered));
        Assert.True(elapsed.ElapsedMilliseconds < 600,
            $"receive loop waited for the handlers: {elapsed.ElapsedMilliseconds}ms for {datagrams} datagrams");

        await Task.WhenAll(hangingTasks);
    }

    [Fact]
    public async Task AStalledForwardEndpoint_DoesNotFanOutOneRequestPerMessage()
    {
        // A listener that accepts and never answers, so each forwarded POST hangs until its
        // timeout instead of failing fast. The in-flight request count is then a direct
        // measurement of how the forwarding path was bounded.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var inFlight = 0;
        var sockets = new ConcurrentBag<TcpClient>();

        _ = Task.Run(async () =>
        {
            while (true)
            {
                var socket = await listener.AcceptTcpClientAsync();
                Interlocked.Increment(ref inFlight);
                sockets.Add(socket);
            }
        });

        var settings = new ApplicationSettings();
        settings.UDPSettings.EnableUDPServer = true;
        settings.UDPSettings.UDPPort = GetFreeUdpPort().ToString();
        settings.UDPSettings.ForwardMessageToHttp = true;
        settings.UDPSettings.ForwardHttpAddress =
            $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/forward";

        using var service = new UdpServerService(new FakeApplicationSettingsService(settings));
        var handled = 0;
        await service.InitializeAsync(_ =>
        {
            Interlocked.Increment(ref handled);
            return Task.CompletedTask;
        }, (_, _) => { });

        using var client = new UdpClient();
        var endPoint = new IPEndPoint(IPAddress.Loopback, int.Parse(settings.UDPSettings.UDPPort));
        for (var i = 0; i < 20; i++)
        {
            var buffer = new byte[1500];
            var written = new Heartbeat("fanout", "1.0", "1").WriteMessageTo(buffer);
            await client.SendAsync(buffer, written, endPoint);
        }

        await Task.Delay(500);

        Assert.Equal(20, handled);
        Assert.True(Volatile.Read(ref inFlight) <= 2,
            $"{inFlight} forwards in flight for 20 messages - the endpoint stalled, not just slow");

        foreach (var socket in sockets) socket.Dispose();
    }

    [Fact]
    public void GetUdpBindingAddress_ReturnsQuestionMark_WhenSettingsProviderThrows()
    {
        var service = new UdpServerService(new ThrowingApplicationSettingsService());

        Assert.Equal("?", service.GetUdpBindingAddress());
    }

    private static int GetFreeUdpPort()
    {
        using var probe = new UdpClient(0);
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
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
