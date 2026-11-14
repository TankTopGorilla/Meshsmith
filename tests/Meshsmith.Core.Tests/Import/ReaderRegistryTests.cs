using Meshsmith.Core.Import;
using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Tests;

public class ReaderRegistryTests
{
    [Fact]
    public void Stl_is_read_by_the_registry_and_matches_the_direct_reader()
    {
        var path = StlFixtures.WriteCube(10);
        var direct = StlReader.Read(path);

        var read = MeshReaderRegistry.Read(path, new MeshReadOptions());

        Assert.Equal("STL", read.Format);
        Assert.Equal(1.0, read.AppliedScale);
        Assert.Equal(direct.Points.Length, read.Soup.Points.Length);
        for (int i = 0; i < direct.Points.Length; i++)
            Assert.Equal(direct.Points[i], read.Soup.Points[i]);
    }

    [Fact]
    public void Unknown_extension_and_magic_is_a_named_failure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cw-unknown-{Guid.NewGuid():N}.obj");
        File.WriteAllText(path, "v 0 0 0\nf 1 2 3\n");

        var ex = Assert.Throws<MeshFormatException>(() =>
            MeshReaderRegistry.Read(path, new MeshReadOptions()));

        Assert.Contains("obj", ex.Message);
        Assert.NotEmpty(ex.Feature);
    }

    [Fact]
    public void An_already_cancelled_token_stops_the_read_before_any_bytes_are_parsed()
    {
        var stl = StlFixtures.WriteCube(10);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            MeshReaderRegistry.Read(stl, new MeshReadOptions(), null, cancellation.Token));

        // The same token over a path that does not exist: cancellation must win over file access,
        // which is only true if the token is checked before the head is sniffed.
        var missing = Path.Combine(Path.GetTempPath(), $"cw-missing-{Guid.NewGuid():N}.stl");
        Assert.Throws<OperationCanceledException>(() =>
            MeshReaderRegistry.Read(missing, new MeshReadOptions(), null, cancellation.Token));
    }

    [Fact]
    public void Progress_reaches_one_for_the_stl_reader()
    {
        var path = StlFixtures.WriteCube(10);
        var recorder = new ProgressRecorder();

        var read = MeshReaderRegistry.Read(path, new MeshReadOptions(), recorder);

        Assert.True(read.Soup.TriangleCount > 0);
        Assert.NotEmpty(recorder.Values);
        Assert.True(recorder.Values[^1] >= 0.99, $"last progress value was {recorder.Values[^1]}");
    }

    /// <summary>Synchronous IProgress: the reader reports on the calling thread, so the values are
    /// complete the moment Read returns - no dispatcher, no wait.</summary>
    private sealed class ProgressRecorder : IProgress<double>
    {
        public List<double> Values { get; } = new();
        public void Report(double value) => Values.Add(value);
    }
}
