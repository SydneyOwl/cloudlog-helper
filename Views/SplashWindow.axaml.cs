using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace CloudlogHelper.Views;

public partial class SplashWindow : Window
{
    private readonly Func<Task?> _mainAction;
    private readonly Func<Task?> _workLoad;

    public SplashWindow()
    {        
        if (!Design.IsDesignMode) throw new Exception("This should be called from designer only.");
        InitializeComponent();
    }

    public SplashWindow(Func<Task?> workload, Func<Task?> mainAction)
    {
        InitializeComponent();
        _workLoad = workload;
        _mainAction = mainAction;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        LoadAndInit();
    }

    // ReSharper disable once AsyncVoidMethod
    private async void LoadAndInit()
    {
        if (Design.IsDesignMode)return;
        await _workLoad?.Invoke()!; 
         _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await _mainAction?.Invoke()!;
            Close();
        });
    }
}