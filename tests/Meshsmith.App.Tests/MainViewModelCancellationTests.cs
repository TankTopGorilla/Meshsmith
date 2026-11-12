using System.IO;
using Meshsmith.App.Services;
using Meshsmith.App.ViewModels;
using Meshsmith.Core.Pipeline;

namespace Meshsmith.App.Tests;

/// <summary>Cancel must stop the run, leave no result behind, and must not be mistaken for a failure.</summary>
public class MainViewModelCancellationTests
{
    /// <summary>Stays in flight until the token fires, so the test owns the moment of cancellation.</summary>
    private sealed class BlockingConversion : IConversionService
    {
        private readonly TaskCompletionSource<ConversionResult> _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken Token { get; private set; }

        public async Task<ConversionResult> ConvertAsync(string stlPath, ConversionSettings settings,
                                                         IProgress<ConversionProgress> progress,
                                                         CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            using var registration = cancellationToken.Register(() => _gate.TrySetCanceled(cancellationToken));
            return await _gate.Task;
        }
    }

    private static async Task<MainViewModel> LoadedAsync(BlockingConversion conversion)
    {
        var viewModel = new MainViewModel(new FakeFileDialogs(), conversion, new FakeMeshProbe());
        await viewModel.LoadFileAsync(TempFiles.Stl());
        return viewModel;
    }

    [Fact]
    public async Task Cancelling_a_running_conversion_returns_to_idle_without_an_error()
    {
        var conversion = new BlockingConversion();
        var viewModel = await LoadedAsync(conversion);

        var running = viewModel.ConvertAsync();
        Assert.True(viewModel.IsBusy);
        Assert.True(viewModel.CanCancel);

        viewModel.Cancel();
        await running;

        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasResult);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.CanCancel);
        Assert.Equal("Conversion cancelled", viewModel.StatusRight);
        Assert.All(viewModel.Stages, stage => Assert.Equal(StageState.Pending, stage.State));
        Assert.Equal("READY", viewModel.StageText);
    }

    [Fact]
    public void Cancel_is_unavailable_when_nothing_is_running()
    {
        var viewModel = new MainViewModel(new FakeFileDialogs(), new BlockingConversion(), new FakeMeshProbe());

        Assert.False(viewModel.CanCancel);
    }

    [Fact]
    public async Task Loading_another_file_cancels_the_running_conversion()
    {
        var conversion = new BlockingConversion();
        var viewModel = await LoadedAsync(conversion);
        var second = TempFiles.Stl();

        var running = viewModel.ConvertAsync();
        await viewModel.LoadFileAsync(second);

        Assert.True(conversion.Token.IsCancellationRequested);

        await running;

        Assert.False(viewModel.HasResult);
        Assert.Equal(Path.GetFileName(second), viewModel.SourceName);
    }
}
