using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Seg;

/// <summary>
/// A primitive-hypothesis segmenter: flood first, then sequential RANSAC over what the flood refused.
///
/// <para><b>Why two tiers, not pure RANSAC.</b> Measured on the R2 drum: pure RANSAC recovers 87.8 %
/// of one connected shallow-ribbed body, while flood alone recovers 21.9 % of it - but on a band of
/// DISCONNECTED patches flood recovers 100 % in 0.01 s. Replacing flood wholesale would regress every
/// part it already handles. So tier 1 is today's classifier loop, unchanged; tier 2 only ever ADDS
/// regions in the residual tier 1 refused; tier 3 rescues the leftover's connected patches. That keeps
/// the change monotone and makes a regression attributable to one tier.</para>
///
/// <para><b>Why local blocks, not minimal sets.</b> Every fitter here is least-squares with
/// all-or-nothing rejection and a minimum point count (cylinder 6, cone/sphere 8, torus 32), so a
/// 3-point minimal set is not a valid sample for anything but a plane. And uniform random sampling is
/// hopeless when thousands of surfaces compete: at an inlier fraction of 1/5000 a 3-point plane needs
/// ~10^10 draws. A spatial block holds one to three surfaces, so a single draw suffices - measured
/// 2,000 of 2,000 knurl patches recovered.</para>
///
/// <para><b>Determinism.</b> No <see cref="System.Random"/>: the seed order comes from xorshift64* with
/// a fixed constant, consumed by one Fisher-Yates shuffle at construction, and every iteration is over
/// ascending arrays. Ties break on a total order (score, then type rank, then seed index). Two runs on
/// the same mesh are byte-identical, which T7 pins.</para>
/// </summary>
public static class RansacSegmenter
{
    public sealed record Options(double Tolerance, double DihedralDeg, int RefitIterations)
    {
        public static Options Default(double tolerance) => new(tolerance, 12, 3);

        /// <summary>Pieces below this are never accepted, for the same reason the classifier's floor
        /// exists: without one, a 3 degree split "covers" 78.7 % of clamp's jaw with 963 one-triangle
        /// planes and coverage cannot tell that from recognition. The floor works both ways - the
        /// 8-triangle knurl patch is refused entirely and the 24-triangle one is recovered.</summary>
        public int Floor { get; init; } = 16;

        /// <summary>Cells per longest axis of the centroid grid.</summary>
        public int GridDivisions { get; init; } = 64;

        /// <summary>How many points a local block must hold before it is fitted. Below this the fit
        /// is dominated by whatever else shares the cell.</summary>
        public int LocalMinPoints { get; init; } = 48;

        /// <summary>The widest ring the block may grow to. A pathological sparse region degrades to
        /// "no hypothesis here", never to a block that spans the part.</summary>
        public int LocalMaxRing { get; init; } = 4;

        /// <summary>
        /// The local screen, as a share of the block. Measured both ways: at 0.60 the 24-triangle knurl
        /// band comes back as 2,000 plane regions with a median of 24 triangles and 100 % of the band
        /// recovered; with the screen off it "recovers" 94.9 % but as 602 regions absorbing 43,729
        /// triangles - 72.9 triangles per 24-triangle cell, i.e. regions merged straight across patch
        /// boundaries. Splitting a patch is the defect this number exists to prevent, so the share
        /// stays. The absolute <see cref="Floor"/> count applies on top of it.
        /// </summary>
        public double LocalMinShare { get; init; } = 0.60;

        /// <summary>Cap on the points a single local fit sees: a 400-point least-squares solve over a
        /// block that big is as informative as 4,000 and 10x cheaper.</summary>
        public int BlockCap { get; init; } = 400;

        /// <summary>Deterministic strided triples per seed for the plane hypotheses. Exactly 3 points
        /// make a plane, so no RNG is needed in the hot loop.</summary>
        public int PlaneTriplesPerSeed { get; init; } = 24;

        /// <summary>
        /// Fits are GENERATED at this multiple of the scoring tolerance and then SCORED at 1x. Every
        /// fitter rejects a set outright if one point exceeds its tolerance, so a noisy block would
        /// produce no hypothesis at all at 1x; the looser generation costs nothing because nothing is
        /// accepted on the fit's own residual.
        /// </summary>
        public double FitTolMultiplier { get; init; } = 2.0;

