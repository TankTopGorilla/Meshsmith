using Meshsmith.Core.Brep;
using Meshsmith.Core.Mesh;
using Occt;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// ShapeUpgrade_UnifySameDomain can build a defect that was not in the facets: a coplanar region that
/// is not a disc gets merged into one face, whose boundary then touches itself and which BRepCheck
/// reports as unorientable. Measured on clamp.stl body 0 - the exact 5,076-facet shell is a valid
/// solid and the merged 3,788-face one is not - and the old code returned the merged one, so the
/// pipeline threw away a valid faceted body and the part exited 5.
///
/// The fixture is that body, extracted from the real part: no synthetic mesh reproduced this through
/// SewRobust (fifteen were tried), so the test uses the data that actually fails.
/// </summary>
public class SolidBuilderUnifyRepairTests
{
    /// <summary>The tripwire. Before the fix this returns IsValid=false at 3,788 faces even though the
    /// shell it was built from has 0 free edges and validates on its own.</summary>
    [Fact]
    public void Sew_does_not_return_a_shape_that_the_coplanar_merge_made_invalid()
    {
        var faces = ClampBody0Facets();

        var result = SolidBuilder.SewRobust(faces, SewTolerance);

        Assert.Equal(0, result.FreeEdges);        // the facets themselves close
        Assert.True(result.IsValid);              // ...and the merge must not break them
        Assert.True(result.Faces < faces.Count);  // the merge still happened
        Assert.True(new BRepCheck_Analyzer(result.Shape).IsValid());
    }

    /// <summary>
    /// The merge must still happen where it is safe, or R1's face-count work is lost: a cube's twelve
    /// triangles must collapse to six faces.
    /// </summary>
    [Fact]
    public void Sew_still_merges_a_body_whose_coplanar_faces_are_a_disc()
    {
        var mesh = Cube(0, 0, 0, 10);
        var faces = FacetedBuilder.Build(mesh, Enumerable.Range(0, mesh.TriangleCount).ToArray());

        var result = SolidBuilder.SewRobust(faces, SewTolerance);

        Assert.Equal(0, result.FreeEdges);
        Assert.True(result.IsValid);
        Assert.Equal(6, result.Faces);
        Assert.Equal(1000.0, result.Volume, 3);
    }

    /// <summary>
    /// A body whose facets are already invalid must still come back invalid - the repair must not
    /// paper over a real defect by returning something the checker happens to accept.
    /// </summary>
    [Fact]
    public void Sew_reports_an_invalid_shell_as_invalid()
    {
        var mesh = Cube(0, 0, 0, 10);
        var faces = FacetedBuilder.Build(mesh, new[] { 0, 1 });   // two triangles: not a solid

        var result = SolidBuilder.SewRobust(faces, SewTolerance);

        Assert.False(result.IsValid);
    }

    /// <summary>The pipeline's own tolerance for this body: 2 * clamp(2e-4 * diag, 1e-3, 0.5) = 0.023897.
    /// Measured: at this tolerance the facet shell has 0 free edges and the merge is what breaks.</summary>
    private const double SewTolerance = 0.023897;

    /// <summary>clamp.stl's body 0, read and welded the way ConversionPipeline does it, faceted one
    /// face per triangle. Measured: 5,076 triangles, 0 free edges after sewing.</summary>
    private static List<TopoDS_Face> ClampBody0Facets()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "clamp_body0.stl");
        var soup = StlReader.Read(path);
        var mesh = MeshWelder.Weld(soup, 1e-6 * soup.Points.Max(p => Math.Abs(p.X) + Math.Abs(p.Y) + Math.Abs(p.Z)));
        var triangles = Enumerable.Range(0, mesh.TriangleCount).ToArray();
        return FacetedBuilder.Build(mesh, triangles);
    }

    /// <summary>A 10 mm cube as a triangulated mesh: 12 triangles, 8 vertices. The vertex order and
    /// winding are StlFixtures' (0,0,0),(s,0,0),(s,s,0),(0,s,0),(0,0,s),(s,0,s),(s,s,s),(0,s,s) - a
    /// different index order inverted the winding when this was measured and produced a zero-volume
    /// shell at 8 faces instead of 6. Do not "tidy" this array.</summary>
    private static MeshData Cube(double x, double y, double z, double s)
    {
        var vertices = new[]
        {
            new Meshsmith.Core.Geometry.Vec3(x, y, z),
            new Meshsmith.Core.Geometry.Vec3(x + s, y, z),
            new Meshsmith.Core.Geometry.Vec3(x + s, y + s, z),
            new Meshsmith.Core.Geometry.Vec3(x, y + s, z),
            new Meshsmith.Core.Geometry.Vec3(x, y, z + s),
            new Meshsmith.Core.Geometry.Vec3(x + s, y, z + s),
            new Meshsmith.Core.Geometry.Vec3(x + s, y + s, z + s),
            new Meshsmith.Core.Geometry.Vec3(x, y + s, z + s)
        };

        int[] quads = { 0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4, 1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7 };
        var indices = new List<int>();
        for (int q = 0; q < quads.Length; q += 4)
        {
            indices.AddRange(new[] { quads[q], quads[q + 1], quads[q + 2] });
            indices.AddRange(new[] { quads[q], quads[q + 2], quads[q + 3] });
        }

        return new MeshData { Vertices = vertices, Indices = indices.ToArray() };
    }
}
