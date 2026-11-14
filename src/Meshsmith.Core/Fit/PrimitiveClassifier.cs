using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Meshsmith.Core.Seg;

namespace Meshsmith.Core.Fit;

public sealed record ClassifiedRegion(
    int[] Triangles, Primitive Primitive, Vec3 AverageNormal, double Area);

public static class PrimitiveClassifier
{
    public sealed record Options(double Tolerance, double DihedralDeg, int RefitIterations)
    {
        public static Options Default(double tolerance) => new(tolerance, 12, 3);

        /// <summary>
        /// The second pass over the residual: re-grow it with a relaxed threshold, fit, and split what
        /// still does not fit. On by default; the rollback lever and the monotonicity test switch it.
        /// </summary>
        public bool SecondChance { get; init; } = true;

        /// <summary>
        /// How far the residual may re-grow before it is re-fitted. 20 deg is the measured value: a
        /// 48x24 torus steps 15 deg in v and a 12 deg grower fragments it, while 20 hands the body over
        /// (measured: 32 seeds of 96 triangles at 12 deg, 2 of 1536 at 20 deg).
        /// </summary>
        public double SecondChanceDihedralDeg { get; init; } = 20;

        /// <summary>
        /// The split ladder for a piece that still does not fit. The first level IS the primary
        /// threshold, so the first split reproduces today's segmentation exactly - measured: a 1-level
        /// ladder changes no part at all, and the 12/6/3 ladder is where every gain comes from.
        /// </summary>
        public IReadOnlyList<double> SecondChanceLevels { get; init; } = new[] { 12.0, 6.0, 3.0 };

        /// <summary>
        /// Pieces below this are never accepted. Measured on clamp's jaw: without a floor, a 3 degree
        /// split "covers" 78.7 % of it with 963 one-triangle planes, and the coverage metric cannot tell
        /// that from recognition. 16 removes the 1..15-triangle crumbs and keeps the 33-triangle
        /// curvature patches (32 costs another 6.2 points on clamp).
        /// </summary>
        public int MinPieceTriangles { get; init; } = 16;
    }

    public static List<ClassifiedRegion> Classify(MeshData mesh, Options options,
                                                  CancellationToken cancellationToken = default)
    {
        var topo = MeshTopology.Build(mesh);
        var seeded = RegionGrower.Grow(mesh, topo, options.DihedralDeg);
        var result = new List<ClassifiedRegion>();
        var assigned = new bool[mesh.TriangleCount];

        foreach (var seed in seeded.OrderByDescending(r => r.Length))
        {
            // Region growing is where most of the wall time goes on a dense mesh, so the token is
            // observed per seed rather than only between pipeline stages.
            cancellationToken.ThrowIfCancellationRequested();

            var region = seed.Where(t => !assigned[t]).ToArray();
            if (region.Length == 0) continue;

            var prim = FitWithRefit(mesh, region, options, out region);
            if (prim is null) continue;

            foreach (var t in region) assigned[t] = true;
            result.Add(new ClassifiedRegion(region, prim with { SourceTriangles = region.Length },
                AverageNormal(mesh, region), Area(mesh, region)));
        }

        var leftovers = Enumerable.Range(0, mesh.TriangleCount).Where(t => !assigned[t]).ToArray();
        if (leftovers.Length > 0)
            result.Add(new ClassifiedRegion(leftovers,
                new FacetedPrimitive { Triangles = leftovers.Length, SourceTriangles = leftovers.Length },
                AverageNormal(mesh, leftovers), Area(mesh, leftovers)));

        // The residual gets a second chance BEFORE the merge and the connectivity split: the pass can
        // hand back a region that spans two patches (measured on ATLAS: 19 triangles in 2 pieces), and
        // it creates coplanar planes that MergeSameSurface has to fold together. Running it last would
        // recreate the R1.2 defect and leave duplicate coplanar faces in the export.
        if (options.SecondChance)
            result = SecondChance(mesh, topo, result, options, cancellationToken);

        return SplitByConnectivity(mesh, topo, MergeSameSurface(mesh, result, options.Tolerance));
    }

