using System.Windows;

namespace Meshsmith.App.Services;

/// <summary>The clipboard, behind an interface so a test can read back exactly what was copied.
/// WPF's <see cref="Clipboard"/> needs an STA thread; the command that uses this runs on the UI
/// thread, which is one.</summary>
public interface IClipboard
{
    void SetText(string text);
}

public sealed class WpfClipboard : IClipboard
{
    public void SetText(string text) => Clipboard.SetText(text);
}
