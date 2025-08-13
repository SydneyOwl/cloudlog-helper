using System.Collections.Generic;
using System.Threading.Tasks;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;

namespace CloudlogHelper.Services.Interfaces;

public interface IMessageBoxManagerService
{
    Task<string> DoShowMessageboxAsync(List<ButtonDefinition> buttons, Icon iconType,
        string title, string message);
}