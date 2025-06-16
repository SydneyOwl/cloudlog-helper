using System;
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

    private bool _execOnStart;

    private CancellationTokenSource _source = new();

    public QsoSyncAssistantViewModel()
    {
        SaveConf = ReactiveCommand.Create(_saveAndApplyConf);

        StartSyncCommand =
            ReactiveCommand.CreateFromTask(_startSync, this.WhenAnyValue(x => x.SyncStarted).Select(x => !x));
        StopSyncCommand = ReactiveCommand.CreateFromTask(_stopSync, this.WhenAnyValue(x => x.SyncStarted).Select(x => x));
        RemoveLogPathCommand = ReactiveCommand.Create<string>(path =>
        {
            Settings.QsoSyncAssistantSettings.LocalLogPath?.Remove(path);
        });

        AddLogPathCommand = ReactiveCommand.CreateFromTask(AddLogPath);
        Settings.QsoSyncAssistantSettings.LocalLogPath ??= new ObservableCollection<string>();

        this.WhenActivated(disposable =>
        {
            if (_execOnStart) StartSyncCommand.Execute().Subscribe().DisposeWith(disposable);
        });
    }

    public void ExecuteOnStart()
    {
        _execOnStart = true;
    }

    public ReactiveCommand<Unit, Unit> SaveConf { get; }

    public ApplicationSettings Settings { get; set; } = ApplicationSettings.GetInstance();


    public Interaction<Unit, IStorageFile[]> ShowFileSelectWindow { get; } = new();

    public ReactiveCommand<Unit, Unit> StartSyncCommand { get; }
    public ReactiveCommand<Unit, Unit> StopSyncCommand { get; }
    public ReactiveCommand<Unit, Unit> AddLogPathCommand { get; }
    public ReactiveCommand<string, Unit> RemoveLogPathCommand { get; }

    [Reactive] public float CurrentProgress { get; private set; }
    [Reactive] public string CurrentInfo { get; private set; } = "Waiting...";
    [Reactive] public bool SyncStarted { get; private set; }


    private void _saveAndApplyConf()
    {
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
        if (progress is not null) CurrentProgress = progress.Value;
        if (!string.IsNullOrEmpty(info)) CurrentInfo += $"\n[{DateTime.Now}] {info}";
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
            
            _logProgress("Login successfully. Checking datetime format...", 15);
            var datetimeFormat = await QsoSyncAssistantUtil.GetDateFormat(Settings.CloudlogSettings.CloudlogUrl, cookies, _source.Token);
            if (string.IsNullOrEmpty(datetimeFormat)) throw new Exception("Failed to detect datetime format!");

            _logProgress($"Detected datetime format: {datetimeFormat}.Downloading QSOs...", 25);
            var rawData = await QsoSyncAssistantUtil.DownloadQSOs(Settings.CloudlogSettings.CloudlogUrl,
                stationCallsign!,
                int.Parse(Settings.CloudlogSettings.CloudlogStationInfo?.StationId!),
                Settings.QsoSyncAssistantSettings.CloudlogQSOSampleCount,
                cookies,
                _source.Token);

            _logProgress("Qsos downloaded. Analysing...", 30);
            var cloudRaw = JsonConvert.DeserializeObject<List<AdvanceQSOInfo>>(rawData);
            if (cloudRaw is null) throw new Exception("Failed to parse downloaded data.");
            // transform it
            var cloudParsed = cloudRaw
                .Select(x =>
                {
                    x.ParseDatetime(datetimeFormat);
                    return x;
                })
                .Where(x => x is not null)
                .ToList();

            _logProgress($"{cloudParsed.Count} Qsos parsed successfully.", 30);
            if (Settings.QsoSyncAssistantSettings.LocalLogPath!.Count == 0)
                throw new Exception("Please select ur local logs.");

            var sEach = (float)50 / Settings.QsoSyncAssistantSettings.LocalLogPath.Count;
            foreach (var localLog in Settings.QsoSyncAssistantSettings.LocalLogPath)
                try
                {
                    if (_source.IsCancellationRequested)break;
                    _logProgress($"Try reading qso data from {localLog}, this may take sometime...", CurrentProgress);

                    // not elegant for large files...
                    var parser = new ADIF();
                    parser.ReadFromFile(localLog, Settings.QsoSyncAssistantSettings.LocalQSOSampleCount,_source.Token);

                    _logProgress(
                        $"Parsing qso data from {localLog} successfully. Read {parser.QSOCount} Qsos. Checking qsos not uploaded...",
                        CurrentProgress);

                    var localParsed = parser.TheQSOs
                        .Select(AdvanceQSOInfo.Parse)
                        .ToList();

                    var comparer = new QSOComparer(_source.Token);
                    
                    var compareRes = localParsed.Except(cloudParsed, comparer).ToList();
                    
                    for (var i = compareRes.Count - 1; i >= 0; i--)
                    {
                        if (!string.Equals(compareRes[i].De, stationCallsign, StringComparison.InvariantCultureIgnoreCase))
                        {
                            _logProgress($"Found QSOs not recorded: {compareRes[i].Dx} {compareRes[i].Mode}, but the station callsign does not match({compareRes[i].De} != {stationCallsign}) so ignored.");
                            compareRes.RemoveAt(i);
                            continue;
                        }
                        _logProgress($"Found QSOs not recorded: {compareRes[i].Dx} {compareRes[i].Mode} {compareRes[i].Band}");
                    }

                    if (compareRes.Count == 0)
                    {
                        _logProgress(
                            $"All QSOs uploaded. Skipping...",
                            CurrentProgress+sEach);
                        continue;
                    }
                    
                    _logProgress(
                        $"Found {compareRes.Count} QSOs not uploaded. Generating adif file...");

                    var adifText = new StringBuilder();
                    foreach (var advanceQSOInfo in compareRes)
                    {
                        adifText.AppendLine(advanceQSOInfo.RawData.ToString());
                    }
                    
                    _logProgress($"Uploading adif file...");

                    var adifUploadRes = await QsoSyncAssistantUtil.UploadAdifLogAsync(Settings.CloudlogSettings.CloudlogUrl,
                        adifText.ToString(),Settings.CloudlogSettings.CloudlogStationInfo?.StationId!,cookies,_source.Token);
                    if (adifUploadRes)
                    {
                        _logProgress($"Adif file uploaded successfully.", CurrentProgress+sEach);
                        continue;
                    }
                    _logProgress($"Adif file uploaded failed. ignored.", CurrentProgress+sEach);
                }
                catch (Exception e)
                {
                    _logProgress($"Parsing qso data from {localLog} failed: {e.Message}. Skipping...",
                        CurrentProgress + sEach);
                }
        }
        catch (Exception ex)
        {
            _logProgress($"Failed to sync QSOs: {ex.Message}");
        }
        finally
        {
            _logProgress("Done.", 100);
            SyncStarted = false;
        }
    }

    public class QSOComparer : IEqualityComparer<AdvanceQSOInfo?>
    {
        private CancellationToken _token;
        public QSOComparer(CancellationToken token = default) : base()
        {
            _token = token;
        }
        public bool Equals(AdvanceQSOInfo? x, AdvanceQSOInfo? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            // return true;

            return Math.Abs((x.QsoTimeOn - y.QsoTimeOn).TotalMinutes) < 5
                   && string.Equals(x.Dx, y.Dx, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.Mode, y.Mode, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.Band, y.Band, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.RstReceived, y.RstReceived, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(x.RstSent, y.RstSent, StringComparison.OrdinalIgnoreCase);

            // && string.Equals(x.StationCallsign, y.StationCallsign, StringComparison.OrdinalIgnoreCase);
        }
    
        public int GetHashCode(AdvanceQSOInfo obj)
        {
            // return 0;
            if (_token.IsCancellationRequested) throw new OperationCanceledException("User aborted.");
            if (obj is null) return 0;
            return HashCode.Combine(
                obj.QsoTimeOn.Date,
                obj.Dx?.ToUpperInvariant(),
                obj.Band?.ToUpperInvariant(),
                obj.RstSent?.ToUpperInvariant(),
                obj.RstReceived?.ToUpperInvariant()
            );
        }
    }
}