using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.WizardSteps;

public sealed class BasicWizardStepViewModel : WizardStepViewModelBase
{
    public BasicWizardStepViewModel(BasicSettings basicSettings) : base(1)
    {
        BasicSettings = basicSettings;
        EnableChartFeature = !BasicSettings.DisableAllCharts;

        this.WhenAnyValue(x => x.EnableChartFeature)
            .Subscribe(enabled => BasicSettings.DisableAllCharts = !enabled);
    }

    public BasicSettings BasicSettings { get; }

    [Reactive] public bool EnableChartFeature { get; set; }

    public override Task<WizardValidationResult> ValidateBeforeContinueAsync()
    {
        if (string.IsNullOrWhiteSpace(BasicSettings.MyMaidenheadGrid))
        {
            return Task.FromResult(WizardValidationResult.Success);
        }

        var cleanedGrid = BasicSettings.MyMaidenheadGrid.Trim().ToUpperInvariant();
        BasicSettings.MyMaidenheadGrid = cleanedGrid;
        if (MaidenheadGridUtil.CheckMaidenhead(cleanedGrid))
        {
            return Task.FromResult(WizardValidationResult.Success);
        }

        return Task.FromResult(WizardValidationResult.Failed(TranslationHelper.GetString(LangKeys.GridError)));
    }
}
