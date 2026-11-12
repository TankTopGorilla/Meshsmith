using System.IO;
using Meshsmith.App.Services;
using Meshsmith.App.ViewModels;
using Meshsmith.Core.Pipeline;

namespace Meshsmith.App.Tests;

/// <summary>A load can overlap a conversion two ways: it supersedes one, or it fails while one is
/// still running. Neither finishing may report the other idle, because both the Cancel button and
/// Convert's enabled state read the same flag.</summary>
public class BusyOwnershipTests
{
    /// <summary>Stays in flight until the test releases it and ignores the token on purpose: this
    /// pins the view model's bookkeeping for the worst case (a kernel that takes a moment to
    /// unwind), not the kernel's cancellation.</summary>
    private sealed class GatedConversion : IConversionService
    {
        private readonly TaskCompletionSource<ConversionResult> _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate.TrySetCanceled();

        public async Task<ConversionResult> ConvertAsync(string stlPath, ConversionSettings settings,
                                                         IProgress<ConversionProgress> progress,
                                                         CancellationToken cancellationToken) =>
            await _gate.Task;
    }

    [Fact]
    public async Task A_failed_load_does_not_release_a_running_conversions_busy_state()
    {
        var conversion = new GatedConversion();
        var probe = new FakeMeshProbe();
        var viewModel = new MainViewModel(new FakeFileDialogs(), conversion, probe);
        await viewModel.LoadFileAsync(TempFiles.Stl());

        var running = viewModel.ConvertAsync();
        Assert.True(viewModel.IsBusy);

        probe.ShouldThrow = true;
        await viewModel.LoadFileAsync(TempFiles.Stl());     // refused: the old model stays loaded

        Assert.True(viewModel.HasError);
        Assert.True(viewModel.IsBusy);                      // the conversion still owns the shell
        Assert.True(viewModel.CanCancel);                   // so Cancel is still offered
        Assert.False(viewModel.CanConvert);                 // and a second run cannot start
        Assert.NotEqual("LOADING", viewModel.StageText);    // nor is the bar stuck on the failed load

        conversion.Release();
        await running;
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task A_superseded_run_keeps_the_shell_busy_until_it_unwinds()
    {
        var conversion = new GatedConversion();
        var viewModel = new MainViewModel(new FakeFileDialogs(), conversion, new FakeMeshProbe());
        await viewModel.LoadFileAsync(TempFiles.Stl());

        var running = viewModel.ConvertAsync();
        await viewModel.LoadFileAsync(TempFiles.Stl());     // supersedes, and cancels the old run

        Assert.True(viewModel.IsBusy);                      // it has not unwound yet
        Assert.False(viewModel.CanConvert);

        conversion.Release();
        await running;

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanConvert);
        Assert.False(viewModel.HasResult);
    }
}
