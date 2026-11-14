using Meshsmith.Core.Import;

namespace Meshsmith.Core.Tests;

/// <summary>The read stage is the one place a cancel used to wait for the whole file: the pipeline
/// handed the token to a reader that parsed the buffer in one uninterruptible loop. These pin the
/// two halves of the fix - the token is honoured mid-stream, and the byte fraction the status bar
/// shows is real.</summary>
public class StlMeshReaderCancellationTests
{
    /// <summary>Synchronous on purpose: <see cref="Progress{T}"/> posts to the thread pool, which
    /// would make a deterministic cancellation test a race.</summary>
    private sealed class InlineProgress : IProgress<double>
    {
        private readonly Action<double> _action;

        public InlineProgress(Action<double> action) => _action = action;

        public void Report(double value) => _action(value);
    }

    private static string Washer() => StlFixtures.WriteWasher(30, 10, 10, 256);   // 2,048 triangles

    [Fact]
    public void Read_stops_when_the_token_is_cancelled_mid_stream()
    {
        // 2,048 triangles is 102,484 bytes: big enough to have a middle, small enough to be a test.
        IMeshReader reader = new StlMeshReader();
        using var cancellation = new CancellationTokenSource();
        var fractions = new List<double>();

        var progress = new InlineProgress(fraction =>
        {
            fractions.Add(fraction);
            if (fraction >= 0.5) cancellation.Cancel();     // half the bytes are behind us
        });

        var read = () => reader.Read(Washer(), new MeshReadOptions(), progress, cancellation.Token);

        Assert.Throws<OperationCanceledException>(read);
        Assert.Contains(fractions, f => f > 0.03 && f < 1.0);   // it really was mid-stream
        Assert.DoesNotContain(1.0, fractions);                  // and no soup escaped to the end
    }

    [Fact]
    public void Read_reports_byte_progress_that_ends_at_one()
    {
        IMeshReader reader = new StlMeshReader();
        var fractions = new List<double>();

        var read = reader.Read(Washer(), new MeshReadOptions(),
                               new InlineProgress(fractions.Add), CancellationToken.None);

        Assert.Equal(2_048, read.Soup.TriangleCount);
        Assert.NotEmpty(fractions);
        Assert.True(fractions[0] <= 0.1, $"first report was {fractions[0]:0.###}");

        for (int i = 1; i < fractions.Count; i++)
            Assert.True(fractions[i] >= fractions[i - 1],
                        $"progress went backwards: {fractions[i - 1]:0.###} then {fractions[i]:0.###}");

        Assert.Equal(1.0, fractions[^1]);
    }
}
