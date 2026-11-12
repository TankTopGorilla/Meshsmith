using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Xunit;

namespace Meshsmith.Core.Tests;

public class MeshRepairTests
{
    private static MeshData Weld(IEnumerable<(Vec3 A, Vec3 B, Vec3 C)> triangles)
    {
        var points = triangles.SelectMany(t => new[] { t.A, t.B, t.C }).ToArray();
        return MeshWelder.Weld(new TriangleSoup { Points = points }, 1e-6);
    }

    private static MeshHealth Health(MeshData mesh) => MeshHealth.Measure(mesh, MeshTopology.Build(mesh));

    [Fact]
    public void A_clean_cube_is_left_alone()
    {
        var cube = Weld(StlFixtures.Cube(10));
        var (_, report) = MeshRepair.Repair(cube);

        Assert.False(report.Changed);
        Assert.Equal(0, report.Flipped);
        Assert.Equal(0, report.HolesFilled);
        Assert.Equal(12, cube.TriangleCount);
    }

    [Fact]
    public void A_degenerate_triangle_is_dropped()
    {
        var triangles = StlFixtures.Cube(10).ToList();
        // Distinct vertices, zero area: collinear. (Repeated-vertex triangles never reach repair -
        // the welder already drops those.)
        triangles.Add((new Vec3(0, 0, 0), new Vec3(5, 0, 0), new Vec3(10, 0, 0)));
        var mesh = Weld(triangles);

        var (repaired, report) = MeshRepair.Repair(mesh);

        Assert.Equal(1, report.DegenerateDropped);
        Assert.Equal(12, repaired.TriangleCount);
        Assert.True(Health(repaired).IsWatertight);
    }

    [Fact]
    public void A_duplicate_face_is_dropped()
    {
        var triangles = StlFixtures.Cube(10).ToList();
        triangles.Add(triangles[0]);
        var mesh = Weld(triangles);

        var (repaired, report) = MeshRepair.Repair(mesh);

        Assert.Equal(1, report.DuplicatesDropped);
        Assert.Equal(12, repaired.TriangleCount);
    }

    [Fact]
    public void An_opposite_winding_duplicate_is_kept()
    {
        var triangles = StlFixtures.Cube(10).ToList();
        var (a, b, c) = triangles[0];
        triangles.Add((a, c, b));            // back-to-back sheet, not a duplicate
        var mesh = Weld(triangles);

        var (repaired, report) = MeshRepair.Repair(mesh);

        Assert.Equal(0, report.DuplicatesDropped);
        Assert.Equal(13, repaired.TriangleCount);
    }

    [Fact]
    public void An_inverted_triangle_is_flipped()
    {
        var triangles = StlFixtures.Cube(10).ToList();
        var (a, b, c) = triangles[0];
        triangles[0] = (a, c, b);
        var mesh = Weld(triangles);
        double before = mesh.SignedVolume();

        var (repaired, report) = MeshRepair.Repair(mesh);

        Assert.Equal(1, report.Flipped);
        Assert.True(repaired.SignedVolume() > 0);
        Assert.Equal(Math.Abs(before), Math.Abs(repaired.SignedVolume()), 1e-6);
        Assert.Equal(1000.0, repaired.SignedVolume(), 1e-6);
    }

    [Fact]
    public void A_hole_is_filled()
    {
        var triangles = StlFixtures.Cube(10).ToList();
        // Remove two adjacent faces: that leaves a 6-edge hole in the cube's surface.
        var keep = triangles.Where((_, i) => i != 0 && i != 1).ToList();

        var (repaired, report) = MeshRepair.Repair(Weld(keep));

        Assert.True(report.HolesFilled >= 1, report.Describe());
        Assert.True(Health(repaired).IsWatertight, $"open={Health(repaired).OpenEdges}");
        Assert.Equal(1000.0, Math.Abs(repaired.SignedVolume()), 1.0);
    }

    [Fact]
    public void A_hole_over_the_cap_is_left_open()
    {
        var triangles = StlFixtures.Cube(10).ToList();
        var keep = triangles.Where((_, i) => i != 0 && i != 1).ToList();
        var mesh = Weld(keep);
        int openBefore = Health(mesh).OpenEdges;

        var (repaired, report) = MeshRepair.Repair(mesh, new RepairOptions(MaxHoleEdges: 2));

        Assert.Equal(0, report.HolesFilled);
        Assert.Equal(openBefore, Health(repaired).OpenEdges);
    }
}