        /// <summary>Seeds sampled per tier-A round. Bounds one round's work: a mesh that defeats the
        /// size screen degrades to tier B's single pass, not a quadratic hunt.</summary>
        public int TierASeeds { get; init; } = 384;

        /// <summary>Tier A accepts a region only while it is at least this share of what is left.
        /// Without it the round keeps handing back crumbs long after the big surfaces are gone.</summary>
        public double TierAMinShare { get; init; } = 0.02;

        /// <summary>The hard bound on hypotheses for a whole run (measured peak: 59,063 of 120,000).</summary>
        public int MaxHypotheses { get; init; } = 120_000;

        /// <summary>Tier 3: split the leftover into connected patches and offer each to the fitters.
        /// This is what turns the knurl's cells into editable planes instead of one faceted mass.</summary>
        public bool RescueComponents { get; init; } = true;

        /// <summary>
        /// OFF, and measured to be right. A local torus hypothesis is null for every patch of 600
        /// triangles or less (32: structural guard; 600: null; 2000: FitMax 1.18; 4000: 0.56) and only
        /// the WHOLE tube fits (16,000 of 16,384 -> R=19.9948, r=6.9921). Leaving it on costs 16,768
        /// wasted fits on a torus mesh and recovers nothing; tori come from tier 1, whose flood pieces
        /// are whole bodies.
        /// </summary>
        public bool EnableTorus { get; init; }

        public bool EnableCone { get; init; } = true;

        /// <summary>The xorshift64* seed for the one shuffle. A fixed constant, not a clock.</summary>
        public ulong Seed { get; init; } = 0x52414E5341430001UL;

        /// <summary>The cap on a coherent patch used as a torus domain (only read when
        /// <see cref="EnableTorus"/> is on).</summary>
        public int TorusPatchCap { get; init; } = 600;

        /// <summary>Normal continuity for that patch, in degrees.</summary>
        public double TorusPatchDihedralDeg { get; init; } = 25;
    }

    public static List<ClassifiedRegion> Classify(MeshData mesh, Options options,
                                                  CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(options);

        var run = new Run(mesh, options, cancellationToken);
        return run.Execute();
    }

    /// <summary>
    /// One classification. A class rather than a pile of locals because the scoring loop needs the
    /// grid, the assignment flags and the region list in the same place, and threading eight
    /// parameters through every helper is how a segmenter becomes unreadable.
    /// </summary>
    private sealed class Run
    {
        private readonly MeshData _mesh;
        private readonly Options _o;
        private readonly CancellationToken _ct;
        private readonly MeshTopology _topo;
        private readonly CentroidGrid _grid;
        private readonly Vec3[] _centroid;
        private readonly Vec3[] _normal;

        private readonly bool[] _assigned;      // true = some region owns this triangle
        private readonly bool[] _tried;         // tier B termination proof: this seed produced nothing
        private readonly int[] _order;          // one fixed permutation

        private readonly List<ClassifiedRegion> _regions = new();
        private int _hypotheses;

        public Run(MeshData mesh, Options options, CancellationToken ct)
        {
            _mesh = mesh;
            _o = options;
            _ct = ct;

            int n = mesh.TriangleCount;
            _centroid = new Vec3[n];
            _normal = new Vec3[n];
            for (int t = 0; t < n; t++)
            {
                _centroid[t] = mesh.Centroid(t);
                _normal[t] = mesh.Normal(t);
            }

            _topo = MeshTopology.Build(mesh);
            _grid = new CentroidGrid(mesh, options.GridDivisions);
            _assigned = new bool[n];
            _tried = new bool[n];
            _order = Shuffled(n, options.Seed);
        }

        public List<ClassifiedRegion> Execute()
        {
            Flood();                                     // TIER 1
            RansacOnResidual();                          // TIER 2
            if (_o.RescueComponents) Rescue();            // TIER 3
            FinishLeftover();

            // The same tail the classifier uses, so both segmenters produce one shape of region: a
            // coplanar merge for the crumb planes, then one connected patch per face.
            var merged = PrimitiveClassifier.MergeSameSurface(_mesh, _regions, _o.Tolerance);
            return PrimitiveClassifier.SplitByConnectivity(_mesh, _topo, merged);
        }

