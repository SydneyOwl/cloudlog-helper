using System;
using System.Collections.Generic;
using System.Globalization;
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
using ScottPlot.Statistics;

namespace CloudlogHelper.ViewModels.Charts;

public class StationStatisticsChartWindowViewModel : ChartWindowViewModel
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    public AvaPlot PlotControl { get; private set; }
    
    private IChartDataCacheService<ChartQSOPoint> _chartDataCacheService;
    
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

    // <Band, <Dxcc, count>>
    private Dictionary<string, Dictionary<string, double?>?> _accumulatedStationCount = new();
    
    // <Band, Histogram>
    private Dictionary<string, Histogram> _accumulatedStationDistance = new();
    private Dictionary<string, Histogram> _accumulatedStationBearing = new();
    public ReactiveCommand<Unit, Unit> RefreshChart { get; }
    [Reactive] public int SampleCount { get; set; } = 0;

    public StationStatisticsChartWindowViewModel()
    {
        
    }

    public StationStatisticsChartWindowViewModel(IChartDataCacheService<ChartQSOPoint> chartDataCacheService,
        IApplicationSettingsService applicationSettingsService)
    {
        _basicSettings = applicationSettingsService.GetCurrentSettings().BasicSettings;
        _chartDataCacheService = chartDataCacheService;

        Application.Current!.ActualThemeVariantChanged += (sender, args) =>
        {
            UpdateChart();
        };
        
        RefreshChart = ReactiveCommand.Create(UpdateChart);

        PlotControl = new AvaPlot();
        PlotControl.Multiplot.AddPlots(4);
        PlotControl.Multiplot.Layout = new ScottPlot.MultiplotLayouts.Grid(rows: 2, columns: 2);
        PlotControl.UserInputProcessor.Disable();

        _chartDataCacheService.GetItemAddedObservable()
            .Do(item =>
            {
                // acc count
                if (!_accumulatedStationCount.TryGetValue(item.Band, out _))
                    _accumulatedStationCount[item.Band] = new Dictionary<string, double?>();

                if (!_accumulatedStationCount[item.Band]!.TryGetValue(item.DXCC, out _))
                    _accumulatedStationCount[item.Band]![item.DXCC] = 0;

                _accumulatedStationCount[item.Band]![item.DXCC] += 1;
                
                // acc dist
                if (!_accumulatedStationDistance.TryGetValue(item.Band, out _))
                    _accumulatedStationDistance[item.Band] = Histogram.WithBinCount(50,0,20000);
                
                if (item.Distance >= 0) _accumulatedStationDistance[item.Band].Add(item.Distance);
                
                // acc bearing
                if (!_accumulatedStationBearing.TryGetValue(item.Band, out _))
                    _accumulatedStationBearing[item.Band] = Histogram.WithBinSize(10,0,360);
                
                if (item.Azimuth >= 0) _accumulatedStationBearing[item.Band].Add(item.Azimuth);


                SampleCount += 1;
            })
            .Throttle(TimeSpan.FromSeconds(DefaultConfigs.UpdateChartsThrottleTime))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe((_) =>
            {
                UpdateChart();
            });

        this.WhenActivated(disposable =>
        {
        
            SelectedBand = "10m";


            this.WhenAnyValue(x => x.SelectedBand,
                    x => x.SelectedClient,
                    x=> x.SelectedMode,
                    x => x.UpdatePaused)
                .Throttle(TimeSpan.FromMilliseconds(352))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe((_) => UpdateChart())
                .DisposeWith(disposable);
        });
        
        _ = _addFake();
        UpdateChart();
    }

    private void _updatePlot_top10decoded()
    {
        ClassLogger.Trace("Updating bar1.");
        
        var plot1 = PlotControl.Multiplot.GetPlot(0);
        plot1.Clear();

        if (!_accumulatedStationCount.TryGetValue(SelectedBand, out var bandData))return;
        
        if (bandData is null)return;
        
        var orderedData = bandData.OrderByDescending(x => x.Value)
            .Where(x => x.Value is not null && x.Value > 0)
            .Take(10)
            .ToList();

        var ticks = new List<Tick>();
        var bars = new List<Bar>();
        
        for (var i = 0; i < orderedData.Count; i++)
        {
            bars.Add(new Bar()
            {
                Position = i + 1,
                Value = orderedData[i].Value ?? 0,
                FillColor = colors[i % colors.Length],
                Label = orderedData[i].Value?.ToString("N0") ?? "Unknown"
            });
            ticks.Add(new Tick(i + 1, orderedData[i].Key));
        }
        
        var barPlot = plot1.Add.Bars(bars);
        barPlot.ValueLabelStyle.ForeColor = Application.Current!.ActualThemeVariant == ThemeVariant.Dark ? Colors.White : Colors.Black;

        foreach (var bar in barPlot.Bars)
        {
            bar.Label = bar.Value.ToString(CultureInfo.InvariantCulture);
            bar.LineWidth = 0;
        }
        
        plot1.Title($"Top 10 decoded DXCCs - {SelectedBand} Band");
        plot1.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks.ToArray());
        plot1.Axes.Bottom.MajorTickStyle.Length = 0;
        plot1.Axes.Bottom.TickLabelStyle.Alignment = Alignment.MiddleCenter;
        plot1.Axes.Margins(bottom: 0, top:0.3); 
        plot1.HideGrid();
        plot1.Axes.AutoScale();
    }
    
    private void _updatePlot_distance()
    { 
        ClassLogger.Trace("Updating bar2.");
        
        var barColor = Application.Current!.ActualThemeVariant == ThemeVariant.Dark 
            ? Color.FromHex("#64B5F6")  
            : Color.FromHex("#4DB6AC"); 
        
        var plot2 = PlotControl.Multiplot.GetPlot(2);
        if (!_accumulatedStationDistance.TryGetValue(SelectedBand, out var bandData))return;
        plot2.Clear();
        var histogramBars = plot2.Add.Histogram(bandData);
        foreach (var histogramBarsBar in histogramBars.Bars)
        {
            histogramBarsBar.FillColor = barColor.WithAlpha(0.7);
            histogramBarsBar.LineColor = barColor;                     
        }
        plot2.Title($"Decoded station distance - {SelectedBand} Band");
        plot2.Axes.AutoScale();
    }
    
    private void _updatePlot_bearing()
    { 
        ClassLogger.Trace("Updating bar3.");
        
        var barColor = Application.Current!.ActualThemeVariant == ThemeVariant.Dark 
            ? Color.FromHex("#64B5F6")  
            : Color.FromHex("#4DB6AC"); 
        
        var plot3 = PlotControl.Multiplot.GetPlot(3);
        if (!_accumulatedStationBearing.TryGetValue(SelectedBand, out var bandData))return;
        plot3.Clear();
        var histogramBars = plot3.Add.Histogram(bandData);
        foreach (var histogramBarsBar in histogramBars.Bars)
        {
            histogramBarsBar.FillColor = barColor.WithAlpha(0.7);
            histogramBarsBar.LineColor = barColor;                     
        }
        plot3.Title($"Decoded station bearing - {SelectedBand} Band");
        plot3.Axes.AutoScale();
    }
    
    private void UpdateChart()
    {
        if (_basicSettings.DisableAllCharts)
        {
            LastDataUpdatedAt = TranslationHelper.GetString(LangKeys.chartsdisabled);
            return;
        }
        if (UpdatePaused || IsExecutingChartUpdate)return;
        try
        {
            IsExecutingChartUpdate = true;
            _updatePlot_top10decoded();
            _updatePlot_distance();
            _updatePlot_bearing();
            _refreshTheme();
        }
        catch (Exception e)
        {
            PlotControl.Plot.Clear();
            ClassLogger.Error(e);
        }
        finally
        {
            PlotControl.Plot.Axes.AutoScale();
            LastDataUpdatedAt = $"{DateTime.Now.ToShortDateString()} {DateTime.Now.ToLongTimeString()}";
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
        
            plot.Legend.BackgroundColor = legendBgColor;
            plot.Legend.FontColor = legendFontColor;
            plot.Legend.OutlineColor = legendOutlineColor;
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
        
            plot.Legend.BackgroundColor = legendBgColor;
            plot.Legend.FontColor = legendFontColor;
            plot.Legend.OutlineColor = legendOutlineColor;
        }
    }

    private async Task _addFake()
    {
        var ranBand = new string[] { "6m", "10m", "15m", "20m", "40m" };
        var ranDxcc = new string[]
            { "BY", "JA", "K", "P5", "VK", "VE", "PY", "Pen1s", "Pussy", "Pen2s", "Pen3s", "Pen4s" };
        
        try
        {
            for (; ;)
            {
                _chartDataCacheService.Add(new ChartQSOPoint
                {
                    DxCallsign = null,
                    DXCC = ranDxcc[new Random().Next(0, ranDxcc.Length)],
                    Azimuth = new Random().NextDouble()*360,
                    Distance = new Random().NextDouble()*20000,
                    Mode = null,
                    Snr = 0,
                    Band = "10m",
                    Client = null
                });
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.StackTrace);
        }
        
    }
}