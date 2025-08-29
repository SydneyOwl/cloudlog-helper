using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels.UserControls;

namespace CloudlogHelper.Views.UserControls;

public partial class ErrorPanelUserControl : ReactiveUserControl<ErrorPanelUserControlViewModel>
{
    public ErrorPanelUserControl()
    {
        InitializeComponent();
    }
}