        // ---- Tier 1: today's loop, unchanged --------------------------------------------------

        /// <summary>
        /// The classifier's own region loop, reused rather than reimplemented: grow at the primary
        /// dihedral, largest piece first, <see cref="PrimitiveClassifier.FitRegion"/> in its documented
        /// order, then refit dropping what the fit does not explain. Pieces that no fit explains are
        /// collected as the residual tier 2 works on.
        /// </summary>
        private void Flood()
        {
            var seeded = RegionGrower.Grow(_mesh, _topo, _o.DihedralDeg);
            var residual = new List<int>();

            foreach (var piece in seeded.OrderByDescending(r => r.Length))
            {
                _ct.ThrowIfCancellationRequested();

                var region = piece.Where(t => !_assigned[t]).ToArray();
                if (region.Length == 0) continue;

                if (region.Length < _o.Floor) { residual.AddRange(region); continue; }

                var prim = FitWithRefit(region, out var kept);
                if (prim is null) { residual.AddRange(piece.Where(t => !_assigned[t])); continue; }

                foreach (var t in kept) _assigned[t] = true;
                _regions.Add(new ClassifiedRegion(kept,
                    prim with { SourceTriangles = kept.Length },
                    PrimitiveClassifier.AverageNormal(_mesh, kept),
                    PrimitiveClassifier.Area(_mesh, kept)));
            }

            _residual = residual;
        }

        private List<int> _residual = new();

        /// <summary>
        /// Fit a piece and drop the triangles the fit does not explain, up to
        /// <see cref="Options.RefitIterations"/> times - <see cref="PrimitiveClassifier"/>'s rule,
        /// including the 2x refit tolerance for a torus (its DistanceTo is one sagitta in from every
        /// direction at a facet centroid).
        /// </summary>
        private Primitive? FitWithRefit(int[] region, out int[] kept)
        {
            Primitive? prim = null;
            for (int iteration = 0; iteration < _o.RefitIterations; iteration++)
            {
                var fit = PrimitiveClassifier.FitRegion(_mesh, region, _o.Tolerance, _mesh.Diagonal);
                if (fit is null) { prim = null; break; }
                prim = fit;

                double refitTol = fit is TorusPrimitive ? 2 * _o.Tolerance : _o.Tolerance;
                var keep = region.Where(t => Math.Abs(fit.DistanceTo(_centroid[t])) <= refitTol).ToArray();

                if (keep.Length == 0) { prim = null; break; }
                if (keep.Length == region.Length) { region = keep; break; }
                region = keep;
            }
            kept = region;
            return prim;
        }

        // ---- Tier 2: sequential RANSAC over the residual ---------------------------------------

