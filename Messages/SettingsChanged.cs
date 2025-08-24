using CloudlogHelper.Enums;

namespace CloudlogHelper.Messages;

/// <summary>
///     Sent when Settings window is opened or closed.
/// </summary>
public struct SettingsChanged
{
    /// <summary>
    ///     Part of the config changed.
    /// </summary>
    public ChangedPart Part { get; init; }
}