using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Colormaps;
using ScottPlot.PlotStyles;
using ScottPlot.Plottables;

namespace CloudlogHelper.ViewModels.Charts;

public class PolarChartWindowViewModel : ChartWindowViewModel
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly BasicSettings _basicSettings;

    private readonly IChartDataCacheService _chartDataCacheService;

    private PolarAxis _polarAxis;

    public PolarChartWindowViewModel()
    {
    }

    public PolarChartWindowViewModel(IChartDataCacheService chartDataCacheService,
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
        ClearChart = ReactiveCommand.Create(ClearData);

        this.WhenActivated(disposable =>
        {
            SaveChart.ThrownExceptions.Subscribe().DisposeWith(disposable);

            _chartDataCacheService.GetItemAddedObservable()
                .Throttle(TimeSpan.FromSeconds(DefaultConfigs.UpdateChartsThrottleTime))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => { UpdatePolar(); })
                .DisposeWith(disposable);

            this.WhenAnyValue(x => x.KValue,
                    x => x.AngWeightValue,
                    x => x.DistWeightValue,
                    x => x.ShowDestColor,
                    x => x.QSOSamples)
                .Throttle(TimeSpan.FromMilliseconds(500))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => UpdatePolar())
                .DisposeWith(disposable);

            this.WhenAnyValue(x => x.SelectedBand,
                    x => x.SelectedClient,
                    x => x.SelectedMode,
                    x => x.UpdatePaused)
                .Throttle(TimeSpan.FromMilliseconds(352))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => UpdatePolar())
                .DisposeWith(disposable);
        });

        UpdatePolar();
    }

    [Reactive] public int KValue { get; set; } = DefaultConfigs.DefaulPolarKValue;
    [Reactive] public double AngWeightValue { get; set; } = DefaultConfigs.DefaulPolarAngWeightValue;
    [Reactive] public double DistWeightValue { get; set; } = DefaultConfigs.DefaulPolarDistWeightValue;
    [Reactive] public int QSOSamples { get; set; } = DefaultConfigs.DefaultPolarQSOSamples;
    [Reactive] public bool ShowDestColor { get; set; } = true;

    public Interaction<Unit, IStorageFile?> OpenSaveFilePickerInteraction { get; set; } = new();
    public ReactiveCommand<Unit, Unit> SaveChart { get; }
    public ReactiveCommand<Unit, Unit> RefreshChart { get; }
    public ReactiveCommand<Unit, Unit> ClearChart { get; }
    public AvaPlot PlotControl { get; }

    private void ClearData()
    {
        _chartDataCacheService.ClearPolarBuffer();
        UpdatePolar();
    }

    private void UpdatePolar()
    {
        if (IsExecutingChartUpdate || UpdatePaused) return;
        if (!MaidenheadGridUtil.CheckMaidenhead(_basicSettings.MyMaidenheadGrid))
        {
            ErrorMessage = TranslationHelper.GetString(LangKeys.griderror);
            ShowErrorMsg = true;
            return;
        }

        if (_basicSettings.DisableAllCharts)
        {
            ErrorMessage = TranslationHelper.GetString(LangKeys.chartsdisabled);
            ShowErrorMsg = true;
            return;
        }

        try
        {
            ShowErrorMsg = false;
            ClassLogger.Trace("Updating polar.");
            IsExecutingChartUpdate = true;
            PlotControl.Plot.Clear();

            var cacheData = _chartDataCacheService.TakeLatestN(QSOSamples,
                    FilterDupeCallsign ? ChartQSOPoint.ChartQsoPointComparer : null,
                    point => point.Band == SelectedBand
                             // && point.Mode == SelectedMode 
                             && point.Client == SelectedClient && point.Distance >= 0)
                .ToArray();

            var maxDistance = QSOPointUtil.CalculateRobustMaxDistance(cacheData) + 500;
            ClassLogger.Trace($"Use maxDistance:{maxDistance}");
            var densities = new double[1];
            if (ShowDestColor)
                densities = QSOPointUtil.CalculateDensitiesKNN(cacheData, maxDistance, KValue,
                    DistWeightValue, AngWeightValue);

            _polarAxis = PlotControl.Plot.Add.PolarAxis(maxDistance <= 1000 ? 1000 : maxDistance);
            _polarAxis.Rotation = Angle.FromDegrees(-90);
            _polarAxis.Clockwise = true;

            if (cacheData.Length == 0) return;
            var distLina = (double)((int)(maxDistance / 5 / 100) * 100);
            if (maxDistance <= 1000) distLina = 200;
            var circleDistance = new[] { distLina, distLina * 2, distLina * 3, distLina * 4, distLina * 5 };

            var labels = circleDistance.Select(x => x + "km").ToArray();
            // polarAxis.SetCircles(distLina,5);
            _polarAxis.SetCircles(circleDistance, labels);

            IColormap colormap = Application.Current!.ActualThemeVariant == ThemeVariant.Dark
                ? new MellowRainbow()
                : new Turbo();

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
            ClassLogger.Error(e,"Error while updating polar.");
            PlotControl.Plot.Clear();
            ShowErrorMsg = true;
            ErrorMessage = e.Message;
        }
        finally
        {
            _refreshTheme();
            PlotControl.Plot.Axes.AutoScale();
            PlotControl.Refresh();
            LastDataUpdatedAt = $"{DateTime.Now.ToShortDateString()} {DateTime.Now.ToLongTimeString()}";
            IsExecutingChartUpdate = false;
        }
    }

    private void _refreshTheme()
    {
        if (Application.Current!.ActualThemeVariant == ThemeVariant.Dark)
            _setDarkTheme();
        else
            _setLightTheme();
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
            polarAxisCircle.LineStyle = new LineStyle
            {
                Width = 1f,
                Color = Colors.Black.WithAlpha(0.5)
            };
            polarAxisCircle.LabelStyle.ForeColor = Colors.Black;
        }

        foreach (var spoke in _polarAxis.Spokes)
        {
            spoke.LineStyle = new LineStyle
            {
                Width = 1f,
                Color = Colors.Black.WithAlpha(0.5)
            };
            spoke.LabelStyle.ForeColor = Colors.Black;
        }
    }
}