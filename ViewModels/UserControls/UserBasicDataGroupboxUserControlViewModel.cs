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

public class UserBasicDataGroupboxUserControlViewModel : ViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private ReactiveCommand<Unit, Unit> _pollCommand;

    private readonly CloudlogSettings _settings = ApplicationSettings.GetInstance().CloudlogSettings.GetReference();

    public bool InitSkipped { get; private set; }
    public UserBasicDataGroupboxUserControlViewModel(){}
    
    public static UserBasicDataGroupboxUserControlViewModel Create(bool skipInit = false)
    {
        var vm = new UserBasicDataGroupboxUserControlViewModel();
        vm.InitSkipped = skipInit;
        if (!skipInit)
        {
            vm.Initialize(); 
        }
        return vm;
    }
    
    private void Initialize()
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
                    OP = TranslationHelper.GetString(LangKeys.unknown);
                    GridSquare = TranslationHelper.GetString(LangKeys.unknown);
                    QsToday = TranslationHelper.GetString(LangKeys.unknown);
                    QsMonth = TranslationHelper.GetString(LangKeys.unknown);
                    QsYear = TranslationHelper.GetString(LangKeys.unknown);
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

    [Reactive] public string? OP { get; set; } = TranslationHelper.GetString(LangKeys.unknown);
    [Reactive] public string? GridSquare { get; set; } = TranslationHelper.GetString(LangKeys.unknown);
    [Reactive] public string? QsToday { get; set; } = TranslationHelper.GetString(LangKeys.unknown);
    [Reactive] public string? QsMonth { get; set; } = TranslationHelper.GetString(LangKeys.unknown);

    [Reactive] public string? QsYear { get; set; } = TranslationHelper.GetString(LangKeys.unknown);
    // [Reactive] public string? QsAvgMin { get; set; } = LangKeys.calculating;
    // [Reactive] public string? QsAvgHour { get; set; } = LangKeys.calculating;

    private async Task _refreshUserBasicData()
    {
        // await Task.Delay(500); //dirty... Validation part in Settings(init) is not ready yet so wait for 500ms
        ClassLogger.Debug("Refreshing userbasic data....");
        if (_settings.IsCloudlogHasErrors(true))
            throw new Exception(TranslationHelper.GetString(LangKeys.confcloudlogfirst));

        var info = await CloudlogUtil.GetStationInfoAsync(_settings.CloudlogUrl, _settings.CloudlogApiKey,
            _settings.CloudlogStationInfo?.StationId!);
        if (info is null)
        {
            throw new Exception(TranslationHelper.GetString(LangKeys.failedstationinfo));
        }

        OP = info.Value.StationCallsign;
        GridSquare = info.Value.StationGridsquare;

        // polling statstics
        var statistic = await CloudlogUtil.GetStationStatisticsAsync(_settings.CloudlogUrl, _settings.CloudlogApiKey);
        if (statistic is null)
        {
            throw new Exception(TranslationHelper.GetString(LangKeys.failedstationstat));
            return;
        }

        QsToday = statistic.Value.Today;
        QsMonth = statistic.Value.MonthQsos;
        QsYear = statistic.Value.YearQsos;
    }
}