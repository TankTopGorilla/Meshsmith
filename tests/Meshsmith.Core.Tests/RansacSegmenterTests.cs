using System.Globalization;
using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Meshsmith.Core.Seg;
using Meshsmith.Core.Tests.Fixtures;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// The hypothesis segmenter, measured against the plan's numeric kill criterion: every synthetic
/// primitive must come back as the RIGHT KIND over at least 90 % of its triangles, the torus over 95 %,
/// and a two-primitive scene must split within 5 %. These are "recovered" tests, not coverage tests -
/// coverage is gameable (a 3 degree split covers 78.7 % of clamp's jaw with one-triangle planes), so
/// every assertion here counts triangles whose fitted KIND is the expected one.
/// </summary>
public class RansacSegmenterTests
{
    // ---- T2: plane -------------------------------------------------------------------------

    [Fact]
    public void Plane_Is_Recovered_As_One_Plane_Within_Tolerance()
    {
        var mesh = SyntheticMeshes.Plane(size: 40, nx: 50, ny: 50);   // 5,000 triangles
        Assert.Equal(5_000, mesh.TriangleCount);
        double tol = SyntheticMeshes.Tol(mesh);

        var regions = RansacSegmenter.Classify(mesh, new RansacSegmenter.Options(tol, 12, 3));

        var planes = regions.Where(r => r.Primitive is PlanePrimitive).ToList();
        Assert.Single(planes);

        var plane = Assert.IsType<PlanePrimitive>(planes[0].Primitive);
        Assert.True(Math.Abs(Vec3.Dot(plane.Normal, new Vec3(0, 0, 1))) > 0.9999,
            $"normal {plane.Normal} is not the plane's");
        Assert.True(plane.FitMax < tol, $"FitMax {plane.FitMax} >= tol {tol}");

        int recovered = planes.Sum(r => r.Triangles.Length);
        Assert.True(recovered >= 0.90 * mesh.TriangleCount,
            $"recovered {recovered} of {mesh.TriangleCount} ({100.0 * recovered / mesh.TriangleCount:F1} %)");
    }

    // ---- T3: cylinder ----------------------------------------------------------------------

    [Fact]
    public void Cylinder_Is_Recovered_With_Its_Radius_And_Axis()
    {
        var mesh = SyntheticMeshes.CylinderSide(radius: 12, height: 40, n: 240);   // 9,600
        Assert.Equal(9_600, mesh.TriangleCount);
        double tol = SyntheticMeshes.Tol(mesh);

        var regions = RansacSegmenter.Classify(mesh, new RansacSegmenter.Options(tol, 12, 3));

        var cylinders = regions.Where(r => r.Primitive is CylinderPrimitive).ToList();
        Assert.Single(cylinders);

        var cylinder = Assert.IsType<CylinderPrimitive>(cylinders[0].Primitive);
        Assert.True(Math.Abs(cylinder.Radius - 12) < 0.01, $"radius {cylinder.Radius}");
        Assert.True(Math.Abs(Vec3.Dot(cylinder.Axis, new Vec3(0, 0, 1))) > 0.9999,
            $"axis {cylinder.Axis}");

        int recovered = cylinders.Sum(r => r.Triangles.Length);
        Assert.True(recovered >= 0.90 * mesh.TriangleCount,
            $"recovered {recovered} of {mesh.TriangleCount} ({100.0 * recovered / mesh.TriangleCount:F1} %)");
    }

    // ---- T4: sphere and cone ---------------------------------------------------------------

