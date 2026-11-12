using System.IO;

namespace Meshsmith.App.Tests;

/// <summary>The Cancel button is invisible until something runs, so a render cannot prove it is
/// wired. This asserts the two bindings that make it appear and stop the run.</summary>
public class StatusBarWiringTests
{
    [Fact]
    public void The_status_bar_offers_cancel_while_busy()
    {
        var xaml = File.ReadAllText(
            Path.Combine(RepoRoot.Path, "src", "Meshsmith.App", "Views", "StatusBarView.xaml"));

        Assert.Contains("Command=\"{Binding CancelCommand}\"", xaml);
        Assert.Contains("Visibility=\"{Binding IsBusy", xaml);
    }
}
