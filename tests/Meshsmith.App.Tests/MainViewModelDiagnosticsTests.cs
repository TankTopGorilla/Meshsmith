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
                                                                              FakeFileDialogs? dialogs = null)
    {
        var conversion = new FakeConversion
        {
            FailAtStage = "building",
            StageToReport = "building",
            FailureMessage = "The conversion worker stopped unexpectedly (exit code -1073741819)."
        };
        var viewModel = new MainViewModel(dialogs ?? new FakeFileDialogs(), conversion,
                                          new FakeMeshProbe(), uiContext: null, diagnostics);
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
