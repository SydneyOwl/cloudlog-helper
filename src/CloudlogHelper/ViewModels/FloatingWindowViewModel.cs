using Avalonia.Controls;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels;
public class FloatingWindowViewModel : ViewModelBase
{
    [Reactive] public Control? TargetControl { get; set; }
    
    public FloatingWindowViewModel(){}

    public FloatingWindowViewModel(Control ctrl)
    {
        TargetControl = ctrl;
    }
}