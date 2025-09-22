using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels;

namespace CloudlogHelper.Views;

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
        if (ViewModel is null)return;
        ViewModel.UpdatePaused = true;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (ViewModel is null)return;
        ViewModel.UpdatePaused = false;
    }
}