        private void RansacOnResidual()
        {
            int n = _mesh.TriangleCount;
            if (_residual.Count < _o.Floor) return;

            // A face must be a CONNECTED patch of at least Floor triangles, and connectivity is a
            // property of the mesh, not of any hypothesis. So a mesh component smaller than the floor
            // can never become an analytic region under ANY model, and no seed inside it is worth a
            // hypothesis. Pruning that up front is what keeps tier B linear: on KnurlBand(2,000, 8)
            // the 16,000 band triangles are 2,000 components of 8, and without this every one of them
            // is a tier-B seed that scores the whole band and is then refused - measured as the suite
            // going 95 s -> 420 s. With it, they are simply excluded and stay faceted.
            var residualSet = _residual.ToArray();
            foreach (var component in Components(residualSet))
            {
                if (component.Length >= _o.Floor) continue;
                foreach (var t in component) _tried[t] = true;
            }

            // Tier A: one hypothesis per seed; accept only the round's single global best, and only
            // while it is big. One region per round makes order-dependence impossible - a weak
            // hypothesis cannot pre-empt a strong one, because nothing is accepted until it IS the best.
            int remainingMinimum = Math.Max(_o.Floor, n / 100);
            while (_hypotheses < _o.MaxHypotheses)
            {
                _ct.ThrowIfCancellationRequested();

                int remaining = n - AssignedCount();
                if (remaining < remainingMinimum) return;

                Candidate? best = null;
                int stride = Math.Max(1, n / Math.Max(1, _o.TierASeeds));
                for (int k = 0; k < _o.TierASeeds && _hypotheses < _o.MaxHypotheses; k++)
                {
                    int t = _order[(int)((long)k * stride % n)];
                    if (_assigned[t] || _tried[t]) continue;

                    var candidate = Hypothesise(t);
                    if (candidate is null) continue;
                    if (best is null || Better(candidate, best)) best = candidate;
                }

                if (best is null) break;
                double need = Math.Max(_o.Floor, remaining * _o.TierAMinShare);
                if (best.Score < need) break;

                // A hypothesis whose every connected component is under the floor assigns nothing.
                // That is this round's evidence that tier A has no BIG region left (the knurl's
                // 16,000-triangle band is one cylinder score made of 2,000 eight-triangle patches),
                // so the long tail belongs to tier B rather than to another identical round.
                if (Accept(best) == 0) break;
            }

            // Tier B: the long tail. Fixed permutation, one hypothesis per seed, floor only, and a
            // seed that produced nothing is marked so the pass terminates.
            foreach (int t in _order)
            {
                if (_hypotheses >= _o.MaxHypotheses) break;
                if (_assigned[t] || _tried[t]) continue;

                if ((_hypotheses & 0x3FF) == 0) _ct.ThrowIfCancellationRequested();

                var candidate = Hypothesise(t);

                // A candidate is accepted only when it actually assigns something. Score is a GLOBAL
                // count, but Accept keeps only components at or above the floor, so a global score of
                // 16,000 can assign nothing at all; treating that as success would leave the seed
                // unmarked and re-hypothesise it forever.
                if (candidate is not null && candidate.Score >= _o.Floor && Accept(candidate) > 0) continue;
                _tried[t] = true;
            }
        }

        private int AssignedCount()
        {
            int count = 0;
            for (int t = 0; t < _assigned.Length; t++) if (_assigned[t]) count++;
            return count;
        }

        private sealed class Candidate
        {
            public required Primitive Model { get; init; }
            public int Score { get; set; }
            public int[] Triangles { get; set; } = Array.Empty<int>();
            public int SeedIndex { get; init; }
        }

