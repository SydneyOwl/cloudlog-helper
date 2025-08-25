using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CloudlogHelper.Exceptions;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace CloudlogHelper.Views;

public partial class SplashWindow : Window
{
    private readonly Func<Window, Task?> _postExec;
    private readonly Func<Task?> _preCheck;
    private readonly Func<Task?> _workload;

    private int _ctrlPressCount;

    public SplashWindow()
    {
        if (!Design.IsDesignMode) throw new InvalidOperationException("This should be called from designer only.");
        InitializeComponent();
    }

    public SplashWindow(Func<Task?> preCheck, Func<Task?> workload, Func<Window, Task?> postExec)
    {
        InitializeComponent();
        _preCheck = preCheck;
        _workload = workload;
        _postExec = postExec;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        LoadAndInit();
    }

    // ReSharper disable once AsyncVoidMethod
    private async void LoadAndInit()
    {
        if (Design.IsDesignMode) return;
        try
        {
            statusText.Text = "Executing Pre-Check...";
            statusTextDetailed.Text = "Checking for dupe process";
            await _preCheck?.Invoke()!;

            statusText.Text = "Initialization in progress...";
            statusTextDetailed.Text = "Database / Log services initialization";
            var workloadTask = Task.Run(async () => { await _workload?.Invoke()!; });
            await workloadTask.ConfigureAwait(false);
             
            // wait for user to press ctrl...
            await Task.Delay(600);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                statusText.Text = "Loading Window...";
                statusTextDetailed.Text = "Executing post-exec...";
                await _postExec?.Invoke(this)!;
                Close();
            });
        }
        catch (DuplicateProcessException e)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                Topmost = false;
                statusTextDetailed.Text = "ERROR: " + e.Message;
                statusTextDetailed.Background = Brushes.Orange;
                statusTextDetailed.Foreground = Brushes.Black;
                await MessageBoxManager.GetMessageBoxStandard("Error",
                    TranslationHelper.GetString(LangKeys.dupeinstance), ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
            });
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                Topmost = false;
                statusTextDetailed.Text = "ERROR: " + ex.Message;
                statusTextDetailed.Background = Brushes.Orange;
                statusTextDetailed.Foreground = Brushes.Black;
                var wResult = await MessageBoxManager.GetMessageBoxStandard("Error",
                    TranslationHelper.GetString(LangKeys.bootfail), ButtonEnum.OkAbort,
                    MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
                if (wResult == ButtonResult.Ok) ApplicationStartUpUtil.ResetApplication();
            });
            Environment.Exit(0);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is not (Key.RightCtrl or Key.LeftCtrl)) return;
        if (++_ctrlPressCount >= 3)
        {
            _ctrlPressCount = int.MinValue;
            ApplicationStartUpUtil.ResetApplication();
        }
    }
}