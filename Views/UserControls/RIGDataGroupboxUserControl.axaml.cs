using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels;
using CloudlogHelper.ViewModels.UserControls;
using NLog;
using ReactiveUI;

namespace CloudlogHelper.Views.UserControls;

public partial class RIGDataGroupboxUserControl : ReactiveUserControl<RIGDataGroupboxUserControlViewModel>
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public RIGDataGroupboxUserControl()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
            ViewModel!.OpenSettingsWindow
                .RegisterHandler(DoOpenSettingsWindowAsync)
                .DisposeWith(disposables);
        });
    }

    private async Task DoOpenSettingsWindowAsync(IInteractionContext<Unit, Unit> interaction)
    {
        if (App.WindowTracker.TryGetWindow(typeof(SettingsWindow), out var target))
        {
            target!.Show();
            target.Activate();
            interaction.SetOutput(Unit.Default);
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is Window window)
        {
            var wd = new SettingsWindow { DataContext = new SettingsWindowViewModel() };
            App.WindowTracker.Track(wd);
            await wd.ShowDialog(window);
        }

        interaction.SetOutput(Unit.Default);
    }
}