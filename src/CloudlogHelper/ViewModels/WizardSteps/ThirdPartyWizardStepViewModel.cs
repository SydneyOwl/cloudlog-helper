using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CloudlogHelper.Models;
using CloudlogHelper.ViewModels.UserControls;

namespace CloudlogHelper.ViewModels.WizardSteps;

public sealed class ThirdPartyWizardStepViewModel : WizardStepViewModelBase
{
    public ThirdPartyWizardStepViewModel(
        ObservableCollection<LogSystemConfig> logSystems,
        Func<Exception, Task>? logSystemTestErrorHandler = null) : base(3)
    {
        LogSystems = logSystems;
        LogSystemCard = new LogSystemCardViewModel(LogSystems, logSystemTestErrorHandler);
    }

    public ObservableCollection<LogSystemConfig> LogSystems { get; }
    public LogSystemCardViewModel LogSystemCard { get; }
}