        /// <summary>
        /// One hypothesis from one seed: widen a centroid block until it holds enough points, then fit
        /// a plane from deterministic strided triples and every curved primitive by least squares over
        /// the whole block. Every candidate is scored globally before it is returned.
        /// </summary>
        private Candidate? Hypothesise(int seed)
        {
            _hypotheses++;

            var block = new List<int>();
            int cell = _grid.CellOf(_centroid[seed]);
            for (int ring = 1; ring <= _o.LocalMaxRing; ring++)
            {
                block.Clear();
                _grid.Collect(cell, ring, block);
                if (block.Count >= _o.LocalMinPoints) break;
            }
            if (block.Count < 8) { _tried[seed] = true; return null; }

            block.Sort();
            if (block.Count > _o.BlockCap) block = Strided(block, _o.BlockCap);

            var points = new List<Vec3>(block.Count);
            var normals = new List<Vec3>(block.Count);
            foreach (int t in block) { points.Add(_centroid[t]); normals.Add(_normal[t]); }

            double genTol = _o.Tolerance * _o.FitTolMultiplier;
            Candidate? best = null;

            void Consider(Primitive? model, int seedIndex)
            {
                if (model is null) return;

                double tol = RefitTol(model);
                int inliers = 0;
                foreach (int t in block)
                    if (Math.Abs(model.DistanceTo(_centroid[t])) <= tol) inliers++;

                // Two screens: the absolute floor, then the block share. A model that explains only a
                // corner of its own block is a artefact of the block, not a surface.
                if (inliers < _o.Floor) return;
                if (inliers < _o.LocalMinShare * block.Count) return;

                var candidate = new Candidate { Model = model, SeedIndex = seedIndex };
                candidate.Score = GlobalScore(model, tol, candidate);
                if (best is null || Better(candidate, best)) best = candidate;
            }

            // PLANE: exact planes through strided triples, best one, then one least-squares refinement.
            // Curved primitives cannot use triples (FitCylinder needs 6 points and a normal field), so
            // the plane is the only type sampled this way - measured as the design that works.
            PlanePrimitive? bestPlane = null;
            int bestPlaneInliers = 0;
            int tripleStride = Math.Max(1, block.Count / Math.Max(1, _o.PlaneTriplesPerSeed));
            for (int i = 0; i + 2 < block.Count; i += tripleStride)
            {
                var a = points[i];
                var b = points[(i + tripleStride) % block.Count];
                var c = points[(i + 2 * tripleStride) % block.Count];
                var normal = Vec3.Cross(b - a, c - a).Normalized();
                if (normal.Length < 0.5) continue;                     // collinear triple

                var plane = new PlanePrimitive { Point = a, Normal = normal };
                int inliers = 0;
                foreach (int t in block)
                    if (Math.Abs(plane.DistanceTo(_centroid[t])) <= _o.Tolerance) inliers++;
                if (inliers > bestPlaneInliers) { bestPlaneInliers = inliers; bestPlane = plane; }
            }
            if (bestPlane is not null && bestPlaneInliers >= 3)
            {
                var inlierPoints = new List<Vec3>(bestPlaneInliers);
                foreach (int t in block)
                    if (Math.Abs(bestPlane.DistanceTo(_centroid[t])) <= _o.Tolerance)
                        inlierPoints.Add(_centroid[t]);
                if (inlierPoints.Count >= 3)
                    bestPlane = Fitters.FitPlane(inlierPoints, _o.Tolerance) ?? bestPlane;
            }
            Consider(bestPlane, seed);

            // CURVED: one least-squares fit over the whole block, at the looser generation tolerance.
            Consider(Fitters.FitCylinder(points, normals, genTol, _mesh.Diagonal), seed);
            if (_o.EnableCone)
                Consider(Fitters.FitCone(points, normals, genTol, _mesh.Diagonal), seed);

            // Sphere LAST and guarded: FitSphere has no discriminating power of its own (two rings of a
            // cylinder fit a sphere exactly), so the latitude rule is what separates a real sphere.
            var sphere = Fitters.FitSphere(points, genTol, _mesh.Diagonal);
            if (sphere is not null && PrimitiveClassifier.PlausibleSphere(points, sphere, _mesh.Diagonal))
                Consider(sphere, seed);

            // TORUS: off by default (see Options.EnableTorus). When on, it needs a normal-coherent
            // patch, not a cube - a spatial block cuts the tube at arbitrary places and never fits.
            if (_o.EnableTorus && block.Count >= 32 && Fitters.FitPlane(points, _o.Tolerance) is null)
            {
                var patch = CoherentPatch(seed, _o.TorusPatchCap, _o.TorusPatchDihedralDeg);
                if (patch.Count >= 32)
                {
                    var patchPoints = new List<Vec3>(patch.Count);
                    var patchNormals = new List<Vec3>(patch.Count);
                    foreach (int t in patch) { patchPoints.Add(_centroid[t]); patchNormals.Add(_normal[t]); }
                    Consider(Fitters.FitTorus(patchPoints, patchNormals, genTol, _mesh.Diagonal), seed);
                }
            }

            // A plane must not win merely by being the cheapest fit: if a curved model explains nearly
            // as much of the unassigned set, the curved one is the truth (a cylinder's facets are only
            // coplanar in pairs).
            if (best is not null && best.Model is PlanePrimitive)
            {
                var specific = SpecificFromBlock(points, normals, genTol, seed);
                if (specific is not null && specific.Score >= best.Score / 1.5 &&
                    specific.Score >= _o.Floor && Better(specific, best))
                    best = specific;
            }

            return best;
        }

