using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CloudlogHelper.Services.Interfaces;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
using NLog;

namespace CloudlogHelper.Services;

public class MessageBoxManagerService:IMessageBoxManagerService, IDisposable
{
    private IClassicDesktopStyleApplicationLifetime desktop;
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public MessageBoxManagerService(IClassicDesktopStyleApplicationLifetime topLevel)
    {
        desktop = topLevel;
    }
     
    public async Task<string> DoShowMessageboxAsync(List<ButtonDefinition>  buttons, Icon iconType,
        string title, string message)
    {
        var result = string.Empty;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                if (desktop.MainWindow is null)return;
                result = await MessageBoxManager.GetMessageBoxCustom(
                    new MessageBoxCustomParams
                    {
                        ButtonDefinitions = buttons,
                        ContentTitle = title,
                        ContentMessage = message,
                        Icon = iconType,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        CanResize = false,
                        SizeToContent = SizeToContent.WidthAndHeight,
                        ShowInCenter = true
                    }).ShowWindowDialogAsync(desktop.MainWindow);
            }
            catch (Exception ex)
            {
                ClassLogger.Warn(ex, "Error showing message box.");
            }
        });
        return result;
    }

    public void Dispose()
    {
        // TODO release managed resources here
    }
}