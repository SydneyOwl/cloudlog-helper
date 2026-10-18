using Material.Icons;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels;

/// <summary>
///     One entry in the wizard sidebar. The window view model keeps this list in sync with the
///     current step so the sidebar is a single data-driven template instead of a hand-written
///     pair of controls per step.
/// </summary>
public class WizardStepItem : ReactiveObject
{
    public int Index { get; init; }

    public string Title { get; init; } = string.Empty;

    public MaterialIconKind IconKind { get; init; }

    [Reactive] public bool IsCurrent { get; set; }

    /// <summary>
    ///     True for steps the user has already passed - the sidebar shows a check mark for them.
    /// </summary>
    [Reactive] public bool IsVisited { get; set; }

    /// <summary>
    ///     Future steps cannot be jumped to from the sidebar; forward movement goes through the
    ///     Next button so its validation runs.
    /// </summary>
    [Reactive] public bool CanNavigate { get; set; }
}
