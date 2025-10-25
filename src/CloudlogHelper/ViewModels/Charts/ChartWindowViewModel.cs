using System;
using System.Collections.ObjectModel;
using CloudlogHelper.Messages;
using CloudlogHelper.Utils;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.Charts;

public class ChartWindowViewModel : ViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public bool IsExecutingChartUpdate;

    public ChartWindowViewModel()
    {
        MessageBus.Current.Listen<ClientStatusChanged>().Subscribe(x =>
        {
            var currStatusMode = x.CurrStatus.Mode;
            var currStatusDialFrequencyInHz = x.CurrStatus.DialFrequencyInHz;
            var currStatusId = x.CurrStatus.Id;
            var currBand = FreqHelper.GetMeterFromFreq(currStatusDialFrequencyInHz);
            if (string.IsNullOrWhiteSpace(currBand)) return;

            if (!Bands.Contains(currBand)) Bands.Add(currBand);
            if (!Clients.Contains(currStatusId)) Clients.Add(currStatusId);
            if (!Modes.Contains(currStatusMode)) Modes.Add(currStatusMode);

            if (AutoSwitchEnabled)
            {
                SelectedBand = currBand;
                SelectedClient = currStatusId;
                SelectedMode = currStatusMode;
            }
        }, exception => ClassLogger.Error(exception));
    }

    [Reactive] public ObservableCollection<string> Bands { get; set; } = new();
    [Reactive] public ObservableCollection<string> Clients { get; set; } = new();
    [Reactive] public ObservableCollection<string> Modes { get; set; } = new();
    [Reactive] public string? SelectedBand { get; set; } = string.Empty;
    [Reactive] public string? SelectedClient { get; set; } = string.Empty;
    [Reactive] public string? SelectedMode { get; set; } = string.Empty;
    [Reactive] public bool AutoSwitchEnabled { get; set; } = true;
    [Reactive] public bool FilterDupeCallsign { get; set; } = true;
    [Reactive] public bool UpdatePaused { get; set; } = true;
    [Reactive] public string LastDataUpdatedAt { get; set; } = "No data yet";

    [Reactive] public bool ShowErrorMsg { get; set; }

    [Reactive] public string ErrorMessage { get; set; }
}