using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;

namespace CloudlogHelper.Services.Interfaces;

public interface IMessageBoxManagerService
{
    Task<string> DoShowCustomMessageboxDialogAsync(MessageBoxCustomParams cParams, Window? toplevel = null);


    Task<string> DoShowCustomMessageboxDialogAsync(List<ButtonDefinition> buttons, Icon iconType,
        string title, string message, Window? toplevel = null);

    Task<ButtonResult> DoShowStandardMessageboxDialogAsync(Icon iconType, ButtonEnum bType, string title,
        string message, Window? toplevel = null);

    Task<ButtonResult> DoShowStandardMessageboxAsync(Icon iconType, ButtonEnum bType, string title, string message);
}