using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CloudlogHelper.Models;

namespace CloudlogHelper.ViewModels.UserControls;

public sealed class LogSystemCardViewModel : ViewModelBase
{
    public LogSystemCardViewModel(
        ObservableCollection<LogSystemConfig> logSystems,
        Func<Exception, Task>? testConnectionErrorHandler = null)
    {
        LogSystems = logSystems;
        TestConnectionErrorHandler = testConnectionErrorHandler;
    }

    public ObservableCollection<LogSystemConfig> LogSystems { get; }
    public Func<Exception, Task>? TestConnectionErrorHandler { get; }
}
