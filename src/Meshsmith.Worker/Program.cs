using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Meshsmith.Core.Ipc;
using Meshsmith.Core.Pipeline;

namespace Meshsmith.Worker;

/// <summary>The child half of the conversion: a console process that owns nothing but the pipeline.
/// It owns nothing but the pipeline, so an OCCT crash, hang or stack overflow dies with this
/// process instead of taking the window down.
///
/// The protocol rides a named pipe rather than stdout on purpose: the OCCT exporters print their
/// own chatter to stdout, and a shared stream is one interleaved write away from a lost result.
/// The parent cancels by writing <c>cancel</c> down the same pipe; a parent that dies breaks the
/// pipe, which is read here as EOF and treated identically.</summary>
internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitFailed = 2;
    private const int ExitCancelled = 3;
    private const int ExitUsage = 4;

    private static readonly object OutputGate = new();

    private static int Main(string[] args)
    {
        // Before any Occt type is JITted: the native payload lives in occt/x64 beside this exe.
        Meshsmith.Core.NativeLoader.Configure();

        string? pipeName = null;
        string? input = null;
        string? work = null;
        double? tolerance = null;

        // Absent flag means "as it always was": an older app driving a newer worker must not
        // silently lose repair.
        bool repair = true;
        bool verify = true;

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--pipe": pipeName = args[++i]; break;
                case "--input": input = args[++i]; break;
                case "--work": work = args[++i]; break;
                case "--repair": repair = ReadSwitch(args[++i], fallback: true); break;
                case "--verify": verify = ReadSwitch(args[++i], fallback: true); break;
                case "--tolerance":
                    if (double.TryParse(args[++i], System.Globalization.CultureInfo.InvariantCulture,
                                        out var value)) tolerance = value;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(pipeName) || string.IsNullOrWhiteSpace(input)) return ExitUsage;

        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            pipe.Connect(10_000);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return ExitUsage;   // the parent never opened its end
        }

        var reader = new StreamReader(pipe, new UTF8Encoding(false));
        var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };

        // A beat is the difference between "slow" and "stuck" on the other end of the pipe: the
        // parent shows the child's CPU share while a stage is running and says so when the beats stop.
        var self = Process.GetCurrentProcess();
        var lastBeatAt = DateTime.UtcNow;
        var lastCpu = self.TotalProcessorTime;
        using var beat = new Timer(_ =>
        {
            try
            {
                self.Refresh();
                var now = DateTime.UtcNow;
                var cpu = self.TotalProcessorTime;
                double wall = (now - lastBeatAt).TotalMilliseconds;
                double used = (cpu - lastCpu).TotalMilliseconds;
                lastBeatAt = now;
                lastCpu = cpu;

                double percent = wall > 1 ? Math.Clamp(used / wall * 100, 0, 999) : 0;
                Emit(writer, new WorkerMessage { Type = WorkerMessage.Beat, Cpu = Math.Round(percent) });
            }
            catch (Exception)
            {
                // A missing beat costs visibility, never the conversion.
            }
        }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        using var cancellation = new CancellationTokenSource();
        var control = new Thread(() => Watch(reader, cancellation, () =>
        {
            // The parent is gone, so nobody will ever read our result: give the pipeline a moment
            // to unwind and then leave. Without this, a worker parked in an OCCT call keeps
            // burning CPU for a window that no longer exists.
            _ = Task.Delay(2_000).ContinueWith(_ => Environment.Exit(ExitCancelled),
                                               TaskScheduler.Default);
        }))
        {
            IsBackground = true,
            Name = "worker-control"
        };
        control.Start();

        try
        {
            var result = ConversionPipeline.Run(input, new ConversionOptions
            {
                WorkingDirectory = work,
                ToleranceOverride = tolerance,
                RepairMesh = repair,
                VerifyArtifacts = verify,
                KeepTemporaryFiles = true,
                Progress = (stage, fraction) => Emit(writer, new WorkerMessage
                {
                    Type = WorkerMessage.Progress,
                    Stage = stage,
                    Fraction = fraction
                })
            }, cancellation.Token);

            beat.Dispose();   // no beat may follow the result line
            Emit(writer, new WorkerMessage { Type = WorkerMessage.Result, ResultBody = result });
            return ExitOk;
        }
        catch (OperationCanceledException)
        {
            Emit(writer, new WorkerMessage { Type = WorkerMessage.Cancelled });
            return ExitCancelled;
        }
        catch (Exception ex)
        {
            // One line, one message: the parent turns this into the error card.
            Emit(writer, new WorkerMessage { Type = WorkerMessage.Error, Message = ex.Message });
            return ExitFailed;
        }
        finally
        {
            // Both streams sit on the same pipe; disposing one the parent already closed throws
            // "pipe is broken", which would turn an orderly exit into a crash dump.
            Quiet(reader);
            Quiet(writer);
        }
    }

    /// <summary>A malformed switch is not worth a usage error: the pipeline's default is the safe
    /// answer, and the parent only ever sends true/false.</summary>
    private static bool ReadSwitch(string text, bool fallback) =>
        bool.TryParse(text, out var value) ? value : fallback;

    private static void Quiet(IDisposable disposable)
    {
        try
        {
            disposable.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The other end is gone; nothing to flush to.
        }
    }

    /// <summary>Reads control lines until the pipe closes. EOF means the parent is gone, so the
    /// conversion is abandoned rather than left running against a dead UI.</summary>
    private static void Watch(StreamReader reader, CancellationTokenSource cancellation, Action parentGone)
    {
        try
        {
            while (reader.ReadLine() is { } line)
            {
                if (line.Trim().Equals("cancel", StringComparison.OrdinalIgnoreCase))
                {
                    cancellation.Cancel();
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The pipe broke; fall through to the same conclusion as EOF.
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The conversion finished first and disposed the source.
        }

        // Reached only when the pipe ended rather than when a cancel line arrived: the parent died.
        parentGone();
    }

    private static void Emit(StreamWriter writer, WorkerMessage message)
    {
        var line = WorkerLine.Write(message);
        lock (OutputGate)
        {
            try
            {
                writer.WriteLine(line);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The parent stopped listening; the next checkpoint abandons the conversion anyway.
            }
        }
    }
}
