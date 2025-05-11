using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels.UserControls;

namespace CloudlogHelper.UserControls;

public partial class ErrorPanelUserControl : ReactiveUserControl<ErrorPanelViewModel>
{
    public ErrorPanelUserControl()
    {
        InitializeComponent();
    }
}