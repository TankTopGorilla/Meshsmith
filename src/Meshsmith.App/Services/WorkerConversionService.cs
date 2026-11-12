using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Meshsmith.Core.Ipc;
using Meshsmith.Core.Pipeline;

namespace Meshsmith.App.Services;

/// <summary>Runs the conversion in a child process: an OCCT crash, hang or stack overflow then kills
/// the worker instead of the window. Cancellation is a <c>cancel</c> line down the pipe plus a kill
/// after a short grace period, because the one thing a cooperative token cannot do is interrupt a
/// call that never returns.</summary>
public sealed class WorkerConversionService : IConversionService
{
    private readonly string _workerPath;
    private readonly string _workRoot;
    private readonly TimeSpan _killAfter;
    private readonly TimeSpan _connectWithin;

    /// <param name="workerPath">Defaults to <c>Meshsmith.Worker.exe</c> beside this app. The worker
    /// is a console build of the same Core, so it starts in ~0.3 s instead of paying WPF's startup,
    /// and it shares the app's <c>occt</c> folder rather than shipping a second copy.</param>
    /// <param name="killAfter">How long a cancelled worker may take to unwind before it is killed.</param>
    /// <param name="connectWithin">How long a freshly started worker may take to open the pipe.</param>
    /// <param name="workRoot">Where the per-run working directory is created. Defaults to the
    /// system temp directory; tests point it somewhere they can inspect.</param>
    public WorkerConversionService(string? workerPath = null, TimeSpan? killAfter = null,
                                   TimeSpan? connectWithin = null, string? workRoot = null)
    {
        _workerPath = workerPath ?? Path.Combine(AppContext.BaseDirectory, "Meshsmith.Worker.exe");
        _workRoot = workRoot ?? Path.GetTempPath();
        _killAfter = killAfter ?? TimeSpan.FromSeconds(2);
        _connectWithin = connectWithin ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>Every flag the child understands, in the order it expects them. Pure so the
    /// switches can be asserted without starting a process.</summary>
    public static IReadOnlyList<string> BuildArguments(string stlPath, string work,
                                                       ConversionSettings settings, string pipeName)
    {
        var arguments = new List<string>
        {
            "--pipe", pipeName,
            "--input", stlPath,
            "--work", work,
            "--repair", settings.Repair ? "true" : "false",
            "--verify", settings.VerifyArtifacts ? "true" : "false"
        };

        if (settings.ToleranceOverride is { } tolerance)
        {
            arguments.Add("--tolerance");
            arguments.Add(tolerance.ToString("R", CultureInfo.InvariantCulture));
        }

        return arguments;
    }

    public async Task<ConversionResult> ConvertAsync(string stlPath, ConversionSettings settings,
                                                     IProgress<ConversionProgress> progress,
                                                     CancellationToken cancellationToken)
    {
        var pipeName = "meshsmith-" + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                                                         PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        var start = new ProcessStartInfo(_workerPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,   // OCCT's own chatter, kept for the failure message
            RedirectStandardError = true
        };

        // The work directory is chosen here, so the argument order is fixed in one place and the
        // test can assert the exact strings the child will parse.
        var work = Path.Combine(_workRoot, $"meshsmith-{Guid.NewGuid():N}");
        foreach (var argument in BuildArguments(stlPath, work, settings, pipeName))
            start.ArgumentList.Add(argument);

        using var worker = Process.Start(start)
            ?? throw new InvalidOperationException("could not start the conversion worker");

        var noise = new StringBuilder();
        var drain = Task.Run(() =>
        {
            Drain(worker.StandardOutput, noise);
            Drain(worker.StandardError, noise);
        });

        // A worker that dies at startup must say so immediately, not after the connect timeout.
        var connecting = pipe.WaitForConnectionAsync();
        var exitedEarly = worker.WaitForExitAsync();
        var winner = await Task.WhenAny(connecting, exitedEarly, Task.Delay(_connectWithin))
                               .ConfigureAwait(false);

        if (winner != connecting)
        {
            try
            {
                worker.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Already gone.
            }

            await Task.WhenAny(drain, Task.Delay(TimeSpan.FromMilliseconds(250))).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);

            if (winner == exitedEarly)
            {
                throw new InvalidOperationException(
                    $"The conversion worker stopped before it opened its end (exit code {ExitCode(worker)}).{Tail(noise)}");
            }

            throw new InvalidOperationException("the conversion worker did not start");
        }

        var reader = new StreamReader(pipe, new UTF8Encoding(false));
        var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };

