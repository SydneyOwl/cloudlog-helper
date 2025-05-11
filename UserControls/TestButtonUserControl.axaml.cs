using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels.UserControls;

namespace CloudlogHelper.UserControls;

public partial class TestButtonUserControl : ReactiveUserControl<TestButtonViewModel>
{
    public TestButtonUserControl()
    {
        InitializeComponent();
    }
}