using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.UserControls;

public class 
    ClosableErrorPanelUserControlViewModel : ViewModelBase
{
    private ObservableAsPropertyHelper<bool> _showErrorPanel;

    public ClosableErrorPanelUserControlViewModel()
    {
        CloseErrorPanelCommand = ReactiveCommand.Create(() => { ErrorMessage = string.Empty; });
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

    public ReactiveCommand<Unit, Unit> CloseErrorPanelCommand { get; }
}