        var outcome = new TaskCompletionSource<ConversionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = Task.Run(() => PumpMessages(reader, progress, outcome));
        using var registration = cancellationToken.Register(() => Stop(worker, writer));

        // A successful run keeps its directory: Save copies the STEP/IGES out of it. A cancelled or
        // failed one has nothing worth keeping, so it is removed on the way out.
        bool keepOutput = false;

        try
        {
            await Task.WhenAny(outcome.Task, worker.WaitForExitAsync()).ConfigureAwait(false);

            // The last line can still be in flight when the process exits.
            await Task.WhenAny(reading, Task.Delay(TimeSpan.FromMilliseconds(750))).ConfigureAwait(false);

            if (!outcome.Task.IsCompleted)
            {
                if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);

                throw new InvalidOperationException(
                    $"The conversion worker stopped unexpectedly (exit code {ExitCode(worker)}).{Tail(noise)}");
            }

            var conversion = await outcome.Task.ConfigureAwait(false);
            keepOutput = true;
            return conversion;
        }
        finally
        {
            if (!worker.HasExited)
            {
                try
                {
                    worker.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // It exited between the check and the kill.
                }
            }

            // Give the drains a moment so the message above is complete.
            await Task.WhenAny(drain, Task.Delay(TimeSpan.FromMilliseconds(250))).ConfigureAwait(false);

            Quiet(reader);
            Quiet(writer);

            if (!keepOutput) TryDelete(work);
        }
    }

    /// <summary>Removes a working directory the run did not keep. Deleting can lose a race with the
    /// worker's own shutdown, so it retries briefly instead of leaving the directory behind.</summary>
    private static void TryDelete(string directory)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }

    /// <summary>Disposing a writer over a pipe the worker has already closed throws "pipe is
    /// broken". That is not a reason to fail a conversion that already returned its result.</summary>
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

    /// <summary>Ask politely, then insist: a worker stuck inside an OCCT call never reads the line.</summary>
    private void Stop(Process worker, StreamWriter writer)
    {
        try
        {
            writer.WriteLine("cancel");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // Already gone; the wait below settles it.
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await worker.WaitForExitAsync().WaitAsync(_killAfter).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or ObjectDisposedException)
            {
                try
                {
                    worker.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // Nothing left to kill.
                }
            }
        });
    }

    /// <summary>Progress and beats become one report type; anything else is not a report.
    /// Public so the mapping is tested without a pipe and a child process.</summary>
    public static ConversionProgress? Translate(WorkerMessage message) => message.Type switch
    {
        WorkerMessage.Progress when message.Stage is not null =>
            new ConversionProgress(message.Stage, message.Fraction ?? 0),
        WorkerMessage.Beat =>
            new ConversionProgress("", 0, message.Cpu ?? 0, IsBeat: true),
        _ => null
    };

    private static void PumpMessages(StreamReader reader, IProgress<ConversionProgress> progress,
                                     TaskCompletionSource<ConversionResult> outcome)
    {
        try
        {
            while (reader.ReadLine() is { } line)
            {
                var message = WorkerLine.Read(line);
                if (message is null) continue;   // a stray line is not a reason to fail a conversion

                switch (message.Type)
                {
                    case WorkerMessage.Progress or WorkerMessage.Beat
                        when Translate(message) is { } report:
                        progress.Report(report);
                        break;

                    case WorkerMessage.Result when message.ResultBody is { } result:
                        outcome.TrySetResult(result);
                        break;

                    case WorkerMessage.Error:
                        outcome.TrySetException(
                            new InvalidOperationException(message.Message ?? "the conversion failed"));
                        break;

                    case WorkerMessage.Cancelled:
                        outcome.TrySetCanceled();
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The pipe broke under us; the exit code and the captured output tell the story.
        }
    }

    private static void Drain(StreamReader source, StringBuilder sink)
    {
        try
        {
            while (source.ReadLine() is { } line)
            {
                lock (sink)
                {
                    // OCCT prints a banner per export; keep a window, not the whole transcript.
                    if (sink.Length < 8_000) sink.AppendLine(line);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private static int ExitCode(Process worker)
    {
        try
        {
            return worker.HasExited ? worker.ExitCode : -1;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static string Tail(StringBuilder noise)
    {
        string text;
        lock (noise) text = noise.ToString().Trim();

        if (text.Length == 0) return "";
        return " " + (text.Length <= 400 ? text : text[^400..]);
    }
}