        private Candidate? SpecificFromBlock(List<Vec3> points, List<Vec3> normals, double genTol, int seed)
        {
            Candidate? best = null;
            void Take(Primitive? model)
            {
                if (model is null) return;
                double tol = RefitTol(model);
                var candidate = new Candidate { Model = model, SeedIndex = seed };
                candidate.Score = GlobalScore(model, tol, candidate);
                if (candidate.Score < _o.Floor) return;
                if (best is null || Better(candidate, best)) best = candidate;
            }

            Take(Fitters.FitCylinder(points, normals, genTol, _mesh.Diagonal));
            if (_o.EnableCone) Take(Fitters.FitCone(points, normals, genTol, _mesh.Diagonal));
            return best;
        }

        /// <summary>
        /// The count of unassigned triangles within tolerance of a model - the one number regions are
        /// ranked by. The cell cull is SOUND, not a heuristic: |d(p)| >= |d(centre)| - |p - centre| >=
        /// |d(centre)| - CellRadius, so a cell whose centre is farther than tol + CellRadius holds no
        /// inlier. Measured: 357,822 cell tests served 48,884 scores, about 7 cells per score.
        /// </summary>
        private int GlobalScore(Primitive model, double tol, Candidate candidate)
        {
            double slack = tol + _grid.CellRadius;
            var hits = new List<int>();

            foreach (int cellId in _grid.OccupiedCells)
            {
                if (Math.Abs(model.DistanceTo(_grid.CellCentre(cellId))) > slack) continue;
                if (_grid.Cell(cellId) is not { } cell) continue;

                foreach (int t in cell)
                {
                    if (_assigned[t]) continue;
                    if (Math.Abs(model.DistanceTo(_centroid[t])) <= tol) hits.Add(t);
                }
            }

            candidate.Triangles = hits.ToArray();
            return hits.Count;
        }

        /// <summary>
        /// Take a hypothesis. The inlier set is re-filtered against the assignment here as well as in
        /// the score, because a hypothesis is scored before it is accepted and the round that scored it
        /// may have accepted something else first.
        ///
        /// <para><b>The floor applies per COMPONENT, not per accepted set.</b> This is the difference
        /// between "16,000 triangles became a cylinder" and "2,000 eight-triangle faces were invented".
        /// A hypothesis is global, so on a band of sub-floor patches laid on a cylinder it scores
        /// 16,000 - every triangle really is within tolerance of r=12 - and the shared tail then splits
        /// that one region into its connected patches, which is 2,000 pieces of 8. The result passes a
        /// coverage count while producing faces the floor exists to refuse (the plan's R2: "a 3 degree
        /// split covers 78.7 % of clamp's jaw with 963 one-triangle planes"). Accepting a region means
        /// accepting faces, so each component must clear the floor on its own; components that do not
        /// stay unassigned and fall through to the faceted leftover. Measured on KnurlBand(2,000, 8):
        /// 2,000 cylinder regions -> 0, 16,000 triangles back to faceted.</para>
        /// </summary>
        /// <returns>How many triangles this accept actually assigned. Zero means the round made no
        /// progress and the caller must not loop again on the same evidence.</returns>
        private int Accept(Candidate candidate)
        {
            var triangles = candidate.Triangles.Where(t => !_assigned[t]).ToArray();
            if (triangles.Length == 0) return 0;

            int taken = 0;
            foreach (var piece in Components(triangles))
            {
                if (piece.Length < _o.Floor) continue;      // a sub-floor patch is not a face

                foreach (var t in piece) _assigned[t] = true;
                _regions.Add(new ClassifiedRegion(piece,
                    candidate.Model with { SourceTriangles = piece.Length },
                    PrimitiveClassifier.AverageNormal(_mesh, piece),
                    PrimitiveClassifier.Area(_mesh, piece)));
                taken += piece.Length;
            }
            return taken;
        }

