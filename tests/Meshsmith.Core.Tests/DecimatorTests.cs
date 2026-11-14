using Meshsmith.Core.Mesh;
using Meshsmith.Core.Pipeline;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// R4: a mesh above ~200 k triangles cannot be classified at all, so it is decimated before
/// anything is fitted to it. Two things have to hold or the trade is not worth making: the
/// reduction must leave a watertight body of the requested size (a decimator that opens holes
/// hands the sewer an unclosed shell), and it must not move the surface by more than a small
/// multiple of the fit tolerance - that distance IS the detail it erased, and it is reported.
/// </summary>
public class DecimatorTests
{
    [Fact]
    public void Decimate_reduces_triangles_and_stays_watertight()
    {
        // A fine closed synthetic body: a 48x96 sphere, 9,024 triangles.
        var fine = TorusFitTests.Sphere(40, 48, 96);
        Assert.Equal(9_024, fine.TriangleCount);
        Assert.True(MeshHealth.Measure(fine, MeshTopology.Build(fine)).IsWatertight);

        int target = fine.TriangleCount / 2;
        var coarse = Decimator.Decimate(fine, target);

        Assert.True(coarse.TriangleCount <= target,
            $"asked for {target}, got {coarse.TriangleCount}");
        Assert.True(coarse.TriangleCount >= target / 2,
            $"collapsed to {coarse.TriangleCount} - the target was {target}");

        var health = MeshHealth.Measure(coarse, MeshTopology.Build(coarse));
        Assert.Equal(0, health.OpenEdges);
        Assert.Equal(0, health.NonManifoldEdges);
        Assert.Equal(1, health.Bodies);

        // A second repair pass must find nothing left to do: whatever g3 emitted at the collapse
        // boundaries is already gone, so the pipeline's own repair stays quiet on a decimated mesh.
        var (_, report) = MeshRepair.Repair(coarse, new RepairOptions(64));
        Assert.False(report.Changed, report.Describe());
    }

    [Fact]
    public void Decimation_stays_within_the_measured_deviation_bound()
    {
        var fine = TorusFitTests.Sphere(40, 48, 96);      // 9,024 triangles
        Assert.Equal(9_024, fine.TriangleCount);
        double tol = Math.Clamp(2e-4 * fine.Diagonal, 1e-3, 0.5);

        var coarse = Decimator.Decimate(fine, 1_500);
        Assert.True(coarse.TriangleCount <= 1_500, $"got {coarse.TriangleCount}");

        // Every surviving vertex against the ORIGINAL surface: the distance the decimation moved
        // geometry, in pipeline tolerances. Measured at this exact size (9,024 -> 1,500 on a sphere,
        // which is the worst case - a sphere has no sharp edge to hold a collapse in place):
        // max 0.1307 units = 4.716x tol over 752 vertices. The plan's bound is 5x; the sharp-edged
        // parts measure far tighter (clamp: 1.67x at a 5.8x reduction, from the R4 probe).
        var grid = new TriangleGrid(fine);
        double worst = 0;
        int n = 0;
        foreach (var p in coarse.Vertices)
        {
            worst = Math.Max(worst, grid.DistanceToSurface(p));
            n++;
        }
        Assert.True(n > 0);
        Assert.True(worst <= 5 * tol,
            $"worst vertex moved {worst:0.#####} = {worst / tol:0.###}x tol ({n} vertices)");
    }

    /// <summary>Subdivide every triangle into four at its edge midpoints, welding shared edges.</summary>
    internal static MeshData Densify(MeshData mesh)
    {
        var vertices = new List<Meshsmith.Core.Geometry.Vec3>(mesh.Vertices);
        var mids = new Dictionary<(int, int), int>();

        int Mid(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (mids.TryGetValue(key, out int id)) return id;
            vertices.Add((mesh.Vertices[a] + mesh.Vertices[b]) / 2.0);
            mids[key] = id = vertices.Count - 1;
            return id;
        }

        var indices = new List<int>(mesh.TriangleCount * 12);
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int a = mesh.Indices[t * 3], b = mesh.Indices[t * 3 + 1], c = mesh.Indices[t * 3 + 2];
            int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
            indices.AddRange(new[] { a, ab, ca });
            indices.AddRange(new[] { ab, b, bc });
            indices.AddRange(new[] { ca, bc, c });
            indices.AddRange(new[] { ab, bc, ca });
        }

        return new MeshData { Vertices = vertices.ToArray(), Indices = indices.ToArray() };
    }
}
