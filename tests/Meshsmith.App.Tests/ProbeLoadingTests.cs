using System.IO;
using System.Threading;
using Meshsmith.App.Services;
using Meshsmith.App.ViewModels;

namespace Meshsmith.App.Tests;

/// <summary>Probing a large STL must not freeze the shell, and a probe that finishes after the user
/// picked another file must not win.</summary>
public class ProbeLoadingTests
{
    /// <summary>First call blocks until released; later calls return immediately.</summary>
    private sealed class GatedProbe : IMeshProbe
    {
        private int _calls;

        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Gate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MeshInfo Describe(string stlPath)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Started.TrySetResult(true);
                Gate.Task.GetAwaiter().GetResult();
            }
            return new MeshInfo(111, 1, 1, 1);
        }
    }

    [Fact]
    public async Task Loading_reports_busy_while_the_probe_runs()
    {
        var probe = new GatedProbe();
        var viewModel = new MainViewModel(new FakeFileDialogs(), new FakeConversion(), probe);

        var loading = viewModel.LoadFileAsync(TempFiles.Stl());
        await probe.Started.Task;

        Assert.True(viewModel.IsBusy);
        Assert.Equal("LOADING", viewModel.StageText);
        Assert.False(viewModel.CanConvert);

        probe.Gate.SetResult(true);
        await loading;

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.HasSource);
        Assert.Equal("111", viewModel.TrianglesText);
    }

    [Fact]
    public async Task A_slow_probe_does_not_overwrite_a_newer_selection()
    {
        var probe = new GatedProbe();
        var viewModel = new MainViewModel(new FakeFileDialogs(), new FakeConversion(), probe);
        var first = TempFiles.Stl();
        var second = TempFiles.Stl();

        var loadingFirst = viewModel.LoadFileAsync(first);
        await probe.Started.Task;

        await viewModel.LoadFileAsync(second);
        Assert.Equal(Path.GetFileName(second), viewModel.SourceName);

        probe.Gate.SetResult(true);
        await loadingFirst;

        Assert.Equal(Path.GetFileName(second), viewModel.SourceName);
    }
}
