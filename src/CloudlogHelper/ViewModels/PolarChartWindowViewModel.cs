using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CloudlogHelper.Messages;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.PlotStyles;
using ScottPlot.Plottables;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.ViewModels;

public class PolarChartWindowViewModel : ViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    [Reactive] public string LastDataUpdatedAt{ get; set; } = "No data yet";


    private bool _isExecutingChartUpdate;

    [Reactive] public ObservableCollection<string> Bands { get; set; } = new();
    [Reactive] public ObservableCollection<string> Clients { get; set; } = new();
    [Reactive] public ObservableCollection<string> Modes { get; set; } = new();

    [Reactive] public string SelectedBand { get; set; } = string.Empty;
    [Reactive] public string SelectedClient { get; set; } = string.Empty;
    [Reactive] public string SelectedMode { get; set; } = string.Empty;

    [Reactive] public bool AutoSwitchEnabled { get; set; } = true;

    [Reactive] public int KValue { get; set; } = DefaultConfigs.DefaulPolarKValue;
    [Reactive] public double AngWeightValue { get; set; } = DefaultConfigs.DefaulPolarAngWeightValue;
    [Reactive] public double DistWeightValue { get; set; } = DefaultConfigs.DefaulPolarDistWeightValue;
    [Reactive] public int QSOSamples { get; set; } = DefaultConfigs.DefaultPolarQSOSamples;
    [Reactive] public bool ShowDestColor { get; set; } = true;
    [Reactive] public bool FilterDupeCallsign { get; set; } = true;
    [Reactive] public bool UpdatePaused { get; set; }
    
    [Reactive] public bool ShowErrorMsg { get; set; }

    public Interaction<Unit, IStorageFile?> OpenSaveFilePickerInteraction { get; set; } = new();
    public ReactiveCommand<Unit, Unit> SaveChart { get; }
    public ReactiveCommand<Unit, Unit> Test { get; }
    public ReactiveCommand<Unit, Unit> RefreshChart { get; }
    public AvaPlot PlotControl { get; private set; }

    private PolarAxis _polarAxis;

    private IChartDataCacheService<ChartQSOPoint> _chartDataCacheService;
    
    private BasicSettings _basicSettings;

    public PolarChartWindowViewModel()
    {
    }

    public PolarChartWindowViewModel(IChartDataCacheService<ChartQSOPoint> chartDataCacheService,
        IApplicationSettingsService applicationSettingsService)
    {
        _basicSettings = applicationSettingsService.GetCurrentSettings().BasicSettings;
        _chartDataCacheService = chartDataCacheService;

        Application.Current!.ActualThemeVariantChanged += (sender, args) => { UpdatePolar(); };

        PlotControl = new AvaPlot();
        SaveChart = ReactiveCommand.CreateFromTask(async () =>
        {
            var a = await OpenSaveFilePickerInteraction?.Handle(Unit.Default)!;
            if (a is null) return;
            PlotControl.Plot.GetImage(DefaultConfigs.ExportedPolarChartSize,
                DefaultConfigs.ExportedPolarChartSize).SavePng(a.Path.AbsolutePath);
        });

        RefreshChart = ReactiveCommand.Create(UpdatePolar);
        
        this.WhenActivated(disposable =>
        {
            MessageBus.Current.Listen<ClientStatusChanged>().Subscribe(x =>
            {
                var currStatusMode = x.CurrStatus.Mode;
                var currStatusDialFrequencyInHz = x.CurrStatus.DialFrequencyInHz;
                var currStatusId = x.CurrStatus.Id;
                var currBand = FreqHelper.GetMeterFromFreq(currStatusDialFrequencyInHz);
                
                if (!Bands.Contains(currBand)) Bands.Add(currBand);
                if (!Clients.Contains(currStatusId))Clients.Add(currStatusId);
                if (!Modes.Contains(currStatusMode))Modes.Add(currStatusMode);

                if (AutoSwitchEnabled)
                {
                    SelectedBand = currBand;
                    SelectedClient = currStatusId;
                    SelectedMode = currStatusMode;
                }
            }, exception => ClassLogger.Error(exception))
            .DisposeWith(disposable);
            
            SaveChart.ThrownExceptions.Subscribe().DisposeWith(disposable);

            _chartDataCacheService.GetItemAddedObservable()
                .Throttle(TimeSpan.FromSeconds(DefaultConfigs.UpdateChartsThrottleTime))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe((_) =>
                {
                    LastDataUpdatedAt = "Last data recv at:" + DateTime.Now.ToLongTimeString();
                    UpdatePolar();
                })
                .DisposeWith(disposable);

            this.WhenAnyValue(x => x.KValue,
                    x => x.AngWeightValue,
                    x => x.DistWeightValue,
                    x => x.ShowDestColor,
                    x => x.QSOSamples,
                    x => x.UpdatePaused)
                .Throttle(TimeSpan.FromMilliseconds(500))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe((_) => UpdatePolar())
                .DisposeWith(disposable);
            
            this.WhenAnyValue(x => x.SelectedBand,
                    x => x.SelectedClient,
                    x=>x.SelectedMode)
                .Throttle(TimeSpan.FromMilliseconds(352))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe((_) => UpdatePolar())
                .DisposeWith(disposable);
        });

        UpdatePolar();
    }

    private void UpdatePolar()
    {
        if (_isExecutingChartUpdate || UpdatePaused) return;
        if (!MaidenheadGridUtil.CheckMaidenhead(_basicSettings.MyMaidenheadGrid))
        {
            ShowErrorMsg = true;
            return;
        }
        try
        {
            ShowErrorMsg = false;
            ClassLogger.Trace("Updating polar.");
            _isExecutingChartUpdate = true;
            PlotControl.Plot.Clear();

            var cacheData = _chartDataCacheService.TakeLatestN(QSOSamples, FilterDupeCallsign, ChartQSOPoint.ChartQsoPointComparer)
                                                .Where(x => x.Band == SelectedBand
                                                            && x.Mode == SelectedMode
                                                            && x.Client == SelectedClient)
                                                .ToArray();
            var maxDistance = QSOPointUtil.CalculateRobustMaxDistance(cacheData) + 500;
            ClassLogger.Trace($"Use maxDistance:{maxDistance}");
            var densities = new double[1];
            if (ShowDestColor)
            {
                densities = QSOPointUtil.CalculateDensitiesKNN(cacheData, maxDistance, k: KValue,
                    distanceWeight: DistWeightValue, angleWeight: AngWeightValue);
            }

            _polarAxis = PlotControl.Plot.Add.PolarAxis(radius: maxDistance <= 1000 ? 1000 : maxDistance);
            _polarAxis.Rotation = Angle.FromDegrees(-90);
            _polarAxis.Clockwise = true;

            if (cacheData.Length == 0) return;
            var distLina = (double)((int)((maxDistance / 5) / 100) * 100);
            if (maxDistance <= 1000)
            {
                distLina = 200;
            }
            var circleDistance = new[] { distLina, distLina * 2, distLina * 3, distLina * 4, distLina * 5 };
            
            var labels = circleDistance.Select(x => x + "km").ToArray();
            // polarAxis.SetCircles(distLina,5);
            _polarAxis.SetCircles(circleDistance, labels);

            IColormap colormap = Application.Current!.ActualThemeVariant == ThemeVariant.Dark
                ? new ScottPlot.Colormaps.MellowRainbow()
                : new ScottPlot.Colormaps.Turbo();

            if (densities.Length == 0) return;

            var maxDensity = densities.Max();
            var minDensity = densities.Min();

            for (var i = 0; i < cacheData.Length; i++)
            {
                var point = cacheData[i];
                var pt = _polarAxis.GetCoordinates(point.Distance, point.Azimuth);

                var marker = PlotControl.Plot.Add.Marker(pt);
                marker.Shape = MarkerShape.Eks;
                marker.Size = 5;
                marker.Color = Color.FromHex("1E90FF");
                if (ShowDestColor)
                {
                    // 归一化
                    var normalizedDensity = maxDensity > minDensity
                        ? (densities[i] - minDensity) / (maxDensity - minDensity)
                        : 0.5;
                    marker.Color = colormap.GetColor(normalizedDensity);
                }
            }
        }
        catch (Exception e)
        {
            ClassLogger.Error(e);
            PlotControl.Plot.Clear();
        }
        finally
        {
            _refreshTheme();
            PlotControl.Plot.Axes.AutoScale();
            PlotControl.Refresh();
            _isExecutingChartUpdate = false;
        }
    }

    private void _refreshTheme()
    {
        if (Application.Current!.ActualThemeVariant == ThemeVariant.Dark)
        {
            _setDarkTheme();
        }
        else
        {
            _setLightTheme();
        }
    }

    private void _setDarkTheme()
    {
        if (PlotControl?.Plot == null) return;

        PlotControl.Plot.SetStyle(new Dark());

        foreach (var polarAxisCircle in _polarAxis.Circles)
        {
            polarAxisCircle.LineStyle.Color = Colors.White;
            polarAxisCircle.LabelStyle.ForeColor = Colors.White;
        }

        foreach (var spoke in _polarAxis.Spokes)
        {
            spoke.LineStyle.Color = Colors.White;
            spoke.LabelStyle.ForeColor = Colors.White;
        }
    }

    private void _setLightTheme()
    {
        if (PlotControl?.Plot == null) return;
        PlotControl.Plot.SetStyle(new Light());
        foreach (var polarAxisCircle in _polarAxis.Circles)
        {
            polarAxisCircle.LineStyle = new LineStyle()
            {
                Width = 1f,
                Color = Colors.Black.WithAlpha(0.5)
            };
            polarAxisCircle.LabelStyle.ForeColor = Colors.Black;
        }

        foreach (var spoke in _polarAxis.Spokes)
        {
            spoke.LineStyle = new LineStyle()
            {
                Width = 1f,
                Color = Colors.Black.WithAlpha(0.5)
            };
            spoke.LabelStyle.ForeColor = Colors.Black;
        }
    }
}