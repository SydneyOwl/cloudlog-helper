using System;
using System.Collections.Specialized;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ReactiveUI.Avalonia;
using CloudlogHelper.ViewModels.UserControls;
using ReactiveUI;

namespace CloudlogHelper.Views.UserControls;

public partial class UDPLogInfoGroupboxUserControl : ReactiveUserControl<UDPLogInfoGroupboxUserControlViewModel>
{
    public UDPLogInfoGroupboxUserControl()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
            // scroll listbox to end if collection changed.
            this.WhenAnyValue(x => x.ViewModel!.FilteredQsos.Count)
                .Where(count => count > 0)
                .Throttle(TimeSpan.FromMilliseconds(200))
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(args =>
                {
                    var boxCount = QsoBox.ItemCount;
                    if (boxCount > 0)
                    {
                        QsoBox.SelectedIndex = boxCount - 1;
                    }
                })
                .DisposeWith(disposables);
        });
    }
}