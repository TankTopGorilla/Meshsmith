using System.Diagnostics;
using Meshsmith.Core.Fit;
using Meshsmith.Core.Mesh;
using Meshsmith.Core.Pipeline;
using Meshsmith.Core.Seg;
using Meshsmith.Core.Tests.Fixtures;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// The seam: <c>--segmenter flood|ransac</c>, defaulting to flood. Three separate claims, because a
/// flag can fail in three ways and only one of them is "the engine is wrong":
///
/// <list type="bullet">
/// <item>the DEFAULT is flood - so an unflagged run is byte-identical to the code before the flag
/// existed, and the flag is also the rollback lever (T14a);</item>
/// <item>the flag is actually READ - the same input through the CLI produces a different partition
/// when it is passed (T14b);</item>
/// <item>an unknown value is an ERROR - not a silent fallback, which is the failure a caller cannot
/// see (T14c).</item>
/// </list>
///
/// T15 then pins the premise the whole seam rests on: both producers return the same region SHAPE, so
/// everything downstream (FaceBuilder, the sew ladder, the report) is indifferent to which one ran.
/// </summary>
public class SegmenterOptionTests
{
    // ---- T14a: the default ----------------------------------------------------------------

    [Fact]
    public void Options_default_to_Flood()
    {
        Assert.Equal(SegmenterKind.Flood, new ConversionOptions().Segmenter);

        // ...and the default really is today's call, not merely the same enum value: the two
        // producers must disagree on a mesh where flood is known to be weak, or "default" would be
        // vacuous. Measured on R2Drum: flood 19.5 %, ransac 97.5 %.
        var drum = SyntheticMeshes.R2Drum();
        double tol = SyntheticMeshes.Tol(drum);

        var flood = PrimitiveClassifier.Classify(drum, new PrimitiveClassifier.Options(tol, 12, 3));
        var ransac = RansacSegmenter.Classify(drum, new RansacSegmenter.Options(tol, 12, 3));

        int floodAnalytic = flood.Where(r => r.Primitive is not FacetedPrimitive).Sum(r => r.Triangles.Length);
        int ransacAnalytic = ransac.Where(r => r.Primitive is not FacetedPrimitive).Sum(r => r.Triangles.Length);

        Assert.True(ransacAnalytic > floodAnalytic,
            $"ransac {ransacAnalytic} vs flood {floodAnalytic}: the flag would select the same answer");
    }

    // ---- T14b: the flag is read ------------------------------------------------------------

    [Fact]
    public void The_segmenter_flag_changes_the_recognised_region_count()
    {
        // A small drum, so the CLI test costs seconds rather than the minutes a 31,920-triangle body
        // spends in the OCCT export path while asserting the SAME measurable disagreement:
        // ribs=8 seg=16 sideStacks=20 bandStacks=5 domeRings=8 is 8,576 triangles with every dihedral
        // under 12 deg (max 10.00), ONE flood piece, flood 20.9 % vs ransac 95.0 %.
        var drum = SyntheticMeshes.R2Drum(ribs: 8, segmentsPerRib: 16, sideStacks: 20,
                                          bandStacks: 5, domeRings: 8);
        Assert.Equal(8_576, drum.TriangleCount);
        string stl = WriteStl(drum);

        string exe = Path.Combine(CliOutputDirectory(), "Meshsmith.Cli.exe");
        Assert.True(File.Exists(exe), $"no CLI at {exe} - build the solution first");

        string work = Path.Combine(Path.GetTempPath(), $"meshsmith-seg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            var flood = Run(exe, stl, Path.Combine(work, "flood.step"), new[] { "--segmenter", "flood" });
            var ransac = Run(exe, stl, Path.Combine(work, "ransac.step"), new[] { "--segmenter", "ransac" });

            Assert.True(flood.Faces is not null, "the flood run printed no faces= line:\n" + flood.Raw);
            Assert.True(ransac.Faces is not null, "the ransac run printed no faces= line:\n" + ransac.Raw);
            Assert.True(flood.Coverage is not null, "the flood run printed no coverage= field:\n" + flood.Raw);
            Assert.True(ransac.Coverage is not null, "the ransac run printed no coverage= field:\n" + ransac.Raw);

            // Assert COVERAGE, not faces. Measured on this exact body: flood coverage=20.9 %,
            // ransac coverage=95.0 %, and faces=8,576 for BOTH - the drum is open at the bottom, so
            // no closed solid can be sewn from either partition and every region falls back to
            // faceted. faces= is therefore blind to this flag and would pass a flag that did nothing.
            // Coverage is the number the segmenters actually move (the plan's R4 warns that coverage
            // is not editable output; here it is exactly the right instrument, because the claim under
            // test is "the flag changes which regions are recognised", not "the export improved").
            Assert.NotEqual(flood.Coverage, ransac.Coverage);
            Assert.True(ransac.Coverage > flood.Coverage,
                $"--segmenter ransac coverage {ransac.Coverage:P1}, flood {flood.Coverage:P1}");

            Assert.Equal(0, flood.Exit);
            Assert.Equal(0, ransac.Exit);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { }
        }
    }