    public static Primitive? FitRegion(MeshData mesh, int[] triangles, double tol, double diag)
    {
        // The fits below walk a point list and a normal list together, so the two must be built as
        // a pair: one point per unique vertex, each carrying the area-weighted normal of the
        // region's triangles that touch it. One normal per *triangle* is simply a different length
        // (a four-triangle strip of a cylinder has six vertices), which made FitCylinder read past
        // the end of its list and fail the conversion on real meshes.
        var (points, normals) = UniquePointsAndNormals(mesh, triangles);

        var plane = Fitters.FitPlane(points, tol);
        if (plane is not null) return plane;

        var cylinder = Fitters.FitCylinder(points, normals, tol, diag);
        if (cylinder is not null) return cylinder;

        var cone = Fitters.FitCone(points, normals, tol, diag);
        if (cone is not null) return cone;

        // AFTER cylinder and cone, and measured to matter: a sphere fit accepts a cylinder and a cone
        // EXACTLY (two circles lie on a sphere), so trying it earlier drops pin from 100 % to 32.5 %
        // and IKI from 99.2 % to 75.9 %. A torus is the more specific surface, so it goes first.
        var torus = Fitters.FitTorus(points, normals, tol, diag);
        if (torus is not null) return torus;

        var sphere = Fitters.FitSphere(points, tol, diag);
        if (sphere is not null && PlausibleSphere(points, sphere, diag)) return sphere;

        return null;
    }

    /// <summary>
    /// Fit a region and drop the triangles the fit does not explain, up to RefitIterations times.
    /// Extracted from the region loop because the second-chance pass must apply exactly the same rule:
    /// a piece refitted differently from a primary seed would move a region for a reason nobody can
    /// see in the report.
    /// </summary>
    private static Primitive? FitWithRefit(MeshData mesh, int[] region, Options options, out int[] kept)
    {
        Primitive? prim = null;
        for (int it = 0; it < options.RefitIterations; it++)
        {
            var fit = FitRegion(mesh, region, options.Tolerance, mesh.Diagonal);
            if (fit is null) { prim = null; break; }
            prim = fit;

            // A torus's DistanceTo is one sagitta in from every direction at a facet CENTROID, so
            // the fit's own residual test passes at the vertices while the centroid test needs
            // twice the tolerance - measured: at 64x32 the exact fit keeps 0 of 4096 triangles at
            // 1x and all of them at 2x. A cylinder has the same shape of problem (1.14x at 64
            // segments); the existing code already tolerates that, this just names it.
            double refitTol = fit is TorusPrimitive ? 2 * options.Tolerance : options.Tolerance;
            var keep = region.Where(t => Math.Abs(fit.DistanceTo(mesh.Centroid(t))) <= refitTol).ToArray();

            if (keep.Length == 0) { prim = null; break; }
            if (keep.Length == region.Length) { region = keep; break; }
            region = keep;
        }
        kept = region;
        return prim;
    }

    /// <summary>
    /// The residual's second chance. Every seed that failed left its triangles in one faceted region;
    /// this pass re-grows them with a relaxed threshold, refits, and splits what still does not fit at
    /// progressively finer thresholds. It is MONOTONE by construction: a piece is accepted only when
    /// the fit keeps at least as many triangles as the piece would take away from analytic regions,
    /// so coverage (RecognitionStats counts triangles in non-faceted regions) can only rise.
    ///
    /// Measured on the corpus (floor 16, levels 12/6/3, grower 20): ATLAS 64.746 -> 79.036 %,
    /// clamp 34.302 -> 55.429 %, clamp_body0 33.865 -> 57.939 %, and pin / P1 / IKI / washer / the
    /// four torus fixtures are byte-identical - their residual is empty or recovers nothing.
    /// </summary>
    private static List<ClassifiedRegion> SecondChance(MeshData mesh, MeshTopology topo,
                                                       List<ClassifiedRegion> regions, Options options,
                                                       CancellationToken cancellationToken)
    {
        var residual = regions.Where(r => r.Primitive is FacetedPrimitive)
                              .SelectMany(r => r.Triangles).ToArray();
        if (residual.Length == 0) return regions;

        int n = mesh.TriangleCount;
        var owner = Enumerable.Repeat(-1, n).ToArray();       // region index per triangle
        for (int i = 0; i < regions.Count; i++)
            foreach (var t in regions[i].Triangles) owner[t] = i;

        var taken = new bool[n];                              // won by a second-chance region
        var accepted = new List<ClassifiedRegion>();
        int floor = Math.Max(1, options.MinPieceTriangles);

        void Refine(int[] candidate, int level)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.Length < floor) return;

            var allowed = new bool[n];
            foreach (var t in candidate) allowed[t] = true;

            double dihedral = level == 0
                ? options.SecondChanceDihedralDeg
                : options.SecondChanceLevels[level - 1];
            var pieces = RegionGrower.Grow(mesh, topo, dihedral, allowed);

