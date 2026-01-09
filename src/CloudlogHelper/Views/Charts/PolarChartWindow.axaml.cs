using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using CloudlogHelper.ViewModels.Charts;
using ReactiveUI;

namespace CloudlogHelper.Views.Charts;

public partial class PolarChartWindow : ReactiveWindow<PolarChartWindowViewModel>
{
    public PolarChartWindow()
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