        /// <summary>
        /// The connected components of a triangle set, by shared mesh edge, ascending within a piece
        /// and ordered by smallest index. Semantically identical to
        /// <see cref="PrimitiveClassifier.ConnectedPieces"/> - which is used for the final split, once
        /// per run - but written over a reusable scratch array instead of a Dictionary plus LINQ.
        ///
        /// <para>This is not premature optimisation. Accept runs once per accepted hypothesis AND once
        /// per refused tier-B seed, and the knurl-8 mesh refuses ~16,000 of them, each holding a
        /// 16,000-triangle candidate; the dictionary version took the suite from 95 s to over 420 s.
        /// A bool[] + stack BFS is three array reads per edge and turned that back into seconds.</para>
        /// </summary>
        private IEnumerable<int[]> Components(int[] triangles)
        {
            int n = _mesh.TriangleCount;
            if (_inSet is null || _seen is null || _stack is null)
            {
                _inSet = new bool[n]; _seen = new bool[n]; _stack = new int[n];
            }
            else
            {
                Array.Clear(_seen);
            }

            foreach (var t in triangles) _inSet[t] = true;

            var pieces = new List<int[]>();
            foreach (int start in triangles)        // ascending: the first unseen triangle of a piece
            {
                if (_seen[start]) continue;

                int top = 0;
                _stack[top++] = start;
                _seen[start] = true;
                var piece = new List<int>();

                while (top > 0)
                {
                    int t = _stack[--top];
                    piece.Add(t);
                    for (int e = 0; e < 3; e++)
                    {
                        int nb = _topo.Neighbor[t * 3 + e];
                        if (nb < 0 || _seen[nb] || !_inSet[nb]) continue;
                        _seen[nb] = true;
                        _stack[top++] = nb;
                    }
                }

                piece.Sort();
                pieces.Add(piece.ToArray());
            }

            foreach (var t in triangles) _inSet[t] = false;
            return pieces;
        }

        private bool[]? _inSet;
        private bool[]? _seen;
        private int[]? _stack;

        // ---- Tier 3: connectivity rescue -------------------------------------------------------

        /// <summary>
        /// The leftover is, by construction, a set of small PATCHES - the knurl's cells. One faceted
        /// region over them is uneditable; split into connected patches and offered to the same
        /// fitters, every patch above the floor becomes its own analytic face. This is the mechanism
        /// that turns a 57,283-triangle faceted mass into faces at all.
        /// </summary>
        private void Rescue()
        {
            var leftover = new List<int>();
            for (int t = 0; t < _assigned.Length; t++) if (!_assigned[t]) leftover.Add(t);
            if (leftover.Count < _o.Floor) return;

            var rejected = new List<int>();
            foreach (var piece in PrimitiveClassifier.ConnectedPieces(_topo, leftover.ToArray()))
            {
                if (piece.Length < _o.Floor) { rejected.AddRange(piece); continue; }

                var points = new List<Vec3>(piece.Length);
                var normals = new List<Vec3>(piece.Length);
                foreach (int t in piece) { points.Add(_centroid[t]); normals.Add(_normal[t]); }

                Primitive? best = null;
                double bestFitMax = double.MaxValue;

                void Take(Primitive? model)
                {
                    if (model is null) return;
                    double tol = RefitTol(model);
                    int inliers = 0;
                    foreach (int t in piece)
                        if (Math.Abs(model.DistanceTo(_centroid[t])) <= tol) inliers++;
                    if (inliers < piece.Length * 0.95) return;      // must explain its own patch
                    if (model.FitMax < bestFitMax) { best = model; bestFitMax = model.FitMax; }
                }

                Take(Fitters.FitPlane(points, _o.Tolerance));
                Take(Fitters.FitCylinder(points, normals, _o.Tolerance, _mesh.Diagonal));
                if (_o.EnableCone) Take(Fitters.FitCone(points, normals, _o.Tolerance, _mesh.Diagonal));
                if (_o.EnableTorus) Take(Fitters.FitTorus(points, normals, _o.Tolerance, _mesh.Diagonal));

                if (best is null) { rejected.AddRange(piece); continue; }

                foreach (var t in piece) _assigned[t] = true;
                _regions.Add(new ClassifiedRegion(piece,
                    best with { SourceTriangles = piece.Length },
                    PrimitiveClassifier.AverageNormal(_mesh, piece),
                    PrimitiveClassifier.Area(_mesh, piece)));
            }

            _rejected = rejected;
        }

        private List<int> _rejected = new();

