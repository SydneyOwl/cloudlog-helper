using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using CloudlogHelper.ViewModels;
using CloudlogHelper.ViewModels.UserControls;
using CloudlogHelper.Views;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
using NLog;
using ReactiveUI;
using Notification = Avalonia.Controls.Notifications.Notification;

namespace CloudlogHelper.UserControls;

public partial class RIGDataGroupboxUserControl : ReactiveUserControl<RIGDataGroupboxViewModel>
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    private WindowNotificationManager? _manager;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var topLevel = TopLevel.GetTopLevel(this);
        _manager = new WindowNotificationManager(topLevel){ MaxItems = 3};
    }
    public RIGDataGroupboxUserControl()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
            ViewModel!.ShowAskForRetryMessageBox
                .RegisterHandler(DoShowAskRetryMessageboxAsync)
                .DisposeWith(disposables);
            ViewModel!.ShowNotification
                .RegisterHandler(DoShowNotificationAsync)
                .DisposeWith(disposables);
            ViewModel!.OpenSettingsWindow
                .RegisterHandler(DoOpenSettingsWindowAsync)
                .DisposeWith(disposables);
        });
    }
    
    
    private async Task DoShowNotificationAsync(IInteractionContext<(string, string, NotificationType), Unit> interaction)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _manager?.Show(new Notification(interaction.Input.Item1, interaction.Input.Item2, interaction.Input.Item3));
            interaction.SetOutput(Unit.Default);
        });
    }

    private async Task DoShowAskRetryMessageboxAsync(IInteractionContext<Unit, string> interaction)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is Window window)
                try
                {
                    var result = await MessageBoxManager.GetMessageBoxCustom(
                        new MessageBoxCustomParams
                        {
                            ButtonDefinitions = new List<ButtonDefinition>
                            {
                                new() { Name = "Retry", IsDefault = true },
                                new() { Name = "Open Settings" },
                                new() { Name = "Cancel" }
                            },
                            ContentTitle = "Warning",
                            ContentMessage = "Failed to communicate with rig!",
                            Icon = Icon.Warning,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            CanResize = false,
                            SizeToContent = SizeToContent.WidthAndHeight,
                            ShowInCenter = true
                        }).ShowWindowDialogAsync(window);

                    interaction.SetOutput(result);
                    return;
                }
                catch (Exception ex)
                {
                    ClassLogger.Warn(ex, "Error showing message box.");
                }

            // Console.WriteLine("Oops...");
            interaction.SetOutput("Cancel");
        });
    }

    private async Task DoOpenSettingsWindowAsync(IInteractionContext<Unit, Unit> interaction)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is Window window)
            await new SettingsWindow { DataContext = new SettingsWindowViewModel() }.ShowDialog(window);
        interaction.SetOutput(Unit.Default);
    }
}