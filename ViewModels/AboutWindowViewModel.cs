using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using CloudlogHelper.Resources;
using NLog;
using ReactiveUI;

namespace CloudlogHelper.ViewModels;

public class AboutWindowViewModel : ViewModelBase
{
    public Interaction<string, Unit> OpenUriInteraction { get; }
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    public ReactiveCommand<Unit,Unit> OpenRepo { get; set; }
    
    public AboutWindowViewModel()
    {
        OpenUriInteraction = new Interaction<string, Unit>();
        OpenRepo = ReactiveCommand.CreateFromTask(_openRepo);
    }

    private async Task _openRepo()
    {
        try
        {
            await OpenUriInteraction.Handle(DefaultConfigs.RepoAddress);
        }
        catch(Exception e)
        {
            ClassLogger.Error(e.Message);
        }
    }
}