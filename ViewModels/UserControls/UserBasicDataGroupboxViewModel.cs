using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.UserControls;

public class UserBasicDataGroupboxViewModel : ViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly ReactiveCommand<Unit, Unit> _pollCommand;

    private readonly CloudlogSettings _settings = ApplicationSettings.GetInstance().CloudlogSettings.GetReference();

    public UserBasicDataGroupboxViewModel()
    {
        // poll it!
        _pollCommand = ReactiveCommand.CreateFromTask(_refreshUserBasicData);
        var interval = TimeSpan.FromSeconds(DefaultConfigs.CloudlogInfoPollRequestTimeout);

        this.WhenActivated(disposables =>
        {
            // refresh cloudlog infos immediately if settings changed.
            MessageBus.Current.Listen<SettingsChanged>()
                .Where(x => x.Part == ChangedPart.Cloudlog)
                .Subscribe(x =>
                {
                    ClassLogger.Debug("Setting changed; updating cloudlog info");
                    // _ = _refreshUserBasicData();
                    Observable.Return(Unit.Default) // 触发信号
                        .Delay(TimeSpan.FromMilliseconds(500))
                        .InvokeCommand(_pollCommand)
                        .DisposeWith(disposables);
                    // SendMsgToParentVm("");
                })
                .DisposeWith(disposables);

            _pollCommand.ThrownExceptions.Subscribe(async void (err) =>
                {
                    OP = TranslationHelper.GetString("unknown");
                    GridSquare = TranslationHelper.GetString("unknown");
                    QsToday = TranslationHelper.GetString("unknown");
                    QsMonth = TranslationHelper.GetString("unknown");
                    QsYear = TranslationHelper.GetString("unknown");
                    await App.NotificationManager.SendErrorNotificationAsync(err.Message);
                    // Console.WriteLine(err.Message + " Sent to parent vm");
                })
                .DisposeWith(disposables);

            Observable.Timer(TimeSpan.FromSeconds(1), interval)
                .Select(_ => Unit.Default)
                .InvokeCommand(this, x => x._pollCommand)
                .DisposeWith(disposables);
        });
    }

    [Reactive] public string? OP { get; set; } = TranslationHelper.GetString("unknown");
    [Reactive] public string? GridSquare { get; set; } = TranslationHelper.GetString("unknown");
    [Reactive] public string? QsToday { get; set; } = TranslationHelper.GetString("unknown");
    [Reactive] public string? QsMonth { get; set; } = TranslationHelper.GetString("unknown");

    [Reactive] public string? QsYear { get; set; } = TranslationHelper.GetString("unknown");
    // [Reactive] public string? QsAvgMin { get; set; } = TranslationHelper.GetString("calculating");
    // [Reactive] public string? QsAvgHour { get; set; } = TranslationHelper.GetString("calculating");

    private async Task _refreshUserBasicData()
    {
        // await Task.Delay(500); //dirty... Validation part in Settings(init) is not ready yet so wait for 500ms
        ClassLogger.Debug("Refreshing userbasic data....");
        if (_settings.IsCloudlogHasErrors(true))
            throw new Exception(TranslationHelper.GetString("confcloudlogfirst"));

        var info = await CloudlogUtil.GetStationInfoAsync(_settings.CloudlogUrl, _settings.CloudlogApiKey,
            _settings.CloudlogStationInfo?.StationId!);
        if (info is null)
        {
            // todo tell main viewmodel that exception occurred...
            throw new Exception(TranslationHelper.GetString("failedstationinfo"));
            return;
        }

        OP = info.Value.StationCallsign;
        GridSquare = info.Value.StationGridsquare;

        // polling statstics
        var statistic = await CloudlogUtil.GetStationStatisticsAsync(_settings.CloudlogUrl, _settings.CloudlogApiKey);
        if (statistic is null)
        {
            throw new Exception(TranslationHelper.GetString("failedstationstat"));
            return;
        }

        QsToday = statistic.Value.Today;
        QsMonth = statistic.Value.MonthQsos;
        QsYear = statistic.Value.YearQsos;
    }
}