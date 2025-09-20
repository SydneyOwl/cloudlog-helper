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

public class RigBackendManager:IRigBackendManager, IDisposable
{
    private ApplicationSettings _appSettings;
    private IApplicationSettingsService _appSettingsService;
    private Dictionary<RigBackendServiceEnum, IRigService> _services = new();
    private IRigService _currentService;
    private bool _initialized = false;

    /// <summary>
    ///     Target address for rig info syncing.
    /// </summary>
    private readonly List<string> _syncRigInfoAddr = new();
    
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    
    private CancellationToken _getNewCancellationProcessToken()
    {
        return new CancellationTokenSource(
            TimeSpan.FromDays(DefaultConfigs.DefaultProcessTPStartStopTimeout)
        ).Token;
    }
    public RigBackendManager(IEnumerable<IRigService> rigSources, IApplicationSettingsService appSettingsService)
    {
        _appSettings = appSettingsService.GetCurrentSettings();
        _appSettingsService = appSettingsService;
        foreach (var rigService in rigSources)
        { 
            _services[rigService.GetServiceType()] = rigService;
        }
        
        // bind settings change
        MessageBus.Current.Listen<SettingsChanged>().Subscribe(async void (x) =>
        {
            try
            {
                var currentRigService = _getCurrentRigService();
                if (currentRigService.GetServiceType() != _currentService?.GetServiceType())
                {
                    await _currentService?.StopService(_getNewCancellationProcessToken())!;
                    _currentService = currentRigService;
                }

                switch (x.Part)
                {
                    case ChangedPart.Hamlib:
                        if (_currentService.GetServiceType() != RigBackendServiceEnum.Hamlib)break;
                        
                        if (_appSettingsService.RestartHamlibNeeded())
                        {
                            await _currentService.StopService(_getNewCancellationProcessToken());
                            if (_appSettings.HamlibSettings.UseExternalRigctld)
                            {
                                break;
                            }

                            if (_appSettings.HamlibSettings.PollAllowed)
                            {
                                _syncRigInfoAddr.Clear();
                                _syncRigInfoAddr.AddRange(_appSettings.HamlibSettings.SyncRigInfoAddress.Split(";"));
                                await _startRigctld();
                            }
                        }
                        break;
                    
                    case ChangedPart.FLRig:
                        if (_currentService.GetServiceType() != RigBackendServiceEnum.FLRig)break;
                        
                        if (_appSettings.FLRigSettings.PollAllowed)
                        {
                            _syncRigInfoAddr.Clear();
                            _syncRigInfoAddr.AddRange(_appSettings.HamlibSettings.SyncRigInfoAddress.Split(";"));
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                ClassLogger.Error(e);
            }
        });
    }

    private IRigService _getCurrentRigService()
    {
        if (_appSettings.HamlibSettings.PollAllowed)return _services[RigBackendServiceEnum.Hamlib];
        if (_appSettings.FLRigSettings.PollAllowed)return _services[RigBackendServiceEnum.FLRig];
        return _services[RigBackendServiceEnum.Hamlib];    
    }

    public async Task InitializeAsync()
    {
        if (_initialized)return;
        _initialized = true;
        // select default service on start...
        _currentService = _services[RigBackendServiceEnum.Hamlib];
        try
        {
            if (_appSettings.HamlibSettings.PollAllowed)
            {           
                _syncRigInfoAddr.AddRange(_appSettings.HamlibSettings.SyncRigInfoAddress.Split(";"));
                await _startRigctld();
                return;
            }
            
            if (_appSettings.FLRigSettings.PollAllowed)
            {                
                _syncRigInfoAddr.AddRange(_appSettings.FLRigSettings.SyncRigInfoAddress.Split(";"));
                _currentService = _services[RigBackendServiceEnum.FLRig];
                return;
            }

        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, "Error while initing rigserv");
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
        await StopService();
        await StartService();
    }
    
    public async Task StopService()
    {
        await _currentService.StopService(CancellationToken.None);
    }
    
    public async Task StartService()
    {
        if (GetServiceType() == RigBackendServiceEnum.Hamlib)
        {
            await _startRigctld();
            return;
        }
        await _currentService.StartService(_getNewCancellationProcessToken());
    }

    public async Task<List<RigInfo>> GetSupportedRigModels()
    {
        return await _currentService.GetSupportedRigModels();
    }

    public async Task<RadioData> GetAllRigInfo()
    {
        var appSettingsHamlibSettings = _appSettings.HamlibSettings;
        var appSettingsFLRigSettings = _appSettings.FLRigSettings;

        RadioData data;
        switch (_currentService.GetServiceType())
        {
            case RigBackendServiceEnum.Hamlib:
                if (!appSettingsHamlibSettings.PollAllowed)
                    throw new InvalidPollException("Poll disabled.");
                
                if (appSettingsHamlibSettings.IsHamlibHasErrors())       
                    throw new InvalidConfigurationException(TranslationHelper.GetString(LangKeys.confhamlibfirst));

                if (!_currentService.IsServiceRunning() && !appSettingsHamlibSettings.UseExternalRigctld)
                    await _startRigctld();
                var (ip, port) = _getRigctldIpAndPort();
                data = await _currentService.GetAllRigInfo(appSettingsHamlibSettings.ReportRFPower,
                    appSettingsHamlibSettings.ReportSplitInfo,
                    CancellationToken.None,
                    ip,
                    port);
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
                    appSettingsFLRigSettings.FLRigPort);
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
        return await _currentService.GetServiceVersion();
    }

    public int GetPollingInterval()
    {
        var interval = _currentService.GetServiceType() switch
        {
            RigBackendServiceEnum.FLRig => _appSettings.FLRigSettings.PollInterval,
            RigBackendServiceEnum.Hamlib => _appSettings.HamlibSettings.PollInterval
        };
        
        if (int.TryParse(interval, out var intervalInt))
        {
            return intervalInt;
        }

        return DefaultConfigs.RigDefaultPollingInterval;
    }
    
    public bool GetPollingAllowed()
    {
       return _currentService.GetServiceType() switch
        {
            RigBackendServiceEnum.FLRig => _appSettings.FLRigSettings.PollAllowed,
            RigBackendServiceEnum.Hamlib => _appSettings.HamlibSettings.PollAllowed
        };
    }

    public async Task ExecuteTest(RigBackendServiceEnum backendServiceEnum,
        ApplicationSettings draftSettings)
    {
        var service = _services[backendServiceEnum];
        if (backendServiceEnum == RigBackendServiceEnum.Hamlib)
        {
            var (ip, port) = _getRigctldIpAndPort(draftSettings.HamlibSettings);
            
            if (draftSettings.HamlibSettings is { UseExternalRigctld: false, SelectedRigInfo.Id: not null })
            {
                // local rigctld
                await service.StopService(_getNewCancellationProcessToken());
                await _startRigctld(draftSettings.HamlibSettings);
            }

            _ = await service.GetAllRigInfo(draftSettings.HamlibSettings.ReportRFPower,
                draftSettings.HamlibSettings.ReportSplitInfo, CancellationToken.None, ip, port);

            if (!_appSettings.HamlibSettings.PollAllowed)
            {
                // stop if polling is not enabled
                await service.StopService(_getNewCancellationProcessToken());
            }
        }

        if (backendServiceEnum == RigBackendServiceEnum.FLRig)
        {
            ClassLogger.Debug("FLRig ver" + await service.GetServiceVersion(draftSettings.FLRigSettings.FLRigHost, draftSettings.FLRigSettings.FLRigPort));
            await service.GetAllRigInfo(false, false, CancellationToken.None,
                draftSettings.FLRigSettings.FLRigHost, draftSettings.FLRigSettings.FLRigPort);
        }
    }

    private async Task _startRigctld(HamlibSettings? overrideSettings = null)
    {
        var hamlibSettings = overrideSettings??_appSettings.HamlibSettings;
        if (hamlibSettings.IsHamlibHasErrors())
        {
            await _currentService.StopService(_getNewCancellationProcessToken());
            throw new ArgumentException(TranslationHelper.GetString(LangKeys.confhamlibfirst));
        }
        var defaultArgs = RigUtils.GenerateRigctldCmdArgs(hamlibSettings.SelectedRigInfo!.Id!, hamlibSettings.SelectedPort);

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

        await _currentService.StartService(_getNewCancellationProcessToken(), defaultArgs);
    }
    
    private (string, int) _getRigctldIpAndPort(HamlibSettings? overrideSettings = null)
    {
        var appSettingsHamlibSettings = overrideSettings ?? _appSettings.HamlibSettings;
        // parse addr
        var ip = DefaultConfigs.RigctldDefaultHost;
        var port = DefaultConfigs.RigctldDefaultPort;

        if (appSettingsHamlibSettings.UseExternalRigctld) return IPAddrUtil.ParseAddress(appSettingsHamlibSettings.ExternalRigctldHostAddress);

        if (appSettingsHamlibSettings.UseRigAdvanced &&
            !string.IsNullOrEmpty(appSettingsHamlibSettings.OverrideCommandlineArg))
        {
            var matchPort = Regex.Match(appSettingsHamlibSettings.OverrideCommandlineArg, @"-t\s+(\S+)");
            if (matchPort.Success)
            {
                port = int.Parse(matchPort.Groups[1].Value);
                ClassLogger.Debug($"Match port from args: {port}");
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
            Radio = data.RigName??"Unknown",
            Frequency = data.FrequencyTx,
            Mode = data.ModeTx,
            FrequencyRx = data.FrequencyRx,
            ModeRx = data.ModeRx,
            Power = data.Power
        };
        
        var results = await url
            .WithHeader("User-Agent", DefaultConfigs.DefaultHTTPUserAgent)
            .WithHeader("Content-Type", "application/json")
            .WithTimeout(TimeSpan.FromSeconds(DefaultConfigs.DefaultRequestTimeout))
            .PostStringAsync(JsonConvert.SerializeObject(payloadI), cancellationToken: token)
            .ReceiveString();

        if (results != "OK")
            throw new Exception($"Result does not return as expected: expect \"OK\" but we got {results}");
    }

    public void Dispose()
    {
        _appSettings.Dispose();
    }
}