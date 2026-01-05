using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.Exceptions;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using Flurl.Http;
using Newtonsoft.Json;
using NLog;
using ReactiveUI;

namespace CloudlogHelper.Services;

public class RigBackendManager : IRigBackendManager, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    /// <summary>
    ///     Target address for rig info syncing.
    /// </summary>
    private readonly List<string> _syncRigInfoAddr = new();

    private readonly ApplicationSettings _appSettings;
    private IRigService _currentService;
    private bool _initialized;
    private readonly Dictionary<RigBackendServiceEnum, IRigService> _services = new();

    public RigBackendManager(IEnumerable<IRigService> rigSources, IApplicationSettingsService appSettingsService)
    {
        _appSettings = appSettingsService.GetCurrentSettings();
        foreach (var rigService in rigSources) _services[rigService.GetServiceType()] = rigService;
        
        // bind settings change
        MessageBus.Current.Listen<SettingsChanged>().Subscribe(async void (x) =>
        {
            try
            {
                var currentRigService = _getCurrentRigService();
                if (currentRigService.GetServiceType() != _currentService?.GetServiceType())
                {
                    _currentService = currentRigService;
                    await _currentService?.StopService(_getNewCancellationProcessToken())!;
                }

                switch (x.Part)
                {
                    case ChangedPart.NothingJustClosed:
                        // close all services, if not available.
                        if (!_appSettings.HamlibSettings.PollAllowed)
                            await _services[RigBackendServiceEnum.Hamlib].StopService(_getNewCancellationProcessToken()).ConfigureAwait(false);
                        if (!_appSettings.FLRigSettings.PollAllowed)
                            await _services[RigBackendServiceEnum.FLRig].StopService(_getNewCancellationProcessToken()).ConfigureAwait(false);
                        if (!_appSettings.OmniRigSettings.PollAllowed)
                            if (_services.TryGetValue(RigBackendServiceEnum.OmniRig, out var value))
                            {
                                await value.StopService(_getNewCancellationProcessToken()).ConfigureAwait(false);
                            }
                        break;
                    
                    case ChangedPart.Hamlib:
                        if (_currentService.GetServiceType() != RigBackendServiceEnum.Hamlib) break;

                        // if (_appSettingsService.RestartHamlibNeeded())
                        // {
                            await _currentService.StopService(_getNewCancellationProcessToken()).ConfigureAwait(false);
                            if (_appSettings.HamlibSettings.UseExternalRigctld) break;

                            if (_appSettings.HamlibSettings.PollAllowed)
                            {
                                _syncRigInfoAddr.Clear();
                                _syncRigInfoAddr.AddRange(_appSettings.HamlibSettings.SyncRigInfoAddress.Split(";"));
                                await _startRigctld().ConfigureAwait(false);
                            }
                        // }

                        break;

                    case ChangedPart.FLRig:
                        if (_currentService.GetServiceType() != RigBackendServiceEnum.FLRig) break;

                        if (_appSettings.FLRigSettings.PollAllowed)
                        {
                            _syncRigInfoAddr.Clear();
                            _syncRigInfoAddr.AddRange(_appSettings.HamlibSettings.SyncRigInfoAddress.Split(";"));
                        }

                        break;
                    case ChangedPart.OmniRig:
                        if (_currentService.GetServiceType() != RigBackendServiceEnum.OmniRig) break;
                        // await _currentService.StopService(_getNewCancellationProcessToken());
                        if (_appSettings.OmniRigSettings.PollAllowed)
                        {
                            _syncRigInfoAddr.Clear();
                            _syncRigInfoAddr.AddRange(_appSettings.OmniRigSettings.SyncRigInfoAddress.Split(";"));
                            await _currentService.StartService(_getNewCancellationProcessToken(),_appSettings.OmniRigSettings.SelectedRig).ConfigureAwait(false);
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                ClassLogger.Error(e,"Error while switching rig services.");
            }
        });
    }

    public void Dispose()
    {
        _appSettings.Dispose();
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        // select default service on start...
        _currentService = _services[RigBackendServiceEnum.Hamlib];
        try
        {
            if (_appSettings.HamlibSettings.PollAllowed)
            {
                _syncRigInfoAddr.AddRange(_appSettings.HamlibSettings.SyncRigInfoAddress.Split(";"));
                await _startRigctld().ConfigureAwait(false);
                return;
            }

            if (_appSettings.FLRigSettings.PollAllowed)
            {
                _syncRigInfoAddr.AddRange(_appSettings.FLRigSettings.SyncRigInfoAddress.Split(";"));
                _currentService = _services[RigBackendServiceEnum.FLRig];
            }
            
            if (_appSettings.OmniRigSettings.PollAllowed)
            {
                _syncRigInfoAddr.AddRange(_appSettings.OmniRigSettings.SyncRigInfoAddress.Split(";"));
                _currentService = _services[RigBackendServiceEnum.OmniRig];
                await _currentService.StartService(_getNewCancellationProcessToken(),_appSettings.OmniRigSettings.SelectedRig).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, "Error while initing rig service");
        }
    }

    public RigBackendServiceEnum GetServiceType()
    {
        return _currentService.GetServiceType();
    }

    public IRigService GetServiceByName(RigBackendServiceEnum rigBackend)
    {
        return _services[rigBackend];
    }

    public bool IsServiceRunning()
    {
        return _currentService.IsServiceRunning();
    }

    public async Task RestartService()
    {
        await StopService().ConfigureAwait(false);
        await StartService().ConfigureAwait(false);
    }

    public async Task StopService()
    {
        await _currentService.StopService(_getNewCancellationProcessToken()).ConfigureAwait(false);
    }

    public async Task StartService()
    {
        if (GetServiceType() == RigBackendServiceEnum.Hamlib)
        {
            await _startRigctld().ConfigureAwait(false);
            return;
        }
        
        if (GetServiceType() == RigBackendServiceEnum.OmniRig)
        {
            await _currentService.StartService(_getNewCancellationProcessToken(), _appSettings.OmniRigSettings.SelectedRig).ConfigureAwait(false);
            return;
        }

        await _currentService.StartService(_getNewCancellationProcessToken()).ConfigureAwait(false);
    }

    public async Task<RigInfo[]> GetSupportedRigModels()
    {
        return await _currentService.GetSupportedRigModels().ConfigureAwait(false);
    }

    public async Task<RadioData> GetAllRigInfo()
    {
        var appSettingsHamlibSettings = _appSettings.HamlibSettings;
        var appSettingsFLRigSettings = _appSettings.FLRigSettings;
        var appSettingsOmniSettings = _appSettings.OmniRigSettings;

        RadioData data;
        switch (_currentService.GetServiceType())
        {
            case RigBackendServiceEnum.Hamlib:
                if (!appSettingsHamlibSettings.PollAllowed)
                    throw new InvalidPollException("Poll disabled.");

                if (appSettingsHamlibSettings.IsHamlibHasErrors())
                    throw new InvalidConfigurationException(TranslationHelper.GetString(LangKeys.confhamlibfirst));

                if (!_currentService.IsServiceRunning() && !appSettingsHamlibSettings.UseExternalRigctld)
                    await _startRigctld().ConfigureAwait(false);
                var (ip, port) = _getRigctldIpAndPort();
                data = await _currentService.GetAllRigInfo(appSettingsHamlibSettings.ReportRFPower,
                    appSettingsHamlibSettings.ReportSplitInfo,
                    CancellationToken.None,
                    ip,
                    port).ConfigureAwait(false);
                data.RigName = appSettingsHamlibSettings.SelectedRigInfo?.Model;
                break;
            case RigBackendServiceEnum.FLRig:
                if (!appSettingsFLRigSettings.PollAllowed)
                    throw new InvalidPollException("Poll disabled.");

                if (appSettingsFLRigSettings.IsFLRigHasErrors())
                    throw new InvalidConfigurationException(TranslationHelper.GetString(LangKeys.confflrigfirst));

                data = await _currentService.GetAllRigInfo(appSettingsFLRigSettings.ReportRFPower,
                    appSettingsFLRigSettings.ReportSplitInfo,
                    CancellationToken.None,
                    appSettingsFLRigSettings.FLRigHost,
                    appSettingsFLRigSettings.FLRigPort).ConfigureAwait(false);
                break;
            
            case RigBackendServiceEnum.OmniRig:
                if (!appSettingsOmniSettings.PollAllowed)
                    throw new InvalidPollException("Poll disabled.");

                if (appSettingsOmniSettings.IsOmniRigHasErrors())
                    throw new InvalidConfigurationException(TranslationHelper.GetString(LangKeys.confomnifirst));

                data = await _currentService.GetAllRigInfo(appSettingsFLRigSettings.ReportRFPower,
                    appSettingsFLRigSettings.ReportSplitInfo,
                    CancellationToken.None).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        // finally we sync rig info to user-specified addresses.
        foreach (var se in _syncRigInfoAddr)
        {
            if (string.IsNullOrWhiteSpace(se)) continue;
            _ = _uploadRigInfoToUserSpecifiedAddressAsync(se, data,
                CancellationToken.None);
        }

        return data;
    }

    public async Task<string> GetServiceVersion()
    {
        return await _currentService.GetServiceVersion().ConfigureAwait(false);
    }

    public int GetPollingInterval()
    {
        var interval = _currentService.GetServiceType() switch
        {
            RigBackendServiceEnum.FLRig => _appSettings.FLRigSettings.PollInterval,
            RigBackendServiceEnum.Hamlib => _appSettings.HamlibSettings.PollInterval,
            RigBackendServiceEnum.OmniRig =>  _appSettings.OmniRigSettings.PollInterval
        };

        if (int.TryParse(interval, out var intervalInt))
        {
            if (intervalInt > 1) return intervalInt;
            return 1;
        }

        return DefaultConfigs.RigDefaultPollingInterval;
    }

    public bool GetPollingAllowed()
    {
        return _currentService.GetServiceType() switch
        {
            RigBackendServiceEnum.FLRig => _appSettings.FLRigSettings.PollAllowed,
            RigBackendServiceEnum.Hamlib => _appSettings.HamlibSettings.PollAllowed,
            RigBackendServiceEnum.OmniRig => _appSettings.OmniRigSettings.PollAllowed
        };
    }

    public async Task ExecuteTest(RigBackendServiceEnum backendServiceEnum,
        ApplicationSettings draftSettings,
        CancellationToken token)
    {
        try
        {
            var service = _services[backendServiceEnum];
            if (backendServiceEnum == RigBackendServiceEnum.Hamlib)
            {
                var (ip, port) = _getRigctldIpAndPort(draftSettings.HamlibSettings);

                if (draftSettings.HamlibSettings is { UseExternalRigctld: false, SelectedRigInfo.Id: not null })
                {
                    // local rigctld
                    await service.StopService(_getNewCancellationProcessToken()).ConfigureAwait(false);
                    await _startRigctld(draftSettings.HamlibSettings).ConfigureAwait(false);
                }

                _ = await service.GetAllRigInfo(draftSettings.HamlibSettings.ReportRFPower,
                    draftSettings.HamlibSettings.ReportSplitInfo, CancellationToken.None, ip, port).ConfigureAwait(false);

                if (!_appSettings.HamlibSettings.PollAllowed)
                    // stop if polling is not enabled
                    await service.StopService(_getNewCancellationProcessToken()).ConfigureAwait(false);
            }

            if (backendServiceEnum == RigBackendServiceEnum.FLRig)
            {
                await service.GetAllRigInfo(false, false, CancellationToken.None,
                    draftSettings.FLRigSettings.FLRigHost, draftSettings.FLRigSettings.FLRigPort).ConfigureAwait(false);
            }
            
            if (backendServiceEnum == RigBackendServiceEnum.OmniRig)
            {
                // await service.StopService(_getNewCancellationProcessToken());
                await service.StartService(_getNewCancellationProcessToken(),draftSettings.OmniRigSettings.SelectedRig).ConfigureAwait(false);
                await service.GetAllRigInfo(false, false, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
            if (!token.IsCancellationRequested) throw;
        }
    }

    private CancellationToken _getNewCancellationProcessToken()
    {
        return new CancellationTokenSource(
            TimeSpan.FromSeconds(DefaultConfigs.DefaultProcessTPStartStopTimeout)
        ).Token;
    }

    private IRigService _getCurrentRigService()
    {
        if (_appSettings.HamlibSettings.PollAllowed) return _services[RigBackendServiceEnum.Hamlib];
        if (_appSettings.FLRigSettings.PollAllowed) return _services[RigBackendServiceEnum.FLRig];
        if (_appSettings.OmniRigSettings.PollAllowed) return _services[RigBackendServiceEnum.OmniRig];
        return _services[RigBackendServiceEnum.Hamlib];
    }

    private async Task _startRigctld(HamlibSettings? overrideSettings = null)
    {
        var hamlibSettings = overrideSettings ?? _appSettings.HamlibSettings;
        if (hamlibSettings.IsHamlibHasErrors())
        {
            await _currentService.StopService(_getNewCancellationProcessToken()).ConfigureAwait(false);
            throw new ArgumentException(TranslationHelper.GetString(LangKeys.confhamlibfirst));
        }

        var defaultArgs =
            RigUtils.GenerateRigctldCmdArgs(hamlibSettings.SelectedRigInfo!.Id!, hamlibSettings.SelectedPort);

        if (hamlibSettings.UseRigAdvanced)
        {
            if (string.IsNullOrEmpty(hamlibSettings.OverrideCommandlineArg))
                defaultArgs = RigUtils.GenerateRigctldCmdArgs(hamlibSettings.SelectedRigInfo.Id!,
                    hamlibSettings.SelectedPort,
                    hamlibSettings.DisablePTT,
                    hamlibSettings.AllowExternalControl);
            else
                defaultArgs = hamlibSettings.OverrideCommandlineArg;
        }

        await _currentService.StartService(_getNewCancellationProcessToken(), defaultArgs).ConfigureAwait(false);
    }

    private (string, int) _getRigctldIpAndPort(HamlibSettings? overrideSettings = null)
    {
        var appSettingsHamlibSettings = overrideSettings ?? _appSettings.HamlibSettings;
        // parse addr
        var ip = DefaultConfigs.RigctldDefaultHost;
        var port = DefaultConfigs.RigctldDefaultPort;

        if (appSettingsHamlibSettings.UseExternalRigctld)
            return IPAddrUtil.ParseAddress(appSettingsHamlibSettings.ExternalRigctldHostAddress);

        if (appSettingsHamlibSettings.UseRigAdvanced &&
            !string.IsNullOrEmpty(appSettingsHamlibSettings.OverrideCommandlineArg))
        {
            var matchPort = Regex.Match(appSettingsHamlibSettings.OverrideCommandlineArg, @"-t\s+(\S+)");
            if (matchPort.Success)
            {
                port = int.Parse(matchPort.Groups[1].Value);
                ClassLogger.Trace($"Match port from rigctld args: {port}");
            }
            else
            {
                throw new Exception(TranslationHelper.GetString(LangKeys.failextractinfo));
            }
        }

        return (ip, port);
    }

    private async Task _uploadRigInfoToUserSpecifiedAddressAsync(string url,
        RadioData data, CancellationToken token)
    {
        var payloadI = new RadioApiCallV2
        {
            Radio = data.RigName ?? "Unknown",
            Frequency = data.FrequencyTx,
            Mode = data.ModeTx,
            FrequencyRx = data.FrequencyRx,
            ModeRx = data.ModeRx,
            Power = data.Power
        };

        var results = await url
            .WithHeader("Content-Type", "application/json")
            .PostStringAsync(JsonConvert.SerializeObject(payloadI), cancellationToken: token)
            .ReceiveString().ConfigureAwait(false);

        if (results != "OK")
            throw new Exception($"Result does not return as expected: expect \"OK\" but we got {results}");
    }
}