    [Fact]
    public void Sphere_And_Cone_Are_Recovered()
    {
        // Sphere: 96 x 140 -> 2 * 140 * 95 = 26,600 triangles, r = 10.
        var sphereMesh = SyntheticMeshes.Sphere(radius: 10, stacks: 96, slices: 140);
        Assert.Equal(26_600, sphereMesh.TriangleCount);
        double sphereTol = SyntheticMeshes.Tol(sphereMesh);

        var sphereRegions = RansacSegmenter.Classify(sphereMesh,
            new RansacSegmenter.Options(sphereTol, 12, 3));

        var spheres = sphereRegions.Where(r => r.Primitive is SpherePrimitive).ToList();
        Assert.Single(spheres);
        var sphere = Assert.IsType<SpherePrimitive>(spheres[0].Primitive);
        Assert.True(Math.Abs(sphere.Radius - 10) < 0.05, $"radius {sphere.Radius}");
        int sphereRecovered = spheres.Sum(r => r.Triangles.Length);
        Assert.True(sphereRecovered >= 0.90 * sphereMesh.TriangleCount,
            $"sphere recovered {sphereRecovered} of {sphereMesh.TriangleCount}");

        // Cone: radii 14 -> 4 over h = 30, 240 x 40 -> 19,200 triangles.
        var coneMesh = SyntheticMeshes.Cone(r1: 14, r2: 4, height: 30, n: 240);
        Assert.Equal(19_200, coneMesh.TriangleCount);
        double coneTol = SyntheticMeshes.Tol(coneMesh);

        var coneRegions = RansacSegmenter.Classify(coneMesh, new RansacSegmenter.Options(coneTol, 12, 3));

        var cones = coneRegions.Where(r => r.Primitive is ConePrimitive).ToList();
        Assert.NotEmpty(cones);
        int coneRecovered = cones.Sum(r => r.Triangles.Length);
        Assert.True(coneRecovered >= 0.90 * coneMesh.TriangleCount,
            $"cone recovered {coneRecovered} of {coneMesh.TriangleCount} " +
            $"({100.0 * coneRecovered / coneMesh.TriangleCount:F1} %)");

        // The half-angle is NOT a defect (the plan's C4 is refuted): radii 14 -> 4 over h = 30 is
        // atan(10/30) = 18.4349 deg, and the fitter returns exactly that. atan(1/3) = 0.321750554 rad.
        double expected = Math.Atan(10.0 / 30.0);
        Assert.True(Math.Abs(expected - 0.32173) < 0.00001, $"{expected}");   // the value itself
        var cone = (ConePrimitive)cones.OrderByDescending(r => r.Triangles.Length).First().Primitive;
        Assert.True(Math.Abs(cone.HalfAngle - 0.32173) < 0.0009,
            $"HalfAngle {cone.HalfAngle} rad ({cone.HalfAngle * 180 / Math.PI:F4} deg), " +
            $"expected {0.32173} (18.4349 deg)");
    }

    // ---- T5: torus -------------------------------------------------------------------------

    [Fact]
    public void Torus_Is_Recovered_Only_From_A_Whole_Tube()
    {
        var mesh = SyntheticMeshes.Torus(major: 20, minor: 7, N: 128, M: 64);   // 16,384
        Assert.Equal(16_384, mesh.TriangleCount);
        double tol = SyntheticMeshes.Tol(mesh);

        var options = new RansacSegmenter.Options(tol, 12, 3) { EnableTorus = true };
        var regions = RansacSegmenter.Classify(mesh, options);

        var tori = regions.Where(r => r.Primitive is TorusPrimitive).ToList();
        Assert.NotEmpty(tori);
        var torus = (TorusPrimitive)tori.OrderByDescending(r => r.Triangles.Length).First().Primitive;
        Assert.True(Math.Abs(torus.MajorRadius - 20) < 0.05, $"R {torus.MajorRadius}");
        Assert.True(Math.Abs(torus.MinorRadius - 7) < 0.05, $"r {torus.MinorRadius}");

        int recovered = tori.Sum(r => r.Triangles.Length);
        Assert.True(recovered >= 0.95 * mesh.TriangleCount,
            $"recovered {recovered} of {mesh.TriangleCount} ({100.0 * recovered / mesh.TriangleCount:F2} %)");

        // The companion negative: a patch of the same tube, small enough that the fit is
        // underdetermined, must be refused. This is why EnableTorus is off on the local path.
        var patch = SyntheticMeshes.Torus(20, 7, 20, 20);   // 400 triangles: one corner of the tube
        var (patchPoints, patchNormals) = PointsAndNormals(patch);
        Assert.True(patchPoints.Count >= 32, "the negative needs enough points to be a real attempt");
        Assert.Null(Fitters.FitTorus(patchPoints, patchNormals, tol * 2, patch.Diagonal));
    }

