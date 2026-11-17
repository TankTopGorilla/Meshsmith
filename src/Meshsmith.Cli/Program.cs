using Meshsmith.Core;

internal static class Program
{
    /// <summary>Invariant culture: a Turkish machine formats 33.6 as "33,6" and the gate greps "33.6".</summary>
    private static string Percent(double fraction) =>
        (fraction * 100).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private static int Main(string[] args)
    {
        NativeLoader.Configure();          // no Occt types in this method
        return Run(args);                  // Occt types appear only below
    }

    private static int Run(string[] args)
    {
        if (args.Length >= 2 && args[0] == "probe")
            return OcctProbe.PrintCensus(args[1]);

        if (args.Length >= 2 && args[0] == "convert")
            return Convert(args);

        Console.Error.WriteLine("usage: meshsmith probe <file.step|file.igs>");
        Console.Error.WriteLine("       meshsmith convert <file.stl> [-o out.step] [--iges out.igs] [--faces report.json]");
        Console.Error.WriteLine("                        [--verify]   read the export back even for a big body (slow)");
        Console.Error.WriteLine("                        [--segmenter flood|ransac]   region producer (default flood)");
        return 2;
    }

    private static int Convert(string[] args)
    {
        string input = args[1];
        string? output = null, report = null, iges = null;
        bool verifyAll = false;
        bool decimate = false;
        int decimateTarget = 60_000;
        var segmenter = Meshsmith.Core.Pipeline.SegmenterKind.Flood;

        // Every flag is scanned: the old loop stopped one short of the end, so a value-less flag in
        // the last position (e.g. `... --verify`) was silently ignored.
        try
        {
            for (int i = 2; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o" when i + 1 < args.Length: output = args[++i]; break;
                    case "--iges" when i + 1 < args.Length: iges = args[++i]; break;
                    case "--faces" when i + 1 < args.Length: report = args[++i]; break;
                    case "--verify": verifyAll = true; break;
                    case "--decimate": decimate = true; break;
                    case "--decimate-target" when i + 1 < args.Length:
                        decimateTarget = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    // An unknown value is an ERROR, never a silent fallback to flood: a flag that
                    // silently does nothing is exactly what a caller cannot detect.
                    case "--segmenter" when i + 1 < args.Length:
                        string value = args[++i];
                        segmenter = value switch
                        {
                            "flood" => Meshsmith.Core.Pipeline.SegmenterKind.Flood,
                            "ransac" => Meshsmith.Core.Pipeline.SegmenterKind.Ransac,
                            _ => throw new ArgumentException(
                                $"--segmenter takes flood or ransac, not '{value}'")
                        };
                        break;
                }
            }
        }
        catch (ArgumentException ex)
        {
            // A bad flag value is the caller's mistake, not a crash: one line, exit 2.
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }

        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"no such file: {input}");
            return 2;
        }

        Meshsmith.Core.Pipeline.ConversionResult result;
        try
        {
            result = Meshsmith.Core.Pipeline.ConversionPipeline.Run(input,
                new Meshsmith.Core.Pipeline.ConversionOptions
                {
                    VerifyFaceLimit = verifyAll ? int.MaxValue : 4000,
                    ForceDecimate = decimate,
                    DecimateTarget = decimateTarget,
                    Segmenter = segmenter
                });
        }
        catch (ArgumentException ex)
        {
            // A bad flag value is the caller's mistake, not a crash: one line, exit 2.
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
        catch (Exception ex) when (ex is Meshsmith.Core.Import.MeshFormatException
                                        or Meshsmith.Core.Mesh.StlFormatException)
        {
            // An honest failure is one line with a non-zero exit - never a stack trace.
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        output ??= Path.ChangeExtension(input, ".step");
        File.Copy(result.StepPath, output, overwrite: true);

        iges ??= Path.ChangeExtension(output, ".igs");
        File.Copy(result.IgesPath, iges, overwrite: true);

        if (report is not null) File.Copy(result.FacesPath, report, overwrite: true);

        foreach (string warning in result.ImportWarnings)
            Console.WriteLine($"warn: {warning}");

        foreach (string warning in result.Warnings)
            Console.WriteLine($"warn: {warning}");

        Console.WriteLine("timings: " + string.Join("  ",
            result.Timings.Select(kv => $"{kv.Key}={kv.Value:0.00}s")));
        Console.WriteLine($"faces={result.FaceCount} solids={result.SolidCount} valid={result.IsValidSolid} " +
                          $"volume={result.Volume:F3} deviation={result.SurfaceDeviation:F6} " +
                          $"tol={result.FitTolerance:F4} faceted={result.FacetedTriangles} " +
                          $"coverage={Percent(result.ReconCoverage)} " +
                          $"region_fallbacks={result.RegionFallbacks} " +
                          $"artifact={result.Artifact.Status.ToString().ToLowerInvariant()} " +
                          $"time={result.ElapsedSeconds:F2}s");
        Console.WriteLine($"format={result.SourceFormat}");
        Console.WriteLine($"units=\"{result.UnitNote}\"");
        if (result.DecimateFromTriangles > 0)
            Console.WriteLine($"decim={result.DecimateDeviation:0.000}x tol (from {result.DecimateFromTriangles} " +
                              $"triangles to {result.TriangleCount})");
        if (result.Artifact.Status != Meshsmith.Core.Export.ArtifactStatus.Verified)
            Console.WriteLine($"artifactDetail={result.Artifact.Detail}");
        Console.WriteLine($"step={output}");
        Console.WriteLine($"iges={iges}");
        return result.IsValidSolid ? 0 : 5;
    }
}
