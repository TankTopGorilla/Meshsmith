using System.Diagnostics;
using Meshsmith.Core.Import;
using Meshsmith.Core.Mesh;
using Meshsmith.Core.Pipeline;

namespace Meshsmith.Core.Tests;

/// <summary>P10a's pipeline seam: what was read, in what format, and what its numbers meant has to
/// reach the result - a conversion whose unit is invisible is a part nobody can check.</summary>
public class PipelineImportTests
{
    [Fact]
    public void The_pipeline_reads_through_the_registry()
    {
        string path = StlFixtures.WriteCube(10);
        var reader = new CountingReader();
        var options = new ConversionOptions { Readers = new IMeshReader[] { reader } };

        var result = ConversionPipeline.Run(path, options);

        Assert.Equal(1, reader.Reads);
        Assert.Equal("FAKE", result.SourceFormat);
        Assert.Equal(6, result.FaceCount);          // the cube still converted; only the read changed
    }

    [Fact]
    public void The_result_carries_the_format_and_the_unit_note()
    {
        var result = ConversionPipeline.Run(StlFixtures.WriteCube(10), new ConversionOptions());

        Assert.Equal("STL", result.SourceFormat);
        Assert.False(string.IsNullOrWhiteSpace(result.UnitNote));
    }

    [Fact]
    public void Stl_results_keep_unit_note_millimetres()
    {
        var result = ConversionPipeline.Run(StlFixtures.WriteCube(10), new ConversionOptions());

        Assert.Equal("STL · millimetres", result.UnitNote);
        Assert.Equal(1.0, result.AppliedScale);
        Assert.Empty(result.ImportWarnings);
    }

    [Fact]
    public void A_warning_from_the_reader_reaches_the_result()
    {
        string path = StlFixtures.WriteCube(10);
        var reader = new CountingReader();
        var options = new ConversionOptions { Readers = new IMeshReader[] { reader } };

        var result = ConversionPipeline.Run(path, options);

        Assert.Contains("a warning from the reader", result.ImportWarnings);
    }

    [Fact]
    public void The_mm_setting_changes_the_applied_scale()
    {
        string path = StlFixtures.WriteCube(10);

        var asMetres = new CountingReader();
        var metresResult = ConversionPipeline.Run(path,
            new ConversionOptions { Readers = new IMeshReader[] { asMetres } });

        var asMm = new CountingReader();
        var mmResult = ConversionPipeline.Run(path,
            new ConversionOptions { Readers = new IMeshReader[] { asMm }, TreatNumbersAsMillimetres = true });

        // The setting has to reach the reader - only the reader knows what the format declared.
        Assert.False(asMetres.Seen!.TreatNumbersAsMillimetres == true);
        Assert.True(asMm.Seen!.TreatNumbersAsMillimetres == true);

        // ...and what it decided has to reach the result, both number and sentence.
        Assert.Equal(1000.0, metresResult.AppliedScale);
        Assert.Equal(1.0, mmResult.AppliedScale);
        Assert.Contains("×1000", metresResult.UnitNote);
        Assert.Contains("treated as mm", mmResult.UnitNote);
    }

    [Fact]
    public void Cli_prints_the_format_and_unit_note()
    {
        string exe = Path.Combine(CliOutputDirectory(), "Meshsmith.Cli.exe");
        Assert.True(File.Exists(exe), $"no CLI at {exe} - build the solution first");

        string input = Path.Combine(RepoRoot(), "fixtures", "cube_10.stl");
        string output = Path.Combine(Path.GetTempPath(), $"p10a-cli-{Guid.NewGuid():N}.step");

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

        using var process = Process.Start(start) ?? throw new InvalidOperationException("no CLI process");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(600_000);
        string raw = stdout + stderr;

        Assert.Equal(0, process.ExitCode);
        Assert.Contains(raw.Split('\n'), line => line.TrimEnd('\r') == "format=STL");
        Assert.Contains(raw.Split('\n'), line => line.TrimEnd('\r') == "units=\"STL · millimetres\"");
    }

    // ---- helpers ----------------------------------------------------------------

    /// <summary>A stand-in format: proves the pipeline asks the registry instead of calling the static
    /// reader, and echoes back the unit decision it was handed.</summary>
    private sealed class CountingReader : IMeshReader
    {
        public string Format => "FAKE";
        public int Reads { get; private set; }
        public MeshReadOptions? Seen { get; private set; }

        public bool CanRead(string path, ReadOnlySpan<byte> head) => true;

        public MeshReadResult Read(string path, MeshReadOptions options,
                                   IProgress<double>? byteProgress, CancellationToken token)
        {
            Reads++;
            Seen = options;
            return new MeshReadResult
            {
                Soup = StlReader.Read(path),
                Format = Format,
                UnitNote = options.TreatNumbersAsMillimetres == true ? "fake · treated as mm" : "fake · metres · ×1000",
                AppliedScale = options.TreatNumbersAsMillimetres == true ? 1.0 : 1000.0,
                Warnings = { "a warning from the reader" }
            };
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Meshsmith.slnx")))
            directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Meshsmith.slnx not found above " + AppContext.BaseDirectory);
        return directory.FullName;
    }

    private static string CliOutputDirectory()
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        return Path.Combine(RepoRoot(), "src", "Meshsmith.Cli", "bin", configuration, "net8.0-windows7.0");
    }
}
