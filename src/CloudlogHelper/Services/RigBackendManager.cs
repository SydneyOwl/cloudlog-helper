using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
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
    
    private PollingSettingsCache _settingsCache;
    private DateTime _lastSettingsUpdate = DateTime.MinValue;
    
    private class PollingSettingsCache
    {
        public bool HamlibPollAllowed { get; set; }
        public bool FLRigPollAllowed { get; set; }
        public bool OmniRigPollAllowed { get; set; }
        public int HamlibPollInterval { get; set; }
        public int FLRigPollInterval { get; set; }
        public int OmniRigPollInterval { get; set; }
        public DateTime CacheTime { get; set; }
    }

    public RigBackendManager(IEnumerable<IRigService> rigSources, IApplicationSettingsService appSettingsService)
    {
        _appSettings = appSettingsService.GetCurrentSettings();
        foreach (var rigService in rigSources) 
            _services[rigService.GetServiceType()] = rigService;
        
        // Initialize cache
        UpdateSettingsCache();
        
        // bind settings change
        MessageBus.Current.Listen<SettingsChanged>()
            .Where(x => x.Part == ChangedPart.RigService )
            .Subscribe(async (x) =>
                {
                    try
                    {
                        await HandleSettingsChanged();
                    }
                    catch (Exception e)
                    {
                        ClassLogger.Error(e,"Error while switching rig services.");
                    } 
                });
    }

    private async Task HandleSettingsChanged()
    {
        // Update cache after settings change
        UpdateSettingsCache();
        
        await StopAllServices();
        
        // find current server and start
        _currentService = _getCurrentRigService();

        switch (_currentService.GetServiceType())
        {
            case RigBackendServiceEnum.Hamlib:
                await HandleHamlibSettings();
                break;
            case RigBackendServiceEnum.FLRig:
                await HandleFLRigSettings();
                break;
            case RigBackendServiceEnum.OmniRig:
                await HandleOmniRigSettings();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

    }

    private async Task StopAllServices()
    {
        ClassLogger.Debug("Stopping all rig services now");
        var tasks = new List<Task>();
        
        // if (!_appSettings.HamlibSettings.PollAllowed)
            tasks.Add(_services[RigBackendServiceEnum.Hamlib].StopService(_getNewCancellationProcessToken()));
        
        // if (!_appSettings.FLRigSettings.PollAllowed)
            tasks.Add(_services[RigBackendServiceEnum.FLRig].StopService(_getNewCancellationProcessToken()));
        
        // if (!_appSettings.OmniRigSettings.PollAllowed && 
            if (_services.TryGetValue(RigBackendServiceEnum.OmniRig, out var omniRigService))
        {
            tasks.Add(omniRigService.StopService(_getNewCancellationProcessToken()));
        }
        
        await Task.WhenAll(tasks);
    }

    private async Task HandleHamlibSettings()
    {
        if (_appSettings.HamlibSettings.UseExternalRigctld) 
            return;

        if (_appSettings.HamlibSettings.PollAllowed)
        {
            _syncRigInfoAddr.Clear();
            _syncRigInfoAddr.AddRange(_appSettings.HamlibSettings.SyncRigInfoAddress.Split(";"));
            await _startRigctld();
        }
    }

    private async Task HandleFLRigSettings()
    {
        ClassLogger.Info("FLRig service started.");
        if (_appSettings.FLRigSettings.PollAllowed)
        {
            _syncRigInfoAddr.Clear();
            _syncRigInfoAddr.AddRange(_appSettings.HamlibSettings.SyncRigInfoAddress.Split(";"));
        }
    }

    private async Task HandleOmniRigSettings()
    {
        ClassLogger.Info("omnirig service started.");
        if (_appSettings.OmniRigSettings.PollAllowed)
        {
            _syncRigInfoAddr.Clear();
            _syncRigInfoAddr.AddRange(_appSettings.OmniRigSettings.SyncRigInfoAddress.Split(";"));
            await _currentService.StartService(_getNewCancellationProcessToken(), _appSettings.OmniRigSettings.SelectedRig);
        }
    }

    private async Task StopCurrentService()
    {
        if (_currentService != null)
        {
            await _currentService.StopService(_getNewCancellationProcessToken());
        }
    }

    public void Dispose()
    {
        _appSettings.Dispose();
        
        _syncRigInfoAddr.Clear();
        _services.Clear();
        
        GC.SuppressFinalize(this);
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
                await _startRigctld();
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
                await _currentService.StartService(_getNewCancellationProcessToken(), _appSettings.OmniRigSettings.SelectedRig);
            }
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, "Error while initing rig service");
        }
        
        // Initialize cache
        UpdateSettingsCache();
    }

    public RigBackendServiceEnum GetServiceType()
    {
        return _currentService.GetServiceType();
    }

    public string GetServiceEndpointAddress()
    {
        try
        {
            return _currentService.GetServiceType() switch
            {
                RigBackendServiceEnum.Hamlib => GetHamlibEndpoint(),
                RigBackendServiceEnum.FLRig => $"({_appSettings.FLRigSettings.FLRigHost}:{_appSettings.FLRigSettings.FLRigPort})",
                RigBackendServiceEnum.OmniRig => $"({_appSettings.OmniRigSettings.SelectedRig})",
                _ => throw new ArgumentOutOfRangeException()
            };
        }
        catch (Exception ex)
        {
            ClassLogger.Warn(ex, "Unable to read service endpoint.");
            return "(?)";
        }
    }

    private string GetHamlibEndpoint()
    {
        try
        {
            var (rigctldIp, rigctldPort) = _getRigctldIpAndPort();
            return $"{rigctldIp}:{rigctldPort}";
        }
        catch (Exception ex)
        {
            ClassLogger.Warn(ex, "Unable to read hamlib endpoint.");
            return "(?)";
        }
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
        await _currentService.StopService(_getNewCancellationProcessToken());
    }

    public async Task StartService()
    {
        if (GetServiceType() == RigBackendServiceEnum.Hamlib)
        {
            await _startRigctld();
            return;
        }
        
        if (GetServiceType() == RigBackendServiceEnum.OmniRig)
        {
            await _currentService.StartService(_getNewCancellationProcessToken(), _appSettings.OmniRigSettings.SelectedRig);
            return;
        }

        await _currentService.StartService(_getNewCancellationProcessToken());
    }

    public async Task<RigInfo[]> GetSupportedRigModels()
    {
        return await _currentService.GetSupportedRigModels();
    }

    public async Task<RadioData> GetAllRigInfo()
    {
        return await GetAllRigInfo(CancellationToken.None);
    }

    public async Task<RadioData> GetAllRigInfo(CancellationToken cancellationToken)
    {
        // Create a settings snapshot to avoid repeated property access
        var settingsSnapshot = new
        {
            Hamlib = _appSettings.HamlibSettings,
            FLRig = _appSettings.FLRigSettings,
            OmniRig = _appSettings.OmniRigSettings,
            ServiceType = _currentService.GetServiceType()
        };

        RadioData data;
        
        try
        {
            data = await GetRigDataForService(settingsSnapshot, cancellationToken);
            
            // Fire-and-forget for sync operations with error handling
            if (!cancellationToken.IsCancellationRequested && _syncRigInfoAddr.Any())
            {
                _ = Task.Run(async () =>
                {
                    foreach (var address in _syncRigInfoAddr.Where(addr => !string.IsNullOrWhiteSpace(addr)))
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;
                            
                        try
                        {
                            await _uploadRigInfoToUserSpecifiedAddressAsync(address, data, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            ClassLogger.Warn(ex, $"Failed to sync rig info to {address}");
                        }
                    }
                }, cancellationToken);
            }
            
            return data;
        }
        catch (Exception ex)
        {
            ClassLogger.Error(ex, "Failed to get rig info");
            throw;
        }
    }

    private async Task<RadioData> GetRigDataForService(dynamic settings, CancellationToken cancellationToken)
    {
        return settings.ServiceType switch
        {
            RigBackendServiceEnum.Hamlib => await GetHamlibData(settings.Hamlib, cancellationToken),
            RigBackendServiceEnum.FLRig => await GetFLRigData(settings.FLRig, cancellationToken),
            RigBackendServiceEnum.OmniRig => await GetOmniRigData(settings.OmniRig, cancellationToken),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private async Task<RadioData> GetHamlibData(HamlibSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.PollAllowed)
            throw new InvalidPollException("Poll disabled.");

        if (settings.IsHamlibHasErrors())
            throw new InvalidConfigurationException(TranslationHelper.GetString(LangKeys.confhamlibfirst));

        if (!_currentService.IsServiceRunning() && !settings.UseExternalRigctld)
            await _startRigctld();
            
        var (ip, port) = _getRigctldIpAndPort();
        var data = await _currentService.GetAllRigInfo(
            settings.ReportRFPower,
            settings.ReportSplitInfo,
            cancellationToken,
            ip,
            port);
            
        data.RigName = settings.SelectedRigInfo?.Model;
        return data;
    }

    private async Task<RadioData> GetFLRigData(FLRigSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.PollAllowed)
            throw new InvalidPollException("Poll disabled.");

        if (settings.IsFLRigHasErrors())
            throw new InvalidConfigurationException(TranslationHelper.GetString(LangKeys.confflrigfirst));

        var data = await _currentService.GetAllRigInfo(
            settings.ReportRFPower,
            settings.ReportSplitInfo,
            cancellationToken,
            settings.FLRigHost,
            settings.FLRigPort);
            
        return data;
    }

    private async Task<RadioData> GetOmniRigData(OmniRigSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.PollAllowed)
            throw new InvalidPollException("Poll disabled.");

        if (settings.IsOmniRigHasErrors())
            throw new InvalidConfigurationException(TranslationHelper.GetString(LangKeys.confomnifirst));

        var data = await _currentService.GetAllRigInfo(
            _appSettings.FLRigSettings.ReportRFPower,
            _appSettings.FLRigSettings.ReportSplitInfo,
            cancellationToken);
            
        return data;
    }

    public async Task<string> GetServiceVersion()
    {
        return await _currentService.GetServiceVersion();
    }

    public int GetPollingInterval()
    {
        UpdateSettingsCache(); // Use cached values
        
        return _currentService.GetServiceType() switch
        {
            RigBackendServiceEnum.FLRig => Math.Max(1, _settingsCache.FLRigPollInterval),
            RigBackendServiceEnum.Hamlib => Math.Max(1, _settingsCache.HamlibPollInterval),
            RigBackendServiceEnum.OmniRig => Math.Max(1, _settingsCache.OmniRigPollInterval),
            _ => DefaultConfigs.RigDefaultPollingInterval
        };
    }

    public bool GetPollingAllowed()
    {
        UpdateSettingsCache(); // Use cached values
        
        return _currentService.GetServiceType() switch
        {
            RigBackendServiceEnum.FLRig => _settingsCache.FLRigPollAllowed,
            RigBackendServiceEnum.Hamlib => _settingsCache.HamlibPollAllowed,
            RigBackendServiceEnum.OmniRig => _settingsCache.OmniRigPollAllowed
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
                    await service.StopService(_getNewCancellationProcessToken());
                    await _startRigctld(draftSettings.HamlibSettings);
                }

                _ = await service.GetAllRigInfo(draftSettings.HamlibSettings.ReportRFPower,
                    draftSettings.HamlibSettings.ReportSplitInfo, CancellationToken.None, ip, port);

                if (!_appSettings.HamlibSettings.PollAllowed)
                    // stop if polling is not enabled
                    await service.StopService(_getNewCancellationProcessToken());
                return;
            }

            if (backendServiceEnum == RigBackendServiceEnum.FLRig)
            {
                await service.GetAllRigInfo(false, false, CancellationToken.None,
                    draftSettings.FLRigSettings.FLRigHost, draftSettings.FLRigSettings.FLRigPort);
                return;
            }
            
            if (backendServiceEnum == RigBackendServiceEnum.OmniRig)
            {
                await service.StartService(_getNewCancellationProcessToken(), draftSettings.OmniRigSettings.SelectedRig);
                await service.GetAllRigInfo(false, false, CancellationToken.None);
                return;
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
            await _currentService.StopService(_getNewCancellationProcessToken());
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

        await _currentService.StartService(_getNewCancellationProcessToken(), defaultArgs);
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
                ClassLogger.Debug($"Unable to Match port from rigctld args.");
                throw new Exception(TranslationHelper.GetString(LangKeys.failextractinfo));
            }
        }

        return (ip, port);
    }

    private async Task _uploadRigInfoToUserSpecifiedAddressAsync(string url,
        RadioData data, CancellationToken token)
    {
        var payload = new RadioApiCallV2
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
            .PostStringAsync(JsonConvert.SerializeObject(payload), cancellationToken: token)
            .ReceiveString();
        
        if (results != "OK")
            throw new Exception($"Expected 'OK' but got: {results}");
    }

    private void UpdateSettingsCache()
    {
        if (_settingsCache != null && (DateTime.Now - _lastSettingsUpdate).TotalSeconds < 5)
            return;
            
        _settingsCache = new PollingSettingsCache
        {
            HamlibPollAllowed = _appSettings.HamlibSettings.PollAllowed,
            FLRigPollAllowed = _appSettings.FLRigSettings.PollAllowed,
            OmniRigPollAllowed = _appSettings.OmniRigSettings.PollAllowed,
            HamlibPollInterval = ParsePollInterval(_appSettings.HamlibSettings.PollInterval),
            FLRigPollInterval = ParsePollInterval(_appSettings.FLRigSettings.PollInterval),
            OmniRigPollInterval = ParsePollInterval(_appSettings.OmniRigSettings.PollInterval),
            CacheTime = DateTime.Now
        };
        
        _lastSettingsUpdate = DateTime.Now;
    }

    private int ParsePollInterval(string interval)
    {
        if (int.TryParse(interval, out var intervalInt) && intervalInt > 0)
            return intervalInt;
        return DefaultConfigs.RigDefaultPollingInterval;
    }
}