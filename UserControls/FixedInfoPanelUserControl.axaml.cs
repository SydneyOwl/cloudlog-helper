using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels.UserControls;

namespace CloudlogHelper.UserControls;

public partial class FixedInfoPanelUserControl : ReactiveUserControl<FixedInfoPanelViewModel>
{
    public FixedInfoPanelUserControl()
    {
        InitializeComponent();
    }
}