using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using CloudlogHelper.Messages;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.UserControls;

public class WindowSplitToggleButtonUserControlViewModel : ViewModelBase
{
    private bool _isSplited;
    
    [Reactive] public bool IsSplit { get; set; }
    [Reactive] public string? WindowSeq { get; set; }

    public WindowSplitToggleButtonUserControlViewModel()
    {
        if (!Design.IsDesignMode) throw new InvalidOperationException("This should be called from designer only.");
    }

    public WindowSplitToggleButtonUserControlViewModel(ViewModelBase parentViewModel)
    {
        this.WhenActivated(disposable =>
        {
            this.WhenAnyValue(x => x.IsSplit)
                .Throttle(TimeSpan.FromMilliseconds(300))
                .Subscribe(isSplit =>
                {
                    if (isSplit == _isSplited) return;
                    MessageBus.Current.SendMessage(new WindowSplitChanged
                    {
                        IsSplit = isSplit,
                        Sender = parentViewModel,
                        SenderSeq = WindowSeq
                    });
                    _isSplited = isSplit;
                })
                .DisposeWith(disposable);
        });
    }
}