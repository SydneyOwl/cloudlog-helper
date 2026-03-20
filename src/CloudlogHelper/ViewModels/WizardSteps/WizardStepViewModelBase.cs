using System.Threading.Tasks;
using CloudlogHelper.ViewModels;

namespace CloudlogHelper.ViewModels.WizardSteps;

public readonly record struct WizardValidationResult(bool IsValid, string ErrorMessage)
{
    public static WizardValidationResult Success => new(true, string.Empty);

    public static WizardValidationResult Failed(string errorMessage)
    {
        return new WizardValidationResult(false, errorMessage);
    }
}

public abstract class WizardStepViewModelBase : ViewModelBase
{
    protected WizardStepViewModelBase(int stepIndex)
    {
        StepIndex = stepIndex;
    }

    public int StepIndex { get; }

    public virtual Task<WizardValidationResult> ValidateBeforeContinueAsync()
    {
        return Task.FromResult(WizardValidationResult.Success);
    }

    public virtual Task<WizardValidationResult> ValidateBeforeFinishAsync()
    {
        return Task.FromResult(WizardValidationResult.Success);
    }
}
