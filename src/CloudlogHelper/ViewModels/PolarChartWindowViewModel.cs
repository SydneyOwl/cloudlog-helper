using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Platform.Storage;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using DynamicData;
using NLog;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ScottPlot;
using ScottPlot.Avalonia;
using ScottPlot.Plottables;

namespace CloudlogHelper.ViewModels;

public class PolarChartWindowViewModel : ViewModelBase
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private ConcurrentFixedSizeQueue<PolarQSOPoint> _qsoPoints = new(DefaultConfigs.DefaulPolarQSOSamples);


    [Reactive] private bool _isExecutingChartUpdate { get; set; }

    [Reactive] public int KValue { get; set; } = DefaultConfigs.DefaulPolarKValue;
    [Reactive] public double AngWeightValue { get; set; } =DefaultConfigs.DefaulPolarAngWeightValue;
    [Reactive] public double DistWeightValue { get; set; } = DefaultConfigs.DefaulPolarDistWeightValue;
    [Reactive] public int QSOSamples { get; set; } = DefaultConfigs.DefaulPolarQSOSamples;
    [Reactive] public bool ShowDestColor { get; set; } = true;
    
    public Interaction<Unit, IStorageFile?> OpenSaveFilePickerInteraction { get; set; }  = new();
    public ReactiveCommand<Unit, Unit> SaveChart { get; }
    public ReactiveCommand<Unit, Unit> ClearData { get; }
    public AvaPlot PlotControl { get; private set; }

    private PolarAxis _polarAxis;

    public PolarChartWindowViewModel()
    {
        PlotControl = new AvaPlot();
        
        SaveChart = ReactiveCommand.CreateFromTask(async () =>
        {
            var a = await OpenSaveFilePickerInteraction?.Handle(Unit.Default)!;
            if (a is null)return;
            PlotControl.Plot.GetImage(DefaultConfigs.ExportedPolarChartSize,
                DefaultConfigs.ExportedPolarChartSize).SavePng(a.Path.AbsolutePath);
        });
        
        ClearData = ReactiveCommand.Create(() =>
        {
            _qsoPoints.Clear();
            UpdatePolar();
        }, this.WhenAnyValue(x => x._isExecutingChartUpdate).Select(executing => !executing));
        
        this.WhenActivated(disposable =>
        {
            SaveChart.ThrownExceptions.Subscribe().DisposeWith(disposable);
            ClearData.ThrownExceptions.Subscribe().DisposeWith(disposable);
            this.WhenAnyValue(x => x.KValue,
                    x=>x.AngWeightValue,
                    x=>x.DistWeightValue,
                    x => x.ShowDestColor,
                    x => x.QSOSamples) 
                .Throttle(TimeSpan.FromMilliseconds(500))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe((_)=>UpdatePolar())
                .DisposeWith(disposable);
        });
        foreach (var polarQSOPoint in QSOPointUtil.GenerateFakeFT8Data(1000))
        {
            _qsoPoints.Enqueue(polarQSOPoint);
        }
        UpdatePolar();
    }

    private void UpdatePolar()
    {
        if (_isExecutingChartUpdate)return;
        try
        {
            _isExecutingChartUpdate = true;
            _qsoPoints.Resize(QSOSamples);
            PlotControl.Plot.Clear();
            var cacheData = _qsoPoints.ToArray();
            var maxDistance = QSOPointUtil.CalculateRobustMaxDistance(cacheData);
            var densities = new double[1];
            if (ShowDestColor)
            {
                densities = QSOPointUtil.CalculateDensitiesKNN(cacheData, maxDistance, k: KValue,
                    distanceWeight: DistWeightValue, angleWeight: AngWeightValue);
            }

            _polarAxis = PlotControl.Plot.Add.PolarAxis(radius: maxDistance);
            _polarAxis.Rotation = Angle.FromDegrees(-90);
            _polarAxis.Clockwise = true;

            if (cacheData.Length == 0) return;
            var distLina = (double)((int)((maxDistance / 5) / 100) * 100);
            var circleDistance = new[] { distLina, distLina * 2, distLina * 3, distLina * 4, distLina * 5 };
            var labels = circleDistance.Select(x => x + "km").ToArray();
            // polarAxis.SetCircles(distLina,5);
            _polarAxis.SetCircles(circleDistance, labels);

            IColormap colormap = new ScottPlot.Colormaps.Turbo();
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
            PlotControl.Plot.Clear();
        }
        finally
        {
            _isExecutingChartUpdate = false;
            PlotControl.Refresh();
            PlotControl.Plot.Title("信号来源分布图");
        }
    }
}