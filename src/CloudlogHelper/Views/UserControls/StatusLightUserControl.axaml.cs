using ReactiveUI.Avalonia;
using CloudlogHelper.ViewModels.UserControls;

namespace CloudlogHelper.Views.UserControls;

public partial class StatusLightUserControl : ReactiveUserControl<StatusLightUserControlViewModel>
{
    public StatusLightUserControl()
    {
        InitializeComponent();
    }
}