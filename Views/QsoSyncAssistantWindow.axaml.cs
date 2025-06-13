using System;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels;
using ReactiveUI;

namespace CloudlogHelper.Views;

public partial class QsoSyncAssistantWindow : ReactiveWindow<QsoSyncAssistantViewModel>
{
    public QsoSyncAssistantWindow()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
            Observable.FromEventPattern<EventHandler<WindowClosingEventArgs>, WindowClosingEventArgs>(
                    h => Closing += h,
                    h => Closing -= h)
                .Subscribe(args => 
                {
                    ViewModel!.SaveConf.Execute().Subscribe().DisposeWith(disposables);
                })
                .DisposeWith(disposables);

            Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                    h => ViewModel!.Settings.QsoSyncAssistantSettings.LocalLogPath!.CollectionChanged += h,
                    h => ViewModel!.Settings.QsoSyncAssistantSettings.LocalLogPath!.CollectionChanged -= h)
                .Subscribe(args => 
                {
                    localLogPath.SelectedIndex = args.EventArgs.NewStartingIndex;
                })
                .DisposeWith(disposables);

            ViewModel!.ShowFileSelectWindow.RegisterHandler(ShowFilePickerDialog).DisposeWith(disposables);

            this.WhenAnyValue(x => x.ViewModel!.CurrentInfo)
                .Subscribe(_ =>
                {
                    currentInfoTextBlock.ScrollToEnd();
                })
                .DisposeWith(disposables);
        });
    }
    
    private async Task ShowFilePickerDialog(IInteractionContext<Unit, IStorageFile[]> interaction)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;
        var file = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
        });
        interaction.SetOutput(file.ToArray());
    }
}