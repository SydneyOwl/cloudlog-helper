using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels.UserControls;

namespace CloudlogHelper.Views.UserControls;

public partial class TestButtonUserControl : ReactiveUserControl<TestButtonUserControlViewModel>
{
    public TestButtonUserControl()
    {
        InitializeComponent();
    }
}