            foreach (var piece in pieces.OrderByDescending(p => p.Length))
            {
                if (piece.Length < floor) continue;

                var prim = FitWithRefit(mesh, piece, options, out var kept);
                if (prim is not null)
                {
                    int owned = piece.Count(t => owner[t] >= 0 && !(regions[owner[t]].Primitive is FacetedPrimitive));
                    if (kept.Length >= owned)
                    {
                        foreach (var t in kept) taken[t] = true;
                        accepted.Add(new ClassifiedRegion(kept, prim with { SourceTriangles = kept.Length },
                                                          AverageNormal(mesh, kept), Area(mesh, kept)));
                        continue;
                    }
                }

                if (level + 1 <= options.SecondChanceLevels.Count) Refine(piece, level + 1);
            }
        }

        Refine(Dilate(mesh, topo, regions, owner, residual), 0);

        if (accepted.Count == 0) return regions;

        // Rebuild: a region keeps only what no accepted piece took over. Dropping empty regions keeps
        // the report honest - a face with no triangles is not a face.
        var rebuilt = new List<ClassifiedRegion>(regions.Count + accepted.Count);
        foreach (var region in regions)
        {
            var rest = region.Triangles.Where(t => !taken[t]).ToArray();
            if (rest.Length == 0) continue;
            if (rest.Length == region.Triangles.Length) { rebuilt.Add(region); continue; }
            rebuilt.Add(new ClassifiedRegion(rest, Reprime(region.Primitive, rest.Length),
                                             AverageNormal(mesh, rest), Area(mesh, rest)));
        }
        rebuilt.AddRange(accepted);
        return rebuilt;
    }

    /// <summary>
    /// The residual plus every region that touches it - ONE round, by whole regions. Ring-by-ring mesh
    /// dilation was measured to be strictly worse: a region can be hundreds of triangles deep (the
    /// 48x24 torus's cone band is 96 triangles wide), so a 2..4-ring reach never crosses it, while a
    /// whole-region round does. One round measured equal to two on clamp and ATLAS.
    /// </summary>
    private static int[] Dilate(MeshData mesh, MeshTopology topo, List<ClassifiedRegion> regions,
                                int[] owner, int[] residual)
    {
        var inSet = new bool[mesh.TriangleCount];
        foreach (var t in residual) inSet[t] = true;

        foreach (var t in residual)
        {
            for (int e = 0; e < 3; e++)
            {
                int nb = topo.Neighbor[t * 3 + e];
                if (nb < 0 || inSet[nb]) continue;
                int reg = owner[nb];
                if (reg < 0 || regions[reg].Primitive is FacetedPrimitive) continue;
                foreach (var u in regions[reg].Triangles) inSet[u] = true;
            }
        }

        var result = new List<int>(residual.Length * 2);
        for (int t = 0; t < inSet.Length; t++) if (inSet[t]) result.Add(t);
        return result.ToArray();
    }

    /// <summary>
    /// A sphere fit is exact on anything co-radial, so the residual cannot reject a torus band or a
    /// 2-ring cylinder. Two geometric facts separate them, both measured on every synthetic body and
    /// on clamp: a real sphere shows many distinct LATITUDES about its own polar axis (9..25 for a
    /// 8x16..24x48 sphere) while a band shows exactly 2; and a sphere region on a part is never a
    /// 0.4 mm speck on a 60 mm part.
    /// </summary>
    private static bool PlausibleSphere(IReadOnlyList<Vec3> points, SpherePrimitive sphere, double diag)
    {
        if (sphere.Radius < 1e-4 * diag || sphere.Radius > 3 * diag) return false;

        var latitudes = new List<double>(points.Count);
        foreach (var p in points)
            latitudes.Add(Math.Asin(Math.Clamp((p - sphere.Center).Normalized().Z, -1, 1)));
        latitudes.Sort();

        int rings = 0;
        for (int i = 0; i < latitudes.Count; i++)
            if (i == 0 || latitudes[i] - latitudes[i - 1] > 1e-3) rings++;
        return rings >= 3;
    }

    /// <summary>One point per unique vertex in the region, paired with that vertex's normal —
    /// the area-weighted sum of the normals of the region's own triangles around it, so a vertex
    /// shared with a neighbouring surface does not drag in a normal from outside the region.</summary>
    private static (List<Vec3> Points, List<Vec3> Normals) UniquePointsAndNormals(MeshData mesh, int[] triangles)
    {
        var slots = new Dictionary<int, int>();
        var points = new List<Vec3>();
        var sums = new List<Vec3>();

        foreach (var t in triangles)
        {
            var weighted = mesh.Normal(t) * mesh.Area(t);
            for (int k = 0; k < 3; k++)
            {
                int vi = mesh.Indices[t * 3 + k];
                if (!slots.TryGetValue(vi, out int slot))
                {
                    slot = points.Count;
                    slots[vi] = slot;
                    points.Add(mesh.Vertices[vi]);
                    sums.Add(Vec3.Zero);
                }
                sums[slot] += weighted;
            }
        }

        var normals = new List<Vec3>(sums.Count);
        foreach (var sum in sums) normals.Add(sum.Normalized());

        return (points, normals);
    }

    private static Vec3 AverageNormal(MeshData mesh, int[] triangles)
    {
        var sum = Vec3.Zero;
        foreach (var t in triangles) sum += mesh.Normal(t) * mesh.Area(t);
        return sum.Normalized();
    }

    private static double Area(MeshData mesh, int[] triangles) => triangles.Sum(mesh.Area);

    private static List<ClassifiedRegion> MergeSameSurface(MeshData mesh, List<ClassifiedRegion> regions, double tol)
    {
        // MVP: merge only planes that are coplanar within tolerance.
        var merged = new List<ClassifiedRegion>();
        foreach (var r in regions)
        {
            var target = merged.FirstOrDefault(m => SamePlane(m, r, tol));
            if (target is null) { merged.Add(r); continue; }
            var combined = target.Triangles.Concat(r.Triangles).ToArray();
            merged[merged.IndexOf(target)] = target with
            {
                Triangles = combined,
                Primitive = target.Primitive with { SourceTriangles = combined.Length },
                Area = target.Area + r.Area
            };
        }
        return merged;
    }

    private static bool SamePlane(ClassifiedRegion a, ClassifiedRegion b, double tol) =>
        a.Primitive is PlanePrimitive pa && b.Primitive is PlanePrimitive pb &&
        Math.Abs(Vec3.Dot(pa.Normal, pb.Normal)) > 0.9999 &&
        Math.Abs(Vec3.Dot(pb.Point - pa.Point, pa.Normal)) <= tol;

    /// <summary>
    /// The connected components of a triangle set, by shared mesh edge. Two triangles that only touch
    /// at a vertex are NOT connected: a vertex is how two bodies meet in a welded mesh, not how a shell
    /// is sewn. Ordered by smallest triangle index, so region ids stay deterministic from run to run.
    /// </summary>
    private static List<int[]> ConnectedPieces(MeshTopology topo, int[] triangles)
    {
        if (triangles.Length == 0) return new List<int[]>();
        if (triangles.Length == 1) return new List<int[]> { triangles };

        var parent = new Dictionary<int, int>(triangles.Length);
        foreach (var t in triangles) parent[t] = t;

        int Find(int t)
        {
            while (parent[t] != t) { parent[t] = parent[parent[t]]; t = parent[t]; }
            return t;
        }

        var inSet = new HashSet<int>(triangles);
        foreach (var t in triangles)
            for (int e = 0; e < 3; e++)
            {
                int n = topo.Neighbor[t * 3 + e];
                if (n < 0 || !inSet.Contains(n)) continue;
                int a = Find(t), b = Find(n);
                if (a != b) parent[a] = b;
            }

        return triangles.GroupBy(Find)
                        .Select(g => g.OrderBy(t => t).ToArray())
                        .OrderBy(p => p[0])
                        .ToList();
    }

    /// <summary>The same surface, re-stated for a smaller triangle set.</summary>
    private static Primitive Reprime(Primitive primitive, int triangles) =>
        primitive is FacetedPrimitive
            ? new FacetedPrimitive { Triangles = triangles, SourceTriangles = triangles }
            : primitive with { SourceTriangles = triangles };

    /// <summary>
    /// Splits every region into the connected patches it really is. A region is built from grown seeds
    /// and from coplanar merges, and neither looks at the mesh: MergeSameSurface joins two faces that
    /// lie on the same plane even when they belong to different bodies, and the leftover region holds
    /// every unrecognised triangle of the part. One region becomes one face, and a face has to be a
    /// single connected patch - so both cases are split before the pipeline sees them. Without this,
    /// RegionComponents.Group reports one component for a whole multi-body part (measured on clamp,
    /// IKI P1, IKI Part 1 and ATLAS) and the per-body degradation never engages.
    /// </summary>
    private static List<ClassifiedRegion> SplitByConnectivity(MeshData mesh, MeshTopology topo,
                                                             List<ClassifiedRegion> regions)
    {
        var split = new List<ClassifiedRegion>(regions.Count);
        foreach (var region in regions)
        {
            foreach (var piece in ConnectedPieces(topo, region.Triangles))
            {
                if (piece.Length == region.Triangles.Length) { split.Add(region); continue; }
                split.Add(new ClassifiedRegion(piece, Reprime(region.Primitive, piece.Length),
                                               AverageNormal(mesh, piece), Area(mesh, piece)));
            }
        }
        return split;
    }
}
