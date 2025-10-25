using System;
using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels.Charts;

namespace CloudlogHelper.Views.Charts;

public partial class StationStatisticsChartWindow : ReactiveWindow<StationStatisticsChartWindowViewModel>
{
    public StationStatisticsChartWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        plotControl.Content = null;
        if (ViewModel is null) return;
        ViewModel.UpdatePaused = true;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (ViewModel is null) return;
        ViewModel.UpdatePaused = false;
    }
}