using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels.UserControls;
using ReactiveUI;

namespace CloudlogHelper.UserControls;

public partial class UDPLogInfoGroupboxUserControl : ReactiveUserControl<UDPLogInfoGroupboxViewModel>
{
    public UDPLogInfoGroupboxUserControl()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
            // scroll listbox to end if collection changed.
            ViewModel!.FilteredQsos.CollectionChanged += (sender, args) =>
            {
                QsoBox.SelectedIndex = args.NewStartingIndex;
            };
            ViewModel!.ShowFilePickerDialog.RegisterHandler(ShowSaveFilePickerDialog).DisposeWith(disposables);
        });
    }
    
    private async Task ShowSaveFilePickerDialog(IInteractionContext<Unit, IStorageFile?> interaction)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Adif export",
            SuggestedFileName = $"exported-{DateTime.Now.ToString("yyyyMMdd-HHmmss")}.adi",
            DefaultExtension = "adi",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("adi") { Patterns = new[] { "*.adi" } },
            }
        });
        interaction.SetOutput(file);
    }
}