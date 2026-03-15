using System;
using System.Linq;
using System.IO;
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
using SkiaSharp;

namespace CloudlogHelper.ViewModels.Charts;

public class PolarChartWindowViewModel : ChartWindowViewModel
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private readonly BasicSettings _basicSettings;

    private readonly IChartDataCacheService _chartDataCacheService;
    private readonly IWindowManagerService _windowManagerService;

    private PolarAxis _polarAxis;
    private SKBitmap? _coastlineSourceBitmap;
    private Image? _coastlineOverlayImage;
    private string? _coastlineOverlayCacheKey;

    private const int CoastlineOverlaySize = 1100;
    private const double EarthRadiusKm = 6371.0;

    public PolarChartWindowViewModel()
    {
    }

    public PolarChartWindowViewModel(IChartDataCacheService chartDataCacheService,
        IApplicationSettingsService applicationSettingsService,
        IWindowManagerService windowManagerService)
    {
        _basicSettings = applicationSettingsService.GetCurrentSettings().BasicSettings;
        _chartDataCacheService = chartDataCacheService;
        _windowManagerService = windowManagerService;

        Application.Current!.ActualThemeVariantChanged += (sender, args) => { UpdatePolar(); };

        PlotControl = new AvaPlot();
        SaveChart = ReactiveCommand.CreateFromTask(async () =>
        {
            var a = await _windowManagerService.OpenFileSaverAsync( new FilePickerSaveOptions
            {
                SuggestedFileName = "Polar-Chart.png",
                Title = TranslationHelper.GetString(LangKeys.savelogto)
            }, _windowManagerService.GetToplevel(GetType()));
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
                .Throttle(TimeSpan.FromSeconds(DefaultConfigs.UpdateChartsThrottleSec))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => { UpdatePolar(); })
                .DisposeWith(disposable);

            this.WhenAnyValue(x => x.KValue,
                    x => x.AngWeightValue,
                    x => x.DistWeightValue,
                    x => x.ShowDestColor,
                    x => x.ShowSimpleMap,
                    x => x.FilterDupeCallsign,
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
    [Reactive] public bool ShowSimpleMap { get; set; } = false;

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
                    FilterDupeCallsign ? ChartQSOPoint.BandAndCallsignComparer : null,
                    point => point.Band == SelectedBand
                             // && point.Mode == SelectedMode 
                             && point.Client == SelectedClient && point.Distance >= 0)
                .ToArray();
            
            ClassLogger.Trace($"DUPE FILTER: {FilterDupeCallsign}, SAMPLES: {cacheData.Length}, REQUEST {QSOSamples}");

            var maxDistance = QSOPointUtil.CalculateRobustMaxDistance(cacheData) + 500;
            ClassLogger.Trace($"Use maxDistance:{maxDistance}");
            var densities = new double[1];
            if (ShowDestColor)
                densities = QSOPointUtil.CalculateDensitiesKNN(cacheData, maxDistance, KValue,
                    DistWeightValue, AngWeightValue);

            var axisRadius = maxDistance <= 1000 ? 1000 : maxDistance;
            _addPolarCoastlineOverlay(axisRadius);

            _polarAxis = PlotControl.Plot.Add.PolarAxis(axisRadius);
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

    private void _addPolarCoastlineOverlay(double axisRadiusKm)
    {
        if (!ShowSimpleMap) return;

        var stationLocation = MaidenheadGridUtil.GridToLatLng(_basicSettings.MyMaidenheadGrid);
        if (stationLocation is null) return;
        if (!TryLoadCoastlineSourceMap()) return;

        var isDarkTheme = Application.Current!.ActualThemeVariant == ThemeVariant.Dark;
        var radiusBucket = Math.Ceiling(axisRadiusKm / 250d) * 250d;
        var cacheKey = $"{stationLocation.Latitude:F2}|{stationLocation.Longitude:F2}|{radiusBucket:F0}|{(isDarkTheme ? "dark" : "light")}";

        if (_coastlineOverlayImage is null || _coastlineOverlayCacheKey != cacheKey)
        {
            _coastlineOverlayImage = _buildCoastlineOverlayImage(
                stationLocation.Latitude,
                stationLocation.Longitude,
                radiusBucket,
                isDarkTheme);
            _coastlineOverlayCacheKey = cacheKey;
        }

        if (_coastlineOverlayImage is null) return;

        var overlayRect = new CoordinateRect(-radiusBucket, radiusBucket, -radiusBucket, radiusBucket);
        PlotControl.Plot.Add.ImageRect(_coastlineOverlayImage, overlayRect);
    }

    private bool TryLoadCoastlineSourceMap()
    {
        if (_coastlineSourceBitmap is not null) return true;

        try
        {
            var resourceStream = ApplicationStartUpUtil.GetSingleResourceStream(DefaultConfigs.DefaultPolarCoastlineMapFile);
            if (resourceStream is null) return false;

            using var ms = new MemoryStream();
            resourceStream.CopyTo(ms);
            _coastlineSourceBitmap = SKBitmap.Decode(ms.ToArray());

            if (_coastlineSourceBitmap is null)
            {
                ClassLogger.Warn("Failed to decode coastline source map.");
                return false;
            }

            return true;
        }
        catch (Exception e)
        {
            ClassLogger.Error(e, "Failed to load coastline source map.");
            return false;
        }
    }

    private Image? _buildCoastlineOverlayImage(double centerLat, double centerLon, double radiusKm, bool isDarkTheme)
    {
        if (_coastlineSourceBitmap is null) return null;

        var lineBaseColor = isDarkTheme
            ? new SKColor(173, 216, 255)
            : new SKColor(50, 120, 170);

        using var overlayBitmap = new SKBitmap(CoastlineOverlaySize, CoastlineOverlaySize, SKColorType.Rgba8888, SKAlphaType.Premul);
        overlayBitmap.Erase(SKColors.Transparent);

        var alphaMap = new byte[CoastlineOverlaySize, CoastlineOverlaySize];
        var half = CoastlineOverlaySize / 2.0;

        for (var y = 0; y < CoastlineOverlaySize; y++)
        {
            var dy = (half - (y + 0.5)) / half;
            for (var x = 0; x < CoastlineOverlaySize; x++)
            {
                var dx = ((x + 0.5) - half) / half;
                var distanceKm = Math.Sqrt(dx * dx + dy * dy) * radiusKm;
                if (distanceKm > radiusKm) continue;

                var bearingDeg = (Math.Atan2(dx, dy) * 180.0 / Math.PI + 360.0) % 360.0;
                var dest = _calculateDestinationPoint(centerLat, centerLon, bearingDeg, distanceKm);
                var sourceAlpha = _sampleSourceMapAlpha(dest.lat, dest.lon);

                if (sourceAlpha < 18) continue;

                var lineAlpha = isDarkTheme
                    ? (byte)Math.Clamp((int)Math.Round(sourceAlpha * 0.80), 0, 140)
                    : (byte)Math.Clamp((int)Math.Round(sourceAlpha * 0.78), 0, 125);

                alphaMap[y, x] = lineAlpha;
            }
        }

        var smoothedAlpha = (byte[,])alphaMap.Clone();
        for (var y = 1; y < CoastlineOverlaySize - 1; y++)
        {
            for (var x = 1; x < CoastlineOverlaySize - 1; x++)
            {
                if (alphaMap[y, x] != 0) continue;

                var left = alphaMap[y, x - 1];
                var right = alphaMap[y, x + 1];
                if (left > 0 && right > 0)
                {
                    smoothedAlpha[y, x] = (byte)Math.Clamp((left + right) / 3, 0, 110);
                    continue;
                }

                var top = alphaMap[y - 1, x];
                var bottom = alphaMap[y + 1, x];
                if (top > 0 && bottom > 0)
                    smoothedAlpha[y, x] = (byte)Math.Clamp((top + bottom) / 3, 0, 110);
            }
        }

        for (var y = 0; y < CoastlineOverlaySize; y++)
        {
            for (var x = 0; x < CoastlineOverlaySize; x++)
            {
                var alpha = smoothedAlpha[y, x];
                if (alpha == 0) continue;
                overlayBitmap.SetPixel(x, y, lineBaseColor.WithAlpha(alpha));
            }
        }

        using var skImage = SKImage.FromBitmap(overlayBitmap);
        using var png = skImage.Encode(SKEncodedImageFormat.Png, 100);
        return png is null ? null : new Image(png.ToArray());
    }

    private byte _sampleSourceMapAlpha(double latitudeDeg, double longitudeDeg)
    {
        if (_coastlineSourceBitmap is null) return 0;

        var normalizedLon = ((longitudeDeg + 180d) % 360d + 360d) % 360d - 180d;
        var x = (normalizedLon + 180d) / 360d * (_coastlineSourceBitmap.Width - 1);
        var y = (90d - latitudeDeg) / 180d * (_coastlineSourceBitmap.Height - 1);

        var x0 = Math.Clamp((int)Math.Floor(x), 0, _coastlineSourceBitmap.Width - 1);
        var y0 = Math.Clamp((int)Math.Floor(y), 0, _coastlineSourceBitmap.Height - 1);
        var x1 = Math.Min(x0 + 1, _coastlineSourceBitmap.Width - 1);
        var y1 = Math.Min(y0 + 1, _coastlineSourceBitmap.Height - 1);

        var tx = x - x0;
        var ty = y - y0;

        var a00 = _coastlineSourceBitmap.GetPixel(x0, y0).Alpha;
        var a10 = _coastlineSourceBitmap.GetPixel(x1, y0).Alpha;
        var a01 = _coastlineSourceBitmap.GetPixel(x0, y1).Alpha;
        var a11 = _coastlineSourceBitmap.GetPixel(x1, y1).Alpha;

        var top = a00 + (a10 - a00) * tx;
        var bottom = a01 + (a11 - a01) * tx;
        var mixed = top + (bottom - top) * ty;
        return (byte)Math.Clamp((int)Math.Round(mixed), 0, 255);
    }

    private static (double lat, double lon) _calculateDestinationPoint(
        double startLatDeg,
        double startLonDeg,
        double bearingDeg,
        double distanceKm)
    {
        var lat1 = _degToRad(startLatDeg);
        var lon1 = _degToRad(startLonDeg);
        var bearing = _degToRad(bearingDeg);
        var angularDistance = distanceKm / EarthRadiusKm;

        var sinLat1 = Math.Sin(lat1);
        var cosLat1 = Math.Cos(lat1);
        var sinAngular = Math.Sin(angularDistance);
        var cosAngular = Math.Cos(angularDistance);

        var sinLat2 = sinLat1 * cosAngular + cosLat1 * sinAngular * Math.Cos(bearing);
        sinLat2 = Math.Clamp(sinLat2, -1d, 1d);
        var lat2 = Math.Asin(sinLat2);

        var y = Math.Sin(bearing) * sinAngular * cosLat1;
        var x = cosAngular - sinLat1 * Math.Sin(lat2);
        var lon2 = lon1 + Math.Atan2(y, x);
        lon2 = _normalizeLongitudeRad(lon2);

        return (_radToDeg(lat2), _radToDeg(lon2));
    }

    private static double _normalizeLongitudeRad(double lonRad)
    {
        const double twoPi = Math.PI * 2d;
        return ((lonRad + Math.PI) % twoPi + twoPi) % twoPi - Math.PI;
    }

    private static double _degToRad(double degree) => degree * Math.PI / 180d;

    private static double _radToDeg(double radian) => radian * 180d / Math.PI;

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
