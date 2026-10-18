using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
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
using ScottPlot.AxisRules;
using ScottPlot.Colormaps;
using ScottPlot.MultiplotLayouts;
using ScottPlot.TickGenerators;

namespace CloudlogHelper.ViewModels.Charts;

internal enum PlotType
{
    TopDXCC,
    StationDistance,
    StationBearing,
    WorldHeatmap
}

public class StationStatisticsChartWindowViewModel : ChartWindowViewModel
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private Dictionary<PlotType, Plot> _plotCache = new();

    private readonly Color[] colors =
    {
        Color.FromHex("#64B5F6"),
        Color.FromHex("#4DB6AC"),
        Color.FromHex("#81C784"),
        Color.FromHex("#FFD54F"),
        Color.FromHex("#FF8A65"),
        Color.FromHex("#BA68C8"),
        Color.FromHex("#4FC3F7"),
        Color.FromHex("#AED581"),
        Color.FromHex("#7986CB")
    };

    private readonly BasicSettings _basicSettings;

    private readonly IChartDataCacheService _chartDataCacheService;
    
    private Image? _worldMapImage;

    public StationStatisticsChartWindowViewModel()
    {
    }

    public StationStatisticsChartWindowViewModel(IChartDataCacheService chartDataCacheService,
        IApplicationSettingsService applicationSettingsService,
        IClientRegistryService clientRegistryService) : base(clientRegistryService)
    {
        _basicSettings = applicationSettingsService.GetCurrentSettings().BasicSettings;
        _chartDataCacheService = chartDataCacheService;

        // No auto-switch in this window: band/mode are never set automatically,
        // the user picks them from the dropdowns.
        AutoSwitchEnabled = false;

        Application.Current!.ActualThemeVariantChanged += (sender, args) => { UpdateChart(); };

        RefreshChart = ReactiveCommand.Create(UpdateChart);
        ClearChart = ReactiveCommand.Create(ClearData);
        ChangeSingleChart = ReactiveCommand.Create(_changeSinglePlot);

        PlotControl = new AvaPlot();
        PlotControl.Multiplot.AddPlots(4);
        PlotControl.Multiplot.Layout = new Grid(2, 2);

        _plotCache[PlotType.TopDXCC] = PlotControl.Multiplot.GetPlot(0);
        _plotCache[PlotType.StationDistance] = PlotControl.Multiplot.GetPlot(1);
        _plotCache[PlotType.StationBearing] = PlotControl.Multiplot.GetPlot(2);
        _plotCache[PlotType.WorldHeatmap] = PlotControl.Multiplot.GetPlot(3);

        this.WhenActivated(disposable =>
        {
            _chartDataCacheService.GetItemAddedObservable()
                .Throttle(TimeSpan.FromSeconds(DefaultConfigs.UpdateChartsThrottleSec))
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => { UpdateChart(); })
                .DisposeWith(disposable);
            
            this.WhenAnyValue(x => x.SelectedChart)
                .Select(selected => selected == 4 || selected == 0)
                .BindTo(this, x => x.ShouldShowLocationButton);
            
            this.WhenAnyValue(x => x.SelectedBand,
                    x => x.SelectedMode,
                    x => x.UpdatePaused,
                    x => x.ShowLocation,
                    x => x.FilterDupeCallsign)
                .Throttle(TimeSpan.FromMilliseconds(200))
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => UpdateChart())
                .DisposeWith(disposable);
        });

        UpdateChart();
    }

    public AvaPlot PlotControl { get; }

    public ReactiveCommand<Unit, Unit> RefreshChart { get; }
    public ReactiveCommand<Unit, Unit> ClearChart { get; }
    
    public ReactiveCommand<Unit, Unit> ChangeSingleChart { get; }
    
    [Reactive] public int SelectedChart  {get; set; }
    
    [Reactive] public bool ShouldShowLocationButton {get; set; }
    [Reactive] public bool ShowLocation {get; set; }

    private void _changeSinglePlot()
    {
        switch (++SelectedChart % 5)
        {
            case 0:
                PlotControl.Multiplot.Layout = new Grid(2, 2);
                foreach (var keyValuePair in _plotCache)
                {
                    PlotControl.Multiplot.RemovePlot(keyValuePair.Value);
                }
                
                foreach (var keyValuePair in _plotCache)
                {
                    PlotControl.Multiplot.AddPlot(keyValuePair.Value);
                }
                
                break;
            case 1:
                PlotControl.Multiplot.Layout = new Grid(1, 1);
                PlotControl.Multiplot.Reset(_plotCache[PlotType.TopDXCC]);
                break;
            case 2:
                PlotControl.Multiplot.Layout = new Grid(1, 1);
                PlotControl.Multiplot.Reset(_plotCache[PlotType.StationDistance]);
                break;
            case 3:
                PlotControl.Multiplot.Layout = new Grid(1, 1);
                PlotControl.Multiplot.Reset(_plotCache[PlotType.StationBearing]);
                break;
            case 4:
                PlotControl.Multiplot.Layout = new Grid(1, 1);
                PlotControl.Multiplot.Reset(_plotCache[PlotType.WorldHeatmap]);
                break;
        }
        PlotControl.Refresh();
        if (SelectedChart == 5) SelectedChart = 0;
    }

    private void _updatePlot_top10decoded(StationChartDataSnapshot chartData)
    {
        ClassLogger.Trace("Updating bar1.");

        var plot1 = _plotCache[PlotType.TopDXCC];
        plot1.Clear();

        var orderedData = chartData.StationCountByDxcc.OrderByDescending(x => x.Value)
            .Where(x => x.Value is not null && x.Value > 0)
            .Take(10)
            .ToList();

        if (!orderedData.Any())
        {
            plot1.Title($"Top 10 DXCCs - {_bandModeTitle()}\n(No data available)");
            return;
        }

        var ticks = new List<Tick>();
        var bars = new List<Bar>();

        for (var i = 0; i < orderedData.Count; i++)
        {
            bars.Add(new Bar
            {
                Position = i + 1,
                Value = orderedData[i].Value ?? 0,
                FillColor = colors[i % colors.Length],
                Label = orderedData[i].Value?.ToString("N0") ?? "0"
            });
            ticks.Add(new Tick(i + 1, orderedData[i].Key));
        }

        var barPlot = plot1.Add.Bars(bars);
        barPlot.ValueLabelStyle.ForeColor =
            Application.Current!.ActualThemeVariant == ThemeVariant.Dark ? Colors.White : Colors.Black;
        barPlot.ValueLabelStyle.FontSize = 10;

        foreach (var bar in barPlot.Bars)
        {
            bar.Label = bar.Value.ToString("N0");
            bar.LineWidth = 0;
        }

        plot1.Title($"Top 10 DXCCs - {_bandModeTitle()}");
        plot1.YLabel("Signal Count");
        plot1.Axes.Bottom.TickGenerator = new NumericManual(ticks.ToArray());
        plot1.Axes.Bottom.MajorTickStyle.Length = 0;
        plot1.Axes.Bottom.TickLabelStyle.Alignment = Alignment.MiddleCenter;
        // plot1.Axes.Bottom.TickLabelStyle.Rotation = 45;
        plot1.Axes.Margins(bottom: 0, top: 0.3, left: 0.05, right: 0.05);
        plot1.Axes.AutoScale();
    }

    private void _updatePlot_distance(StationChartDataSnapshot chartData)
    {
        ClassLogger.Trace("Updating distance histogram.");

        var barColor = Application.Current!.ActualThemeVariant == ThemeVariant.Dark
            ? Color.FromHex("#64B5F6")
            : Color.FromHex("#4DB6AC");

        var plot2 = _plotCache[PlotType.StationDistance];
        plot2.Clear();

        var bandData = chartData.DistanceHistogram;

        if (bandData.Counts.Sum() == 0)
        {
            plot2.Title($"Station Distance Distribution - {_bandModeTitle()}\n(No data available)");
            return;
        }

        var histogramBars = plot2.Add.Histogram(bandData);

        foreach (var histogramBarsBar in histogramBars.Bars)
        {
            histogramBarsBar.FillColor = barColor.WithAlpha(0.7);
            histogramBarsBar.LineColor = barColor;
        }

        plot2.Title($"Station Distance Distribution - {_bandModeTitle()}");
        plot2.XLabel("Distance (km)");
        plot2.YLabel("Signal Count");
        plot2.Axes.Margins(bottom: 0, top: 0.15, left: 0.05, right: 0.05);

        plot2.Axes.AutoScale();
    }

    private void _updatePlot_bearing(StationChartDataSnapshot chartData)
    {
        ClassLogger.Trace("Updating bearing histogram.");

        var barColor = Application.Current!.ActualThemeVariant == ThemeVariant.Dark
            ? Color.FromHex("#FFB74D")
            : Color.FromHex("#FF9800");

        var plot3 = _plotCache[PlotType.StationBearing];
        plot3.Clear();

        var bandData = chartData.BearingHistogram;

        if (bandData.Counts.Sum() == 0)
        {
            plot3.Title($"Station Bearing Distribution - {_bandModeTitle()}\n(No data available)");
            return;
        }

        var histogramBars = plot3.Add.Histogram(bandData);

        for (var i = 0; i < histogramBars.Bars.Length; i++)
        {
            var bar = histogramBars.Bars[i];
            bar.FillColor = barColor.WithAlpha(0.7);
            bar.LineColor = barColor;
        }

        plot3.Axes.Margins(bottom: 0, top: 0.18, left: 0.05, right: 0.1);
        plot3.Title($"Station Bearing Distribution - {_bandModeTitle()}");
        plot3.XLabel("Bearing (degrees)");
        plot3.YLabel("Signal Count");

        var ticks = new List<Tick>();
        for (var i = 0; i <= 360; i += 45)
        {
            var label = i switch
            {
                0 => "N\n0°",
                45 => "NE\n45°",
                90 => "E\n90°",
                135 => "SE\n135°",
                180 => "S\n180°",
                225 => "SW\n225°",
                270 => "W\n270°",
                315 => "NW\n315°",
                360 => "N\n360°",
                _ => $"{i}°"
            };
            ticks.Add(new Tick(i, label));
        }

        plot3.Axes.Bottom.TickGenerator = new NumericManual(ticks.ToArray());

        plot3.Axes.AutoScale();
        // plot3.Grid.MajorLineStyle.Width = 0.5f;
    }

    private void _updatePlot_world_heatmap(StationChartDataSnapshot chartData)
    {
        ClassLogger.Trace("Updating heatmap plot.");

        var plot4 = _plotCache[PlotType.WorldHeatmap];
        plot4.Clear();

        plot4.Title($"World Heatmap - {_bandModeTitle()}");

        plot4.XLabel("Longitude");
        plot4.YLabel("Latitude");

        plot4.Axes.SetLimitsX(-180, 180);
        plot4.Axes.SetLimitsY(-90, 90);

        var maximumSpan = new MaximumSpan(
            plot4!.Axes.Bottom,
            plot4!.Axes.Left,
            360,
            180);

        plot4!.Axes.Rules.Add(maximumSpan);

        if (!TryLoadWorldMapImage())
        {
            plot4.Title($"World heatmap(No map available) - {_bandModeTitle()}");
            return;
        }

        CoordinateRect worldRect = new(-180, 180, -90, 90);
        plot4.Add.ImageRect(_worldMapImage!, worldRect);
        
        if (ShowLocation)
        {
            var latLng = MaidenheadGridUtil.GridToLatLng(_basicSettings.MyMaidenheadGrid);
            if (latLng is null) return;
            var marker = plot4.Add.Marker(latLng.Longitude, latLng.Latitude);
            
            marker.Color = Colors.Red;
            marker.Size = 10;
            marker.Shape = MarkerShape.HashTag;
        }

        var gridStationCountByBand = chartData.GridStationCount;
        if (_isAllZero(gridStationCountByBand))
        {
            plot4.Title($"World heatmap(No data available) - {_bandModeTitle()}");
            return;
        }

        var smoothedData = QSOPointUtil.ApplyValueCompression(gridStationCountByBand);
        smoothedData = QSOPointUtil.ApplyGaussianBlur(smoothedData, 1.2);

        var heatmap = plot4.Add.Heatmap(smoothedData);
        heatmap.Extent = new CoordinateRect(-180, 180, -90, 90);
        heatmap.Colormap = new Turbo();

        heatmap.Opacity = 0.35;
        heatmap.FlipVertically = true;
        heatmap.Smooth = true;
    }

    private string _bandModeTitle()
    {
        return string.IsNullOrWhiteSpace(SelectedMode)
            ? $"{SelectedBand} Band"
            : $"{SelectedBand} Band ({SelectedMode})";
    }

    private static bool _isAllZero(double[,] grid)
    {
        for (var y = 0; y < grid.GetLength(0); y++)
        for (var x = 0; x < grid.GetLength(1); x++)
            if (grid[y, x] != 0) return false;
        return true;
    }

    private void ClearData()
    {
        _chartDataCacheService.ClearAccuBuffer();
        UpdateChart();
    }

    private void UpdateChart()
    {
        if (UpdatePaused || IsExecutingChartUpdate) return;

        if (!MaidenheadGridUtil.CheckMaidenhead(_basicSettings.MyMaidenheadGrid))
        {
            ErrorMessage = TranslationHelper.GetString(LangKeys.GridError);
            ShowErrorMsg = true;
            return;
        }

        if (_basicSettings.DisableAllCharts)
        {
            ErrorMessage = TranslationHelper.GetString(LangKeys.ChartsDisabled);
            ShowErrorMsg = true;
            return;
        }

        try
        {
            ShowErrorMsg = false;
            IsExecutingChartUpdate = true;
            ClassLogger.Debug("Updating statistic");
            // The statistics chart is a cross-client view of the selected band and mode:
            // the accumulators never split by client, so every client's decodes on the
            // band are aggregated together.
            var chartDataSnapshot =
                _chartDataCacheService.GetStationChartDataSnapshot(SelectedBand, SelectedMode, FilterDupeCallsign);
            switch (SelectedChart)
            {
                case 1:
                    _updatePlot_top10decoded(chartDataSnapshot);
                    break;
                case 2:
                    _updatePlot_distance(chartDataSnapshot);
                    break;
                case 3:
                    _updatePlot_bearing(chartDataSnapshot);
                    break;
                case 4:
                    _updatePlot_world_heatmap(chartDataSnapshot);
                    break;
                default:
                    _updatePlot_top10decoded(chartDataSnapshot);
                    _updatePlot_distance(chartDataSnapshot);
                    _updatePlot_bearing(chartDataSnapshot);
                    _updatePlot_world_heatmap(chartDataSnapshot);
                    break;
            }
            _refreshTheme();
            ClassLogger.Debug("Charts updated");
        }
        catch (Exception e)
        {
            PlotControl.Plot.Clear();
            ClassLogger.Error(e, "Error updating chart");
            ShowErrorMsg = true;
            ErrorMessage = e.Message;
        }
        finally
        {
            LastDataUpdatedAt = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            PlotControl.Refresh();
            IsExecutingChartUpdate = false;
        }
    }

    private readonly record struct PlotThemeColors(
        Color Theme,
        Color FigureBg,
        Color DataBg,
        Color Grid,
        Color LegendBg,
        Color LegendFont,
        Color LegendOutline);

    private static readonly PlotThemeColors DarkPlotTheme = new(
        Color.FromHex("#d7d7d7"),
        Color.FromHex("#181818"),
        Color.FromHex("#1f1f1f"),
        Color.FromHex("#404040"),
        Color.FromHex("#404040"),
        Color.FromHex("#d7d7d7"),
        Color.FromHex("#d7d7d7"));

    private static readonly PlotThemeColors LightPlotTheme = new(
        Color.FromHex("#34495E"),
        Color.FromHex("#FDFDFD"),
        Color.FromHex("#F5F7FA"),
        Color.FromHex("#E3E8ED"),
        Color.FromHex("#FFFFFF"),
        Color.FromHex("#2C3E50"),
        Color.FromHex("#BDC3C7"));

    private void _refreshTheme()
    {
        _applyPlotTheme(Application.Current!.ActualThemeVariant == ThemeVariant.Dark
            ? DarkPlotTheme
            : LightPlotTheme);
    }

    private void _applyPlotTheme(PlotThemeColors theme)
    {
        for (var i = 0; i < PlotControl.Multiplot.GetPlots().Length; i++)
        {
            var plot = PlotControl.Multiplot.GetPlot(i);
            plot.FigureBackground.Color = theme.FigureBg;
            plot.DataBackground.Color = theme.DataBg;
            plot.Axes.Color(theme.Theme);
            plot.Grid.MajorLineColor = theme.Grid;
            plot.Grid.MinorLineColor = theme.Grid.WithAlpha(0.3);

            plot.Legend.BackgroundColor = theme.LegendBg;
            plot.Legend.FontColor = theme.LegendFont;
            plot.Legend.OutlineColor = theme.LegendOutline;

            plot.Axes.Title.Label.FontSize = 14;
            plot.Axes.Left.Label.FontSize = 11;
            plot.Axes.Bottom.Label.FontSize = 11;
        }
    }

    private bool TryLoadWorldMapImage()
    {
        if (_worldMapImage is not null) return true;

        var resourceStream = ApplicationStartUpUtil.GetSingleResourceStream(DefaultConfigs.DefaultWorldMapFile);
        if (resourceStream is null)
        {
            ClassLogger.Error("Resource stream of map is null");
            return false;
        }

        using var ms = new MemoryStream();
        resourceStream.CopyTo(ms);
        _worldMapImage = new Image(ms.ToArray());
        return true;
    }

}
