using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels;

namespace CloudlogHelper.Views;

public partial class AboutWindow : ReactiveWindow<AboutWindowViewModel>
{
    public AboutWindow()
    {
        InitializeComponent();
    }
}