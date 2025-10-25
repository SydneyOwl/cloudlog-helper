namespace CloudlogHelper.Enums;

public enum ChangedPart
{
    /// <summary>
    ///     Sent when setting window is closed and Cloudlog config changed.
    /// </summary>
    Cloudlog,

    /// <summary>
    ///     Sent when setting window is closed and Hamlib config changed.
    /// </summary>
    Hamlib,

    /// <summary>
    ///     Sent when setting window is closed and FLRig config changed.
    /// </summary>
    FLRig,

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