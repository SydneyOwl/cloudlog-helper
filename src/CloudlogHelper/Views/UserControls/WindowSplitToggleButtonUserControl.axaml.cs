using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels.UserControls;

namespace CloudlogHelper.Views.UserControls;

public partial class WindowSplitToggleButtonUserControl : ReactiveUserControl<WindowSplitToggleButtonUserControlViewModel>
{
    public WindowSplitToggleButtonUserControl()
    {
        InitializeComponent();
    }
}