using System;
using System.Collections.Specialized;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using CloudlogHelper.ViewModels.UserControls;
using ReactiveUI;

namespace CloudlogHelper.Views.UserControls;

public partial class UDPLogInfoGroupboxUserControl : ReactiveUserControl<UDPLogInfoGroupboxUserControlViewModel>
{
    public UDPLogInfoGroupboxUserControl()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
            // scroll listbox to end if collection changed.
            Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                    h => ViewModel!.FilteredQsos.CollectionChanged += h,
                    h => ViewModel!.FilteredQsos.CollectionChanged -= h)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(args =>
                {
                    if (args.EventArgs.NewStartingIndex >= 0) // Safety check
                        QsoBox.SelectedIndex = args.EventArgs.NewStartingIndex;
                })
                .DisposeWith(disposables);

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
                new FilePickerFileType("adi") { Patterns = new[] { "*.adi" } }
            }
        });
        interaction.SetOutput(file);
    }
}