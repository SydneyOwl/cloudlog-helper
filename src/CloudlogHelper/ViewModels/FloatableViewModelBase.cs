using CloudlogHelper.ViewModels.UserControls;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels;

/// <summary>
///     For floatable windows
/// </summary>
public class FloatableViewModelBase : ViewModelBase
{
    public FloatableViewModelBase()
    {
        SplitUserControlViewModel = new WindowSplitToggleButtonUserControlViewModel(this);
    }

    [Reactive] public WindowSplitToggleButtonUserControlViewModel? SplitUserControlViewModel { get; set; }
}