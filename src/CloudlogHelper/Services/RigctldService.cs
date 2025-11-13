using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
using NLog;

namespace CloudlogHelper.Services;

public class RigctldService : IRigService, IDisposable
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     Semaphore for controlling access to the background process.
    /// </summary>
    private readonly SemaphoreSlim _backgroundProcessSemaphore = new(1, 1);

    /// <summary>
    ///     Semaphore for controlling access to ExecuteCommand.
    /// </summary>
    private readonly SemaphoreSlim _execCommandSemaphore = new(1, 1);

    /// <summary>
    ///     AppendRigctldLog lock.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     Semaphore for controlling access to the one-time process.
    /// </summary>
    private readonly SemaphoreSlim _onetimeSemaphore = new(1, 1);

    /// <summary>
    ///     Log buffer of rigctld.
    /// </summary>
    private readonly Queue<string> _rigctldLogBuffer = new();

    /// <summary>
    ///     Rigctld processes residing in the background.
    /// </summary>
    private Process? _backgroundProcess;

    /// <summary>
    ///     Whether this is disposed or not.
    /// </summary>
    private bool _disposed;

    /// <summary>
    ///     One-time rigctld client process.
    /// </summary>
    private Process? _onetimeRigctldClient;

    /// <summary>
    ///     Keep-alive tcp client for rigctld communications.
    /// </summary>
    private TcpClient? _tcpClient;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    /// <summary>
    ///     Checks if any rigctld client is currently running, including _onetimeRigctldClient and _backgroundProcess.
    /// </summary>
    /// <returns>True if either background or one-time process is running, false otherwise.</returns>
    public bool IsServiceRunning()
    {
        try
        {
            var running = false;
            if (_backgroundProcess is not null) running = !_backgroundProcess.HasExited;
            if (_onetimeRigctldClient is not null) running = running || !_onetimeRigctldClient.HasExited;
            return running;
        }
        catch (InvalidOperationException)
        {
            _onetimeRigctldClient = null;
            _backgroundProcess = null;
            return false;
        }
    }

    /// <summary>
    ///     Restarts the background rigctld process with specified arguments.
    /// </summary>
    /// <param name="args">Command line arguments for rigctld.</param>
    /// <param name="ignoreIfRunning">If true, won't restart if process is already running.</param>
    /// <param name="timeoutMilliseconds">Timeout in milliseconds for process execution.</param>
    /// <returns>Tuple containing success status and output/error message.</returns>
    public async Task StartService(CancellationToken token, params object[] args)
    {
        if (_backgroundProcess is not null && !_backgroundProcess.HasExited)
        {
            ClassLogger.Trace("Rigctld service is already running. Ignored.");
            return;
        }

        ClassLogger.Info($"Starting hamlib({string.Join(" ", args)})...");

        TerminateBackgroundProcess();
        ClassLogger.Debug("tRigctld offline....Trying to restart Rigctld background process...");
        var readyTcs = new TaskCompletionSource<(bool, string)>();

        await _backgroundProcessSemaphore.WaitAsync(token);

        // find a available port for rigctld
        _backgroundProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = DefaultConfigs.ExecutableRigctldPath,
                Arguments = args[0].ToString(),
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

        _backgroundProcess.Exited += (sender, eventArgs) =>
        {
            ClassLogger.Info($"Rigctld exited with code {_backgroundProcess.ExitCode}.");
            if (_backgroundProcess.ExitCode != 0)
                ClassLogger.Warn(
                    $"Rigctld exited abnormally. Here's the stacktrace:\n============================={ReadAndClearRigctldLog()}\n==============================\n");
            var failReason = RigUtils.GetDescriptionFromReturnCode(_backgroundProcess.ExitCode.ToString());
            readyTcs.TrySetResult((false, failReason));
        };

        _backgroundProcess.ErrorDataReceived += (sender, eventArgs) =>
        {
            AppendRigctldLog(eventArgs.Data);
            if (!string.IsNullOrEmpty(eventArgs.Data))
                // check if application start successfully
                // this is in stderr... very weird.
                if (eventArgs.Data.Contains("main: rigctld listening on port"))
                {
                    ClassLogger.Trace("Found key word: main: rigctld listening on port");
                    readyTcs.TrySetResult((true, ""));
                }
        };

        _backgroundProcess.OutputDataReceived += (sender, eventArgs) =>
        {
            AppendRigctldLog(eventArgs.Data);
            if (!string.IsNullOrWhiteSpace(eventArgs.Data)) ClassLogger.Trace($"stdout:{eventArgs.Data}");
        };

        try
        {
            if (!_backgroundProcess.Start()) throw new Exception("Failed to start Rigctld process");
            ClassLogger.Debug($"Starting Rigctld process with arg {args}");

            _backgroundProcess.BeginErrorReadLine();
            _backgroundProcess.BeginOutputReadLine();

            token.Register(() =>
            {
                if (readyTcs.Task.IsCompleted) return;
                readyTcs.TrySetResult((false, "Execution Timeout"));
            });

            var (item1, item2) = await readyTcs.Task;
            if (item1) return;
            throw new Exception(item2);
        }
        finally
        {
            _backgroundProcessSemaphore.Release();
        }
    }

    public RigBackendServiceEnum GetServiceType()
    {
        return RigBackendServiceEnum.Hamlib;
    }

    public async Task<RadioData> GetAllRigInfo(bool reportRfPower, bool reportSplitInfo,
        CancellationToken token, params object[] args)
    {
        try
        {
            var ip = args[0].ToString()!;
            var port = int.Parse(args[1].ToString()!);
            ClassLogger.Debug($"Querying rig info with ip:{ip} port:{port}");
            var testbk = new RadioData();
            var raw = await ExecuteCommand(ip, port, "f");
            ClassLogger.Trace($"we got: {raw}");
            var freqStr = raw.Split("\n")[0];
            if (!long.TryParse(freqStr, out var freq))
                throw new RigCommException(TranslationHelper.GetString(LangKeys.unsupportedrigfreq) + freqStr);
            testbk.FrequencyRx = freq;
            testbk.FrequencyTx = freq;

            raw = await ExecuteCommand(ip, port, "m");
            var mode = raw.Split("\n")[0];
            if (!DefaultConfigs.AvailableRigModes.Contains(mode))
                throw new RigCommException(TranslationHelper.GetString(LangKeys.unsupportedrigmode) + mode);
            testbk.ModeRx = mode;
            testbk.ModeTx = mode;

            // IT's possible that some rigs does not support spilt / pwr report at all!
            // on my xiegu g90n it can't get the split status...
            try
            {
                #region spilt

                if (reportSplitInfo)
                {
                    raw = await ExecuteCommand(ip, port, "s");
                    var splitStatus = raw.Split("\n")[0];
                    if (splitStatus is "0" or "1")
                    {
                        if (splitStatus == "0")
                        {
                            // tx == rx
                            ClassLogger.Trace("Spilt mode off.");
                        }
                        else
                        {
                            ClassLogger.Trace("Spilt mode on.");
                            testbk.IsSplit = true;
                            // fetch spilt tx freq
                            raw = await ExecuteCommand(ip, port, "i");
                            var txfreqStr = raw.Split("\n")[0];
                            if (!long.TryParse(txfreqStr, out var freqtx))
                                throw new RigCommException(
                                    TranslationHelper.GetString(LangKeys.unsupportedrigfreq) + freqStr);
                            testbk.FrequencyTx = freqtx;

                            // fetch spilt tx mode 
                            raw = await ExecuteCommand(ip, port, "x");
                            var modetx = raw.Split("\n")[0];
                            if (!DefaultConfigs.AvailableRigModes.Contains(modetx))
                                throw new RigCommException(
                                    TranslationHelper.GetString(LangKeys.unsupportedrigmode) + mode);
                            testbk.ModeTx = modetx;
                        }
                    }
                    else
                    {
                        // ignore it
                        ClassLogger.Trace($"Seems like this rig does not support split mode: {splitStatus}.");
                    }
                }

                #endregion

                // then, try to fetch power status...
                // add a "m" to make sure stream ends successfully.
                if (reportRfPower)
                {
                    raw = await ExecuteCommand(ip, port, "l RFPOWER");
                    var pwrStr = raw.Split("\n")[0];
                    if (!float.TryParse(pwrStr, out var pwr))
                    {
                        ClassLogger.Debug($"Seems like this rig does not support RFPOWER reading: {pwrStr}");
                        return testbk;
                    }

                    if (pwr < 0)
                    {
                        ClassLogger.Debug(
                            $"Seems like this rig does not support RFPOWER reading, the reading is negative: {pwrStr}");
                        return testbk;
                    }

                    raw = await ExecuteCommand(ip, port,
                        $"\\power2mW {pwr} {testbk.FrequencyTx} {testbk.ModeTx}");
                    var pwrmWStr = raw.Split("\n")[0];
                    if (!float.TryParse(pwrmWStr, out var pwrmw))
                    {
                        ClassLogger.Debug($"Seems like this rig does not support RFPOWER power2mW: {pwrStr}");
                        return testbk;
                    }

                    if (pwrmw < 0)
                    {
                        ClassLogger.Debug(
                            $"Seems like this rig does not support RFPOWER power2mW, the reading is negative: {pwrStr}");
                        return testbk;
                    }

                    testbk.Power = (float)Math.Round(pwrmw / 1000, 2);
                }
            }
            catch (Exception e)
            {
                ClassLogger.Debug($"Seems like this rig has unsupported feature: {e.Message}");
            }

            return testbk;
        }
        catch (Exception e)
        {
            if (token.IsCancellationRequested) return new RadioData();
            throw;
        }
    }

    public async Task<string> GetServiceVersion(params object[] args)
    {
        return await StartOnetimeRigctldAsync("--version");
    }

    public async Task<List<RigInfo>> GetSupportedRigModels()
    {
        var rigListText = await StartOnetimeRigctldAsync("--list");
        return _parseAllModelsFromRawOutput(rigListText);
    }

    public Task StopService(CancellationToken token)
    {
        ClassLogger.Info("Stoping hamlib...");
        TerminateBackgroundProcess();
        TerminateOnetimeProcess();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Starts a one-time rigctld process with specified arguments.
    /// </summary>
    /// <param name="args">Command line arguments for rigctld.</param>
    /// <param name="timeoutMilliseconds">Timeout in milliseconds for process execution.</param>
    /// <returns>Tuple containing success status and output/error message.</returns>
    private async Task<string> StartOnetimeRigctldAsync(string args,
        int timeoutMilliseconds = 5000)
    {
        var outputBuilder = new StringBuilder();
        await _onetimeSemaphore.WaitAsync();
        try
        {
            TerminateOnetimeProcess();

            _onetimeRigctldClient = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = DefaultConfigs.ExecutableRigctldPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    WorkingDirectory = DefaultConfigs.HamlibFilePath
                },
                EnableRaisingEvents = true
            };

            _onetimeRigctldClient.OutputDataReceived += (sender, e) =>
            {
                AppendRigctldLog(e.Data);
                if (!string.IsNullOrEmpty(e.Data)) outputBuilder.AppendLine(e.Data);
            };

            _onetimeRigctldClient.ErrorDataReceived += (sender, e) => { AppendRigctldLog(e.Data); };


            if (!_onetimeRigctldClient.Start())
                throw new Exception(TranslationHelper.GetString(LangKeys.inithamlibfailed));

            _onetimeRigctldClient.BeginOutputReadLine();
            _onetimeRigctldClient.BeginErrorReadLine();

            await _onetimeRigctldClient.WaitForExitAsync(new CancellationTokenSource(timeoutMilliseconds).Token);

            var exitCode = _onetimeRigctldClient.ExitCode;
            if (exitCode != 0)
            {
                outputBuilder.Clear();
                outputBuilder.Append("Rigctld Error: ");
                outputBuilder.Append(RigUtils.GetDescriptionFromReturnCode(exitCode.ToString()));

                ClassLogger.Warn(
                    $"Failed to start onetime rigctld:\n============================={ReadAndClearRigctldLog()}\n==============================\n");
            }

            return outputBuilder.ToString();
        }
        finally
        {
            TerminateOnetimeProcess();
            _onetimeSemaphore.Release();
        }
    }

    /// <summary>
    ///     Parses all rig models from raw hamlib output.
    /// </summary>
    /// <param name="rawOutput">The raw output string from hamlib.</param>
    /// <returns>Dictionary mapping model names to their IDs.</returns>
    private List<RigInfo> _parseAllModelsFromRawOutput(string rawOutput)
    {
        var result = new List<RigInfo>();
        // Dynamically calculate extraction length based on the header row
        using var tmp = rawOutput.Split("\n", StringSplitOptions.RemoveEmptyEntries).AsEnumerable().GetEnumerator();
        while (tmp.MoveNext())
            if (tmp.Current.Contains("Rig #"))
                break;

        var columnBounds = RigUtils.GetColumnBounds(tmp.Current);
        while (tmp.MoveNext())
            try
            {
                result.Add(RigUtils.ParseRigLine(tmp.Current, columnBounds));
            }
            catch (Exception e)
            {
                ClassLogger.Warn(e, "Failed to parse line {tmp.Current}");
            }

        return result;
    }

    /// <summary>
    ///     Terminates the background rigctld process if running.
    /// </summary>
    public void TerminateBackgroundProcess()
    {
        if (_backgroundProcess is null) return;
        try
        {
            ClassLogger.Debug("Terminating BackgroundProcess...");
            _backgroundProcess?.Kill();
            _backgroundProcess?.Dispose();
            _backgroundProcess = null;
            _tcpClient?.Close();
            _tcpClient?.Dispose();
            _tcpClient = null;
        }
        catch (Exception ex)
        {
            ClassLogger.Warn(ex, "Failed to terminate process.");
            // ignored
        }
    }

    /// <summary>
    ///     Terminates the one-time rigctld process if running.
    /// </summary>
    public void TerminateOnetimeProcess()
    {
        if (_onetimeRigctldClient is null) return;
        try
        {
            ClassLogger.Debug("Terminating OnetimeProcess...");
            _onetimeRigctldClient?.Kill();
            _onetimeRigctldClient?.Dispose();
            _onetimeRigctldClient = null;
        }
        catch (Exception ex)
        {
            ClassLogger.Warn(ex, "Failed to terminate process.");
            // ignored
        }
    }

    private void AppendRigctldLog(string? log)
    {
        if (string.IsNullOrEmpty(log)) return;
        if (!log.EndsWith("\n")) log += "\n";

        if (DefaultConfigs.MaxRigctldOutputLineCount == -1)
        {
            ClassLogger.Debug(log.Trim());
            return;
        }

        if (DefaultConfigs.MaxRigctldOutputLineCount == 0) return;

        lock (_lock)
        {
            _rigctldLogBuffer.Enqueue(log);
            while (_rigctldLogBuffer.Count > DefaultConfigs.MaxRigctldOutputLineCount)
                _rigctldLogBuffer.TryDequeue(out _);
        }
    }

    private string ReadAndClearRigctldLog()
    {
        if (DefaultConfigs.MaxRigctldOutputLineCount <= 0)
            return "Seems like rigctld logging is disabled in default config.\n";
        lock (_lock)
        {
            var snapShot = _rigctldLogBuffer.ToArray();
            _rigctldLogBuffer.Clear();
            return string.Join(string.Empty, snapShot);
        }
    }

    /// <summary>
    ///     Execute command by specifying addr and port.
    /// </summary>
    /// <param name="host"></param>
    /// <param name="port"></param>
    /// <param name="cmd"></param>
    /// <param name="throwExceptionIfRprtError"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private async Task<string> ExecuteCommand(string host, int port, string cmd,
        bool throwExceptionIfRprtError = true)
    {
        using var cts = new CancellationTokenSource(DefaultConfigs.RigctldSocketTimeout);
        await _execCommandSemaphore.WaitAsync(cts.Token);
        try
        {
            if (_tcpClient is null || !_tcpClient.Connected)
            {
                ClassLogger.Warn("Seems like tcp client is not connected. retrying...");
                _tcpClient?.Dispose();
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(host, port, cts.Token);
            }

            if (!cmd.EndsWith("\n")) cmd += "\n";
            var data = Encoding.ASCII.GetBytes(cmd);
            var stream = _tcpClient.GetStream();
            await stream.WriteAsync(data, 0, data.Length, cts.Token);
            ClassLogger.Trace($"Sent: {cmd}");

            var strData = new StringBuilder();
            var buffer = new byte[4096];
            do
            {
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                if (bytesRead == 0) break;
                var response = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                ClassLogger.Trace($"Received raw: {response}");
                strData.Append(response);
                // sometimes the stream is reaching end but data is not fully sent;
                // for example the dump_state message.
                // response always end with "\n"
                if (response.EndsWith("\n")) break;
            } while (true);

            var sp = strData.ToString();
            var results = sp.Split("\n", StringSplitOptions.RemoveEmptyEntries);
            if (results.Length == 0) throw new RigCommException("Invalid data format - maybe there's a dupe process?");
            // for commands that may return a code
            var rprtCode = results.Last();
            if (rprtCode.Contains("RPRT") && throwExceptionIfRprtError)
            {
                if (rprtCode == "RPRT 0") return sp;

                var code = rprtCode.Split(" ").Last();
                var des = RigUtils.GetDescriptionFromReturnCode(code.Replace("-", ""));
                throw new RigCommException(des);
            }

            return sp;
        }
        catch (OperationCanceledException e)
        {
            throw new RigCommException(TranslationHelper.GetString(LangKeys.rigtimeout));
        }
        finally
        {
            _execCommandSemaphore.Release();
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            TerminateBackgroundProcess();
            TerminateOnetimeProcess();
            _backgroundProcessSemaphore?.Dispose();
            _onetimeSemaphore?.Dispose();
            _execCommandSemaphore?.Dispose();
            _tcpClient?.Dispose();
        }

        _disposed = true;
    }
}