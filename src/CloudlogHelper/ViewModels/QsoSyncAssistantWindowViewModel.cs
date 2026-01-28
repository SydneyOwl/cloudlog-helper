using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADIFLib;
using AutoMapper;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CloudlogHelper.Database;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using DynamicData.Binding;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels;

public class QsoSyncAssistantWindowViewModel : ViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly IDatabaseService _dbService;

    private readonly IInAppNotificationService _inAppNotification;
    
    private readonly IWindowManagerService _windowManagerService;
    
    private readonly IApplicationSettingsService settingsService;

    private bool _executeOnStart;

    private readonly IMapper _mapper;

    private CancellationTokenSource _source = new();

    public QsoSyncAssistantWindowViewModel()
    {
        if (!Design.IsDesignMode) throw new InvalidOperationException("This should be called from designer only.");
        StartSyncCommand = ReactiveCommand.Create(() => { });
        StopSyncCommand = ReactiveCommand.Create(() => { });
    }

    public QsoSyncAssistantWindowViewModel(IDatabaseService dbService,
        IInAppNotificationService winNotification,
        IApplicationSettingsService ss,
        IWindowManagerService windowManagerService,
        IMapper mapper)
    {
        _mapper = mapper;
        _dbService = dbService;
        _windowManagerService = windowManagerService;
        _inAppNotification = winNotification;
        settingsService = ss;
        Settings = settingsService.GetCurrentSettings().FastDeepClone();

        SaveConf = ReactiveCommand.Create(_saveAndApplyConf);

        StartSyncCommand =
            ReactiveCommand.CreateFromTask(_startSync, this.WhenAnyValue(x => x.SyncStarted).Select(x => !x));
        StopSyncCommand =
            ReactiveCommand.CreateFromTask(_stopSync, this.WhenAnyValue(x => x.SyncStarted).Select(x => x));
        RemoveLogPathCommand = ReactiveCommand.Create<string>(path =>
        {
            Settings.QsoSyncAssistantSettings.LocalLogPath?.Remove(path);
        });

        AddLogPathCommand = ReactiveCommand.CreateFromTask(AddLogPath);
        Settings.QsoSyncAssistantSettings.LocalLogPath ??= new ObservableCollectionExtended<string>();

        this.WhenActivated(disposable =>
        {
            Disposable.Create(async void () =>
            {
                try
                {
                    await StopSyncCommand.Execute();
                    await SaveConf.Execute();
                }
                catch (Exception ex)
                {
                    ClassLogger.Error(ex, "Cleaning up QSO Assistant error");
                }
            }).DisposeWith(disposable);
        });
    }

    public ReactiveCommand<Unit, Unit> SaveConf { get; }

    public ApplicationSettings Settings { get; set; }
    
    public ReactiveCommand<Unit, Unit> StartSyncCommand { get; }
    public ReactiveCommand<Unit, Unit> StopSyncCommand { get; }
    public ReactiveCommand<Unit, Unit> AddLogPathCommand { get; }
    public ReactiveCommand<string, Unit> RemoveLogPathCommand { get; }

    [Reactive] public float CurrentProgress { get; private set; }
    [Reactive] public string CurrentInfo { get; private set; } = "Waiting...";
    [Reactive] public bool SyncStarted { get; private set; }

    public void EnableExecuteOnStart()
    {
        _executeOnStart = true;
    }


    private void _saveAndApplyConf()
    {
        if (settingsService.TryGetDraftSettings(this, out var draft))
        {
            _mapper.Map(Settings, draft);
            settingsService.ApplySettings(this);
        }
    }

    private async Task AddLogPath()
    {
        Settings.QsoSyncAssistantSettings.LocalLogPath ??= new ObservableCollectionExtended<string>();
        var ww = await _windowManagerService.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true
        }, _windowManagerService.GetToplevel(GetType()));
        foreach (var storageFile in ww)
        {
            if (Settings.QsoSyncAssistantSettings.LocalLogPath.Contains(storageFile.TryGetLocalPath()!)) continue;
            Settings.QsoSyncAssistantSettings.LocalLogPath.Add(storageFile.TryGetLocalPath()!);
        }
    }

    private void _logProgress(string info, float? progress = null, Exception? ex = null)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            if (progress is not null) CurrentProgress = progress.Value;
            if (!string.IsNullOrEmpty(info)) CurrentInfo += $"\n[{DateTime.Now}] {info}";
            ClassLogger.Debug(ex, info);
        });
    }

    private async Task _stopSync()
    {
        if (_source.IsCancellationRequested)
        {
            _logProgress("Operation already requested cancelled. Please wait...");
            return;
        }

        var stopObservable = this.WhenAnyValue(x => x.SyncStarted)
            .Where(started => !started)
            .Do(_ => _logProgress("Successfully cancelled."))
            .Timeout(TimeSpan.FromSeconds(10))
            .Take(1);

        _source.Cancel();
        _logProgress("Cancelling operation...");

        await stopObservable;
    }

    private async Task _startSync()
    {
        _source = new CancellationTokenSource();
        if (Settings.QsoSyncAssistantSettings.IsQsoSyncAssistantSettingsHasErrors())
        {
            _logProgress("Please complete all settings!");
            return;
        }

        if (Settings.CloudlogSettings.IsCloudlogHasErrors(true))
        {
            _logProgress("Please complete all Cloudlog Settings in main application!");
            return;
        }

        try
        {
            var stationCallsign = Settings.CloudlogSettings.CloudlogStationInfo?.StationCallsign;
            await Dispatcher.UIThread.InvokeAsync(() =>
                SyncStarted = true);
            _logProgress("Starting login and downloading qsos from cloudlog/wavelog...", 10);
            var cookies = await QsoSyncAssistantUtil.LoginAndGetCookies(Settings.CloudlogSettings.CloudlogUrl,
                Settings.QsoSyncAssistantSettings.CloudlogUserName!,
                Settings.QsoSyncAssistantSettings.CloudlogPassword!,
                _source.Token);

            _logProgress(
                $"Login successfully. Downloading QSOs within {Settings.QsoSyncAssistantSettings.CloudlogQSODayRange} days...",
                15);
            var cloudAdif = await QsoSyncAssistantUtil.DownloadQSOFile(Settings.CloudlogSettings.CloudlogUrl,
                Settings.CloudlogSettings.CloudlogStationInfo!.StationId!,
                Settings.QsoSyncAssistantSettings.CloudlogQSODayRange,
                cookies,
                _source.Token);
            _logProgress("Qsos downloaded. Analysing...", 30);

            var cloudParser = new ADIF();
            await Task.Run(() => { cloudParser.ReadFromString(cloudAdif, _source.Token); });

            _logProgress($"{cloudParser.QSOCount} Qsos from cloud parsed successfully.", 30);

            var cloudParsed = cloudParser.TheQSOs
                .AsParallel()
                .WithCancellation(_source.Token)
                .Select(AdifLog.Parse)
                .ToList();

            if (Settings.QsoSyncAssistantSettings.LocalLogPath!.Count == 0)
                throw new Exception("Please select ur local logs.");

            var errorOccurred = false;

            var sEach = (float)50 / Settings.QsoSyncAssistantSettings.LocalLogPath.Count;
            foreach (var localLog in Settings.QsoSyncAssistantSettings.LocalLogPath)
                try
                {
                    if (_source.IsCancellationRequested)
                        throw new OperationCanceledException("Operation cancelled by user.");
                    _logProgress($"Try reading qso data from {localLog}, this may take sometime...", CurrentProgress);

                    // not elegant for large files...
                    var localParser = new ADIF();
                    await Task.Run(() =>
                    {
                        // read last n lines
                        var lq = QsoSyncAssistantUtil.ReadLastestQsos(localLog,
                            Settings.QsoSyncAssistantSettings.LocalQSOSampleCount);
                        localParser.ReadFromString(lq, _source.Token);
                    });

                    _logProgress(
                        $"Parsing qso data from {localLog} successfully. Read {localParser.QSOCount} Qsos. Checking qsos not uploaded...",
                        CurrentProgress);

                    var localParsed = localParser.TheQSOs
                        .AsParallel()
                        .WithCancellation(_source.Token)
                        .Select(AdifLog.Parse)
                        .ToList();

                    var compareRes = localParsed.Except(cloudParsed, AdifLog.AdifLogComparer).ToList();

                    for (var i = compareRes.Count - 1; i >= 0; i--)
                    {
                        if (!string.Equals(compareRes[i].StationCallsign, stationCallsign,
                                StringComparison.InvariantCultureIgnoreCase))
                        {
                            _logProgress(
                                $"Found QSOs not recorded: {compareRes[i].Call} {compareRes[i].Mode}, but the station callsign does not match({compareRes[i].StationCallsign} != {stationCallsign}) so ignored.");
                            compareRes.RemoveAt(i);
                            continue;
                        }

                        if (await _dbService.IsQsoIgnored(
                                IgnoredQsoDatabase.Parse(RecordedCallsignDetail.Parse(compareRes[i]))))
                        {
                            _logProgress(
                                $"QSO: {compareRes[i].Call} {compareRes[i].Mode} is not recorded, but it's marked as ignored.");
                            compareRes.RemoveAt(i);
                            continue;
                        }

                        _logProgress(
                            $"Found QSOs not recorded: {compareRes[i].Call} {compareRes[i].Mode} {compareRes[i].Band}");
                    }

                    if (compareRes.Count == 0)
                    {
                        _logProgress(
                            "All QSOs uploaded. Skipping...",
                            CurrentProgress + sEach);
                        continue;
                    }

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        MessageBus.Current.SendMessage(new QsoUploadRequested
                        {
                            QsoData = compareRes.Select(x => RecordedCallsignDetail.Parse(x)).ToList()
                        });
                    });

                    _logProgress(
                        $"Found {compareRes.Count} QSOs not uploaded. Adding them into upload queue...",
                        CurrentProgress + sEach);
                }
                catch (Exception e)
                {
                    errorOccurred = true;
                    _logProgress($"Parsing qso data from {localLog} failed: {e.Message}. Skipping...",
                        CurrentProgress + sEach, e);
                }

            if (errorOccurred)
                throw new Exception("One(or some) of the local files process failed. Please check them in logs.");
            _logProgress(TranslationHelper.GetString(LangKeys.qsosyncsucc), 100);
            if (_executeOnStart)
                await _inAppNotification.SendSuccessNotificationAsync(
                    $"{TranslationHelper.GetString(LangKeys.qsosyncsucc)}");
        }
        catch (Exception ex)
        {
            _logProgress($"Failed to sync QSOs: {ex.Message}", 100, ex);
            ClassLogger.Error(ex, "Sync qso error");
            if (_executeOnStart)
                await _inAppNotification.SendErrorNotificationAsync(
                    $"{TranslationHelper.GetString(LangKeys.failedsyncqso)}{ex.Message}");
        }
        finally
        {
            _executeOnStart = false;
            await Dispatcher.UIThread.InvokeAsync(() =>
                SyncStarted = false);
        }
    }
}