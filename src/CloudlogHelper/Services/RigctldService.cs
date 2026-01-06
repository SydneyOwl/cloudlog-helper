using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.Exceptions;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using DynamicData.Kernel;
using NLog;

namespace CloudlogHelper.Services;

public sealed class RigctldService : IRigService, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    
    private readonly SemaphoreSlim _backgroundProcessLock = new(1, 1);
    private readonly SemaphoreSlim _execCommandLock = new(1, 1);
    private readonly ConcurrentQueue<string> _rigctldLogBuffer = new();
    private readonly object _logBufferLock = new();
    
    private Process? _backgroundProcess;
    private Process? _onetimeProcess;
    private TcpClient? _tcpClient;
    private bool _disposed;
    
    private List<RigInfo>? _cachedRigModels;
    private string? _cachedVersion;
    
    public RigBackendServiceEnum GetServiceType() => RigBackendServiceEnum.Hamlib;
    
    public bool IsServiceRunning()
    {
        try
        {
            if (_tcpClient is not null)
            {
                var endPoint = (IPEndPoint)_tcpClient.Client.RemoteEndPoint!;
                if (_tcpClient.Connected && !IPAddress.IsLoopback(endPoint.Address)) return true;
            }
           
            return 
                (_backgroundProcess?.HasExited == false) || 
                (_onetimeProcess?.HasExited == false);
        }
        catch (InvalidOperationException)
        {
            // StopServiceSync();
            return false;
        }
    }
    
    public async Task StartService(CancellationToken token, params object[] args)
    {
        ClassLogger.Info("Starting hamlib service...");
        
        if (_backgroundProcess?.HasExited == false)
        {
            ClassLogger.Trace("Rigctld service already running");
            return;
        }
        
        TerminateBackgroundProcess();
        ClassLogger.Trace("Restarting rigctld background process...");
        
        var readyTcs = new TaskCompletionSource<(bool Success, string Message)>();
        await _backgroundProcessLock.WaitAsync(token);
        
        try
        {
            _backgroundProcess = CreateRigctldProcess(args[0]?.ToString() ?? string.Empty);
            SetupProcessEventHandlers(_backgroundProcess, readyTcs);
            
            if (!_backgroundProcess.Start())
                throw new Exception("Failed to start rigctld process");
            
            ClassLogger.Trace($"Starting rigctld with args: {args[0]}");
            
            _backgroundProcess.BeginErrorReadLine();
            _backgroundProcess.BeginOutputReadLine();
            
            token.Register(() => readyTcs.TrySetResult((false, "Execution timeout")));
            
            var result = await readyTcs.Task;
            if (!result.Success)
                throw new Exception(result.Message);
        }
        finally
        {
            _backgroundProcessLock.Release();
        }
    }
    
    public void StopServiceSync()
    {
        ClassLogger.Info("Stopping hamlib...");
        TerminateBackgroundProcess();
        TerminateOnetimeProcess();
        ClassLogger.Info("Hamlib service stopped.");
    }
    
    public Task StopService(CancellationToken token)
    {
        StopServiceSync();
        return Task.CompletedTask;
    }
    
    public async Task<RadioData> GetAllRigInfo(
        bool reportRfPower, 
        bool reportSplitInfo,
        CancellationToken token,
        params object[] args)
    {
        try
        {
            var ip = args[0]?.ToString() ?? throw new ArgumentNullException(nameof(args));
            var port = int.Parse(args[1]?.ToString() ?? "0");
            
            ClassLogger.Debug($"Querying rig info at {ip}:{port}");
            
            var radioData = await GetBaseRadioData(ip, port);
            
            if (reportSplitInfo)
                await TryGetSplitInfo(radioData, ip, port);
            
            if (reportRfPower)
                await TryGetPowerInfo(radioData, ip, port);
            
            return radioData;
        }
        catch (Exception e) when (!token.IsCancellationRequested)
        {
            throw new RigCommException($"Failed to get rig info: {e.Message}");
        }
    }
    
    public async Task<string> GetServiceVersion(params object[] args)
    {
        return _cachedVersion ??= await ExecuteOnetimeCommandAsync("--version");
    }
    
    public async Task<RigInfo[]> GetSupportedRigModels()
    {
        if (_cachedRigModels != null)
            return _cachedRigModels.ToArray();
        
        var rigListText = await ExecuteOnetimeCommandAsync("--list");
        _cachedRigModels = ParseRigModels(rigListText);
        return _cachedRigModels.ToArray();
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        TerminateBackgroundProcess();
        TerminateOnetimeProcess();
        
        _backgroundProcessLock?.Dispose();
        _execCommandLock?.Dispose();
        _tcpClient?.Dispose();
        
        _disposed = true;
        // GC.SuppressFinalize(this);
    }
    
    #region Internal
    
    private Process CreateRigctldProcess(string arguments)
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = DefaultConfigs.ExecutableRigctldPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                WorkingDirectory = DefaultConfigs.HamlibFilePath
            },
            EnableRaisingEvents = true
        };
    }
    
    private void SetupProcessEventHandlers(Process process, TaskCompletionSource<(bool, string)> readyTcs)
    {
        process.Exited += (_, _) =>
        {
            ClassLogger.Debug($"Rigctld exited with code {process.ExitCode}");
            if (process.ExitCode != 0)
                ClassLogger.Warn($"Rigctld exited abnormally:\n{ReadAndClearLogBuffer()}");
            
            var failReason = RigUtils.GetDescriptionFromReturnCode(process.ExitCode.ToString());
            readyTcs.TrySetResult((false, failReason));
        };
        
        process.ErrorDataReceived += (_, e) =>
        {
            AppendToLogBuffer(e.Data);
            if (!string.IsNullOrEmpty(e.Data) && e.Data.Contains("main: rigctld listening on port"))
                readyTcs.TrySetResult((true, string.Empty));
        };
        
        process.OutputDataReceived += (_, e) =>
        {
            AppendToLogBuffer(e.Data);
            // if (!string.IsNullOrWhiteSpace(e.Data))
            //     ClassLogger.Trace($"stdout: {e.Data}");
        };
    }
    
    private async Task<RadioData> GetBaseRadioData(string ip, int port)
    {
        var radioData = new RadioData();
        
        // Get frequency
        var freqRaw = await ExecuteCommand(ip, port, "f");
        var freqStr = freqRaw.Split('\n')[0];
        if (!long.TryParse(freqStr, out var freq))
            throw new RigCommException($"{TranslationHelper.GetString(LangKeys.unsupportedrigfreq)}{freqStr}");
        
        radioData.FrequencyRx = freq;
        radioData.FrequencyTx = freq;
        
        // Get mode
        var modeRaw = await ExecuteCommand(ip, port, "m");
        var mode = modeRaw.Split('\n')[0];
        if (!DefaultConfigs.AvailableRigModes.Contains(mode))
            throw new RigCommException($"{TranslationHelper.GetString(LangKeys.unsupportedrigmode)}{mode}");
        
        radioData.ModeRx = mode;
        radioData.ModeTx = mode;
        
        return radioData;
    }
    
    private async Task TryGetSplitInfo(RadioData radioData, string ip, int port)
    {
        try
        {
            var splitRaw = await ExecuteCommand(ip, port, "s");
            var splitStatus = splitRaw.Split('\n')[0];
            
            if (splitStatus == "0")
            {
                ClassLogger.Trace("Rigctld reported Split mode off");
                return;
            }
            
            if (splitStatus == "1")
            {
                ClassLogger.Trace("Rigctld reported Split mode on");
                radioData.IsSplit = true;
                
                // Get TX frequency
                var txFreqRaw = await ExecuteCommand(ip, port, "i");
                var txFreqStr = txFreqRaw.Split('\n')[0];
                if (!long.TryParse(txFreqStr, out var txFreq))
                    throw new RigCommException($"Unsupported TX frequency: {txFreqStr}");
                radioData.FrequencyTx = txFreq;
                
                // Get TX mode
                var txModeRaw = await ExecuteCommand(ip, port, "x");
                var txMode = txModeRaw.Split('\n')[0];
                if (!DefaultConfigs.AvailableRigModes.Contains(txMode))
                    throw new RigCommException($"Unsupported TX mode: {txMode}");
                radioData.ModeTx = txMode;
            }
            else
            {
                ClassLogger.Debug($"Rigctld: Rig doesn't support split mode: {splitStatus}");
            }
        }
        catch (Exception e)
        {
            ClassLogger.Debug(e,$"Rigctld: Failed to get split info.");
        }
    }
    
    private async Task TryGetPowerInfo(RadioData radioData, string ip, int port)
    {
        try
        {
            var powerRaw = await ExecuteCommand(ip, port, "l RFPOWER");
            var powerStr = powerRaw.Split('\n')[0];
            
            if (!float.TryParse(powerStr, out var power) || power < 0)
            {
                ClassLogger.Debug($"Rigctld: Rig doesn't support RFPOWER reading: {powerStr}");
                return;
            }
            
            var powerMwRaw = await ExecuteCommand(ip, port, 
                $"\\power2mW {power} {radioData.FrequencyTx} {radioData.ModeTx}");
            var powerMwStr = powerMwRaw.Split('\n')[0];
            
            if (float.TryParse(powerMwStr, out var powerMw) && powerMw >= 0)
                radioData.Power = (float)Math.Round(powerMw / 1000, 2);
        }
        catch (Exception e)
        {
            ClassLogger.Debug($"Rigctld: Failed to get power info: {e.Message}");
        }
    }
    
    private async Task<string> ExecuteOnetimeCommandAsync(string arguments, int timeout = DefaultConfigs.RigctldSocketTimeout)
    {
        await _execCommandLock.WaitAsync();
        try
        {
            TerminateOnetimeProcess();
            _onetimeProcess = CreateRigctldProcess(arguments);
            
            var outputBuilder = new StringBuilder();
            
            _onetimeProcess.OutputDataReceived += (_, e) =>
            {
                AppendToLogBuffer(e.Data);
                if (!string.IsNullOrEmpty(e.Data))
                    outputBuilder.AppendLine(e.Data);
            };
            
            _onetimeProcess.ErrorDataReceived += (_, e) => AppendToLogBuffer(e.Data);
            
            if (!_onetimeProcess.Start())
                throw new Exception(TranslationHelper.GetString(LangKeys.inithamlibfailed));
            
            _onetimeProcess.BeginOutputReadLine();
            _onetimeProcess.BeginErrorReadLine();
            
            using var cts = new CancellationTokenSource(timeout);
            await _onetimeProcess.WaitForExitAsync(cts.Token);
            
            if (_onetimeProcess.ExitCode != 0)
            {
                ClassLogger.Warn($"Onetime rigctld failed:\n{ReadAndClearLogBuffer()}");
                return $"Rigctld Error: {RigUtils.GetDescriptionFromReturnCode(_onetimeProcess.ExitCode.ToString())}";
            }
            
            return outputBuilder.ToString();
        }
        finally
        {
            TerminateOnetimeProcess();
            _execCommandLock.Release();
        }
    }
    
    private async Task<string> ExecuteCommand(string host, int port, string command)
    {
        using var cts = new CancellationTokenSource(DefaultConfigs.RigctldSocketTimeout);
        await _execCommandLock.WaitAsync(cts.Token);
        
        try
        {
            if (_tcpClient?.Connected != true)
            {
                _tcpClient?.Dispose();
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(host, port, cts.Token);
            }
            
            command = command.EndsWith('\n') ? command : command + '\n';
            var data = Encoding.ASCII.GetBytes(command);
            var stream = _tcpClient.GetStream();
            
            await stream.WriteAsync(data, 0, data.Length, cts.Token);
            ClassLogger.Trace($"Sent: {command.Trim()}");
            
            var response = new StringBuilder();
            var buffer = new byte[4096];
            
            do
            {
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                if (bytesRead == 0) break;
                
                var chunk = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                ClassLogger.Trace($"Received: {chunk.Trim()}");
                response.Append(chunk);
                
                if (chunk.EndsWith('\n'))
                    break;
            } while (!cts.Token.IsCancellationRequested);
            
            var result = response.ToString();
            var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            if (lines.Length == 0)
                throw new RigCommException("Invalid response format");
            
            var lastLine = lines.Last();
            if (lastLine.StartsWith("RPRT"))
            {
                var code = lastLine.Split(' ').Last();
                if (code != "0")
                {
                    var description = RigUtils.GetDescriptionFromReturnCode(code.TrimStart('-'));
                    throw new RigCommException(description);
                }
            }
            
            return result;
        }
        catch (OperationCanceledException)
        {
            throw new RigCommException(TranslationHelper.GetString(LangKeys.rigtimeout));
        }
        finally
        {
            _execCommandLock.Release();
        }
    }
    
    private List<RigInfo> ParseRigModels(string rawOutput)
    {
        var models = new List<RigInfo>();
        var lines = rawOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        var headerLine = lines.FirstOrDefault(l => l.Contains("Rig #"));
        if (headerLine == null) return models;
        
        var columnBounds = RigUtils.GetColumnBounds(headerLine);
        
        foreach (var line in lines.SkipWhile(l => !l.Contains("Rig #")).Skip(1))
        {
            try
            {
                models.Add(RigUtils.ParseRigLine(line, columnBounds));
            }
            catch (Exception e)
            {
                ClassLogger.Warn(e, $"Failed to parse line: {line}");
            }
        }
        
        return models;
    }
    
    private void TerminateBackgroundProcess()
    {
        TerminateProcess(ref _backgroundProcess, "background");
        _tcpClient?.Close();
        _tcpClient = null;
    }
    
    private void TerminateOnetimeProcess()
    {
        TerminateProcess(ref _onetimeProcess, "onetime");
    }
    
    private void TerminateProcess(ref Process? process, string processType)
    {
        if (process == null) return;
        
        try
        {
            ClassLogger.Debug($"Terminating {processType} process...");
            process.Kill();
            process.Dispose();
            process = null;
        }
        catch (Exception ex)
        {
            ClassLogger.Warn(ex, $"Failed to terminate {processType} process");
        }
    }
    
    private void AppendToLogBuffer(string? message)
    {
        if (string.IsNullOrEmpty(message)) return;
        
        if (DefaultConfigs.MaxRigctldOutputLineCount == -1)
        {
            ClassLogger.Debug(message.Trim());
            return;
        }
        
        if (DefaultConfigs.MaxRigctldOutputLineCount == 0) return;
        
        lock (_logBufferLock)
        {
            _rigctldLogBuffer.Enqueue(message);
            while (_rigctldLogBuffer.Count > DefaultConfigs.MaxRigctldOutputLineCount)
                _rigctldLogBuffer.TryDequeue(out _);
        }
    }
    
    private string ReadAndClearLogBuffer()
    {
        if (DefaultConfigs.MaxRigctldOutputLineCount <= 0)
            return "Rigctld logging disabled\n";
        
        lock (_logBufferLock)
        {
            var snapshot = string.Join(string.Empty, _rigctldLogBuffer);
            _rigctldLogBuffer.Clear();
            return snapshot;
        }
    }
    
    #endregion
}