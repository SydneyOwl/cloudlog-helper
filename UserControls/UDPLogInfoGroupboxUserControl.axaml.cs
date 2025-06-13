using System;
using System.Collections.Specialized;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using CloudlogHelper.ViewModels.UserControls;
using ReactiveUI;
using Notification = Avalonia.Controls.Notifications.Notification;

namespace CloudlogHelper.UserControls;

public partial class UDPLogInfoGroupboxUserControl : ReactiveUserControl<UDPLogInfoGroupboxViewModel>
{
    private WindowNotificationManager? _manager;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var topLevel = TopLevel.GetTopLevel(this);
        _manager = new WindowNotificationManager(topLevel){ MaxItems = 3};
    }
    public UDPLogInfoGroupboxUserControl()
    {
        InitializeComponent();
        this.WhenActivated(disposables =>
        {
            // scroll listbox to end if collection changed.
            Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                    h => ViewModel!.FilteredQsos.CollectionChanged += h,
                    h => ViewModel!.FilteredQsos.CollectionChanged -= h)
                .Subscribe(args =>
                {
                    if (args.EventArgs.NewStartingIndex >= 0) // Safety check
                    {
                        QsoBox.SelectedIndex = args.EventArgs.NewStartingIndex;
                    }
                })
                .DisposeWith(disposables);

            ViewModel!.ShowFilePickerDialog.RegisterHandler(ShowSaveFilePickerDialog).DisposeWith(disposables);
            ViewModel!.ShowNotification.RegisterHandler(DoShowNotificationAsync).DisposeWith(disposables);
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
    private async Task DoShowNotificationAsync(IInteractionContext<(string, string, NotificationType), Unit> interaction)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _manager?.Show(new Notification(interaction.Input.Item1, interaction.Input.Item2, interaction.Input.Item3));
            interaction.SetOutput(Unit.Default);
        });
    }
}