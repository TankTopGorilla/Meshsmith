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

            Primitive? prim = null;
            for (int it = 0; it < options.RefitIterations; it++)
            {
                var fit = FitRegion(mesh, region, options.Tolerance, mesh.Diagonal);
                if (fit is null) { prim = null; break; }
                prim = fit;

                var kept = region.Where(t =>
                    Math.Abs(fit.DistanceTo(mesh.Centroid(t))) <= options.Tolerance).ToArray();

                if (kept.Length == 0) { prim = null; break; }
                if (kept.Length == region.Length) { region = kept; break; }
                region = kept;
            }

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

        return null;
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