    // ---- T14c: a bad value is loud ---------------------------------------------------------

    [Fact]
    public void An_unknown_segmenter_value_fails_loudly()
    {
        string exe = Path.Combine(CliOutputDirectory(), "Meshsmith.Cli.exe");
        Assert.True(File.Exists(exe), $"no CLI at {exe} - build the solution first");

        // The input has to exist, or the run would fail for the wrong reason. A tiny STL is enough:
        // the flag is parsed before the pipeline is entered.
        string stl = WriteStl(SyntheticMeshes.Plane(10, 2, 2));
        string work = Path.Combine(Path.GetTempPath(), $"meshsmith-segbad-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            var bad = Run(exe, stl, Path.Combine(work, "bad.step"), new[] { "--segmenter", "wobble" });

            Assert.NotEqual(0, bad.Exit);
            Assert.Contains("--segmenter takes flood or ransac", bad.Raw);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { }
        }
    }

    // ---- T15: the shared region shape ------------------------------------------------------

    /// <summary>
    /// Both segmenters must produce regions the pipeline can consume without knowing which ran. That
    /// is the whole premise of the seam: every field a downstream stage reads is populated, and the
    /// partition covers the mesh. A plumbing change that bypassed the shared merge-and-split tail
    /// would fail here - the tail is what guarantees a region is one connected patch.
    /// </summary>
    [Fact]
    public void The_ransac_segmenter_produces_the_same_region_shape()
    {
        var mesh = SyntheticMeshes.TwoPrimitive();
        double tol = SyntheticMeshes.Tol(mesh);

        var regions = RansacSegmenter.Classify(mesh, new RansacSegmenter.Options(tol, 12, 3));
        Assert.NotEmpty(regions);

        var seen = new int[mesh.TriangleCount];
        bool anyDirectional = false;
        foreach (var region in regions)
        {
            Assert.NotEmpty(region.Triangles);
            Assert.NotNull(region.Primitive);
            Assert.True(region.Area > 0, "a region with no area is not a face");

            // Same field the classifier sets, so the report reads it identically.
            Assert.Equal(region.Triangles.Length, region.Primitive.SourceTriangles);
            Assert.False(double.IsNaN(region.AverageNormal.X), "AverageNormal must be a number");
            if (region.AverageNormal.Length > 0.5) anyDirectional = true;

            foreach (var t in region.Triangles) seen[t]++;
        }

        // NOT "every AverageNormal is a unit vector": a region spanning a full revolution has
        // area-weighted normals that cancel exactly, and the plan's R1 requires the cylinder side to
        // come back as ONE region over 360 degrees. The classifier produces the same zero for that
        // body, so demanding a direction here would fail the correct answer.
        Assert.True(anyDirectional, "no region carried a usable AverageNormal at all");

        Assert.All(seen, count => Assert.Equal(1, count));
    }

    // ---- helpers ---------------------------------------------------------------------------

    private readonly record struct CliRun(int Exit, int? Faces, double? Coverage, string Raw);

    private static CliRun Run(string exe, string input, string output, string[] flags)
    {
        var start = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("convert");
        start.ArgumentList.Add(input);
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add(output);
        foreach (string flag in flags) start.ArgumentList.Add(flag);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("no CLI process");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(600_000);

        string raw = stdout + stderr;
        int? faces = null;
        double? coverage = null;
        foreach (string line in raw.Split('\n'))
        {
            if (!line.StartsWith("faces=", StringComparison.Ordinal)) continue;

            // One line holds both: "faces=N ... coverage=20.9% ...". Invariant culture on both sides,
            // because a Turkish machine formats this as 20,9 % and the field is parsed by a machine.
            foreach (string field in line.Split(' '))
            {
                if (field.StartsWith("faces=", StringComparison.Ordinal))
                    faces = int.Parse(field["faces=".Length..], System.Globalization.CultureInfo.InvariantCulture);
                else if (field.StartsWith("coverage=", StringComparison.Ordinal))
                    coverage = double.Parse(field["coverage=".Length..].TrimEnd('%'),
                        System.Globalization.CultureInfo.InvariantCulture) / 100.0;
            }
        }
        return new CliRun(process.ExitCode, faces, coverage, raw);
    }

    /// <summary>The CLI's own output directory, found above the test binary by the solution marker.</summary>
    private static string CliOutputDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Meshsmith.slnx")))
            directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Meshsmith.slnx not found above " + AppContext.BaseDirectory);

        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        return Path.Combine(directory.FullName, "src", "Meshsmith.Cli", "bin", configuration,
                            "net8.0-windows7.0");
    }

    private static string WriteStl(MeshData mesh)
    {
        string path = Path.Combine(Path.GetTempPath(), $"seg-{Guid.NewGuid():N}.stl");
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
