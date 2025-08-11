using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels.UserControls;

namespace CloudlogHelper.Views.UserControls;

public partial class ClosableErrorPanelUserControl : ReactiveUserControl<ClosableErrorPanelUserControlViewModel>
{
    public ClosableErrorPanelUserControl()
    {
        InitializeComponent();
    }
}