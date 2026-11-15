using System.IO;
using Meshsmith.App.Services;
using Meshsmith.App.ViewModels;
using Xunit;

namespace Meshsmith.App.Tests;

public class MainViewModelDiagnosticsTests
{
    private sealed class FakeDiagnostics : IDiagnosticService
    {
        public DiagnosticInputs? Last { get; private set; }
        public string? Return { get; set; }

        public string? Capture(DiagnosticInputs inputs)
        {
            Last = inputs;
            return Return;
        }
    }

    private static (MainViewModel ViewModel, FakeConversion Conversion) Failing(FakeDiagnostics diagnostics,
                                                                              FakeFileDialogs? dialogs = null,
                                                                              FakeClipboard? clipboard = null)
    {
        var conversion = new FakeConversion
        {
            FailAtStage = "building",
            StageToReport = "building",
            FailureMessage = "The conversion worker stopped unexpectedly (exit code -1073741819)."
        };
        var viewModel = new MainViewModel(dialogs ?? new FakeFileDialogs(), conversion,
                                          new FakeMeshProbe(), uiContext: null, diagnostics,
                                          clipboard: clipboard);
        return (viewModel, conversion);
    }

    [Fact]
    public async Task A_failed_conversion_captures_a_bundle()
    {
        var bundle = Path.Combine(Path.GetTempPath(), $"bundle-{Guid.NewGuid():N}.zip");
        File.WriteAllText(bundle, "zip");
        var diagnostics = new FakeDiagnostics { Return = bundle };
        var (viewModel, _) = Failing(diagnostics);

        try
        {
            await viewModel.LoadFileAsync(TempFiles.Stl());
            await viewModel.ConvertAsync();

            Assert.True(viewModel.HasError);
            Assert.True(viewModel.HasDiagnostic);
            Assert.Equal(bundle, viewModel.DiagnosticPath);

            Assert.NotNull(diagnostics.Last);
            Assert.Contains("exit code -1073741819", diagnostics.Last!.FailureText);
            Assert.Equal("building", diagnostics.Last.Stage);
            Assert.Equal("CONVERSION FAILED", diagnostics.Last.FailureTitle);
            Assert.EndsWith(".stl", diagnostics.Last.SourcePath);
        }
        finally
        {
            File.Delete(bundle);
        }
    }

    [Fact]
    public async Task Saving_the_bundle_copies_it_where_the_user_asked()
    {
        var bundle = Path.Combine(Path.GetTempPath(), $"bundle-{Guid.NewGuid():N}.zip");
        var target = Path.Combine(Path.GetTempPath(), $"bundle-copy-{Guid.NewGuid():N}.zip");
        File.WriteAllText(bundle, "zip");
        var diagnostics = new FakeDiagnostics { Return = bundle };
        var dialogs = new FakeFileDialogs { SavePath = target };
        var (viewModel, _) = Failing(diagnostics, dialogs);

        try
        {
            await viewModel.LoadFileAsync(TempFiles.Stl());
            await viewModel.ConvertAsync();

            viewModel.SaveDiagnosticBundleCommand.Execute(null);

            Assert.True(File.Exists(target));
            Assert.Equal($"Saved {Path.GetFileName(target)}", viewModel.StatusRight);
        }
        finally
        {
            File.Delete(bundle);
            if (File.Exists(target)) File.Delete(target);
        }
    }

    [Fact]
    public async Task Copied_diagnostics_carry_the_version_a_timestamp_the_failure_and_a_path_that_exists()
    {
        var bundle = Path.Combine(Path.GetTempPath(), $"bundle-{Guid.NewGuid():N}.zip");
        File.WriteAllText(bundle, "zip");
        var clipboard = new FakeClipboard();
        var (viewModel, _) = Failing(new FakeDiagnostics { Return = bundle }, clipboard: clipboard);

        try
        {
            await viewModel.LoadFileAsync(TempFiles.Stl());
            await viewModel.ConvertAsync();

            viewModel.CopyDiagnosticsCommand.Execute(null);

            var text = Assert.IsType<string>(clipboard.Last);
            Assert.Contains("Meshsmith 0.1.5", text);
            Assert.Matches("[0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2}", text);
            Assert.Contains("CONVERSION FAILED", text);
            Assert.Contains("exit code -1073741819", text);
            Assert.Contains(bundle, text);
            Assert.True(File.Exists(bundle), "the path in the block must be a file that exists");
        }
        finally
        {
            File.Delete(bundle);
        }
    }

    [Fact]
    public async Task Copying_diagnostics_opens_no_dialog_and_confirms_in_the_status_bar()
    {
        var bundle = Path.Combine(Path.GetTempPath(), $"bundle-{Guid.NewGuid():N}.zip");
        File.WriteAllText(bundle, "zip");
        var dialogs = new FakeFileDialogs { SavePath = Path.Combine(Path.GetTempPath(), "never.zip") };
        var clipboard = new FakeClipboard();
        var (viewModel, _) = Failing(new FakeDiagnostics { Return = bundle }, dialogs, clipboard);

        try
        {
            await viewModel.LoadFileAsync(TempFiles.Stl());
            await viewModel.ConvertAsync();

            viewModel.CopyDiagnosticsCommand.Execute(null);

            Assert.Equal(0, dialogs.PickSaveCalls);          // no dialog: one command, one clipboard
            Assert.Equal("Diagnostics copied", viewModel.StatusRight);
            Assert.Single(clipboard.Copied);
        }
        finally
        {
            File.Delete(bundle);
        }
    }

    [Fact]
    public async Task The_diagnostics_block_names_the_log_when_no_bundle_was_captured()
    {
        Meshsmith.App.App.Log("p6 copy diagnostics");        // the live log exists, a bundle does not
        var clipboard = new FakeClipboard();
        var (viewModel, _) = Failing(new FakeDiagnostics { Return = null }, clipboard: clipboard);

        await viewModel.LoadFileAsync(TempFiles.Stl());
        await viewModel.ConvertAsync();

        Assert.False(viewModel.HasDiagnostic);
        Assert.True(viewModel.CopyDiagnosticsCommand.CanExecute(null));

        viewModel.CopyDiagnosticsCommand.Execute(null);

        var text = Assert.IsType<string>(clipboard.Last);
        Assert.Contains("log: ", text);
        Assert.Contains(Meshsmith.App.App.LogFilePath, text);   // a path the user can actually open
    }

    [Fact]
    public async Task A_new_conversion_clears_the_previous_bundle()
    {
        var bundle = Path.Combine(Path.GetTempPath(), $"bundle-{Guid.NewGuid():N}.zip");
        File.WriteAllText(bundle, "zip");
        var diagnostics = new FakeDiagnostics { Return = bundle };
        var (viewModel, conversion) = Failing(diagnostics);

        try
        {
            await viewModel.LoadFileAsync(TempFiles.Stl());
            await viewModel.ConvertAsync();
            Assert.True(viewModel.HasDiagnostic);

            conversion.FailAtStage = null;
            conversion.Result = Results.Sample(TempFiles.FacesJson());
            await viewModel.ConvertAsync();

            Assert.False(viewModel.HasDiagnostic);
        }
        finally
        {
            File.Delete(bundle);
        }
    }
}
