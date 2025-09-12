using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using CloudlogHelper.ViewModels;
using ReactiveUI;
using ScottPlot;

namespace CloudlogHelper.Views;

public partial class PolarChartWindow : ReactiveWindow<PolarChartWindowViewModel>
{
    public PolarChartWindow()
    {
        InitializeComponent();
        this.WhenActivated(disposable =>
        {
            ViewModel!.OpenSaveFilePickerInteraction.RegisterHandler(SaveFilePickerDialog).DisposeWith(disposable);
        });
    }
    
    private async Task SaveFilePickerDialog(IInteractionContext<Unit, IStorageFile?> interaction)
    {
        var file = await GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                SuggestedFileName = "Polar-Chart.png",
                Title = TranslationHelper.GetString(LangKeys.savelogto)
            });
        interaction.SetOutput(file);
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