using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Meshsmith.App.Services;

namespace Meshsmith.App.Tests;

/// <summary>End-to-end against the real child process: the point of this service is that the
/// conversion runs somewhere that can die without taking the window with it, so the tests spawn the
/// real executable rather than stubbing the protocol.</summary>
public class WorkerConversionTests
{
    private sealed class InlineProgress : IProgress<ConversionProgress>
    {
        private readonly Action<ConversionProgress> _action;

        public InlineProgress(Action<ConversionProgress> action) => _action = action;

        public void Report(ConversionProgress value) => _action(value);
    }

    [Fact]
    public async Task A_conversion_runs_in_a_child_process_and_returns_its_result()
    {
        var stages = new List<string>();
        var progress = new InlineProgress(p => { lock (stages) stages.Add(p.Stage); });

        var result = await new WorkerConversionService().ConvertAsync(
            Path.Combine(RepoRoot.Path, "fixtures", "cube_10.stl"), ConversionSettings.Default, progress,
            CancellationToken.None);

        Assert.Equal(6, result.FaceCount);
        Assert.True(result.IsValidSolid);
        Assert.True(File.Exists(result.StepPath));
        Assert.True(File.Exists(result.FacesPath));
        Assert.Contains("reading", stages);
        Assert.Contains("building", stages);
    }

    [Fact]
    public async Task A_worker_that_cannot_start_is_reported_as_such()
    {
        // cmd.exe is not our worker: with these arguments it exits immediately.
        var bogus = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var service = new WorkerConversionService(bogus, connectWithin: TimeSpan.FromSeconds(10));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ConvertAsync(Path.Combine(RepoRoot.Path, "fixtures", "cube_10.stl"),
                                 ConversionSettings.Default, new InlineProgress(_ => { }),
                                 CancellationToken.None));

