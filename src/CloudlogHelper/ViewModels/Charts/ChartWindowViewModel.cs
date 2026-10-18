using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using CloudlogHelper.Models;
using CloudlogHelper.Services.Interfaces;
using DynamicData;
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

    private readonly IClientRegistryService? _clientRegistryService;

    /// <summary>
    ///     Band/mode watchers per client, keyed by the full client id. Sessions are never
    ///     removed from the registry, so the watchers live until the app shuts down.
    /// </summary>
    private readonly Dictionary<string, IDisposable> _sessionWatchers = new(StringComparer.Ordinal);

    public ChartWindowViewModel()
    {
    }

    public ChartWindowViewModel(IClientRegistryService clientRegistryService)
    {
        _clientRegistryService = clientRegistryService;

        // Connect() replays the sessions that already exist when the (singleton) chart VM
        // is created and raises new ones as clients appear.
        clientRegistryService.Connect()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(changes =>
                {
                    foreach (var change in changes)
                        if (change.Reason == ChangeReason.Add)
                            _watchSession(change.Current);
                },
                exception => ClassLogger.Error(exception, "Error while watching client sessions."));

        // Selecting a client (manually or via the initial pick) snaps band/mode to that
        // client's current state, the same way auto-switch follows band/mode changes.
        this.WhenAnyValue(x => x.SelectedClient)
            .Subscribe(_onSelectedClientChanged,
                exception => ClassLogger.Error(exception, "Error while switching chart client."));

        this.WhenAnyValue(x => x.AutoSwitchEnabled)
            .Where(enabled => enabled)
            .Subscribe(_ => _onSelectedClientChanged(SelectedClient),
                exception => ClassLogger.Error(exception, "Error while toggling auto-switch."));
    }

    [Reactive] public ObservableCollection<string> Bands { get; set; } = new();
    [Reactive] public ObservableCollection<ClientSession> Clients { get; set; } = new();
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

    private void _watchSession(ClientSession session)
    {
        if (_sessionWatchers.ContainsKey(session.Id)) return;

        // WhenAnyValue emits the current values right away, so attaching the watcher also
        // seeds the dropdowns with whatever state the session already carries.
        _sessionWatchers[session.Id] = session.WhenAnyValue(s => s.Band, s => s.Mode)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(
                values => _onSessionBandModeChanged(session, values.Item1, values.Item2),
                exception => ClassLogger.Error(exception, $"Error while watching client {session.Id}."));
    }

    private void _onSessionBandModeChanged(ClientSession session, string band, string mode)
    {
        if (!Clients.Contains(session)) Clients.Add(session);
        if (!string.IsNullOrWhiteSpace(band) && !Bands.Contains(band)) Bands.Add(band);
        if (!string.IsNullOrWhiteSpace(mode) && !Modes.Contains(mode)) Modes.Add(mode);

        if (string.IsNullOrEmpty(SelectedClient))
        {
            // Fresh start with nothing selected: adopt the first session instead of
            // auto-switching the client later on.
            SelectedClient = session.Id;
            return;
        }

        // Auto-switch follows band/mode of the selected client only. The client
        // selection itself is never switched automatically.
        if (AutoSwitchEnabled && session.Id == SelectedClient)
        {
            if (!string.IsNullOrWhiteSpace(band)) SelectedBand = band;
            if (!string.IsNullOrWhiteSpace(mode)) SelectedMode = mode;
        }
    }

    private void _onSelectedClientChanged(string? clientId)
    {
        if (!AutoSwitchEnabled || _clientRegistryService is null || string.IsNullOrEmpty(clientId)) return;

        var session = _clientRegistryService.GetSession(clientId);
        if (session is null) return;

        if (!string.IsNullOrWhiteSpace(session.Band)) SelectedBand = session.Band;
        if (!string.IsNullOrWhiteSpace(session.Mode)) SelectedMode = session.Mode;
    }
}
