// #if false
using System;
using System.Threading.Tasks;
using Nancy;
using Nancy.Hosting.Self;
using NLog;

namespace CloudlogHelper.Utils;

public class TCPDebugServer : IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private NancyHost _nancyHost;

    public TCPDebugServer(string host, int port)
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
        GC.SuppressFinalize(this);
        ClassLogger.Info("NancyService stopped");
    }
}

public class APIModule : NancyModule
{
    public APIModule()
    {
        Get("/async", async (args, ct) => 
        {
            await Task.Delay(100); 
            return "Hello Async World!";
        });
    }
}
// #endif