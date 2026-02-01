using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CloudlogHelper.Enums;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using Material.Icons;
using Material.Icons.Avalonia;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.UserControls;

public class UserBasicDataGroupboxUserControlViewModel : FloatableViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly IInAppNotificationService _inAppNotification;

    private readonly CloudlogSettings _settings;

    private ReactiveCommand<Unit, Unit> _pollCommand;

    public UserBasicDataGroupboxUserControlViewModel()
    {
        if (!Design.IsDesignMode) throw new InvalidOperationException("This should be called from designer only.");
        _settings = new CloudlogSettings();
    }

    public UserBasicDataGroupboxUserControlViewModel(CommandLineOptions cmd,
        IInAppNotificationService inAppNotification,
        IApplicationSettingsService applicationSettingsService)
    {
        _inAppNotification = inAppNotification;
        _settings = applicationSettingsService.GetCurrentSettings().CloudlogSettings;
        InitSkipped = cmd.AutoUdpLogUploadOnly;
        if (!InitSkipped) Initialize();
    }

    public bool InitSkipped { get; }

    [Reactive] public string? OP { get; set; } = TranslationHelper.GetString(LangKeys.unknown);
    [Reactive] public string? GridSquare { get; set; } = TranslationHelper.GetString(LangKeys.unknown);
    [Reactive] public string? QsToday { get; set; } = TranslationHelper.GetString(LangKeys.unknown);
    [Reactive] public string? QsMonth { get; set; } = TranslationHelper.GetString(LangKeys.unknown);
    [Reactive] public string? QsYear { get; set; } = TranslationHelper.GetString(LangKeys.unknown);
    [Reactive] public MaterialIconKind? BackendType { get; set; } = MaterialIconKind.QuestionMark;
    
    private void Initialize()
    {
        // poll it!
        _pollCommand = ReactiveCommand.CreateFromTask(_refreshUserBasicData);
        var interval = TimeSpan.FromSeconds(DefaultConfigs.CloudlogInfoPollInterval);

        this.WhenActivated(disposables =>
        {
            // refresh cloudlog infos immediately if settings changed.
            MessageBus.Current.Listen<SettingsChanged>()
                .Where(x => x.Part == ChangedPart.Cloudlog)
                .Subscribe(x =>
                {
                    Observable.Return(Unit.Default)
                        .Delay(TimeSpan.FromMilliseconds(500))
                        .InvokeCommand(_pollCommand)
                        .DisposeWith(disposables);
                    
                    _ = _refreshBackendType();
                })
                .DisposeWith(disposables);

            _pollCommand.ThrownExceptions.Subscribe(async void (err) =>
                {
                    _setStatusToUnknown();
                    await _inAppNotification.SendErrorNotificationAsync(err.Message);
                })
                .DisposeWith(disposables);

            Observable.Timer(TimeSpan.FromSeconds(1), interval)
                .Select(_ => Unit.Default)
                .InvokeCommand(this, x => x._pollCommand)
                .DisposeWith(disposables);
            
            _ = _refreshBackendType();
        });
    }

    private async Task _refreshBackendType()
    {
        if (!_settings.AutoPollStationStatus)
        {
            BackendType = MaterialIconKind.QuestionMark;
            return;
        }
        
        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultConfigs.CloudlogInfoPollTimeout));
        var info = await CloudlogUtil.GetCurrentServerInstanceTypeAsync(_settings.CloudlogUrl, source.Token);
        
        ClassLogger.Trace($"Detected {info}");

        BackendType = info switch
        {
            ServerInstanceType.Cloudlog => MaterialIconKind.Cloud,
            ServerInstanceType.Wavelog => MaterialIconKind.Wifi,
            _ => MaterialIconKind.QuestionMark
        };
    }

    private async Task _refreshUserBasicData()
    {
        using var source = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultConfigs.CloudlogInfoPollTimeout));
        
        // await Task.Delay(500); //dirty... Validation part in Settings(init) is not ready yet so wait for 500ms
        if (!_settings.AutoPollStationStatus)
        {
            _setStatusToUnknown();
            return;
        }
        
        if (_settings.IsCloudlogHasErrors(true))
            throw new Exception(TranslationHelper.GetString(LangKeys.confcloudlogfirst));

        var info = await CloudlogUtil.GetStationInfoAsync(_settings.CloudlogUrl, _settings.CloudlogApiKey,
            _settings.CloudlogStationInfo?.StationId!, source.Token);
        if (info is null) throw new Exception(TranslationHelper.GetString(LangKeys.failedstationinfo));

        OP = info.StationCallsign;
        GridSquare = info.StationGridsquare;

        // polling statstics
        var statistic = await CloudlogUtil.GetStationStatisticsAsync(_settings.CloudlogUrl,
            _settings.CloudlogApiKey, source.Token);
        if (statistic is null)
        {
            throw new Exception(TranslationHelper.GetString(LangKeys.failedstationstat));
            return;
        }

        QsToday = statistic.Value.Today;
        QsMonth = statistic.Value.MonthQsos;
        QsYear = statistic.Value.YearQsos;
    }

    private void _setStatusToUnknown()
    {
        OP = TranslationHelper.GetString(LangKeys.unknown);
        GridSquare = TranslationHelper.GetString(LangKeys.unknown);
        QsToday = TranslationHelper.GetString(LangKeys.unknown);
        QsMonth = TranslationHelper.GetString(LangKeys.unknown);
        QsYear = TranslationHelper.GetString(LangKeys.unknown);
    }
}