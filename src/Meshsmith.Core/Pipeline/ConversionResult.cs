using Meshsmith.Core.Export;
using Meshsmith.Core.Mesh;

namespace Meshsmith.Core.Pipeline;

public sealed class ConversionResult
{
    public required string StepPath { get; init; }
    public required string IgesPath { get; init; }
    public required string FacesPath { get; init; }
    public required int FaceCount { get; init; }
    public required int SolidCount { get; init; }
    public required bool IsValidSolid { get; init; }
    public required double Volume { get; init; }
    public required double SurfaceDeviation { get; init; }
    public required double FitTolerance { get; init; }
    public required double ElapsedSeconds { get; init; }
    public required int FacetedTriangles { get; init; }
    public required int Edges { get; init; }
    public required int TriangleCount { get; init; }
    public required string Kind { get; init; }

    /// <summary>What the written STEP contains, read back from disk.</summary>
    public required ArtifactCheck Artifact { get; init; }

    /// <summary>Seconds per pipeline stage, measured - not the progress percentages, which are
    /// effort guesses. "total" is the sum the stopwatch saw.</summary>
    public IReadOnlyDictionary<string, double> Timings { get; init; } = new Dictionary<string, double>();

    /// <summary>What the repair stage changed before anything was fitted.</summary>
    public RepairReport Repair { get; init; } = RepairReport.None;
    public List<string> Warnings { get; init; } = new();
}
