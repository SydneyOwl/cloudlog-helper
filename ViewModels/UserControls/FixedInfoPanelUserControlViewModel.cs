using System.Reactive.Disposables;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.UserControls;

public class FixedInfoPanelUserControlViewModel : ViewModelBase
{
    private ObservableAsPropertyHelper<bool> _showFixedInfoPanel;

    public FixedInfoPanelUserControlViewModel()
    {
        this.WhenActivated(disposables =>
        {
            _showFixedInfoPanel = this.WhenAnyValue(x => x.InfoMessage)
                .Select(msg => !string.IsNullOrEmpty(msg))
                .ToProperty(this, x => x.ShowFixedInfoPanel)
                .DisposeWith(disposables);
        });
    }

    [Reactive] public string InfoMessage { get; set; }
    public bool ShowFixedInfoPanel => _showFixedInfoPanel.Value;
}