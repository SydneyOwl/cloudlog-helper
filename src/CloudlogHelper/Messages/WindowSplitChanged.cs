using CloudlogHelper.ViewModels;

namespace CloudlogHelper.Messages;

/// <summary>
///     Sent if window split is requested.
/// </summary>
public struct WindowSplitChanged
{
    public bool IsSplit { get; set; }
    public ViewModelBase? Sender { get; set; }
    public string? SenderSeq { get; set; }
}