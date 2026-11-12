using Meshsmith.Core.Export;
using System.IO;
using Meshsmith.App.Services;
using Meshsmith.Core.Pipeline;

namespace Meshsmith.App.Tests;

internal sealed class FakeFileDialogs : IFileDialogService
{
    public string? StlPath { get; set; }
    public string? SavePath { get; set; }
    public string? FolderPath { get; set; }
    public string? LastFilter { get; private set; }
    public string? LastSuggestedName { get; private set; }
    public string? LastInitialDirectory { get; private set; }

    /// <summary>How many times the open dialog was put in front of the user: a re-run must not
    /// raise it.</summary>
    public int PickStlCalls { get; private set; }

    public string? PickStl()
    {
        PickStlCalls++;
        return StlPath;
    }

    public string? PickSave(string filter, string suggestedName, string? initialDirectory = null)
    {
        LastFilter = filter;
        LastSuggestedName = suggestedName;
        LastInitialDirectory = initialDirectory;
        return SavePath;
    }

    public string? PickFolder(string? initialDirectory)
    {
        LastInitialDirectory = initialDirectory;
        return FolderPath;
    }
}

internal sealed class FakeMeshProbe : IMeshProbe
{
    public MeshInfo Info { get; set; } = new(2048, 60, 60, 10);
    public bool ShouldThrow { get; set; }

    public MeshInfo Describe(string stlPath) =>
        ShouldThrow ? throw new InvalidOperationException("unreadable STL") : Info;
}

internal sealed class FakeConversion : IConversionService
{
    public ConversionResult? Result { get; set; }
    public string? FailAtStage { get; set; }
    public string FailureMessage { get; set; } = "no regions recognised";
    public string StageToReport { get; set; } = "building";

    /// <summary>What the view model asked for last; the seam tests read this.</summary>
    public ConversionSettings? LastSettings { get; private set; }

    public Task<ConversionResult> ConvertAsync(string stlPath, ConversionSettings settings,
                                               IProgress<ConversionProgress> progress,
                                               CancellationToken cancellationToken)
    {
        LastSettings = settings;
        progress.Report(new ConversionProgress("reading", 0.05));
        progress.Report(new ConversionProgress(StageToReport, 0.55));

        if (FailAtStage is not null && FailAtStage == StageToReport)
            throw new InvalidOperationException(FailureMessage);

        return Task.FromResult(Result ?? throw new InvalidOperationException("no result configured"));
    }
}

internal static class Results
{
    public static ConversionResult Sample(string facesPath) => new()
    {
        Artifact = ArtifactCheck.Skipped("fake"),
        StepPath = facesPath,
        IgesPath = facesPath,
        FacesPath = facesPath,
        FaceCount = 4,
        SolidCount = 1,
        IsValidSolid = true,
        Volume = 25130.179,
        SurfaceDeviation = 0.002365,
        FitTolerance = 0.0171,
        ElapsedSeconds = 1.48,
        FacetedTriangles = 0,
        Edges = 12,
        TriangleCount = 2048,
        Kind = "prismatic",
        Warnings = new List<string>()
    };
}
