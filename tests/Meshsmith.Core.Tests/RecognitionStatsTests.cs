using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Meshsmith.Core.Pipeline;
using Xunit;

namespace Meshsmith.Core.Tests;

public class RecognitionStatsTests
{
    [Fact]
    public void Analytic_share_of_a_mixed_classification()
    {
        var regions = new List<ClassifiedRegion>
        {
            Region(Enumerable.Range(0, 70).ToArray(),
                   new PlanePrimitive { Point = Vec3.Zero, Normal = new Vec3(0, 0, 1) }),
            Region(Enumerable.Range(70, 30).ToArray(), new FacetedPrimitive { Triangles = 30 })
        };

        var stats = RecognitionStats.Compute(FlatMesh(100), regions);

        Assert.Equal(100, stats.TotalTriangles);
        Assert.Equal(70, stats.AnalyticTriangles);
        Assert.Equal(0.70, stats.Coverage, 6);
    }

    [Fact]
    public void Empty_classification_is_zero_coverage()
    {
        var stats = RecognitionStats.Compute(FlatMesh(10), Array.Empty<ClassifiedRegion>());

        Assert.Equal(10, stats.TotalTriangles);
        Assert.Equal(0, stats.AnalyticTriangles);
        Assert.Equal(0.0, stats.Coverage);
    }

    private static ClassifiedRegion Region(int[] triangles, Primitive prim) =>
        new(triangles, prim, new Vec3(0, 0, 1), triangles.Length);

    private static MeshData FlatMesh(int triangles) => new()
    {
        Vertices = new[] { Vec3.Zero, new Vec3(1, 0, 0), new Vec3(0, 1, 0) },
        Indices = Enumerable.Range(0, triangles).SelectMany(_ => new[] { 0, 1, 2 }).ToArray()
    };
}
