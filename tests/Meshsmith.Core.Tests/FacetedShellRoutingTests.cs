using Meshsmith.Core.Brep;
using Meshsmith.Core.Export;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Meshsmith.Core.Pipeline;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// R5-T2's seam: the shell path is legal only for a body that is nothing but mesh triangles whose
/// every edge is used twice. The observables are the diagnostic counters - which route the pipeline
/// took (<see cref="FacetedShellBuilder.BuildCallCount"/>), whether the sewer ran at all
/// (<see cref="SolidBuilder.SewCallCount"/>), and whether the shell was the LAST body built
/// (<see cref="FacetedShellBuilder.SewCallsWhenBuilt"/>) - because both routes produce a valid solid
/// and the result alone cannot tell them apart.
///
/// Where the shell path fires on a real input is measured, not assumed: the classifier turns the
/// deformed blob below into 246 one-triangle plane regions, those sews fail, and the whole-part
/// fallback (all 396 triangles, closed) takes the shell path. That is the knob's shape exactly - its
/// 57,283 faceted triangles of 60,000 cannot close on their own either.
/// </summary>
public class FacetedShellRoutingTests
{
    [Fact]
    public void A_faceted_body_is_built_without_a_sewing_ladder()
    {
        var mesh = FullyFacetedBlob(dx: 0);
        string stl = WriteStl(mesh);
        string work = Path.Combine(Path.GetTempPath(), $"r5-shell-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            ResetCounters();

            var result = ConversionPipeline.Run(stl, new ConversionOptions { WorkingDirectory = work });

            // The pipeline really took the shell path...
            Assert.True(FacetedShellBuilder.BuildCallCount == 1,
                $"shellBuilds={FacetedShellBuilder.BuildCallCount} sews={SolidBuilder.SewCallCount} " +
                $"faces={result.FaceCount} tris={result.TriangleCount} kind={result.Kind} " +
                $"valid={result.IsValidSolid} warnings={string.Join(" | ", result.Warnings)}");

            // ...and the shell was the FINAL body: no sewer ran after it, so the ladder that used to
            // pay for the whole part is gone from the end of the pipeline (measured: sews=8 before the
            // shell was built, sews=8 at the end).
            Assert.Equal(SolidBuilder.SewCallCount, FacetedShellBuilder.SewCallsWhenBuilt);

            // One face per triangle: this body's facets are not coplanar, so the merge changes nothing
            // - and the volume is the mesh's own signed volume.
            Assert.Equal(result.TriangleCount, result.FaceCount);
            Assert.Equal(mesh.SignedVolume(), result.Volume, 2);
            Assert.True(result.IsValidSolid);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// The per-body degradation path must not change: a part with analytic bodies plus a faceted one
    /// keeps the sewer end to end. The four analytic fixtures' byte-identity rides on this branch, and
    /// a mixed body's surface-backed faces cannot share vertices with facets.
    /// </summary>
    [Fact]
    public void A_mixed_body_still_sews()
    {
        var mesh = CubeWithFacetedBody();
        string stl = WriteStl(mesh);
        string work = Path.Combine(Path.GetTempPath(), $"r5-mixed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            ResetCounters();

            var result = ConversionPipeline.Run(stl, new ConversionOptions { WorkingDirectory = work });

            Assert.True(SolidBuilder.SewCallCount > 0, "a mixed body must still go through the sewer");
            Assert.Equal(0, FacetedShellBuilder.BuildCallCount);
            Assert.True(result.FacetedTriangles > 0, "the faceted body was not recognised as faceted");
            Assert.True(result.FacetedTriangles < result.TriangleCount, "the part was not mixed");

            // Measured on this fixture: two closed bodies, 402 faces (396 facets + 6 plane faces),
            // volume 4974.2725 - the mesh's own signed volume - valid, artifact verified.
            Assert.Equal(2, result.SolidCount);
            Assert.Equal(402, result.FaceCount);
            Assert.Equal(mesh.SignedVolume(), result.Volume, 2);
            Assert.True(result.IsValidSolid);
            Assert.Equal(ArtifactStatus.Verified, result.Artifact.Status);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { }
        }
    }

    // ---- helpers ----------------------------------------------------------------

    private static void ResetCounters()
    {
        SolidBuilder.SewCallCount = 0;
        FacetedShellBuilder.BuildCallCount = 0;
        FacetedShellBuilder.SewCallsWhenBuilt = -1;
    }

    /// <summary>
    /// A closed sphere whose every vertex is randomly scaled by up to 60 %: no plane, cylinder, cone,
    /// torus or sphere fits a piece of it, so nothing survives the fit (measured: 396 of 396
    /// triangles land in the faceted fallback, <c>kind=faceted_fallback</c>). Closed - every edge used
    /// twice - so the shell path is legal for it.
    /// </summary>
    private static MeshData FullyFacetedBlob(double dx)
    {
        const double radius = 10;
        const int stacks = 12, slices = 18;

        var random = new Random(31);
        var vertices = new List<Vec3> { new(dx, 0, radius) };
        for (int i = 1; i < stacks; i++)
        {
            double phi = Math.PI * i / stacks;
            for (int j = 0; j < slices; j++)
            {
                double theta = 2 * Math.PI * j / slices;
                double k = 1 - 0.6 + random.NextDouble() * 1.2;       // uniform in [0.4, 1.6]
                double z = radius * Math.Cos(phi) * k, r = radius * Math.Sin(phi) * k;
                vertices.Add(new Vec3(dx + r * Math.Cos(theta), r * Math.Sin(theta), z));
            }
        }
        int south = vertices.Count;
        vertices.Add(new Vec3(dx, 0, -radius));

        int Ring(int i, int j) => 1 + (i - 1) * slices + (j % slices);
        var indices = new List<int>();
        for (int j = 0; j < slices; j++) indices.AddRange(new[] { 0, Ring(1, j), Ring(1, j + 1) });
        for (int i = 1; i < stacks - 1; i++)
            for (int j = 0; j < slices; j++)
            {
                indices.AddRange(new[] { Ring(i, j), Ring(i + 1, j), Ring(i + 1, j + 1) });
                indices.AddRange(new[] { Ring(i, j), Ring(i + 1, j + 1), Ring(i, j + 1) });
            }
        for (int j = 0; j < slices; j++)
            indices.AddRange(new[] { south, Ring(stacks - 1, j + 1), Ring(stacks - 1, j) });

        return new MeshData { Vertices = vertices.ToArray(), Indices = indices.ToArray() };
    }

    /// <summary>A 10 mm cube at the origin plus the faceted blob 30 mm to its right: six plane regions
    /// and a purely faceted body, which is the mixed case in miniature.</summary>
    private static MeshData CubeWithFacetedBody()
    {
        const double s = 10;
        var cube = new[]
        {
            new Vec3(0, 0, 0), new Vec3(s, 0, 0), new Vec3(s, s, 0), new Vec3(0, s, 0),
            new Vec3(0, 0, s), new Vec3(s, 0, s), new Vec3(s, s, s), new Vec3(0, s, s)
        };
        int[] quads = { 0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4, 1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7 };

        var vertices = new List<Vec3>(cube);
        var indices = new List<int>();
        for (int q = 0; q < quads.Length; q += 4)
        {
            indices.AddRange(new[] { quads[q], quads[q + 1], quads[q + 2] });
            indices.AddRange(new[] { quads[q], quads[q + 2], quads[q + 3] });
        }

        var blob = FullyFacetedBlob(dx: 30);
        int offset = vertices.Count;
        vertices.AddRange(blob.Vertices);
        foreach (int i in blob.Indices) indices.Add(i + offset);

        return new MeshData { Vertices = vertices.ToArray(), Indices = indices.ToArray() };
    }

    private static string WriteStl(MeshData mesh)
    {
        string path = Path.Combine(Path.GetTempPath(), $"r5-{Guid.NewGuid():N}.stl");
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);
        w.Write(new byte[80]);
        w.Write((uint)mesh.TriangleCount);
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var n = mesh.Normal(t);
            w.Write((float)n.X); w.Write((float)n.Y); w.Write((float)n.Z);
            for (int k = 0; k < 3; k++)
            {
                var p = mesh.V(t, k);
                w.Write((float)p.X); w.Write((float)p.Y); w.Write((float)p.Z);
            }
            w.Write((ushort)0);
        }
        return path;
    }
}
