using System.Text.Json;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Meshsmith.Core.Pipeline;
using Meshsmith.Core.Report;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// The predicate is what stops the per-body sew ladder from spending four rungs on an analytic set
/// that provably cannot close. Its one hazard is the false positive: a set that sews today must be
/// told `true`. The third test is the closed-body case that rules out counting inter-region edges
/// only, and the second pins the deliberate limit - an open plate with an interior seam is `true`.
/// </summary>
public class AnalyticSeamTests
{
    [Fact]
    public void An_analytic_island_inside_facets_cannot_close()
    {
        var (mesh, topo) = Grid();

        // One analytic triangle (region 0), the rest of the sheet faceted (region 1): the island's
        // every edge borders a triangle that is not in its own sewn list, so no rung has a partner.
        int[] region = { 0, 1, 1, 1, 1, 1, 1, 1 };
        bool[] analytic = { true, false };

        Assert.False(AnalyticSeam.CanClose(topo, region, analytic, new[] { 0, 1 }));
        Assert.False(AnalyticSeam.CanClose(topo, region, analytic, new[] { 0 }));
    }

    [Fact]
    public void An_open_plate_with_a_shared_interior_edge_is_not_skipped()
    {
        var mesh = new MeshData
        {
            Vertices = new[]
            {
                new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 1, 0)
            },
            Indices = new[] { 0, 1, 2, 0, 2, 3 }
        };
        var topo = MeshTopology.Build(mesh);

        // The diagonal is analytic-analytic, so the ladder runs as today even though the plate is
        // open and will fail: `true` means "not proven impossible", nothing more.
        Assert.True(AnalyticSeam.CanClose(topo, new[] { 0, 0 }, new[] { true }, new[] { 0 }));
    }

    [Fact]
    public void A_fully_analytic_closed_body_is_not_skipped()
    {
        var mesh = new MeshData
        {
            Vertices = new[]
            {
                new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 1, 0),
                new Vec3(0, 0, 1), new Vec3(1, 0, 1), new Vec3(1, 1, 1), new Vec3(0, 1, 1)
            },
            Indices = new[]
            {
                0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4,
                1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7
            }
        };
        var topo = MeshTopology.Build(mesh);

        // A closed body joins on interior edges - the same edges a cylinder band and a torus use.
        // Counting inter-region edges only would skip this, and it closes today.
        Assert.True(AnalyticSeam.CanClose(topo, Enumerable.Repeat(0, 12).ToArray(), new[] { true },
                                          new[] { 0 }));
    }

    [Fact]
    public void Only_the_component_s_own_triangles_count()
    {
        // The same grid plus one lone triangle 10 mm away, its own analytic region: the sheet is
        // connected, the lone triangle is not.
        var mesh = new MeshData
        {
            Vertices = GridVertices().Concat(new[]
            {
                new Vec3(10, 0, 0), new Vec3(11, 0, 0), new Vec3(10, 1, 0)
            }).ToArray(),
            Indices = GridIndices().Concat(new[] { 9, 10, 11 }).ToArray()
        };
        var topo2 = MeshTopology.Build(mesh);

        int[] region = { 0, 0, 0, 0, 0, 0, 0, 0, 1 };
        bool[] analytic = { true, true };

        Assert.True(AnalyticSeam.CanClose(topo2, region, analytic, new[] { 0 }));
        Assert.False(AnalyticSeam.CanClose(topo2, region, analytic, new[] { 1 }));
    }

    [Fact]
    public void The_skip_count_is_reported_and_defaults_to_zero()
    {
        // A part that closes today must take no skip: the counter is the observable that catches a
        // predicate which "proves" too much.
        var r = ConversionPipeline.Run(StlFixtures.WriteCylinder(20, 30, 256),
            new ConversionOptions { KeepTemporaryFiles = true });
        Assert.True(r.IsValidSolid, string.Join("; ", r.Warnings));
        Assert.Equal(0, r.SkippedLadders);

        // The count reaches faces.json under its documented key.
        var json = JsonSerializer.Serialize(new FacesDocument
        {
            Summary = new Summary { SkippedLadders = 2 }
        });
        Assert.Contains("\"skipped_ladders\":2", json.Replace(" ", ""));

        // R3/R4's rule: a faces.json written before the field existed still deserialises, at 0.
        const string old = """
        {"schema":1,"summary":{"faces":5,"triangles":2048}}
        """;
        var doc = JsonSerializer.Deserialize<FacesDocument>(old);
        Assert.NotNull(doc);
        Assert.Equal(5, doc!.Summary.Faces);
        Assert.Equal(0, doc.Summary.SkippedLadders);
    }

    private static (MeshData Mesh, MeshTopology Topology) Grid()
    {
        var mesh = new MeshData { Vertices = GridVertices(), Indices = GridIndices() };
        return (mesh, MeshTopology.Build(mesh));
    }

    /// <summary>3 x 3 vertices at z = 0: index = x + 3 * y.</summary>
    private static Vec3[] GridVertices()
    {
        var v = new Vec3[9];
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                v[x + 3 * y] = new Vec3(x, y, 0);
        return v;
    }

    /// <summary>The four quads of the grid, each split into two triangles: 8 triangles.</summary>
    private static int[] GridIndices()
    {
        var indices = new List<int>(24);
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                int a = x + 3 * y, b = a + 1, c = a + 4, d = a + 3;
                indices.AddRange(new[] { a, b, c, a, c, d });
            }
        }
        return indices.ToArray();
    }
}
