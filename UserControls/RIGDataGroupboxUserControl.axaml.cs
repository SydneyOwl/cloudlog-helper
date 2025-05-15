using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using CloudlogHelper.ViewModels;
using CloudlogHelper.ViewModels.UserControls;
using CloudlogHelper.Views;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Models;
using ReactiveUI;

namespace CloudlogHelper.UserControls;

public partial class RIGDataGroupboxUserControl : ReactiveUserControl<RIGDataGroupboxViewModel>
{
    public RIGDataGroupboxUserControl()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
            ViewModel!.ShowAskForRetryMessageBox
                .RegisterHandler(DoShowAskRetryMessageboxAsync)
                .DisposeWith(disposables);
            ViewModel!.OpenSettingsWindow
                .RegisterHandler(DoOpenSettingsWindowAsync)
                .DisposeWith(disposables);
        });
    } 
    private async Task DoShowAskRetryMessageboxAsync(IInteractionContext<Unit, string> interaction)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is Window window)
            {
                try
                {
                    var result = await MessageBoxManager.GetMessageBoxCustom(
                        new MessageBoxCustomParams
                        {
                            ButtonDefinitions = new List<ButtonDefinition>
                            {
                                new ButtonDefinition { Name = "Retry", IsDefault = true },
                                new ButtonDefinition { Name = "Open Settings" },
                                new ButtonDefinition { Name = "Cancel" }
                            },
                            ContentTitle = "Warning",
                            ContentMessage = "Failed to communicate with rig!",
                            Icon = MsBox.Avalonia.Enums.Icon.Warning,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            CanResize = false,
                            SizeToContent = SizeToContent.WidthAndHeight,
                            ShowInCenter = true,
                        }).ShowWindowDialogAsync(window);
                
                    interaction.SetOutput(result);
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error showing message box: {ex.Message}");
                }
            }
        
            Console.WriteLine("Oops...");
            interaction.SetOutput("Cancel");
        });
    }

    private async Task DoOpenSettingsWindowAsync(IInteractionContext<Unit, Unit> interaction)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is Window window)
        {
            await new SettingsWindow() { DataContext = new SettingsWindowViewModel() }.ShowDialog(window);
        }
        interaction.SetOutput(Unit.Default);
    }
}