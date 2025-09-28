﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
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
using ScottPlot.Statistics;
using Range = ScottPlot.Range;

namespace CloudlogHelper.ViewModels.Charts;

public class StationStatisticsChartWindowViewModel : ChartWindowViewModel
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    public AvaPlot PlotControl { get; private set; }
    
    private IChartDataCacheService _chartDataCacheService;
    
    private BasicSettings _basicSettings;
    
    private readonly Color[] colors = {
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
    
    public ReactiveCommand<Unit, Unit> RefreshChart { get; }
    public ReactiveCommand<Unit, Unit> ClearChart { get; }
    [Reactive] public int SampleCount { get; set; } = 0;

    public StationStatisticsChartWindowViewModel()
    {
        
    }

    public StationStatisticsChartWindowViewModel(IChartDataCacheService chartDataCacheService,
        IApplicationSettingsService applicationSettingsService)
    {
        _basicSettings = applicationSettingsService.GetCurrentSettings().BasicSettings;
        _chartDataCacheService = chartDataCacheService;

        Application.Current!.ActualThemeVariantChanged += (sender, args) =>
        {
            UpdateChart();
        };
        
        RefreshChart = ReactiveCommand.Create(UpdateChart);
        ClearChart = ReactiveCommand.Create(ClearData);

        PlotControl = new AvaPlot();
        PlotControl.Multiplot.AddPlots(4);
        PlotControl.Multiplot.Layout = new ScottPlot.MultiplotLayouts.Grid(rows: 2, columns: 2);
        // PlotControl.UserInputProcessor.Disable();

        _chartDataCacheService.GetItemAddedObservable()
            .Do(item => SampleCount += 1)
            .Throttle(TimeSpan.FromSeconds(DefaultConfigs.UpdateChartsThrottleTime))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe((_) =>
            {
                UpdateChart();
            });

        this.WhenActivated(disposable =>
        {
            this.WhenAnyValue(x => x.SelectedBand,
                    x => x.SelectedClient,
                    x=> x.SelectedMode,
                    x => x.UpdatePaused)
                .Throttle(TimeSpan.FromMilliseconds(352))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe((_) => UpdateChart())
                .DisposeWith(disposable);
        });
        
        // _ = _addFake();
        UpdateChart();
    }

    private void _updatePlot_top10decoded()
    {
        ClassLogger.Trace("Updating bar1.");
        
        var plot1 = PlotControl.Multiplot.GetPlot(0);
        plot1.Clear();

        var bandData = _chartDataCacheService.GetStationCountByBand(SelectedBand);
        if (bandData is null)
        {
            plot1.Title($"Station Distance Distribution - {SelectedBand} Band\n(No data available)");
            return;
        }
        
        var orderedData = bandData.OrderByDescending(x => x.Value)
            .Where(x => x.Value is not null && x.Value > 0)
            .Take(10)
            .ToList();

        if (!orderedData.Any()) return;

        var ticks = new List<Tick>();
        var bars = new List<Bar>();
        
        for (var i = 0; i < orderedData.Count; i++)
        {
            bars.Add(new Bar()
            {
                Position = i + 1,
                Value = orderedData[i].Value ?? 0,
                FillColor = colors[i % colors.Length],
                Label = orderedData[i].Value?.ToString("N0") ?? "0"
            });
            ticks.Add(new Tick(i + 1, orderedData[i].Key));
        }
        
        var barPlot = plot1.Add.Bars(bars);
        barPlot.ValueLabelStyle.ForeColor = Application.Current!.ActualThemeVariant == ThemeVariant.Dark ? Colors.White : Colors.Black;
        barPlot.ValueLabelStyle.FontSize = 10;

        foreach (var bar in barPlot.Bars)
        {
            bar.Label = bar.Value.ToString("N0");
            bar.LineWidth = 0;
        }
        
        plot1.Title($"Top 10 DXCCs - {SelectedBand} Band");
        plot1.YLabel("Signal Count");
        plot1.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks.ToArray());
        plot1.Axes.Bottom.MajorTickStyle.Length = 0;
        plot1.Axes.Bottom.TickLabelStyle.Alignment = Alignment.MiddleCenter;
        // plot1.Axes.Bottom.TickLabelStyle.Rotation = 45;
        plot1.Axes.Margins(bottom: 0, top: 0.3, left: 0.05, right: 0.05); 
        plot1.Axes.AutoScale();
    }
    
    private void _updatePlot_distance()
    { 
        ClassLogger.Trace("Updating distance histogram.");
        
        var barColor = Application.Current!.ActualThemeVariant == ThemeVariant.Dark 
            ? Color.FromHex("#64B5F6")  
            : Color.FromHex("#4DB6AC"); 
        
        var plot2 = PlotControl.Multiplot.GetPlot(1);
        plot2.Clear();

        var bandData = _chartDataCacheService.GetDistanceHistogramByBand(SelectedBand);

        if (bandData is null || bandData.Counts.Sum() == 0) 
        {
            plot2.Title($"Station Distance Distribution - {SelectedBand} Band\n(No data available)");
            return;
        }
        
        var histogramBars = plot2.Add.Histogram(bandData);
        
        foreach (var histogramBarsBar in histogramBars.Bars)
        {
            histogramBarsBar.FillColor = barColor.WithAlpha(0.7);
            histogramBarsBar.LineColor = barColor;                     
        }
        
        plot2.Title($"Station Distance Distribution - {SelectedBand} Band");
        plot2.XLabel("Distance (km)");
        plot2.YLabel("Signal Count");
        plot2.Axes.Margins(bottom:0,top: 0.15,  left: 0.05, right: 0.05); 
        
        plot2.Axes.AutoScale();
    }
    
    private void _updatePlot_bearing()
    { 
        ClassLogger.Trace("Updating bearing histogram.");
        
        var barColor = Application.Current!.ActualThemeVariant == ThemeVariant.Dark 
            ? Color.FromHex("#FFB74D") 
            : Color.FromHex("#FF9800");
        
        var plot3 = PlotControl.Multiplot.GetPlot(2);
        plot3.Clear();
        
        var bandData = _chartDataCacheService.GetBearingHistogramByBand(SelectedBand);

        if (bandData is null || bandData.Counts.Sum() == 0) 
        {
            plot3.Title($"Station Bearing Distribution - {SelectedBand} Band\n(No data available)");
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
        plot3.Title($"Station Bearing Distribution - {SelectedBand} Band");
        plot3.XLabel("Bearing (degrees)");
        plot3.YLabel("Signal Count");
        
        // 设置X轴刻度为方位角
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
        plot3.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks.ToArray());
        
        plot3.Axes.AutoScale();
        // plot3.Grid.MajorLineStyle.Width = 0.5f;
    }
    
    private void _updatePlot_world_heatmap()
    {
        ClassLogger.Trace("Updating heatmap plot.");
    
        var plot4 = PlotControl.Multiplot.GetPlot(3);
        plot4.Clear();
    
        plot4.Title($"World Heatmap - {SelectedBand} Band");
    
        plot4.XLabel("Longitude");
        plot4.YLabel("Latitude");
    
        var resourceStream = ApplicationStartUpUtil.GetResourceStream(DefaultConfigs.DefaultWorldMapFile);
        if (resourceStream is null)
        {
            ClassLogger.Error("Resource stream of map is null");
            plot4.Title($"World heatmap(No map available) - {SelectedBand} Band");
            return;
        }
    
        using var ms = new MemoryStream();
        resourceStream.CopyTo(ms);
        var img = new Image(ms.ToArray());

        CoordinateRect worldRect = new(left: -180, right: 180, bottom: -90, top: 90);
        plot4.Add.ImageRect(img, worldRect);

        var gridStationCountByBand = _chartDataCacheService.GetGridStationCountByBand(SelectedBand);
        if (gridStationCountByBand is null || gridStationCountByBand.Length == 0)
        {
            gridStationCountByBand = new double[DefaultConfigs.WorldHeatmapHeight, DefaultConfigs.WorldHeatmapWidth];
        }
        
        var smoothedData = QSOPointUtil.ApplyGaussianBlur(gridStationCountByBand, 1.2);
        smoothedData = QSOPointUtil.NormalizeData(smoothedData, 0.7, 1);

        var heatmap = plot4.Add.Heatmap(smoothedData);
        heatmap.Extent = new CoordinateRect(-180, 180, -90, 90);
        heatmap.Colormap = new ScottPlot.Colormaps.Turbo();

        heatmap.Opacity = 0.35;
        heatmap.FlipVertically = true;
        heatmap.Smooth = true;
        
        plot4.Axes.SetLimitsX(-180, 180);
        plot4.Axes.SetLimitsY(-90, 90);
        
        var maximumSpan = new MaximumSpan(
            xAxis: plot4!.Axes.Bottom,
            yAxis: plot4!.Axes.Left,
            xSpan: 360,
            ySpan: 180);
        
        plot4!.Axes.Rules.Add(maximumSpan);
    }

    private void ClearData()
    {
        _chartDataCacheService.ClearAccuBuffer();
        SampleCount = 0;
        UpdateChart();
    }

    private void UpdateChart()
    {
        if (UpdatePaused || IsExecutingChartUpdate) return;
        
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
            IsExecutingChartUpdate = true;
            ClassLogger.Debug("Updating statistic");
            _updatePlot_top10decoded();
            _updatePlot_distance();
            _updatePlot_bearing();
            _updatePlot_world_heatmap();
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
        var themeColor = Color.FromHex("#d7d7d7");
        var figureBgColor = Color.FromHex("#181818");
        var dataBgColor = Color.FromHex("#1f1f1f");
        var gridColor = Color.FromHex("#404040");
        var legendBgColor = Color.FromHex("#404040");
        var legendFontColor = Color.FromHex("#d7d7d7");
        var legendOutlineColor = Color.FromHex("#d7d7d7");

        for (var i = 0; i < PlotControl.Multiplot.GetPlots().Length; i++)
        {
            var plot = PlotControl.Multiplot.GetPlot(i);
            plot.FigureBackground.Color = figureBgColor;
            plot.DataBackground.Color = dataBgColor;
            plot.Axes.Color(themeColor);
            plot.Grid.MajorLineColor = gridColor;
            plot.Grid.MinorLineColor = gridColor.WithAlpha(0.3);
        
            plot.Legend.BackgroundColor = legendBgColor;
            plot.Legend.FontColor = legendFontColor;
            plot.Legend.OutlineColor = legendOutlineColor;
            
            plot.Axes.Title.Label.FontSize = 14;
            plot.Axes.Left.Label.FontSize = 11;
            plot.Axes.Bottom.Label.FontSize = 11;
        }
    }

    private void _setLightTheme()
    {
        var themeColor = Color.FromHex("#34495E");
        var figureBgColor = Color.FromHex("#FDFDFD");
        var dataBgColor = Color.FromHex("#F5F7FA");
        var gridColor = Color.FromHex("#E3E8ED");
        var legendBgColor = Color.FromHex("#FFFFFF");
        var legendFontColor = Color.FromHex("#2C3E50");
        var legendOutlineColor = Color.FromHex("#BDC3C7");

        for (var i = 0; i < PlotControl.Multiplot.GetPlots().Length; i++)
        {
            var plot = PlotControl.Multiplot.GetPlot(i);
            plot.FigureBackground.Color = figureBgColor;
            plot.DataBackground.Color = dataBgColor;
            plot.Axes.Color(themeColor);
            plot.Grid.MajorLineColor = gridColor;
            plot.Grid.MinorLineColor = gridColor.WithAlpha(0.3);
        
            plot.Legend.BackgroundColor = legendBgColor;
            plot.Legend.FontColor = legendFontColor;
            plot.Legend.OutlineColor = legendOutlineColor;
            
            plot.Axes.Title.Label.FontSize = 14;
            plot.Axes.Left.Label.FontSize = 11;
            plot.Axes.Bottom.Label.FontSize = 11;
        }
    }

    private async Task _addFake()
    {
        var ranBand = new string[] { "6m", "10m", "15m", "20m", "40m" };
        var ranDxcc = new string[]
            { "BY", "JA", "K", "P5", "VK", "VE", "PY", "DL", "G", "F", "I", "EA" };
        
        try
        {
            for (; ;)
            {
                _chartDataCacheService.Add(new ChartQSOPoint
                {
                    DxCallsign = null,
                    DXCC = ranDxcc[new Random().Next(0, ranDxcc.Length)],
                    Azimuth = new Random().NextDouble() * 360,
                    Distance = new Random().NextDouble() * 20000,
                    Mode = "FT8",
                    Snr = new Random().Next(-20, 10),
                    Latitude = new Random().NextDouble() * 180 - 90, // 纬度范围：-90 到 90
                    Longitude = new Random().NextDouble() * 360 - 180, // 经度范围：-180 到 180
                    Band = "10m",
                    Client = "TEST"
                });
                await Task.Delay(TimeSpan.FromMilliseconds(1));
            }
        }
        catch (Exception e)
        {
            ClassLogger.Error(e, "Error adding fake data");
        }
    }
}