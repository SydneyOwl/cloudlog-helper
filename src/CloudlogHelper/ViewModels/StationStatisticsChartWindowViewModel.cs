using ReactiveUI.Fody.Helpers;
using ScottPlot;
using ScottPlot.Avalonia;

namespace CloudlogHelper.ViewModels;

public class StationStatisticsChartWindowViewModel : ViewModelBase
{
    [Reactive] public bool UpdatePaused { get; set; }
    public AvaPlot PlotControl { get; private set; }

    public StationStatisticsChartWindowViewModel()
    {
        PlotControl = new AvaPlot();
        PlotControl.Multiplot.AddPlots(1);
        _updateChart();
    }
    
    private void _updateChart()
    {
        var plot1 = PlotControl.Multiplot.GetPlot(0);
        
        string[] categories = { "A", "B", "C" };
        double[] values = { 10, 30, 50 };

        var barPlot = plot1.Add.Bars(values);
        foreach (var bar in barPlot.Bars)
        {
            bar.Label = bar.Value.ToString();
        }

        barPlot.ValueLabelStyle.Bold = true;
        barPlot.ValueLabelStyle.FontSize = 18;

        plot1.Axes.Margins(bottom: 0, top: .2);
    }
}