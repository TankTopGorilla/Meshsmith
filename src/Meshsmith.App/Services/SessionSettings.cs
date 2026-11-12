namespace Meshsmith.App.Services;

/// <summary>
/// What the window writes when it closes. The close handler owns exactly one thing - where the window
/// was - and must take everything else from the live preferences. It used to save a copy captured at
/// startup, which silently discarded every recent file opened during the session.
/// </summary>
public static class SessionSettings
{
    public static AppSettings ForClose(AppSettings preferences, double left, double top,
                                       double width, double height, bool maximized) =>
        preferences with
        {
            Window = WindowPlacement.Capture(left, top, width, height, maximized)
        };
}
