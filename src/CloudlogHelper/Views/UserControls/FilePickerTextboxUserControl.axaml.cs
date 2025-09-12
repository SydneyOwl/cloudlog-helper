using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace CloudlogHelper.Views.UserControls;

public partial class FilePickerTextboxUserControl : UserControl
{
    public FilePickerTextboxUserControl()
    {
        InitializeComponent();
        DataContext = this;
    }
    
    public static readonly StyledProperty<string> SelectedFilePathProperty =
        AvaloniaProperty.Register<FilePickerTextboxUserControl, string>(nameof(SelectedFilePath));

    public string SelectedFilePath
    {
        get => GetValue(SelectedFilePathProperty);
        set => SetValue(SelectedFilePathProperty, value);
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
            SelectedFilePath = pathAbsolutePath;
            FilePathTextBox.Text = pathAbsolutePath;
        }
        catch (Exception ed)
        {
            // ignored...
        }
    }
}