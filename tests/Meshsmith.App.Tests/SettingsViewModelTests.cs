using Meshsmith.App.Services;
using Meshsmith.App.ViewModels;

namespace Meshsmith.App.Tests;

/// <summary>The dialog edits a copy of the three fields it owns. Cancel is free because nothing is
/// written until the caller takes <c>Accept()</c>, and the fields it never shows must survive.</summary>
public class SettingsViewModelTests
{
    [Fact]
    public void The_dialog_starts_from_the_saved_values()
    {
        var original = AppSettings.Default with
        {
            Repair = false,
            VerifyArtifacts = false,
            DefaultOutputFolder = @"C:\out"
        };

        var viewModel = new SettingsViewModel(original, new FakeFileDialogs());

        Assert.False(viewModel.Repair);
        Assert.False(viewModel.VerifyArtifacts);
        Assert.Equal(@"C:\out", viewModel.DefaultOutputFolder);
    }

    [Fact]
    public void Edits_are_not_applied_until_accept_is_called()
    {
        var original = AppSettings.Default;
        var viewModel = new SettingsViewModel(original, new FakeFileDialogs());

        viewModel.Repair = false;
        viewModel.VerifyArtifacts = false;

        // The record the session holds is untouched: that is what makes Cancel an undo.
        Assert.True(original.Repair);
        Assert.True(original.VerifyArtifacts);

        var accepted = viewModel.Accept();

        Assert.False(accepted.Repair);
        Assert.False(accepted.VerifyArtifacts);
    }

    [Fact]
    public void Accept_keeps_the_recent_list_and_the_window_rectangle()
    {
        var window = new WindowPlacement(10, 20, 1440, 900, false);
        var original = AppSettings.Default with
        {
            Recent = new List<string> { @"C:\a.stl" },
            Window = window
        };
        var viewModel = new SettingsViewModel(original, new FakeFileDialogs());

        viewModel.DefaultOutputFolder = @"C:\out";
        var accepted = viewModel.Accept();

        Assert.Equal(new[] { @"C:\a.stl" }, accepted.Recent);
        Assert.Equal(window, accepted.Window);
        Assert.Equal(@"C:\out", accepted.DefaultOutputFolder);
    }

    [Fact]
    public void Restore_defaults_resets_the_form_but_keeps_the_recent_list()
    {
        var original = AppSettings.Default with
        {
            Repair = false,
            VerifyArtifacts = false,
            DefaultOutputFolder = @"C:\out",
            Recent = new List<string> { @"C:\a.stl" }
        };
        var viewModel = new SettingsViewModel(original, new FakeFileDialogs());

        viewModel.RestoreDefaultsCommand.Execute(null);

        Assert.True(viewModel.Repair);
        Assert.True(viewModel.VerifyArtifacts);
        Assert.Equal("", viewModel.DefaultOutputFolder);
        Assert.Equal(new[] { @"C:\a.stl" }, viewModel.Accept().Recent);
    }

    [Fact]
    public void A_blank_output_folder_means_no_default()
    {
        var dialogs = new FakeFileDialogs();
        var viewModel = new SettingsViewModel(AppSettings.Default with { DefaultOutputFolder = @"C:\out" },
                                              dialogs);

        viewModel.DefaultOutputFolder = "   ";
        Assert.Null(viewModel.Accept().DefaultOutputFolder);

        // Browse is the dialog's other way in: a blank field is handed over as "no suggestion".
        viewModel.DefaultOutputFolder = "";
        viewModel.BrowseCommand.Execute(null);
        Assert.Null(dialogs.LastInitialDirectory);

        dialogs.FolderPath = @"D:\parts";
        viewModel.BrowseCommand.Execute(null);
        Assert.Equal(@"D:\parts", viewModel.DefaultOutputFolder);
    }
}
