using Meshsmith.Core.Fit;
using Meshsmith.Core.Mesh;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// The phase's contract in six assertions. The second pass runs on the residual only, is monotone by
/// construction (it may take analytic triangles but never fewer than it takes), never invents a
/// primitive on a body that already fits, and never accepts a piece below its floor.
/// </summary>
public class SecondChanceTests
{
    private static PrimitiveClassifier.Options Options(MeshData mesh, bool secondChance = true) =>
        new(Math.Clamp(2e-4 * mesh.Diagonal, 1e-3, 0.5), 12, 3) { SecondChance = secondChance };

    private static MeshData Load(string name) =>
        MeshWelder.Weld(StlReader.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", name)), 1e-6);

    private static double Coverage(MeshData mesh, PrimitiveClassifier.Options options)
    {
        var regions = PrimitiveClassifier.Classify(mesh, options);
        int analytic = regions.Where(r => r.Primitive is not FacetedPrimitive).Sum(r => r.Triangles.Length);
        return (double)analytic / mesh.TriangleCount;
    }

    [Fact]
    public void Second_chance_is_a_no_op_when_every_region_fits()
    {
        var mesh = MeshWelder.Weld(StlReader.Read(StlFixtures.WriteWasher(30, 10, 10, 256)), 1e-6);
        var regions = PrimitiveClassifier.Classify(mesh, Options(mesh));

        // the washer has no residual at all: the pass must return the same four regions, unchanged
        Assert.Equal(4, regions.Count);
        Assert.Equal(2, regions.Count(r => r.Primitive.Kind == "cylinder"));
        Assert.Equal(2, regions.Count(r => r.Primitive.Kind == "plane"));
    }

    [Fact]
    public void Second_chance_never_lowers_coverage()
    {
        var mesh = Load("clamp_body0.stl");
        double before = Coverage(mesh, Options(mesh, secondChance: false));
        double after = Coverage(mesh, Options(mesh));

        // measured on this fixture: 0.33865 -> 0.57939 at the shipped floor of 16
        Assert.True(after >= before, $"coverage fell: {before} -> {after}");
        Assert.True(after >= 0.55, $"the pass did not engage: {after}");
    }

    [Fact]
    public void Second_chance_recovers_the_clamp_jaw_flats()
    {
        var mesh = Load("clamp_body0.stl");
        var regions = PrimitiveClassifier.Classify(mesh, Options(mesh));

        // the jaw's two flats are 242 and 232 triangles - measured; they only appear when the
        // ladder reaches 3 degrees
        var bigFlats = regions.Where(r => r.Primitive is PlanePrimitive && r.Triangles.Length >= 200).ToList();
        Assert.True(bigFlats.Count >= 2, $"expected >= 2 flats of >= 200 triangles, found {bigFlats.Count}");
        Assert.All(bigFlats, r => Assert.True(r.Primitive.FitMax <= Options(mesh).Tolerance));
    }

    [Fact]
    public void A_piece_below_the_floor_is_never_accepted()
    {
        var mesh = Load("clamp_body0.stl");
        double baseline = Coverage(mesh, Options(mesh, secondChance: false));

        var hugeFloor = Options(mesh) with { MinPieceTriangles = 1_000_000 };
        double withFloor = Coverage(mesh, hugeFloor);

        // nothing can pass a floor larger than the mesh: the pass must be a no-op
        Assert.Equal(baseline, withFloor, 12);
    }

    [Fact]
    public void The_two_torus_fixture_gets_no_torus_from_the_second_pass()
    {
        foreach (var (n, m) in new[] { (24, 12), (48, 24) })
        {
            var mesh = TorusFitTests.TwoTori(n, m);
            var regions = PrimitiveClassifier.Classify(mesh, Options(mesh));

            Assert.DoesNotContain(regions, r => r.Primitive is TorusPrimitive);
            int analytic = regions.Where(r => r.Primitive is not FacetedPrimitive).Sum(r => r.Triangles.Length);
            Assert.Equal(1.0 / 3.0, (double)analytic / mesh.TriangleCount, 6);   // 384/1152, 1536/4608
        }
    }

    [Fact]
    public void A_mesh_with_no_residual_is_returned_untouched()
    {
        var mesh = MeshWelder.Weld(StlReader.Read(StlFixtures.WriteCube(10)), 1e-6);
        var regions = PrimitiveClassifier.Classify(mesh, Options(mesh));

        Assert.Equal(6, regions.Count);
        Assert.All(regions, r => Assert.Equal("plane", r.Primitive.Kind));
    }
}
