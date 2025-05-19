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
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();

    public AboutWindowViewModel()
    {
        OpenUriInteraction = new Interaction<string, Unit>();
        OpenRepo = ReactiveCommand.CreateFromTask(_openRepo);
    }

    public Interaction<string, Unit> OpenUriInteraction { get; }
    public ReactiveCommand<Unit, Unit> OpenRepo { get; set; }

    private async Task _openRepo()
    {
        try
        {
            await OpenUriInteraction.Handle(DefaultConfigs.RepoAddress);
        }
        catch (Exception e)
        {
            ClassLogger.Error(e.Message);
        }
    }
}