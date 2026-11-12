using Meshsmith.Core;

internal static class Program
{
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
        return 2;
    }

    private static int Convert(string[] args)
    {
        string input = args[1];
        string? output = null, report = null, iges = null;
        bool verifyAll = false;

        // Every flag is scanned: the old loop stopped one short of the end, so a value-less flag in
        // the last position (e.g. `... --verify`) was silently ignored.
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o" when i + 1 < args.Length: output = args[++i]; break;
                case "--iges" when i + 1 < args.Length: iges = args[++i]; break;
                case "--faces" when i + 1 < args.Length: report = args[++i]; break;
                case "--verify": verifyAll = true; break;
            }
        }

        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"no such file: {input}");
            return 2;
        }

        var result = Meshsmith.Core.Pipeline.ConversionPipeline.Run(input,
            new Meshsmith.Core.Pipeline.ConversionOptions
            {
                VerifyFaceLimit = verifyAll ? int.MaxValue : 4000
            });

        output ??= Path.ChangeExtension(input, ".step");
        File.Copy(result.StepPath, output, overwrite: true);

        iges ??= Path.ChangeExtension(output, ".igs");
        File.Copy(result.IgesPath, iges, overwrite: true);

        if (report is not null) File.Copy(result.FacesPath, report, overwrite: true);

        foreach (string warning in result.Warnings)
            Console.WriteLine($"warn: {warning}");

        Console.WriteLine("timings: " + string.Join("  ",
            result.Timings.Select(kv => $"{kv.Key}={kv.Value:0.00}s")));
        Console.WriteLine($"faces={result.FaceCount} solids={result.SolidCount} valid={result.IsValidSolid} " +
                          $"volume={result.Volume:F3} deviation={result.SurfaceDeviation:F6} " +
                          $"tol={result.FitTolerance:F4} faceted={result.FacetedTriangles} " +
                          $"artifact={result.Artifact.Status.ToString().ToLowerInvariant()} " +
                          $"time={result.ElapsedSeconds:F2}s");
        if (result.Artifact.Status != Meshsmith.Core.Export.ArtifactStatus.Verified)
            Console.WriteLine($"artifactDetail={result.Artifact.Detail}");
        Console.WriteLine($"step={output}");
        Console.WriteLine($"iges={iges}");
        return result.IsValidSolid ? 0 : 5;
    }
}
