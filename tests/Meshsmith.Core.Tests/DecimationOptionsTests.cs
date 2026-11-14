using System.Diagnostics;
using Meshsmith.Core.Mesh;
using Meshsmith.Core.Pipeline;
using Xunit;

namespace Meshsmith.Core.Tests;

/// <summary>
/// R4's seam: the threshold and the target are defaults, and the CLI actually reads the flags that
/// override them. A flag nobody proves is read is a flag that silently does nothing - the last-flag
/// pitfall in this CLI was exactly that kind of bug.
/// </summary>
public class DecimationOptionsTests
{
    [Fact]
    public void Options_default_to_200k_above_60k_target()
    {
        var options = new ConversionOptions();

        // The measured pair, not a guess: 386,266 -> 60,000 in 1.08 s, watertight, and the classifier
        // still fits a 40 k mesh at 0.9989x tol. Below the threshold the branch never fires, which is
        // what keeps every ordinary part byte-identical.
        Assert.Equal(200_000, options.DecimateAbove);
        Assert.Equal(60_000, options.DecimateTarget);
        Assert.False(options.ForceDecimate);

        // The pipeline's default mesh must never be decimated: no real part in the corpus is above it.
        var cylinder = MeshWelder.Weld(StlReader.Read(StlFixtures.WriteCylinder(20, 30, 256)), 1e-5);
        Assert.True(cylinder.TriangleCount < options.DecimateAbove);
    }

    [Fact]
    public void The_decimate_target_flag_changes_the_conversion_output()
    {
        // Above the 200 k threshold, so BOTH runs decimate - the only difference is the target the
        // flag sets. A sphere is the cheapest closed body of this size (2 * slices * (stacks - 1)).
        var dense = TorusFitTests.Sphere(40, 250, 500);
        Assert.True(dense.TriangleCount > 200_000, $"{dense.TriangleCount} triangles");
        string stl = WriteStl(dense);

        string exe = Path.Combine(CliOutputDirectory(), "Meshsmith.Cli.exe");
        Assert.True(File.Exists(exe), $"no CLI at {exe} - build the solution first");

        string work = Path.Combine(Path.GetTempPath(), $"meshsmith-decim-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            var byDefault = Run(exe, stl, Path.Combine(work, "default.step"), Array.Empty<string>());
            var twoFlags = Run(exe, stl, Path.Combine(work, "target.step"), new[] { "--decimate-target", "30000" });

            Assert.True(byDefault.Faces is not null, "the default run printed no faces= line:\n" + byDefault.Raw);
            Assert.True(twoFlags.Faces is not null, "the flagged run printed no faces= line:\n" + twoFlags.Raw);

            // The proof the flag is read: the same input, a different target, a different export.
            Assert.NotEqual(byDefault.Faces, twoFlags.Faces);
            Assert.True(twoFlags.Faces < byDefault.Faces,
                $"--decimate-target 30000 produced {twoFlags.Faces} faces, the 60,000 default {byDefault.Faces}");
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { }
        }
    }

    // ---- helpers ----------------------------------------------------------------

    private readonly record struct CliRun(int Exit, int? Faces, string Raw);

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
        foreach (string line in raw.Split('\n'))
        {
            if (!line.StartsWith("faces=", StringComparison.Ordinal)) continue;
            var field = line.Split(' ').First(x => x.StartsWith("faces=", StringComparison.Ordinal));
            faces = int.Parse(field["faces=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        }
        return new CliRun(process.ExitCode, faces, raw);
    }

    /// <summary>The CLI's own output directory, found above the test binary by the solution marker.</summary>
    private static string CliOutputDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Meshsmith.slnx")))
            directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Meshsmith.slnx not found above " + AppContext.BaseDirectory);

        // Same configuration the tests were built in: bin/<Config>/<tfm> under the CLI project.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        return Path.Combine(directory.FullName, "src", "Meshsmith.Cli", "bin", configuration,
                            "net8.0-windows7.0");
    }

    private static string WriteStl(MeshData mesh)
    {
        string path = Path.Combine(Path.GetTempPath(), $"decim-{Guid.NewGuid():N}.stl");
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
