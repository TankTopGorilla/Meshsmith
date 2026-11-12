using System.IO;
using Meshsmith.App.Services;
using Meshsmith.App.ViewModels;

namespace Meshsmith.App.Tests;

/// <summary>A Save that does nothing is indistinguishable from a Save that failed: every path must
/// leave a sentence in the status bar, and none of them may masquerade as a conversion failure.</summary>
public class ExportFeedbackTests
{
    private static async Task<MainViewModel> ConvertedAsync(FakeFileDialogs dialogs, string pipelinePath)
    {
        var conversion = new FakeConversion { Result = Results.Sample(pipelinePath) };
        var viewModel = new MainViewModel(dialogs, conversion, new FakeMeshProbe());
        await viewModel.LoadFileAsync(TempFiles.Stl());
        await viewModel.ConvertAsync();
        return viewModel;
    }

    [Fact]
    public async Task A_successful_save_says_which_file_was_written()
    {
        var target = Path.Combine(Path.GetTempPath(), $"meshsmith-export-{Guid.NewGuid():N}.step");
        var dialogs = new FakeFileDialogs { SavePath = target };
        var viewModel = await ConvertedAsync(dialogs, TempFiles.FacesJson());

        viewModel.SaveStepCommand.Execute(null);

        Assert.True(File.Exists(target));
        Assert.Equal($"Saved {Path.GetFileName(target)}", viewModel.StatusRight);
        Assert.False(viewModel.HasError);

        File.Delete(target);
    }

    [Fact]
    public async Task A_vanished_pipeline_output_is_reported_instead_of_doing_nothing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"gone-{Guid.NewGuid():N}.step");
        var dialogs = new FakeFileDialogs { SavePath = Path.Combine(Path.GetTempPath(), "unused.step") };
        var viewModel = await ConvertedAsync(dialogs, missing);

        viewModel.SaveStepCommand.Execute(null);

        Assert.StartsWith("Export unavailable", viewModel.StatusRight);
        Assert.Empty(dialogs.LastFilter ?? "");
    }

    [Fact]
    public async Task A_failed_copy_is_reported_as_an_export_problem()
    {
        var unwritable = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "part.step");
        var dialogs = new FakeFileDialogs { SavePath = unwritable };
        var viewModel = await ConvertedAsync(dialogs, TempFiles.FacesJson());

        viewModel.SaveStepCommand.Execute(null);

        Assert.StartsWith("Export failed:", viewModel.StatusRight);
        Assert.False(viewModel.HasError);
    }
}
