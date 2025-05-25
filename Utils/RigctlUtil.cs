using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using NLog;

namespace CloudlogHelper.Utils;

/// <summary>
///     Utils for rig communication. Supports both remote and local service.
/// </summary>
public class RigctldUtil
{
    /// <summary>
    ///     Rigctld processes residing in the background.
    /// </summary>
    private static Process? _backgroundProcess;

    /// <summary>
    ///     Semaphore for controlling access to the background process.
    /// </summary>
    private static readonly SemaphoreSlim _backgroundProcessSemaphore = new(1, 1);

    /// <summary>
    ///     One-time rigctld client process.
    /// </summary>
    private static Process? _onetimeRigctldClient;

    /// <summary>
    ///     Semaphore for controlling access to the one-time process.
    /// </summary>
    private static readonly SemaphoreSlim _onetimeSemaphore = new(1, 1);

    /// <summary>
    ///     Simple scheduler for rigctld requests.
    /// </summary>
    private static RigctldScheduler _scheduler;

    /// <summary>
    ///     Semaphore for controlling access to ExecuteCommand.
    /// </summary>
    private static readonly SemaphoreSlim _schedulerSemaphore = new(1, 1);

    /// <summary>
    ///     Log buffer of rigctld.
    /// </summary>
    private static readonly Queue<string> _rigctldLogBuffer = new();

    /// <summary>
    ///     AppendRigctldLog lock.
    /// </summary>
    private static readonly object _lock = new();

    /// <summary>
    ///     Keep-alive tcp client for rigctld communications.
    /// </summary>
    private static TcpClient? _tcpClient;

    private static string? _currentRigctldIp;

    private static int? _currentRigctldPort;

    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public static void InitScheduler()
    {
        _scheduler = new RigctldScheduler();
    }

    /// <summary>
    ///     Checks if any rigctld client is currently running, including _onetimeRigctldClient and _backgroundProcess.
    /// </summary>
    /// <returns>True if either background or one-time process is running, false otherwise.</returns>
    public static bool IsRigctldClientRunning()
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

    private static void AppendRigctldLog(string? log)
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

