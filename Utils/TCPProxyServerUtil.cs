using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace CloudlogHelper.Utils;

[Obsolete]
internal sealed class TCPProxyServerUtil
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private static TcpListener _listener;
    private static CancellationTokenSource _cts = new();
    private static Task _listenerTask;

    private static Task _processingTask;

    /// <summary>
    ///     Start the tcp server.
    /// </summary>
    /// <param name="listenIp"></param>
    /// <param name="listenPort"></param>
    /// <param name="callback"></param>
    public static async Task StartAsync(
        IPAddress listenIp,
        int listenPort,
        Func<string, Task<string>> callback)
    {
        try
        {
            Stop();
            _cts = new CancellationTokenSource();
            ClassLogger.Info($"Starting TCP proxy server on {listenIp}:{listenPort}...");
            _listener = new TcpListener(listenIp, listenPort);
            _listener.Start();
            _listenerTask = Task.Run(() => ListenForClientsAsync(callback, _cts.Token));
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, "Failed to start TCP server");
            throw;
        }
    }

    /// <summary>
    ///     Listen!
    /// </summary>
    /// <param name="callback"></param>
    /// <param name="ct"></param>
    private static async Task ListenForClientsAsync(Func<string, Task<string>> callback, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                ClassLogger.Trace("Waiting for client...");
                var client = await _listener.AcceptTcpClientAsync(ct);
                ClassLogger.Trace($"Clent accepted:{client.Client.LocalEndPoint}");
                _ = HandleClientAsync(client, callback, ct);
            }
        }
        catch (OperationCanceledException)
        {
            ClassLogger.Debug("OperationCanceledException");
            // ignored;
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, "Listener error");
        }
    }

    private static async Task HandleClientAsync(TcpClient client, Func<string, Task<string>> callback,
        CancellationToken ct)
    {
        try
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                while (!ct.IsCancellationRequested)
                {
                    var buffer = new byte[1024];
                    var bytesRead = await stream.ReadAsync(buffer, ct);

                    if (bytesRead > 0)
                    {
                        var request = new byte[bytesRead];
                        Array.Copy(buffer, request, bytesRead);
                        var st = Encoding.ASCII.GetString(request);
                        ClassLogger.Trace($"Recv from outside: {st}");
                        var resp = await callback.Invoke(st);
                        ClassLogger.Trace($"Got response from rigctd: : {resp}, sending back...");
                        var respBytes = Encoding.ASCII.GetBytes(resp);
                        await stream.WriteAsync(respBytes, 0, respBytes.Length, ct);
                        await stream.FlushAsync(ct);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, $"Client handling error: {ex.Message}");
            client.Dispose();
        }
    }

    public static void Stop()
    {
        try
        {
            ClassLogger.Info("Stopping TCP proxy server...");

            _cts.Cancel();
            _listener?.Stop();

            _listenerTask?.Wait(500);
            _processingTask?.Wait(500);

            ClassLogger.Info("TCP proxy server stopped");
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, "Error while stopping server");
        }
    }
}