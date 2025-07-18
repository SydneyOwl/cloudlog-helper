﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ADIFLib;
using Avalonia.Platform.Storage;
using CloudlogHelper.Database;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Utils;
using Newtonsoft.Json;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels;

public class QsoSyncAssistantViewModel : ViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private bool _executeOnStart;

    private CancellationTokenSource _source = new();

    public QsoSyncAssistantViewModel()
    {
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
        Settings.QsoSyncAssistantSettings.LocalLogPath ??= new ObservableCollection<string>();

        this.WhenActivated(disposable =>
        {
            if (_executeOnStart) StartSyncCommand.Execute().Subscribe().DisposeWith(disposable);
        });
    }

    public ReactiveCommand<Unit, Unit> SaveConf { get; }

    public ApplicationSettings Settings { get; set; } = ApplicationSettings.GetDraftInstance();


    public Interaction<Unit, IStorageFile[]> ShowFileSelectWindow { get; } = new();

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
        Settings.ApplySettings();
        Settings.WriteCurrentSettingsToFile();
    }

    private async Task AddLogPath()
    {
        Settings.QsoSyncAssistantSettings.LocalLogPath ??= new ObservableCollection<string>();
        var ww = await ShowFileSelectWindow.Handle(Unit.Default);
        foreach (var storageFile in ww)
        {
            if (Settings.QsoSyncAssistantSettings.LocalLogPath.Contains(storageFile.TryGetLocalPath()!)) continue;
            Settings.QsoSyncAssistantSettings.LocalLogPath.Add(storageFile.TryGetLocalPath()!);
        }
    }

    private void _logProgress(string info, float? progress = null)
    {
        // Dispatcher.UIThread.Invoke(() =>
        // {
        if (progress is not null) CurrentProgress = progress.Value;
        if (!string.IsNullOrEmpty(info)) CurrentInfo += $"\n[{DateTime.Now}] {info}";
        ClassLogger.Debug(info);
        // });
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
            SyncStarted = true;
            _logProgress("Starting login and downloading qsos from cloudlog/wavelog...", 10);
            var cookies = await QsoSyncAssistantUtil.LoginAndGetCookies(Settings.CloudlogSettings.CloudlogUrl,
                Settings.QsoSyncAssistantSettings.CloudlogUserName!,
                Settings.QsoSyncAssistantSettings.CloudlogPassword!,
                _source.Token);

            _logProgress(
                $"Login successfully. Downloading QSOs within {Settings.QsoSyncAssistantSettings.CloudlogQSODayRange} days...",
                15);
            var cloudAdif = await QsoSyncAssistantUtil.DownloadQSOFile(Settings.CloudlogSettings.CloudlogUrl,
                Settings.CloudlogSettings.CloudlogStationInfo!.Value.StationId!,
                Settings.QsoSyncAssistantSettings.CloudlogQSODayRange,
                cookies,
                _source.Token);
            _logProgress("Qsos downloaded. Analysing...", 30);

            var cloudParser = new ADIF();
            await Task.Run(() => { cloudParser.ReadFromString(cloudAdif, _source.Token); });

            _logProgress($"{cloudParser.QSOCount} Qsos from cloud parsed successfully.", 30);

            var cloudParsed = cloudParser.TheQSOs
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

                        if (await DatabaseUtil.IsQsoIgnored(
                                IgnoredQsoDatabase.Parse(RecordedCallsignDetail.Parse(compareRes[i]))))
                        {
                            _logProgress($"QSO: {compareRes[i].Call} {compareRes[i].Mode} is not recorded, but it's marked as ignored.");
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

                    MessageBus.Current.SendMessage(new QsoUploadRequested
                    {
                        QsoData = compareRes.Select(x => RecordedCallsignDetail.Parse(x)).ToList()
                    });

                    _logProgress(
                        $"Found {compareRes.Count} QSOs not uploaded. Adding them into upload queue...",
                        CurrentProgress + sEach);
                }
                catch (Exception e)
                {
                    errorOccurred = true;
                    _logProgress($"Parsing qso data from {localLog} failed: {e.Message}. Skipping...",
                        CurrentProgress + sEach);
                }

            if (errorOccurred)
                throw new Exception("One(or some) of the local files process failed. Please check them in logs.");
            _logProgress(TranslationHelper.GetString("qsosyncsucc"), 100);
            if (_executeOnStart)
                await App.NotificationManager.SendSuccessNotificationAsync(
                    $"{TranslationHelper.GetString("qsosyncsucc")}");
        }
        catch (Exception ex)
        {
            _logProgress($"Failed to sync QSOs: {ex.Message}", 100);
            if (_executeOnStart)
                await App.NotificationManager.SendErrorNotificationAsync(
                    $"{TranslationHelper.GetString("failedsyncqso")}{ex.Message}");
        }
        finally
        {
            SyncStarted = false;
        }
    }
}