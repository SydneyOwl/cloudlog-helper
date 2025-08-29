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

public class MessageBoxManagerService : IMessageBoxManagerService, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private readonly IClassicDesktopStyleApplicationLifetime desktop;

    public MessageBoxManagerService(IClassicDesktopStyleApplicationLifetime topLevel)
    {
        desktop = topLevel;
    }

    public void Dispose()
    {
        // TODO release managed resources here
    }

    public async Task<string> DoShowCustomMessageboxDialogAsync(MessageBoxCustomParams cParams, Window? toplevel = null)
    {
        var result = string.Empty;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                if (desktop.MainWindow is null && toplevel is null) return;
                result = await MessageBoxManager.GetMessageBoxCustom(
                   cParams).ShowWindowDialogAsync(toplevel??desktop.MainWindow);
            }
            catch (Exception ex)
            {
                ClassLogger.Warn(ex, "Error showing message box.");
            }
        });
        return result;
    }
    
    public async Task<string> DoShowCustomMessageboxDialogAsync(List<ButtonDefinition> buttons, Icon iconType,
        string title, string message, Window? toplevel = null)
    {
        return await DoShowCustomMessageboxDialogAsync(new MessageBoxCustomParams
        {
            ButtonDefinitions = buttons,
            ContentTitle = title,
            ContentMessage = message,
            Icon = iconType,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInCenter = true
        }, toplevel);
    }

    public async Task<ButtonResult> DoShowStandardMessageboxDialogAsync(Icon iconType, ButtonEnum bType, string title, string message, Window? toplevel = null)
    {
        var result = ButtonResult.Abort;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                if (desktop.MainWindow is null && toplevel is null) return;
                result = await MessageBoxManager.GetMessageBoxStandard(title,
                    message, bType,
                    iconType).ShowWindowDialogAsync(toplevel??desktop.MainWindow!);
            }
            catch (Exception ex)
            {
                ClassLogger.Warn(ex, "Error showing message box.");
            }
        });
        return result;
    }
    
    public async Task<ButtonResult> DoShowStandardMessageboxAsync(Icon iconType, ButtonEnum bType, string title, string message)
    {
        return await MessageBoxManager.GetMessageBoxStandard(title,
            message, bType,
            iconType).ShowWindowAsync();
    }
}