using System;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using CloudlogHelper.ViewModels;
using ReactiveUI;

namespace CloudlogHelper.Views;

public partial class QsoSyncAssistantWindow : ReactiveWindow<QsoSyncAssistantWindowViewModel>
{
    public QsoSyncAssistantWindow()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
            Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                    h => ViewModel!.Settings.QsoSyncAssistantSettings.LocalLogPath!.CollectionChanged += h,
                    h => ViewModel!.Settings.QsoSyncAssistantSettings.LocalLogPath!.CollectionChanged -= h)
                .Subscribe(args => { localLogPath.SelectedIndex = args.EventArgs.NewStartingIndex; })
                .DisposeWith(disposables);
            
            this.WhenAnyValue(x => x.ViewModel!.CurrentInfo)
                .Subscribe(_ => { Dispatcher.UIThread.Invoke(() => currentInfoTextBlock.ScrollToEnd()); })
                .DisposeWith(disposables);
        });
    }
}