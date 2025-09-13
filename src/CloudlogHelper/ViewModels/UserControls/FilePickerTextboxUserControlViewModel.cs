using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.UserControls;

public class FilePickerTextboxUserControlViewModel:ViewModelBase
{
    [Reactive] public string SelectedFilePath { get; set; }
}