        /// <summary>
        /// Exactly <see cref="PrimitiveClassifier"/>'s leftover shape: ONE faceted region over what is
        /// still unassigned, split per connected patch by the shared tail. Keeping it one region here
        /// (rather than per patch) is what makes the invariant structural: the leftover is by
        /// definition the complement of every accepted region.
        /// </summary>
        private void FinishLeftover()
        {
            var leftover = new List<int>();
            for (int t = 0; t < _assigned.Length; t++) if (!_assigned[t]) leftover.Add(t);
            if (leftover.Count == 0) return;

            var triangles = leftover.ToArray();
            _regions.Add(new ClassifiedRegion(triangles,
                new FacetedPrimitive { Triangles = triangles.Length, SourceTriangles = triangles.Length },
                PrimitiveClassifier.AverageNormal(_mesh, triangles),
                PrimitiveClassifier.Area(_mesh, triangles)));
        }

        // ---- shared rules ----------------------------------------------------------------------

        private double RefitTol(Primitive model) =>
            model is TorusPrimitive ? 2 * _o.Tolerance : _o.Tolerance;

        /// <summary>Type rank: a specific surface beats a generic one on an exact score tie (plane 0,
        /// cylinder 1, cone 2, torus 3, sphere 4), the reverse of FitRegion's attempt order because a
        /// fit that comes last must be strictly better to win.</summary>
        private static int TypeRank(Primitive model) => model.Kind switch
        {
            "plane" => 0,
            "cylinder" => 1,
            "cone" => 2,
            "torus" => 3,
            "sphere" => 4,
            _ => 5
        };

        /// <summary>A total order: higher score, then the more specific type, then the lower seed.
        /// Every comparison the segmenter makes goes through this, so the result cannot depend on the
        /// order the candidates happened to be built in.</summary>
        private static bool Better(Candidate a, Candidate b) =>
            a.Score != b.Score ? a.Score > b.Score
            : TypeRank(a.Model) != TypeRank(b.Model) ? TypeRank(a.Model) > TypeRank(b.Model)
            : a.SeedIndex < b.SeedIndex;

        /// <summary>
        /// A normal-coherent patch grown from a seed by mesh adjacency, capped. This is the domain a
        /// curved primitive needs: a spatial cube cuts a surface at arbitrary places, growth along
        /// normal continuity follows it.
        /// </summary>
        private List<int> CoherentPatch(int seed, int cap, double dihedralDeg)
        {
            double cos = Math.Cos(dihedralDeg * Math.PI / 180.0);
            var seen = new bool[_mesh.TriangleCount];
            var patch = new List<int>(cap) { seed };
            seen[seed] = true;
            var queue = new Queue<int>();
            queue.Enqueue(seed);

            while (queue.Count > 0 && patch.Count < cap)
            {
                int t = queue.Dequeue();
                for (int e = 0; e < 3; e++)
                {
                    int neighbour = _topo.Neighbor[t * 3 + e];
                    if (neighbour < 0 || _assigned[neighbour] || seen[neighbour]) continue;
                    if (Vec3.Dot(_normal[t], _normal[neighbour]) < cos) continue;

                    seen[neighbour] = true;
                    patch.Add(neighbour);
                    queue.Enqueue(neighbour);
                    if (patch.Count >= cap) break;
                }
            }
            return patch;
        }

        /// <summary>Every <paramref name="cap"/>-th element - index arithmetic, so a subsample is as
        /// deterministic as the array it came from.</summary>
        private static List<int> Strided(List<int> source, int cap)
        {
            var thinned = new List<int>(cap);
            for (int i = 0; i < cap; i++) thinned.Add(source[(int)((long)i * source.Count / cap)]);
            return thinned;
        }

        /// <summary>One Fisher-Yates shuffle from xorshift64*, so the seed order is fixed by the
        /// constant and not by the BCL.</summary>
        private static int[] Shuffled(int count, ulong seed)
        {
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;

            ulong state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
            uint NextBelow(uint bound)
            {
                state ^= state >> 12;
                state ^= state << 25;
                state ^= state >> 27;
                ulong next = state * 2685821657736338717UL;
                return (uint)(next % bound);
            }

            for (int i = count - 1; i > 0; i--)
            {
                int j = (int)NextBelow((uint)(i + 1));
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
        }
    }
}
