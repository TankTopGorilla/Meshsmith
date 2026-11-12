using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Meshsmith.App.Services;

namespace Meshsmith.App.Tests;

/// <summary>Every conversion gets a working directory. A successful one keeps it — Save copies the
/// STEP/IGES out of it — while a cancelled or failed one must leave nothing behind, and the startup
/// sweep must clear whatever an older crash left. Unbounded growth in %TEMP% is the bug.</summary>
public class TempCleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"meshsmith-test-{Guid.NewGuid():N}");

    public TempCleanupTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string[] Work => Directory.Exists(_root) ? Directory.GetDirectories(_root) : Array.Empty<string>();

    private sealed class InlineProgress : IProgress<ConversionProgress>
    {
        private readonly Action<ConversionProgress> _action;

        public InlineProgress(Action<ConversionProgress> action) => _action = action;

        public void Report(ConversionProgress value) => _action(value);
    }

    [Fact]
    public async Task A_failed_conversion_leaves_no_working_directory()
    {
        var corrupt = Path.Combine(Path.GetTempPath(), $"meshsmith-bad-{Guid.NewGuid():N}.stl");
        await File.WriteAllBytesAsync(corrupt, new byte[2048]);

        try
        {
            var service = new WorkerConversionService(workRoot: _root);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ConvertAsync(corrupt, ConversionSettings.Default, new InlineProgress(_ => { }),
                                     CancellationToken.None));

            Assert.Empty(Work);
        }
        finally
        {
            File.Delete(corrupt);
        }
    }

    [Fact]
    public async Task A_cancelled_conversion_leaves_no_working_directory()
    {
        // A pipe that never finishes sending the mesh keeps the worker busy without a big fixture.
        var stallName = "meshsmith-stall-" + Guid.NewGuid().ToString("N");
        await using var stall = new NamedPipeServerStream(stallName, PipeDirection.Out, 1,
                                                          PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var connected = stall.WaitForConnectionAsync();

        using var cancellation = new CancellationTokenSource();
        var service = new WorkerConversionService(workRoot: _root, killAfter: TimeSpan.FromMilliseconds(300));
        var running = service.ConvertAsync(@"\\.\pipe\" + stallName, ConversionSettings.Default,
                                           new InlineProgress(_ => { }), cancellation.Token);

        await connected.WaitAsync(TimeSpan.FromSeconds(20));
        await stall.WriteAsync(Encoding.ASCII.GetBytes("solid stall\n"));
        await stall.FlushAsync();

        // Prove the directory exists first, so the assertion after the cancel means deletion.
        var deadline = Stopwatch.StartNew();
        while (Work.Length == 0 && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(50);
        Assert.NotEmpty(Work);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);

        Assert.Empty(Work);
    }

    [Fact]
    public async Task A_successful_conversion_keeps_its_output()
    {
        var service = new WorkerConversionService(workRoot: _root);

        var result = await service.ConvertAsync(Path.Combine(RepoRoot.Path, "fixtures", "cube_10.stl"),
                                                ConversionSettings.Default, new InlineProgress(_ => { }),
                                                CancellationToken.None);

        Assert.True(File.Exists(result.StepPath));
        Assert.StartsWith(_root, result.StepPath, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Work);
    }

    [Fact]
    public void The_sweep_removes_only_directories_past_its_age_limit()
    {
        var stale = Directory.CreateDirectory(Path.Combine(_root, TempSweeper.Prefix + "stale"));
        var fresh = Directory.CreateDirectory(Path.Combine(_root, TempSweeper.Prefix + "fresh"));
        var foreign = Directory.CreateDirectory(Path.Combine(_root, "not-ours"));
        Directory.SetLastWriteTimeUtc(stale.FullName, DateTime.UtcNow.AddDays(-2));
        Directory.SetLastWriteTimeUtc(foreign.FullName, DateTime.UtcNow.AddDays(-2));

        int removed = TempSweeper.Sweep(TimeSpan.FromHours(24), _root);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(stale.FullName));
        Assert.True(Directory.Exists(fresh.FullName));
        Assert.True(Directory.Exists(foreign.FullName));
    }
}
