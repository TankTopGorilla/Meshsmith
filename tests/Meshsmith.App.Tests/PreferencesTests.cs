using Meshsmith.App.Services;
using Meshsmith.App.ViewModels;
using Xunit;

namespace Meshsmith.App.Tests;

public class PreferencesTests
{
    [Fact]
    public void Closing_keeps_the_live_preferences_and_only_replaces_the_window()
    {
        var preferences = AppSettings.Default with
        {
            Repair = false,
            Recent = new List<string> { "a.stl", "b.stl" }
        };

        var saved = SessionSettings.ForClose(preferences, 10, 20, 800, 600, maximized: false);

        Assert.False(saved.Repair);
        Assert.Equal(new[] { "a.stl", "b.stl" }, saved.Recent);
        Assert.Equal(10, saved.Window!.X);
        Assert.Equal(20, saved.Window.Y);
        Assert.Equal(800, saved.Window.W);
        Assert.Equal(600, saved.Window.H);
        Assert.False(saved.Window.Maximized);
    }

    [Fact]
    public void A_maximized_close_keeps_the_restore_bounds()
    {
        var saved = SessionSettings.ForClose(AppSettings.Default, 100, 50, 1200, 900, maximized: true);

        Assert.True(saved.Window!.Maximized);
        Assert.Equal(1200, saved.Window.W);
        Assert.Equal(900, saved.Window.H);
    }

    [Fact]
    public async Task The_live_preferences_carry_the_recent_list()
    {
        var viewModel = new MainViewModel(new FakeFileDialogs(), new FakeConversion(), new FakeMeshProbe());

        await viewModel.LoadFileAsync(TempFiles.Stl());

        Assert.Single(viewModel.Preferences.Recent);
        Assert.EndsWith(".stl", viewModel.Preferences.Recent[0]);
    }

    [Fact]
    public void An_accepted_settings_change_replaces_the_live_preferences()
    {
        var viewModel = new MainViewModel(new FakeFileDialogs(), new FakeConversion(), new FakeMeshProbe());

        viewModel.ApplySettings(AppSettings.Default with { Repair = false, DefaultOutputFolder = "C:/out" });

        Assert.False(viewModel.Preferences.Repair);
        Assert.Equal("C:/out", viewModel.Preferences.DefaultOutputFolder);
    }
}
