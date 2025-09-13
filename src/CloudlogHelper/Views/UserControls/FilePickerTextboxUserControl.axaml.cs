using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels.UserControls;

namespace CloudlogHelper.Views.UserControls;

public partial class FilePickerTextboxUserControl : ReactiveUserControl<FilePickerTextboxUserControlViewModel>
{
    public FilePickerTextboxUserControl()
    {
        InitializeComponent();
    }

    private async void FilePickerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storageProvider is null) return;
            var file = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false
            });
            
            if (file.Count == 0)return;
            var pathAbsolutePath = file[0].Path.AbsolutePath;
            ViewModel!.SelectedFilePath = pathAbsolutePath;
        }
        catch (Exception ed)
        {
            // ignored...
        }
    }
}