    private static string ReadAndClearRigctldLog()
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
    ///     Starts a one-time rigctld process with specified arguments.
    /// </summary>
    /// <param name="args">Command line arguments for rigctld.</param>
    /// <param name="timeoutMilliseconds">Timeout in milliseconds for process execution.</param>
    /// <returns>Tuple containing success status and output/error message.</returns>
    public static async Task<(bool, string)> StartOnetimeRigctldAsync(string args,
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
                    RedirectStandardOutput = true
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
                return (false, TranslationHelper.GetString("inithamlibfailed"));

            _onetimeRigctldClient.BeginOutputReadLine();
            _onetimeRigctldClient.BeginErrorReadLine();

            await _onetimeRigctldClient.WaitForExitAsync(new CancellationTokenSource(timeoutMilliseconds).Token);

            var exitCode = _onetimeRigctldClient.ExitCode;
            if (exitCode != 0)
            {
                outputBuilder.Clear();
                outputBuilder.Append("Rigctld Error: ");
                outputBuilder.Append(GetDescriptionFromReturnCode(exitCode.ToString()));

                ClassLogger.Warn(
                    $"Failed to start onetime rigctld:\n============================={ReadAndClearRigctldLog()}\n==============================\n");
            }

            var status = exitCode == 0;
            return (status, outputBuilder.ToString());
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, "Failed in starting OnetimeRigctl.");
            return (false, ex.Message);
        }
        finally
        {
            TerminateOnetimeProcess();
            _onetimeSemaphore.Release();
        }
    }


    /// <summary>
    ///     Restarts the background rigctld process with specified arguments.
    /// </summary>
    /// <param name="args">Command line arguments for rigctld.</param>
    /// <param name="ignoreIfRunning">If true, won't restart if process is already running.</param>
    /// <param name="timeoutMilliseconds">Timeout in milliseconds for process execution.</param>
    /// <returns>Tuple containing success status and output/error message.</returns>
    public static async Task<(bool, string)> RestartRigctldBackgroundProcessAsync(string args,
        bool ignoreIfRunning = false,
        int timeoutMilliseconds = 5000)
    {
        if (_backgroundProcess is not null && !_backgroundProcess.HasExited && ignoreIfRunning)
        {
            ClassLogger.Trace("Rigctld service is already running. Ignored.");
            return (true, "");
        }

        TerminateBackgroundProcess();
        ClassLogger.Debug("tRigctld offline....Trying to restart Rigctld background process...");
        var readyTcs = new TaskCompletionSource<(bool, string)>();
        var timeoutCts = new CancellationTokenSource(timeoutMilliseconds);

        await _backgroundProcessSemaphore.WaitAsync();

        // find a available port for rigctld
        _backgroundProcess = new Process
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
                RedirectStandardInput = true
            },
            EnableRaisingEvents = true
        };

        _backgroundProcess.Exited += (sender, eventArgs) =>
        {
            ClassLogger.Info($"Rigctld exited with code {_backgroundProcess.ExitCode}.");
            if (_backgroundProcess.ExitCode != 0)
                ClassLogger.Warn(
                    $"Rigctld exited abnormally. Here's the stacktrace:\n============================={ReadAndClearRigctldLog()}\n==============================\n");
            var failReason = GetDescriptionFromReturnCode(_backgroundProcess.ExitCode.ToString());
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
            if (!_backgroundProcess.Start()) return (false, "Failed to start Rigctld process");
            ClassLogger.Debug($"Starting Rigctld process with arg {args}");

            _backgroundProcess.BeginErrorReadLine();
            _backgroundProcess.BeginOutputReadLine();

            timeoutCts.Token.Register(() =>
            {
                if (readyTcs.Task.IsCompleted) return;
                readyTcs.TrySetResult((false, "Execution Timeout"));
            });

            return await readyTcs.Task;
        }
        catch (Exception e)
        {
            ClassLogger.Error(e, "Failed to start Rigctld process");
            return (false, $"Error in RestartRigctldBackgroundProcess: {e.Message}");
        }
        finally
        {
            timeoutCts.Dispose();
            // TerminateBackgroundProcess();
            _backgroundProcessSemaphore.Release();
        }
    }


    /// <summary>
    /// </summary>
    /// <param name="host"></param>
    /// <param name="port"></param>
    /// <param name="cmd"></param>
    /// <param name="highPriority"></param>
    /// <returns></returns>
    public static async Task<string> ExecuteCommandInScheduler(string host, int port, string cmd, bool highPriority)
    {
        if (highPriority)
            return await _scheduler?.EnqueueHighPriorityRequest(() => ExecuteCommand(host, port, cmd, false))!;

        return await _scheduler?.EnqueueLowPriorityRequest(() => ExecuteCommand(host, port, cmd + "\n"))!;
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
    private static async Task<string> ExecuteCommand(string host, int port, string cmd,
        bool throwExceptionIfRprtError = true)
    {
        using var cts = new CancellationTokenSource(DefaultConfigs.RigctldSocketTimeout);
        await _schedulerSemaphore.WaitAsync(cts.Token);
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
            // for commands that may return a code
            var rprtCode = results.Last();
            if (rprtCode.Contains("RPRT") && throwExceptionIfRprtError)
            {
                if (rprtCode == "RPRT 0") return sp;

                var code = rprtCode.Split(" ").Last();
                var des = GetDescriptionFromReturnCode(code.Replace("-", ""));
                throw new Exception(des);
            }

            return sp;
        }
        catch (OperationCanceledException e)
        {
            throw new Exception(TranslationHelper.GetString("rigtimeout"));
        }
        finally
        {
            _schedulerSemaphore.Release();
        }
    }

    public static async Task<RadioData> GetAllRigInfo(string ip, int port, bool reportRFPower, bool reportSplitInfo)
    {
        var testbk = new RadioData();
        var raw = await ExecuteCommandInScheduler(ip, port, "f", false);
        var freqStr = raw.Split("\n")[0];
        if (!long.TryParse(freqStr, out var freq))
            throw new Exception(TranslationHelper.GetString("unsupportedrigfreq") + freqStr);
        testbk.FrequencyRx = freq;
        testbk.FrequencyTx = freq;

        raw = await ExecuteCommandInScheduler(ip, port, "m", false);
        var mode = raw.Split("\n")[0];
        if (!DefaultConfigs.AvailableRigModes.Contains(mode))
            throw new Exception(TranslationHelper.GetString("unsupportedrigmode") + mode);
        testbk.ModeRx = mode;
        testbk.ModeTx = mode;

        // IT's possible that some rigs does not support spilt / pwr report at all!
        // on my xiegu g90n it can't get the split status...
        try
        {
            #region spilt

            if (reportSplitInfo)
            {
                raw = await ExecuteCommandInScheduler(ip, port, "s", false);
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
                        raw = await ExecuteCommandInScheduler(ip, port, "i", false);
                        var txfreqStr = raw.Split("\n")[0];
                        if (!long.TryParse(txfreqStr, out var freqtx))
                            throw new Exception(TranslationHelper.GetString("unsupportedrigfreq") + freqStr);
                        testbk.FrequencyTx = freqtx;

                        // fetch spilt tx mode 
                        raw = await ExecuteCommandInScheduler(ip, port, "x", false);
                        var modetx = raw.Split("\n")[0];
                        if (!DefaultConfigs.AvailableRigModes.Contains(modetx))
                            throw new Exception(TranslationHelper.GetString("unsupportedrigmode") + mode);
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
            if (reportRFPower)
            {
                raw = await ExecuteCommandInScheduler(ip, port, "l RFPOWER", false);
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

                raw = await ExecuteCommandInScheduler(ip, port,
                    $"\\power2mW {pwr} {testbk.FrequencyTx} {testbk.ModeTx}", false);
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

    /// <summary>
    ///     Parses all rig models from raw hamlib output.
    /// </summary>
    /// <param name="rawOutput">The raw output string from hamlib.</param>
    /// <returns>Dictionary mapping model names to their IDs.</returns>
    public static Dictionary<string, string> ParseAllModelsFromRawOutput(string rawOutput)
    {
        var result = new Dictionary<string, string>();
        foreach (var se in rawOutput.Split("\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (se.Contains("Rig #")) continue;
            var tmp = se.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tmp.Length != 6) continue;
            if (string.IsNullOrEmpty(tmp[0]) || string.IsNullOrEmpty(tmp[2])) continue;
            result[tmp[2]] = tmp[0];
        }

        return result;
    }


    /// <summary>
    ///     Gets the description for a hamlib return code.
    /// </summary>
    /// <param name="code">The error code as string.</param>
    /// <returns>Description of the error code.</returns>
    private static string GetDescriptionFromReturnCode(string code)
    {
        var codeDesMap = new Dictionary<string, string>
        {
            { "0", "Command completed successfully" },
            { "1", "Invalid parameter" },
            { "2", "Invalid configuration" },
            { "3", "Memory shortage" },
            { "4", "Feature not implemented" },
            { "5", "Communication timed out" },
            { "6", "IO error" },
            { "7", "Internal Hamlib error" },
            { "8", "Protocol error" },
            { "9", "Command rejected by the rig" },
            { "10", "Command performed, but arg truncated, result not guaranteed" },
            { "11", "Feature not available" },
            { "12", "Target VFO unaccessible" },
            { "13", "Communication bus error" },
            { "14", "Communication bus collision" },
            { "15", "NULL RIG handle or invalid pointer parameter" },
            { "16", "Invalid VFO" },
            { "17", "Argument out of domain of func" },
            { "18", "Function deprecated" },
            { "19", "Security error password not provided or crypto failure" },
            { "20", "Rig is not powered on" },
            { "21", "Limit exceeded" },
            { "22", "Access denied" }
        };
        if (codeDesMap.TryGetValue(code, out var result)) return "Hamlib error:" + result;
        return "Failed to init hamlib!";
    }

    public static string GenerateRigctldCmdArgs(string radioId, string port, bool disablePTT = false,
        bool allowExternal = false)
    {
        var args = new StringBuilder();
        args.Append($"-m {radioId} ");
        args.Append($"-r {port} ");

        var defaultHost = IPAddress.Loopback.ToString();
        if (allowExternal) defaultHost = IPAddress.Any.ToString();
        args.Append($"-T {defaultHost} -t {DefaultConfigs.RigctldDefaultPort} ");

        if (disablePTT) args.Append(@"--set-conf=""rts_state=OFF"" --set-conf ""dtr_state=OFF"" ");

        args.Append("-vvvvv");
        return args.ToString();
    }

    /// <summary>
    ///     Checks for processes that might conflict with rigctld.
    /// </summary>
    /// <returns>Name of the first conflicting process found, or empty string if none.</returns>
    public static string GetPossibleConflictProcess()
    {
        // check if jtdx/wsjtx/Rigctld running in background
        var localAllProcesses = Process.GetProcesses().Select(x => x.ProcessName).ToList();
        var hasConflict = localAllProcesses.Where(processName =>
            DefaultConfigs.PossibleRigctldConfilcts.Any(conflict =>
                processName.Contains(conflict, StringComparison.OrdinalIgnoreCase)
            )
        ).ToList();
        if (hasConflict.Any() && !IsRigctldClientRunning()) return hasConflict[0];

        return string.Empty;
    }

    /// <summary>
    ///     Terminates the background rigctld process if running.
    /// </summary>
    public static void TerminateBackgroundProcess()
    {
        if (_backgroundProcess is null) return;
        try
        {
            ClassLogger.Debug("Terminating BackgroundProcess...");
            _scheduler?.Stop();
            InitScheduler();
            _tcpClient?.Close();
            _tcpClient?.Dispose();
            _tcpClient = null;
            _backgroundProcess?.Kill();
            _backgroundProcess?.Dispose();
            _backgroundProcess = null;
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>
    ///     Terminates the one-time rigctld process if running.
    /// </summary>
    public static void TerminateOnetimeProcess()
    {
        if (_onetimeRigctldClient is null) return;
        try
        {
            ClassLogger.Debug("Terminating OnetimeProcess...");
            _onetimeRigctldClient?.Kill();
            _onetimeRigctldClient?.Dispose();
            _onetimeRigctldClient = null;
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>
    ///     Cleans up all rigctld processes (both background and one-time).
    /// </summary>
    public static void CleanUp()
    {
        TerminateBackgroundProcess();
        TerminateOnetimeProcess();
    }
}