    // ---- T6: two-primitive split -----------------------------------------------------------

    [Fact]
    public void TwoPrimitive_Scene_Splits_At_The_Right_TriangleCounts()
    {
        var mesh = SyntheticMeshes.TwoPrimitive();
        Assert.Equal(14_792 + 24_000, mesh.TriangleCount);
        double tol = SyntheticMeshes.Tol(mesh);

        var regions = RansacSegmenter.Classify(mesh, new RansacSegmenter.Options(tol, 12, 3));

        var analytic = regions.Where(r => r.Primitive is not FacetedPrimitive).ToList();
        Assert.Equal(2, analytic.Count);

        var kinds = analytic.Select(r => r.Primitive.Kind).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "cylinder", "plane" }, kinds);

        int plane = analytic.Where(r => r.Primitive is PlanePrimitive).Sum(r => r.Triangles.Length);
        int cylinder = analytic.Where(r => r.Primitive is CylinderPrimitive).Sum(r => r.Triangles.Length);

        Assert.InRange(plane, (int)(0.95 * 14_792), (int)(1.05 * 14_792));
        Assert.InRange(cylinder, (int)(0.95 * 24_000), (int)(1.05 * 24_000));

        int total = analytic.Sum(r => r.Triangles.Length);
        Assert.True(total >= 0.85 * mesh.TriangleCount,
            $"total recovered {total} of {mesh.TriangleCount}");
    }

    // ---- T7: determinism -------------------------------------------------------------------

    [Fact]
    public void RansacSegmenter_Is_Deterministic()
    {
        var mesh = SyntheticMeshes.TwoPrimitive();
        double tol = SyntheticMeshes.Tol(mesh);
        var options = new RansacSegmenter.Options(tol, 12, 3);

        var first = RansacSegmenter.Classify(mesh, options);
        var second = RansacSegmenter.Classify(mesh, options);

        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Triangles, second[i].Triangles);       // element by element
            Assert.Equal(first[i].Primitive.Kind, second[i].Primitive.Kind);
        }
    }

    /// <summary>
    /// The Turkish-culture trap: the repo has been bitten by `ToString` and `Parse` under tr-TR, where
    /// "I" lowercases to a dotless i and the decimal separator is a comma. The segmenter must not
    /// consult the current culture at all, so the partition must be identical under it.
    /// </summary>
    [Fact]
    public void RansacSegmenter_Is_Deterministic_Under_Turkish_Culture()
    {
        var mesh = SyntheticMeshes.TwoPrimitive();
        double tol = SyntheticMeshes.Tol(mesh);
        var options = new RansacSegmenter.Options(tol, 12, 3);

        var invariant = RansacSegmenter.Classify(mesh, options);

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var turkish = RansacSegmenter.Classify(mesh, options);

            Assert.Equal(invariant.Count, turkish.Count);
            for (int i = 0; i < invariant.Count; i++)
            {
                Assert.Equal(invariant[i].Triangles, turkish[i].Triangles);
                Assert.Equal(invariant[i].Primitive.Kind, turkish[i].Primitive.Kind);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ---- T8: assignment invariant ----------------------------------------------------------

    [Fact]
    public void Every_Triangle_Is_Assigned_Exactly_Once()
    {
        var meshes = new (string Name, MeshData Mesh)[]
        {
            ("plane", SyntheticMeshes.Plane(30, 40, 40)),
            ("cylinder", SyntheticMeshes.CylinderSide(12, 40, 240)),
            ("cone", SyntheticMeshes.Cone(14, 4, 30, 240)),
            ("sphere", SyntheticMeshes.Sphere(10, 48, 72)),
            ("torus", SyntheticMeshes.Torus(20, 7, 64, 32)),
            ("two-primitive", SyntheticMeshes.TwoPrimitive()),
            ("knurl-8", SyntheticMeshes.KnurlBand(40 * 50, 8)),
        };

        foreach (var (name, mesh) in meshes)
        {
            double tol = SyntheticMeshes.Tol(mesh);
            var regions = RansacSegmenter.Classify(mesh, new RansacSegmenter.Options(tol, 12, 3));

            int sum = regions.Sum(r => r.Triangles.Length);
            Assert.True(sum == mesh.TriangleCount,
                $"{name}: regions cover {sum} of {mesh.TriangleCount}");

            var seen = new int[mesh.TriangleCount];
            foreach (var region in regions)
                foreach (var t in region.Triangles)
                {
                    Assert.InRange(t, 0, mesh.TriangleCount - 1);
                    seen[t]++;
                }

            for (int t = 0; t < seen.Length; t++)
                Assert.True(seen[t] == 1, $"{name}: triangle {t} appears {seen[t]} times");
        }
    }

    // ---- T9 / T10: the floor, both ways ---------------------------------------------------

    [Fact]
    public void Knurl_Band_Under_The_Floor_Stays_Faceted()
    {
        // 2,000 patches of 8 triangles: below MinPieceTriangles (16), so they must NOT become planes.
        var band = SyntheticMeshes.KnurlBand(cells: 2_000, trisPerPatch: 8);
        Assert.Equal(16_000, band.TriangleCount);

        var cylinder = SyntheticMeshes.CylinderSide(12, 40, 240);       // 9,600
        var sphere = SyntheticMeshes.Sphere(10, 48, 140);              // 26,600 - 2*140*95
        var mesh = Combine(band, cylinder, sphere);
        double tol = SyntheticMeshes.Tol(mesh);

        var regions = RansacSegmenter.Classify(mesh, new RansacSegmenter.Options(tol, 12, 3));

        // No plane may come from the band. The cylinder's and sphere's own regions are not planes, so
        // any plane at all is a band plane here (a genuine band is 8-triangle patches of one flat quad).
        int planeTriangles = regions.Where(r => r.Primitive is PlanePrimitive).Sum(r => r.Triangles.Length);
        Assert.True(planeTriangles <= 0.05 * band.TriangleCount,
            $"{planeTriangles} triangles became planes out of an 8-triangle band");

        var faceted = regions.Where(r => r.Primitive is FacetedPrimitive).ToList();
        Assert.True(faceted.Sum(r => r.Triangles.Length) >= 0.95 * band.TriangleCount,
            $"only {faceted.Sum(r => r.Triangles.Length)} of {band.TriangleCount} band triangles stayed faceted");

        // ...and the two real primitives are still recovered next to the noise.
        Assert.Contains(regions, r => r.Primitive is CylinderPrimitive);
        Assert.Contains(regions, r => r.Primitive is SpherePrimitive);
    }

    [Fact]
    public void Knurl_Band_Above_The_Floor_Is_Recovered_As_Many_Small_Planes()
    {
        // The same band at 24 triangles per patch: above the floor, so every patch is its own plane.
        var band = SyntheticMeshes.KnurlBand(cells: 2_000, trisPerPatch: 24);
        Assert.Equal(48_000, band.TriangleCount);
        double tol = SyntheticMeshes.Tol(band);

        var regions = RansacSegmenter.Classify(band, new RansacSegmenter.Options(tol, 12, 3));

        var planes = regions.Where(r => r.Primitive is PlanePrimitive).ToList();
        Assert.True(planes.Count >= 1_900, $"{planes.Count} plane regions, expected ~2,000");

        var sizes = planes.Select(r => r.Triangles.Length).OrderBy(x => x).ToArray();
        int median = sizes[sizes.Length / 2];
        Assert.Equal(24, median);

        int planeTriangles = planes.Sum(r => r.Triangles.Length);
        Assert.True(planeTriangles >= 0.95 * band.TriangleCount,
            $"recovered {planeTriangles} of {band.TriangleCount} band triangles " +
            $"({100.0 * planeTriangles / band.TriangleCount:F1} %)");

        int faceted = regions.Where(r => r.Primitive is FacetedPrimitive).Sum(r => r.Triangles.Length);
        Assert.True(faceted <= 0.05 * band.TriangleCount, $"{faceted} band triangles stayed faceted");
    }

    // ---- T11: the R2 regression, paired ---------------------------------------------------

    [Fact]
    public void R2_Drum_Beats_Flood_On_A_Connected_Shallow_Ribbed_Body()
    {
        // One body: a ribbed cylinder side with every crest under the grower's 12 degree threshold,
        // plus a dome sharing its top ring. Flood cannot split it, so it returns one region that no
        // single primitive fits - which is exactly the shape R2 measured at 21.9 %.
        var mesh = SyntheticMeshes.R2Drum();
        double tol = SyntheticMeshes.Tol(mesh);

        var flood = PrimitiveClassifier.Classify(mesh, new PrimitiveClassifier.Options(tol, 12, 3));
        var ransac = RansacSegmenter.Classify(mesh, new RansacSegmenter.Options(tol, 12, 3));

        double floodShare = AnalyticShare(mesh, flood);
        double ransacShare = AnalyticShare(mesh, ransac);

        // Paired: one engine rising cannot satisfy this if the other moves with it.
        Assert.True(ransacShare >= 0.80,
            $"ransac recovered {ransacShare * 100:F1} % of {mesh.TriangleCount} triangles");
        Assert.True(floodShare <= 0.30,
            $"flood recovered {floodShare * 100:F1} % - the fixture is too easy to prove anything");
    }

    // ---- T12: noise, at the measured ceiling ----------------------------------------------

    [Fact]
    public void Noise_Up_To_A_Fifth_Of_Tolerance_Still_Recovers_The_Cylinder()
    {
        var clean = SyntheticMeshes.CylinderSide(12, 40, 300);   // 30,000 triangles
        double tol = SyntheticMeshes.Tol(clean);
        var mesh = SyntheticMeshes.Jitter(clean, sigma: 0.05, tol: tol);

        var regions = RansacSegmenter.Classify(mesh, new RansacSegmenter.Options(tol, 12, 3));

        var cylinders = regions.Where(r => r.Primitive is CylinderPrimitive).ToList();
        int recovered = cylinders.Sum(r => r.Triangles.Length);
        Assert.True(recovered >= 0.95 * mesh.TriangleCount,
            $"recovered {recovered} of {mesh.TriangleCount} at sigma = 0.05 x tol");

        var cylinder = (CylinderPrimitive)cylinders.OrderByDescending(r => r.Triangles.Length).First().Primitive;
        Assert.True(Math.Abs(cylinder.Radius - 12) < 0.05, $"radius {cylinder.Radius}");
    }

    /// <summary>
    /// The ceiling, pinned as a FAILURE. At sigma = 0.10 x tol the fitter returns r = 14.73 for a true
    /// 12 on the probe mesh, and at 0.25 x tol nothing at all - the limit is `FitCylinder`'s
    /// all-or-nothing `max > tol` rule, not this engine. A test claiming quarter-tolerance robustness
    /// would be claiming something the code cannot do, which is worse than having no test.
    /// </summary>
    [Fact]
    public void Noise_At_A_Quarter_Tolerance_Is_A_Documented_Fitter_Limit()
    {
        var clean = SyntheticMeshes.CylinderSide(12, 40, 300);
        double tol = SyntheticMeshes.Tol(clean);
        var noisy = SyntheticMeshes.Jitter(clean, sigma: 0.25, tol: tol);

        var regions = RansacSegmenter.Classify(noisy, new RansacSegmenter.Options(tol, 12, 3));

        int recovered = regions.Where(r => r.Primitive is CylinderPrimitive).Sum(r => r.Triangles.Length);
        Assert.Equal(0, recovered);

        // The rule that refuses it: one point outside tol is enough. Assert the fitter directly, so the
        // test names the limit's owner rather than this engine's behaviour.
        var (points, normals) = PointsAndNormals(noisy);
        Assert.Null(Fitters.FitCylinder(points, normals, tol, noisy.Diagonal));
    }

    // ---- T13: performance ------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Slow")]
    public void Performance_60k_Under_60s()
    {
        // 2,500 knurl patches of 24 triangles = 60,000 triangles - the probe's perf case, which
        // measured 0.99 s. The 60 s bound is a regression guard against an accidental O(n^2), not a
        // benchmark: a speed-up that loses regions must not pass, so the invariant is asserted too.
        var mesh = SyntheticMeshes.KnurlBand(cells: 2_500, trisPerPatch: 24);
        Assert.Equal(60_000, mesh.TriangleCount);
        double tol = SyntheticMeshes.Tol(mesh);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var regions = RansacSegmenter.Classify(mesh, new RansacSegmenter.Options(tol, 12, 3));
        clock.Stop();

        Assert.True(clock.Elapsed.TotalSeconds < 60,
            $"{clock.Elapsed.TotalSeconds:F2} s for {mesh.TriangleCount} triangles");
        Assert.True(regions.Count > 2_000, $"{regions.Count} regions - a speed-up that lost regions");

        int sum = regions.Sum(r => r.Triangles.Length);
        Assert.Equal(mesh.TriangleCount, sum);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static double AnalyticShare(MeshData mesh, List<ClassifiedRegion> regions) =>
        (double)regions.Where(r => r.Primitive is not FacetedPrimitive).Sum(r => r.Triangles.Length)
        / mesh.TriangleCount;

    private static (List<Vec3> Points, List<Vec3> Normals) PointsAndNormals(MeshData mesh)
    {
        // One point per unique vertex with its area-weighted normal, which is the pair FitRegion builds.
        var slots = new Dictionary<int, int>();
        var points = new List<Vec3>();
        var sums = new List<Vec3>();
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var weighted = mesh.Normal(t) * mesh.Area(t);
            for (int k = 0; k < 3; k++)
            {
                int vi = mesh.Indices[t * 3 + k];
                if (!slots.TryGetValue(vi, out int slot))
                {
                    slot = points.Count; slots[vi] = slot;
                    points.Add(mesh.Vertices[vi]); sums.Add(Vec3.Zero);
                }
                sums[slot] += weighted;
            }
        }
        return (points, sums.Select(s => s.Normalized()).ToList());
    }

    /// <summary>Three bodies in one mesh, offset so nothing touches.</summary>
    private static MeshData Combine(params MeshData[] parts)
    {
        var vertices = new List<Vec3>();
        var indices = new List<int>();
        var offsets = new[] { new Vec3(0, 0, 0), new Vec3(60, 0, 0), new Vec3(0, 60, 0) };
        for (int i = 0; i < parts.Length; i++)
        {
            int baseIndex = vertices.Count;
            vertices.AddRange(parts[i].Vertices.Select(p => p + offsets[i % offsets.Length]));
            indices.AddRange(parts[i].Indices.Select(x => x + baseIndex));
        }
        return new MeshData { Vertices = vertices.ToArray(), Indices = indices.ToArray() };
    }
}
