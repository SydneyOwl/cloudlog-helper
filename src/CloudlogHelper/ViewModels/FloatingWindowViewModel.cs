using Avalonia.Controls;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels;

public class FloatingWindowViewModel : ViewModelBase
{
    public FloatingWindowViewModel()
    {
    }

    public FloatingWindowViewModel(Control ctrl)
    {
        TargetControl = ctrl;
    }

    [Reactive] public Control? TargetControl { get; set; }
}