        Assert.Contains("conversion worker", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancelling_stops_the_worker_and_reports_cancellation()
    {
        var dense = DenseStl.Create(40_000);
        using var cancellation = new CancellationTokenSource();
        var elapsed = Stopwatch.StartNew();

        // Cancel the moment the worker says it is building: deterministic, no sleeping.
        var progress = new InlineProgress(p =>
        {
            if (p.Stage == "building" && !cancellation.IsCancellationRequested) cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WorkerConversionService().ConvertAsync(dense, ConversionSettings.Default, progress,
                                                      cancellation.Token));

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(20), $"took {elapsed.Elapsed}");
        File.Delete(dense);
    }

    [Fact]
    public async Task A_worker_that_cannot_observe_the_cancel_is_killed()
    {
        // A named pipe that never finishes sending the mesh: the worker blocks inside the STL read,
        // never reaches a checkpoint, and therefore never reads the cancel line. This is the case
        // the kill exists for - a kernel call that would otherwise run forever.
        var stallName = "meshsmith-stall-" + Guid.NewGuid().ToString("N");
        await using var stall = new NamedPipeServerStream(stallName, PipeDirection.Out, 1,
                                                          PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var connected = stall.WaitForConnectionAsync();

        using var cancellation = new CancellationTokenSource();
        var service = new WorkerConversionService(killAfter: TimeSpan.FromMilliseconds(400));
        var running = service.ConvertAsync(@"\\.\pipe\" + stallName, ConversionSettings.Default,
                                           new InlineProgress(_ => { }), cancellation.Token);

        await connected.WaitAsync(TimeSpan.FromSeconds(20));
        await stall.WriteAsync(Encoding.ASCII.GetBytes("solid stall\n"));
        await stall.FlushAsync();
        await Task.Delay(400);        // let the worker block in the read
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task A_worker_whose_parent_disappears_leaves_on_its_own()
    {
        var dense = DenseStl.Create(40_000);
        var pipeName = "meshsmith-orphan-" + Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Meshsmith.Worker.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--pipe");
        start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add("--input");
        start.ArgumentList.Add(dense);
        start.ArgumentList.Add("--work");
        start.ArgumentList.Add(Path.Combine(Path.GetTempPath(), $"meshsmith-{Guid.NewGuid():N}"));

        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                                             PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var worker = Process.Start(start) ?? throw new InvalidOperationException("no worker");

        try
        {
            await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(30));
            using var reader = new StreamReader(pipe, new UTF8Encoding(false));

            // Wait until the worker is inside the sew stage: past the cheap checkpoints, so the
            // only thing that can end it is the watchdog rather than a cooperative check.
            var sawSewing = false;
            while (!sawSewing)
            {
                var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(90));
                sawSewing = line is not null && line.Contains("\"sewing\"", StringComparison.Ordinal);
            }

            var abandoned = Stopwatch.StartNew();
            pipe.Dispose();   // exactly what a dead parent looks like to the child

            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(worker.HasExited);
            Assert.Equal(3, worker.ExitCode);   // cancelled, not an unhandled-exception code
            Assert.True(abandoned.Elapsed < TimeSpan.FromSeconds(5),
                        $"worker outlived its parent by {abandoned.Elapsed}");
        }
        finally
        {
            if (!worker.HasExited)
            {
                worker.Kill(entireProcessTree: true);
            }

            File.Delete(dense);
        }
    }

    [Fact]
    public void The_worker_arguments_carry_the_repair_verify_and_tolerance_switches()
    {
        var arguments = WorkerConversionService.BuildArguments(
            @"C:\in.stl", @"C:\work",
            new ConversionSettings(Repair: false, VerifyArtifacts: true, ToleranceOverride: 0.05),
            "pipe-1");

        Assert.Equal(new[]
        {
            "--pipe", "pipe-1",
            "--input", @"C:\in.stl",
            "--work", @"C:\work",
            "--repair", "false",
            "--verify", "true",
            "--tolerance", "0.05"
        }, arguments);

        // No override means no flag at all: the worker's own default is the automatic tolerance.
        var automatic = WorkerConversionService.BuildArguments(
            @"C:\in.stl", @"C:\work", ConversionSettings.Default, "pipe-2");

        Assert.Equal(new[]
        {
            "--pipe", "pipe-2",
            "--input", @"C:\in.stl",
            "--work", @"C:\work",
            "--repair", "true",
            "--verify", "true"
        }, automatic);
    }

    /// <summary>A tessellated cylinder big enough to keep the pipeline busy for several seconds.
    /// ASCII STL, so the test does not depend on a binary writer.</summary>
    private static class DenseStl
    {
        public static string Create(int triangles)
        {
            const int radial = 500;
            const double radius = 20, height = 30;
            int rings = Math.Max(2, triangles / (2 * radial));

            var path = Path.Combine(Path.GetTempPath(), $"meshsmith-dense-{Guid.NewGuid():N}.stl");
            using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
            writer.WriteLine("solid dense");

            for (int ring = 0; ring < rings; ring++)
            {
                double z0 = height * ring / rings, z1 = height * (ring + 1) / rings;
                for (int i = 0; i < radial; i++)
                {
                    double a0 = 2 * Math.PI * i / radial, a1 = 2 * Math.PI * (i + 1) / radial;
                    double x0 = radius * Math.Cos(a0), y0 = radius * Math.Sin(a0);
                    double x1 = radius * Math.Cos(a1), y1 = radius * Math.Sin(a1);

                    Write(writer, x0, y0, z0, x1, y1, z0, x1, y1, z1);
                    Write(writer, x0, y0, z0, x1, y1, z1, x0, y0, z1);
                }
            }

            writer.WriteLine("endsolid dense");
            return path;
        }

        private static void Write(TextWriter writer, double ax, double ay, double az,
                                  double bx, double by, double bz, double cx, double cy, double cz)
        {
            var (nx, ny, nz) = Normal(ax, ay, az, bx, by, bz, cx, cy, cz);
            writer.WriteLine($"facet normal {F(nx)} {F(ny)} {F(nz)}");
            writer.WriteLine("  outer loop");
            writer.WriteLine($"    vertex {F(ax)} {F(ay)} {F(az)}");
            writer.WriteLine($"    vertex {F(bx)} {F(by)} {F(bz)}");
            writer.WriteLine($"    vertex {F(cx)} {F(cy)} {F(cz)}");
            writer.WriteLine("  endloop");
            writer.WriteLine("endfacet");
        }

        private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

        private static (double X, double Y, double Z) Normal(double ax, double ay, double az,
                                                            double bx, double by, double bz,
                                                            double cx, double cy, double cz)
        {
            double ux = bx - ax, uy = by - ay, uz = bz - az;
            double vx = cx - ax, vy = cy - ay, vz = cz - az;
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            return length < 1e-12 ? (0, 0, 0) : (nx / length, ny / length, nz / length);
        }
    }
}
