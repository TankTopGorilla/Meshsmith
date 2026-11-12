using System.IO;
using System.Text.RegularExpressions;

namespace Meshsmith.App.Tests;

/// <summary>A dialog and a close handler cannot be exercised headless, so these are string and XAML
/// assertions in the same idiom as the status-bar and accessibility tests: they pin that the wiring
/// exists and is named, and the visual check is the real proof.</summary>
public class SettingsWiringTests
{
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot.Path, "src", "Meshsmith.App" }.Concat(parts).ToArray()));

    [Fact]
    public void The_top_bar_offers_settings_re_run_and_a_recent_list()
    {
        var xaml = Read("Views", "TopBar.xaml");

        Assert.Contains("ReRunCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("RecentFiles", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenRecentCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("SettingsCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_applies_the_saved_placement_and_saves_it_on_close()
    {
        var window = Read("MainWindow.xaml.cs");

        Assert.Contains("ApplyPlacement", window, StringComparison.Ordinal);
        Assert.Contains("OnClosing", window, StringComparison.Ordinal);
        Assert.Contains("RestoreBounds", window, StringComparison.Ordinal);
        // The close handler owns only the geometry; everything else must come from the live
        // preferences, not the copy taken at startup (that bug ate the session's recent files).
        Assert.Contains("SessionSettings.ForClose(ViewModel.Preferences", window, StringComparison.Ordinal);

        // The one place a human window is created must hand it a store and the saved settings.
        var app = Read("App.xaml.cs");
        Assert.Contains("SettingsStore", app, StringComparison.Ordinal);
        Assert.Contains("store.Load()", app, StringComparison.Ordinal);
    }

    [Fact]
    public void The_settings_dialog_names_its_controls()
    {
        var xaml = Read("Views", "SettingsDialog.xaml");

        Assert.Contains("Repair", xaml, StringComparison.Ordinal);
        Assert.Contains("VerifyArtifacts", xaml, StringComparison.Ordinal);
        Assert.Contains("DefaultOutputFolder", xaml, StringComparison.Ordinal);
        Assert.Contains("BrowseCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("RestoreDefaultsCommand", xaml, StringComparison.Ordinal);

        int names = Regex.Matches(xaml, @"AutomationProperties\.Name=""[^""]+""").Count;
        Assert.True(names >= 6, $"expected at least 6 accessible names in the settings dialog, found {names}");
    }
}
