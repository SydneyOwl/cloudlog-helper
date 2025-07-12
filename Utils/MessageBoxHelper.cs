using System;
using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
using NLog;
using ReactiveUI;

namespace CloudlogHelper.Utils;

public class MessageBoxHelper
{
     private Window _topLevel;
     private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

     public MessageBoxHelper(Window topLevel)
     {
          _topLevel = topLevel;
     }
     
     public async Task<string> DoShowMessageboxAsync(List<ButtonDefinition>  buttons, Icon iconType,
          string title, string message)
     {
          var result = string.Empty;
          await Dispatcher.UIThread.InvokeAsync(async () =>
          {
               try
               {
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
                         }).ShowWindowDialogAsync(_topLevel);
               }
               catch (Exception ex)
               {
                    ClassLogger.Warn(ex, "Error showing message box.");
               }
          });
          return result;
     }

}