using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels;
using ReactiveUI;

namespace CloudlogHelper.Views;

public partial class AboutWindow : ReactiveWindow<AboutWindowViewModel>
{
    public AboutWindow()
    {
        InitializeComponent();
        this.WhenActivated(disposable =>
        {
            ViewModel!.OpenUriInteraction.RegisterHandler(OpenBrowser).DisposeWith(disposable);
        });
    }

    private async Task OpenBrowser(IInteractionContext<string, Unit> interaction)
    {
        var launcher = GetTopLevel(this)?.Launcher;
        if (launcher == null) return;
        await launcher.LaunchUriAsync(new Uri(interaction.Input));
    }
}