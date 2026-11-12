using System.IO;
using Meshsmith.App.Services;
using Meshsmith.App.ViewModels;
using Meshsmith.Core.Pipeline;

namespace Meshsmith.App.Tests;

/// <summary>Regression cover for the stale-conversion race: a conversion that finishes after the
/// user has already loaded another file must not publish its result against that new file.</summary>
public class MainViewModelConcurrencyTests
{
    /// <summary>Holds its task open until the test completes the gate, so the conversion is
    /// genuinely in flight while the file is swapped underneath it.</summary>
    private sealed class GatedConversion : IConversionService
    {
        private readonly TaskCompletionSource<ConversionResult> _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? StartedFor { get; private set; }

        private IProgress<ConversionProgress>? _reporter;

        public Task<ConversionResult> ConvertAsync(string stlPath, ConversionSettings settings,
                                                   IProgress<ConversionProgress> progress,
                                                   CancellationToken cancellationToken)
        {
            StartedFor = stlPath;
            _reporter = progress;
            progress.Report(new ConversionProgress("reading", 0.1));
            return _gate.Task;
        }

        /// <summary>Replays a late progress report from the superseded conversion.</summary>
        public void Report(ConversionProgress progress) => _reporter!.Report(progress);

        public void Release(ConversionResult result) => _gate.SetResult(result);
    }

    private static async Task<(MainViewModel ViewModel, string Second)> LoadFirstAsync(
        string first, GatedConversion conversion)
    {
        var viewModel = new MainViewModel(new FakeFileDialogs(), conversion, new FakeMeshProbe());
        await viewModel.LoadFileAsync(first);
        return (viewModel, TempFiles.Stl());
    }

    [Fact]
    public async Task A_conversion_superseded_by_a_new_load_publishes_no_result()
    {
        var conversion = new GatedConversion();
        var first = TempFiles.Stl();
        var (viewModel, second) = await LoadFirstAsync(first, conversion);

        var running = viewModel.ConvertAsync();
        Assert.Equal(first, conversion.StartedFor);

        // The user picks a different model while cube_10-style work is still running.
        await viewModel.LoadFileAsync(second);
        conversion.Report(new ConversionProgress("building", 0.55));
        conversion.Release(Results.Sample(TempFiles.FacesJson(planes: 3, cylinders: 1)));
        await running;

        Assert.False(viewModel.HasResult);
        Assert.Equal(Path.GetFileName(second), viewModel.SourceName);
        Assert.Equal("—", viewModel.FacesText);
        Assert.Equal(0, viewModel.PlaneCount);
        Assert.Equal(0, viewModel.Progress);         // the stale progress report was dropped too
        Assert.Equal("READY", viewModel.StageText);
        Assert.False(viewModel.IsBusy);              // finally still releases the busy flag
        Assert.True(viewModel.CanConvert);
    }

    [Fact]
    public async Task A_superseded_conversion_does_not_raise_result_ready()
    {
        var conversion = new GatedConversion();
        var first = TempFiles.Stl();
        var (viewModel, second) = await LoadFirstAsync(first, conversion);

        int raised = 0;
        Meshsmith.Core.Pipeline.ConversionResult? published = null;
        viewModel.ResultReady += (_, result) => { raised++; published = result; };

        var running = viewModel.ConvertAsync();
        Assert.Equal(first, conversion.StartedFor);

        await viewModel.LoadFileAsync(second);
        conversion.Release(Results.Sample(TempFiles.FacesJson()));
        await running;

        Assert.Equal(0, raised);
        Assert.Null(published);
        Assert.False(viewModel.HasResult);
    }
}
