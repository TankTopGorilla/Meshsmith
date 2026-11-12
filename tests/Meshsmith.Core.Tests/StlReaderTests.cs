using Meshsmith.Core.Mesh;
using Xunit;

namespace Meshsmith.Core.Tests;

public class StlReaderTests
{
    [Fact]
    public void Reads_binary_cube()
    {
        var path = StlFixtures.WriteCube(10);
        var soup = StlReader.Read(path);
        Assert.Equal(12, soup.TriangleCount);
        Assert.Equal(36, soup.Points.Length);
    }

    [Fact]
    public void Rejects_truncated_binary()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, new byte[40]);
        Assert.Throws<StlFormatException>(() => StlReader.Read(path));
    }

    [Fact]
    public void Reads_ascii_stl()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ascii-{Guid.NewGuid():N}.stl");
        File.WriteAllText(path, """
            solid test
              facet normal 0 0 1
                outer loop
                  vertex 0 0 0
                  vertex 10 0 0
                  vertex 0 10 0
                endloop
              endfacet
            endsolid test
            """);
        var soup = StlReader.Read(path);
        Assert.Equal(1, soup.TriangleCount);
        Assert.Equal(0, soup.Points[0].X, 9);
        File.Delete(path);
    }

    [Fact]
    public void GenerateFixtures()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../fixtures"));
        StlFixtures.WriteRepositoryFixtures(dir);
        Assert.True(File.Exists(Path.Combine(dir, "cube_10.stl")));
        Assert.True(File.Exists(Path.Combine(dir, "washer_r30_r10_h10.stl")));
    }
}
