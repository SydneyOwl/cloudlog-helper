namespace CloudlogHelper.Enums;

public enum ProgramShutdownMode
{
    /// <summary>
    ///     Shutdown mode is not specified. This means user didn't click "remember choice".
    /// </summary>
    NotSpecified,

    /// <summary>
    ///     Minimize to tray.
    /// </summary>
    ToTray,

    /// <summary>
    ///     Close the application.
    /// </summary>
    Shutdown
}