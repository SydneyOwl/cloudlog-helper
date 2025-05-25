namespace CloudlogHelper.Messages;

public enum ChangedPart
{
    /// <summary>
    ///     Sent when setting window is closed and Cloudlog config changed.
    /// </summary>
    Cloudlog,

    /// <summary>
    ///     Sent when setting window is closed and Clublog config changed.
    /// </summary>
    Clublog,

    /// <summary>
    ///     Sent when setting window is closed and Hamlib config changed.
    /// </summary>
    Hamlib,

    /// <summary>
    ///     Sent when setting window is closed and UDP config changed.
    /// </summary>
    UDPServer,

    /// <summary>
    ///     Sent when setting window is opened.
    /// </summary>
    NothingJustOpened,

    /// <summary>
    ///     Sent when setting window is closed, even if nothing changed.
    /// </summary>
    NothingJustClosed
}

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