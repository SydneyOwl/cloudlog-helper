using System.Reactive.Disposables;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.UserControls;

public class ErrorPanelUserControlViewModel : ViewModelBase
{
    private ObservableAsPropertyHelper<bool> _showErrorPanel;

    public ErrorPanelUserControlViewModel()
    {
        this.WhenActivated(disposables =>
        {
            _showErrorPanel = this.WhenAnyValue(x => x.ErrorMessage)
                .Select(msg => !string.IsNullOrEmpty(msg))
                .ToProperty(this, x => x.ShowErrorPanel)
                .DisposeWith(disposables);
        });
    }

    [Reactive] public string ErrorMessage { get; set; }
    public bool ShowErrorPanel => _showErrorPanel.Value;
}