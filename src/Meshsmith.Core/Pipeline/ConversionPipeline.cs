using System.Diagnostics;
using Meshsmith.Core.Brep;
using Meshsmith.Core.Export;
using Meshsmith.Core.Fit;
using Meshsmith.Core.Geometry;
using Meshsmith.Core.Mesh;
using Meshsmith.Core.Report;
using Meshsmith.Core.Validation;
using Occt;

namespace Meshsmith.Core.Pipeline;

public static class ConversionPipeline
{
    private static readonly object OcctGate = new();

    public static ConversionResult Run(string stlPath, ConversionOptions options,
                                       CancellationToken cancellationToken = default)
    {
        lock (OcctGate)
        {
            return RunCore(stlPath, options, cancellationToken);
        }
    }

    private static ConversionResult RunCore(string stlPath, ConversionOptions options,
                                            CancellationToken cancellationToken)
    {
        // Cancellation is cooperative: checked before any work and between stages, because an
        // OCCT call in progress cannot be interrupted from managed code.
        cancellationToken.ThrowIfCancellationRequested();

        var sw = Stopwatch.StartNew();

        // Where the time actually goes. Progress percentages are guesses about effort; these are
        // measurements, and they are what tells a slow part apart from a slow stage.
        var marks = new List<(string Stage, double At)>();
        void Mark(string stage) => marks.Add((stage, sw.Elapsed.TotalSeconds));
        var work = string.IsNullOrWhiteSpace(options.WorkingDirectory)
            ? Path.Combine(Path.GetTempPath(), $"meshsmith-{Guid.NewGuid():N}")
            : options.WorkingDirectory;
        Directory.CreateDirectory(work);

        options.Progress?.Invoke("reading", 0.05);
        var soup = StlReader.Read(stlPath);
        var mesh = MeshWelder.Weld(soup, Math.Max(1e-9, 1e-6 * SoupDiagonal(soup)));

        var repair = RepairReport.None;
        if (options.RepairMesh)
        {
            var (repaired, report) = MeshRepair.Repair(mesh, new RepairOptions(options.MaxHoleEdges));
            mesh = repaired;
            repair = report;
        }

        Mark("reading");

        // Dense meshes: the classifier fits per triangle and its region growing is quadratic-ish in
        // the count, so a 386 k part never finishes. Decimating BEFORE recognition is the only order
        // that works - the fits have to see a tessellation fine enough to resolve their surfaces, and
        // an R3 finding says a mesh too coarse to resolve sagitta under `tol` classifies as nothing.
        // Below the threshold the branch never fires, so every ordinary part is byte-identical.
        var decimationFrom = 0;
        var decimationDeviation = 0.0;
        if (options.DecimateTarget > 0 &&
            (options.ForceDecimate || (options.DecimateAbove > 0 && mesh.TriangleCount > options.DecimateAbove)))
        {
            var original = mesh;
            decimationFrom = original.TriangleCount;
            mesh = Decimator.Decimate(mesh, options.DecimateTarget, new RepairOptions(options.MaxHoleEdges));

            // What the reduction erased, measured against the mesh that came in - the pipeline's own
            // deviation figure compares the export with the mesh it classified, which is the decimated
            // one and so cannot answer this. Stride 16: the same sample the R4 probe used, and dense
            // enough that a collapse anywhere shows up. Normalised by `tol` below, which the decimated
            // mesh itself sets.
            decimationDeviation = MeshDeviation(original, mesh, stride: 16);

            options.Progress?.Invoke("reading", 0.15);
        }

        var diag = mesh.Diagonal;
        double tol = options.ToleranceOverride ?? Math.Clamp(2e-4 * diag, 1e-3, 0.5);
        if (decimationFrom > 0) decimationDeviation /= tol;

        cancellationToken.ThrowIfCancellationRequested();

        options.Progress?.Invoke("recognising", 0.20);
        var regions = PrimitiveClassifier.Classify(mesh, new PrimitiveClassifier.Options(tol, 12, 3),
                                                   cancellationToken);
        Mark("recognising");
        if (regions.Count == 0) throw new InvalidOperationException("no regions recognised");
        var recon = RecognitionStats.Compute(mesh, regions);

        options.Progress?.Invoke("building", 0.55);
        var faces = new List<TopoDS_Face>(regions.Count);
        var faceRecords = new List<FaceRecord>(regions.Count);
        var regionFaces = new List<TopoDS_Face>?[regions.Count];   // null => this region is faceted
        var triangleRegion = Enumerable.Repeat(-1, mesh.TriangleCount).ToArray();   // index into regions
        var triangleFace = Enumerable.Repeat(-1, mesh.TriangleCount).ToArray();     // id in faces.json
        var topo = MeshTopology.Build(mesh);
        var warnings = new List<string>();

        var health = MeshHealth.Measure(mesh, topo);
        if (!health.IsWatertight)
            warnings.Add($"the mesh is not watertight: {health.OpenEdges} open edges and " +
                         $"{health.NonManifoldEdges} edges shared by more than two triangles - " +
                         "the export will carry those holes");
        else if (health.Bodies > 1)
            warnings.Add($"the mesh holds {health.Bodies} separate bodies");

        if (repair.Changed) warnings.Add(repair.Describe());

        int facetedTriangles = 0;
        int regionFallbacks = 0;      // regions that LOST an analytic face (build threw, or their body
                                      // would not sew). A region the classifier never made analytic did
                                      // not fall back, and neither did the all-or-nothing path - the
                                      // `faceted` count already shows that one.
        bool closedFaces = false;     // a region used ClosedFaceBuilder: its patches must NOT be merged
                                      // back into one doubly-periodic face (measured: the merged face
                                      // writes an empty STEP)

        // A region that was never analytic, or one OCCT refuses to trim, becomes one face per triangle.
        void AddFaceted(int regionId, bool counted = true)
        {
            var target = regions[regionId];
            regionFaces[regionId] = null;
            if (counted) regionFallbacks++;
            facetedTriangles += target.Triangles.Length;
            faces.AddRange(FacetedBuilder.Build(mesh, target.Triangles));

            // The documented schema gives a faceted face per mesh triangle.
            foreach (var t in target.Triangles)
            {
                int recordId = faceRecords.Count;
                triangleFace[t] = recordId;
                faceRecords.Add(new FaceRecord
                {
                    Id = recordId,
                    Category = "faceted",
                    Area = mesh.Area(t),
                    Center = new[] { mesh.Centroid(t).X, mesh.Centroid(t).Y, mesh.Centroid(t).Z },
                    Normal = new[] { mesh.Normal(t).X, mesh.Normal(t).Y, mesh.Normal(t).Z },
                    SourceTriangles = 1,
                    Surface = new Dictionary<string, object?> { ["kind"] = "faceted" }
                });
            }
        }

        for (int id = 0; id < regions.Count; id++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var region = regions[id];
            foreach (var t in region.Triangles) triangleRegion[t] = id;

            if (region.Primitive is FacetedPrimitive)
            {
                AddFaceted(id, counted: false);
                continue;
            }

            int faceId = faceRecords.Count;

            try
            {
                var surface = SurfaceFactory.Create(region.Primitive);
                var loops = BoundaryLoops.Extract(mesh, topo, region.Triangles);
                var sampleTri = region.Triangles.OrderByDescending(mesh.Area).First();
                var samplePoint = mesh.Centroid(sampleTri);
                var sampleNormal = mesh.Normal(sampleTri);

                List<TopoDS_Face> built;

                if (loops.Count == 0)
                {
                    // A CLOSED torus or sphere body has no boundary at all. FaceBuilder throws "face
                    // has no boundary" for it, and the old path then faceted the whole body. The
                    // measured repair is the region's own uv bounding box, split once per periodic
                    // direction: one natural-bounds face sews to zero shells, any 2-way split sews to
                    // a valid solid with the exact volume.
                    built = ClosedFaceBuilder.Build(region.Primitive, surface, mesh, region.Triangles,
                                                     tol, samplePoint, sampleNormal);
                    closedFaces = true;
                }
                else
                {
                    var uv = new List<(double U, double V)[]>(loops.Count);
                    foreach (var loop in loops)
                    {
                        var pts = loop.Select(i => mesh.Vertices[i]).ToArray();
                        uv.Add(UvProjector.Project(surface, pts));
                    }

                    var uvTol = UvProjector.UvTolerance(region.Primitive, tol, MeanRadius(mesh, region));
                    built = FaceBuilder.Build(surface, region.Primitive, uv, uvTol, samplePoint, sampleNormal);
                }

                faces.AddRange(built);
                regionFaces[id] = built;

                foreach (var t in region.Triangles) triangleFace[t] = faceId;
                faceRecords.Add(Record(faceId, region, mesh));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A region the fit believes in can still be untrimmable: an open boundary, a
                // degenerate loop, a projection that lands off the surface, a trim OCCT refuses.
                // Text meshes are full of them, and the exception type differs per failure mode, so
                // the whole family degrades to facets rather than failing the part. Cancellation is
                // the one thing that must not be swallowed.
                warnings.Add($"region {id} fell back to faceted: {ex.Message}");
                AddFaceted(id);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        Mark("building");
        options.Progress?.Invoke("sewing", 0.75);
        var sewTol = Math.Max(2 * tol, 1e-3);
        var sewed = SolidBuilder.SewRobust(faces, sewTol, unify: !closedFaces);

        if (!sewed.IsValid)
        {
            // Per body, not per part. clamp.stl is four separate bodies; faceting all of them because one
            // will not close throws away the analytic faces of the other three. A component that will not
            // sew is replaced by its own exact triangles and the ones that closed keep their faces. This
            // is only sound between bodies - a crack inside one body cannot be faceted away - so when
            // every component fails the code falls through to the exact faceted solid below.
            var components = RegionComponents.Group(mesh, topo, regions);
            var keepAnalytic = new bool[regions.Count];
            bool anyKept = false, anyDropped = false;

            foreach (var component in components)
            {
                var componentFaces = new List<TopoDS_Face>();
                foreach (int r in component)
                    if (regionFaces[r] is { } builtFaces) componentFaces.AddRange(builtFaces);

                bool ok = componentFaces.Count > 0 && SolidBuilder.SewRobust(componentFaces, sewTol, unify: !closedFaces).IsValid;
                foreach (int r in component) keepAnalytic[r] = ok;
                if (ok) anyKept = true; else anyDropped = true;
            }

            bool perBodyRebuilt = false;

            if (anyKept && anyDropped)
            {
                faces.Clear();
                faceRecords.Clear();
                triangleFace = Enumerable.Repeat(-1, mesh.TriangleCount).ToArray();
                facetedTriangles = 0;
                regionFallbacks = 0;

                for (int r = 0; r < regions.Count; r++)
                {
                    if (keepAnalytic[r] && regionFaces[r] is { } kept)
                    {
                        int faceId = faceRecords.Count;
                        faces.AddRange(kept);
                        foreach (var t in regions[r].Triangles) triangleFace[t] = faceId;
                        faceRecords.Add(Record(faceId, regions[r], mesh));
                    }
                    else
                    {
                        warnings.Add($"region {r} fell back to faceted: its body did not sew");
                        AddFaceted(r, counted: regionFaces[r] is not null);
                    }
                }

                options.Progress?.Invoke("sewing", 0.85);
                sewed = SolidBuilder.SewRobust(faces, sewTol, unify: !closedFaces);
                perBodyRebuilt = true;
            }

            // A body rebuilt per component that CLOSED is kept even when BRepCheck still objects. The
            // defect sits inside a fully faceted component - the exact faceted fallback below reproduces
            // the same closed-but-invalid compound on the same part (measured on IKI P1: 7 of 8 solids
            // valid, the faceted one not) - so re-faceting cannot repair it, it only throws away the
            // analytic faces the degradation just saved. An OPEN body is still replaced: a sew that did
            // not close is exactly what facets do close. `perBodyRebuilt` keeps this relaxation off the
            // single-body path, where a closed-but-invalid analytic shell must still fall back.
            if (!sewed.IsValid && (!perBodyRebuilt || sewed.FreeEdges > 0))
            {
                warnings.Add(sewed.FreeEdges > 0
                    ? $"analytic faces could not be sewn ({sewed.FreeEdges} free edges); " +
                      "exported the exact faceted solid instead"
                    : "the analytic body did not pass the solid check; " +
                      "exported the exact faceted solid instead");

                faces.Clear();
                faceRecords.Clear();
                triangleFace = Enumerable.Repeat(-1, mesh.TriangleCount).ToArray();
                facetedTriangles = 0;

                for (int i = 0; i < regions.Count; i++) AddFaceted(i, counted: false);

                options.Progress?.Invoke("sewing", 0.85);
                sewed = SolidBuilder.SewRobust(faces, sewTol, unify: !closedFaces);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        Mark("sewing");
        var stepPath = Path.Combine(work, "part.step");
        StepExporter.Write(sewed.Shape, stepPath);

        var igesPath = Path.Combine(work, "part.igs");
        IgesExporter.Write(sewed.Shape, igesPath);

        // The face cap keeps the happy path fast; it must never decide a verdict. A body that just
        // failed the in-memory check is exactly the case where the file has to be read back.
        bool bodyValid = sewed.IsValid;
        bool overTheLimit = sewed.Faces > options.VerifyFaceLimit;

        Mark("writing");
        var artifact =
            !options.VerifyArtifacts
                ? ArtifactCheck.Skipped("artifact verification is switched off")
                : overTheLimit && bodyValid
                    ? ArtifactCheck.Skipped(
                        $"{sewed.Faces} faces is above the {options.VerifyFaceLimit}-face verification limit")
                    : ArtifactVerifier.Verify(stepPath, sewed.Faces, sewed.Shells, sewed.Volume);

        if (artifact.Status == Export.ArtifactStatus.Mismatch)
            warnings.Add($"the written STEP does not match the converted body: {artifact.Detail}");

        var verdict = SolidVerdict.Decide(bodyValid, artifact);
        if (verdict.Note is not null) warnings.Add(verdict.Note);

        if (sewed.FreeEdges > 0) warnings.Insert(0, $"{sewed.FreeEdges} free edges after sewing");

        Mark("verifying");
        options.Progress?.Invoke("validating", 0.90);
        var deviation = DeviationChecker.Compute(mesh, regions, triangleRegion, sewed.Shape, tol);

        // The last checkpoint: validation takes seconds on a dense mesh, and a cancel that lands
        // inside it must not still hand back a finished result.
        cancellationToken.ThrowIfCancellationRequested();

        if (decimationFrom > 0 && decimationDeviation > 2)
            warnings.Insert(0, $"the mesh was decimated {decimationFrom} -> {mesh.TriangleCount} triangles and " +
                                $"the reduction moved the surface by {decimationDeviation:0.##}x the fit " +
                                "tolerance - detail was erased; lower DecimateTarget if the fits suffer");

        var doc = new FacesDocument
        {
            Tolerance = tol,
            Kind = facetedTriangles == mesh.TriangleCount ? "faceted_fallback" : "prismatic",
            TriangleFace = triangleFace,
            Faces = faceRecords,
            Summary = new Summary
            {
                Faces = sewed.Faces,
                Triangles = mesh.TriangleCount,
                Planes = faceRecords.Count(f => f.Category == "plane"),
                Cylinders = faceRecords.Count(f => f.Category == "cylinder"),
                Cones = faceRecords.Count(f => f.Category == "cone"),
                Toruses = faceRecords.Count(f => f.Category == "torus"),
                Spheres = faceRecords.Count(f => f.Category == "sphere"),
                FacetedTriangles = facetedTriangles,
                ReconCoverage = recon.Coverage,
                RegionFallbacks = regionFallbacks,
                Edges = CountEdges(sewed.Shape)
            }
        };
        var facesPath = Path.Combine(work, "part-faces.json");
        File.WriteAllText(facesPath, System.Text.Json.JsonSerializer.Serialize(doc,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        Mark("validating");
        var timings = new Dictionary<string, double>();
        double previousMark = 0;
        foreach (var (stage, at) in marks)
        {
            timings[stage] = Math.Round(at - previousMark, 3);
            previousMark = at;
        }
        timings["total"] = Math.Round(sw.Elapsed.TotalSeconds, 3);

        File.WriteAllText(Path.Combine(work, "report.json"),
            System.Text.Json.JsonSerializer.Serialize(new ConversionReport
            {
                ResultKind = doc.Kind,
                OutputFaces = faces.Count,
                SurfaceTypes = new Dictionary<string, int>
                {
                    ["PLANE"] = doc.Summary.Planes,
                    ["CYLINDER"] = doc.Summary.Cylinders,
                    ["CONE"] = doc.Summary.Cones,
                    ["TORUS"] = doc.Summary.Toruses,
                    ["SPHERE"] = doc.Summary.Spheres
                },
                FacetedTriangles = facetedTriangles,
                FitTolerance = tol,
                SurfaceDeviation = deviation,
                ReconCoverage = recon.Coverage,
                RegionFallbacks = regionFallbacks,
                Warnings = warnings,
                ArtifactStatus = artifact.Status.ToString(),
                ArtifactDetail = artifact.Detail,
                Repair = new Dictionary<string, int>
                {
                    ["degenerate_dropped"] = repair.DegenerateDropped,
                    ["duplicates_dropped"] = repair.DuplicatesDropped,
                    ["flipped"] = repair.Flipped,
                    ["holes_filled"] = repair.HolesFilled,
                    ["hole_edges"] = repair.HoleEdges,
                    ["non_manifold_edges"] = repair.NonManifoldEdges
                },
                Timings = timings,
                ElapsedSeconds = sw.Elapsed.TotalSeconds
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        sw.Stop();

        return new ConversionResult
        {
            StepPath = stepPath,
            IgesPath = igesPath,
            FacesPath = facesPath,
            FaceCount = sewed.Faces,
            SolidCount = sewed.Shells,
            IsValidSolid = verdict.Valid,
            Timings = timings,
            Volume = sewed.Volume,
            SurfaceDeviation = deviation,
            FitTolerance = tol,
            ElapsedSeconds = sw.Elapsed.TotalSeconds,
            FacetedTriangles = facetedTriangles,
            ReconCoverage = recon.Coverage,
            RegionFallbacks = regionFallbacks,
            Edges = doc.Summary.Edges,
            TriangleCount = mesh.TriangleCount,
            Kind = doc.Kind,
            Artifact = artifact,
            Repair = repair,
            DecimateFromTriangles = decimationFrom,
            DecimateDeviation = decimationDeviation,
            Warnings = warnings
        };
    }

    /// <summary>Farthest sampled distance from one mesh's surface to the other's, in model units.
    /// Stride skips triangles: a collapse anywhere still shows up in the max, and 386 k triangles at
    /// stride 16 is 24 k lookups instead of 386 k. This is the "did the decimation erase detail"
    /// number, and it is why the reduction is reported rather than assumed harmless.</summary>
    private static double MeshDeviation(MeshData from, MeshData to, int stride)
    {
        var grid = new TriangleGrid(to);
        double worst = 0;
        for (int t = 0; t < from.TriangleCount; t += stride)
        {
            foreach (var p in new[] { from.V0(t), from.V1(t), from.V2(t), from.Centroid(t) })
                worst = Math.Max(worst, grid.DistanceToSurface(p));
        }
        return worst;
    }

    private static double SoupDiagonal(TriangleSoup soup)
    {
        if (soup.Points.Length == 0) return 1;
        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vec3(double.MinValue, double.MinValue, double.MinValue);
        foreach (var p in soup.Points)
        {
            min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
            max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
        }
        return Math.Max((max - min).Length, 1e-9);
    }

    private static double MeanRadius(MeshData mesh, ClassifiedRegion region)
    {
        if (region.Primitive is not (CylinderPrimitive or ConePrimitive)) return 1;
        var axisPoint = region.Primitive is CylinderPrimitive c ? c.PointOnAxis : ((ConePrimitive)region.Primitive).Apex;
        var axis = region.Primitive is CylinderPrimitive cc ? cc.Axis : ((ConePrimitive)region.Primitive).Axis;

        double sum = 0; int n = 0;
        foreach (var t in region.Triangles)
            foreach (var p in new[] { mesh.V0(t), mesh.V1(t), mesh.V2(t) })
            {
                var d = p - axisPoint;
                sum += (d - axis * Vec3.Dot(d, axis)).Length;
                n++;
            }
        return n == 0 ? 1 : sum / n;
    }

    private static FaceRecord Record(int id, ClassifiedRegion region, MeshData mesh)
    {
        var prim = region.Primitive;
        return new FaceRecord
        {
            Id = id,
            Category = prim.Kind,
            Area = region.Area,
            Center = new[] { mesh.Centroid(region.Triangles[0]).X, mesh.Centroid(region.Triangles[0]).Y, mesh.Centroid(region.Triangles[0]).Z },
            Normal = new[] { region.AverageNormal.X, region.AverageNormal.Y, region.AverageNormal.Z },
            SourceTriangles = region.Triangles.Length,
            Fit = new FitRecord { Max = prim.FitMax, Mean = prim.FitMean },
            Surface = prim switch
            {
                PlanePrimitive p => new Dictionary<string, object?>
                {
                    ["kind"] = "plane",
                    ["point"] = new[] { p.Point.X, p.Point.Y, p.Point.Z },
                    ["normal"] = new[] { p.Normal.X, p.Normal.Y, p.Normal.Z }
                },
                CylinderPrimitive c => new Dictionary<string, object?>
                {
                    ["kind"] = "cylinder",
                    ["point"] = new[] { c.PointOnAxis.X, c.PointOnAxis.Y, c.PointOnAxis.Z },
                    ["axis"] = new[] { c.Axis.X, c.Axis.Y, c.Axis.Z },
                    ["radius"] = c.Radius
                },
                ConePrimitive k => new Dictionary<string, object?>
                {
                    ["kind"] = "cone",
                    ["apex"] = new[] { k.Apex.X, k.Apex.Y, k.Apex.Z },
                    ["axis"] = new[] { k.Axis.X, k.Axis.Y, k.Axis.Z },
                    ["half_angle_degrees"] = k.HalfAngle * 180 / Math.PI
                },
                TorusPrimitive t => new Dictionary<string, object?>
                {
                    ["kind"] = "torus",
                    ["point"] = new[] { t.PointOnAxis.X, t.PointOnAxis.Y, t.PointOnAxis.Z },
                    ["axis"] = new[] { t.Axis.X, t.Axis.Y, t.Axis.Z },
                    ["major_radius"] = t.MajorRadius,
                    ["minor_radius"] = t.MinorRadius
                },
                SpherePrimitive s => new Dictionary<string, object?>
                {
                    ["kind"] = "sphere",
                    ["center"] = new[] { s.Center.X, s.Center.Y, s.Center.Z },
                    ["radius"] = s.Radius
                },
                _ => new Dictionary<string, object?> { ["kind"] = prim.Kind }
            }
        };
    }

    private static int CountEdges(TopoDS_Shape shape)
    {
        int n = 0;
        var ex = new TopExp_Explorer(shape, TopAbs_ShapeEnum.TopAbs_EDGE);
        while (ex.More) { n++; ex.Next(); }
        return n;
    }
}
