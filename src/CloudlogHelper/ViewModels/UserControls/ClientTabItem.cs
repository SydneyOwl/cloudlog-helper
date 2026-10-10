using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace CloudlogHelper.ViewModels.UserControls;

/// <summary>
///     One tab in the client selector above the QSO list. <see cref="Key" /> is the full
///     client key matched against <see cref="CloudlogHelper.Models.RecordedCallsignDetail.ClientId" />.
/// </summary>
public class ClientTabItem : ReactiveObject
{
    public string Key { get; init; } = string.Empty;

    [Reactive] public string Label { get; set; } = string.Empty;

    /// <summary>
    ///     True while this client is on air.
    /// </summary>
    [Reactive] public bool Transmitting { get; set; }
}
