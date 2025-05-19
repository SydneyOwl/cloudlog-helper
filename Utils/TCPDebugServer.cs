using Nancy.Hosting.Self;
using System;
using NLog;

public class NancyService : IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private NancyHost _nancyHost;

    public NancyService(string host, int port)
    {
        var config = new HostConfiguration
        {
            UrlReservations = new UrlReservations { CreateAutomatically = true }
        };
        
        _nancyHost = new NancyHost(config, new Uri($"http://{host}:{port}"));
        _nancyHost.Start();
        ClassLogger.Info($"NancyService started on port {port}");
    }

    public void Dispose()
    {
        _nancyHost?.Stop();
        _nancyHost?.Dispose();
        ClassLogger.Info("NancyService stopped");
    }
}