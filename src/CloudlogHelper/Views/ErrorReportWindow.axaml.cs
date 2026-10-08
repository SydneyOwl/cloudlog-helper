using System;
using System.IO;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ReactiveUI.Avalonia;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using CloudlogHelper.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace CloudlogHelper.Views;

// This is not mvvm :(
public partial class ErrorReportWindow : ReactiveWindow<ErrorReportWindowViewModel>
{
    private readonly string _errorMessage = "";

    public ErrorReportWindow()
    {
        InitializeComponent();
    }

    public ErrorReportWindow(string logPath)
    {
        try
        {
            _errorMessage = File.ReadAllText(logPath);
            File.Delete(logPath);
        }
        catch (Exception e)
        {
            Console.WriteLine(@"Unable to read crash log...");
            Environment.Exit(-1);
        }

        InitializeComponent();
        ErrBlock.Text = _errorMessage;
    }

    public void ExitWindowButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Environment.Exit(-1);
    }

    public async void LogSaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var ts = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
            var file = await GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    SuggestedFileName = "CrashLog-" + ts + ".log",
                    Title = TranslationHelper.GetString(LangKeys.SaveLogTo)
                });
            if (file is not null)
            {
                var openWriteStream = await file.OpenWriteAsync();
                var st = new StreamWriter(openWriteStream);
                await st.WriteAsync(_errorMessage);
                st.Close();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            await MessageBoxManager.GetMessageBoxStandard(TranslationHelper.GetString(LangKeys.Error),
                ex.Message, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(this);
        }
    }
}