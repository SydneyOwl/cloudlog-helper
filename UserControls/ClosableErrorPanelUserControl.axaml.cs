using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels.UserControls;

namespace CloudlogHelper.UserControls;

public partial class ClosableErrorPanelUserControl : ReactiveUserControl<ClosableErrorPanelViewModel>
{
    public ClosableErrorPanelUserControl()
    {
        InitializeComponent();
    }
}