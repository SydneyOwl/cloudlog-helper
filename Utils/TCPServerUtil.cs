using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace CloudlogHelper.Utils;

/// <summary>
///
/// </summary>
sealed class TCPServerUtil
{
    private static TcpListener? _tcpListener;
    private static readonly CancellationTokenSource _cts = new();
    private static Task? _listenerTask;
    
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public static async Task RestartTCPServerAsync(IPAddress ip, int listenPort, IPAddress forwardIp, int forwardPort,
        Action<string>? tcpLogger = null)
    {
        try
        {
            TerminateTCPServer();
            ClassLogger.Debug("Starting TCP proxy server...");
            
            _tcpListener = new TcpListener(ip, listenPort);
            _listenerTask = Task.Run(() => ListenForConnections(forwardIp, forwardPort, tcpLogger, _cts.Token));
        }
        catch (Exception e)
        {
            ClassLogger.Error($"Exception starting TCP server: {e.Message}");
            tcpLogger?.Invoke($"Error: {e.Message}");
        }
    }

    private static async Task ListenForConnections(IPAddress forwardIp, int forwardPort, Action<string>? logger, CancellationToken ct)
    {
        if (_tcpListener is null) return;
        
        _tcpListener.Start();
        logger?.Invoke($"TCP proxy started, forwarding to {forwardIp}:{forwardPort}");
        
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _tcpListener.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, forwardIp, forwardPort, logger, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception e)
        {
            ClassLogger.Error($"TCP listener error: {e.Message}");
            logger?.Invoke($"TCP error: {e.Message}");
        }
    }

    private static async Task HandleClientAsync(TcpClient client, IPAddress forwardIp, int forwardPort, Action<string>? logger, CancellationToken ct)
    {
        try
        {
            using (client)
            using (var forwardClient = new TcpClient())
            {
                await forwardClient.ConnectAsync(forwardIp, forwardPort, ct);
                
                logger?.Invoke($"New connection from {client.Client.RemoteEndPoint}, forwarding to {forwardIp}:{forwardPort}");
                
                var clientStream = client.GetStream();
                var forwardStream = forwardClient.GetStream();
                
                // Start bidirectional forwarding
                var clientToForward = clientStream.CopyToAsync(forwardStream, ct);
                var forwardToClient = forwardStream.CopyToAsync(clientStream, ct);
                
                await Task.WhenAny(clientToForward, forwardToClient);
            }
        }
        catch (Exception e)
        {
            ClassLogger.Warn($"Connection handling error: {e.Message}");
            logger?.Invoke($"Connection error: {e.Message}");
        }
    }

    public static void TerminateTCPServer()
    {
        ClassLogger.Debug("Shutting down TCP server...");
        try
        {
            if (!_cts.IsCancellationRequested) _cts.Cancel();
            _tcpListener?.Stop();
            _listenerTask?.Wait(1000); // Give it a second to shut down
        }
        catch (Exception e)
        {
            ClassLogger.Warn($"Error occurred while shutting down TCP server: {e.Message}");
        }

        _tcpListener = null;
        _listenerTask = null;
    }
}