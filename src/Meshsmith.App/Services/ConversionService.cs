using Meshsmith.Core.Pipeline;

namespace Meshsmith.App.Services;

/// <summary>One progress report from the conversion. <paramref name="IsBeat"/> marks a heartbeat:
/// the stage and fraction are unchanged, and only the CPU share is new.</summary>
public readonly record struct ConversionProgress(string Stage, double Fraction,
                                                double CpuPercent = 0, bool IsBeat = false);

/// <summary>What the user chose, in the app's vocabulary. Each service maps it onto
/// <c>ConversionOptions</c>: in-process directly, out-of-process as worker flags. Core's
/// <c>ConversionOptions</c> is deliberately not the app's contract — it also carries the working
/// directory, the verification face limit and the temp-file switch, which are plumbing.</summary>
public sealed record ConversionSettings(bool Repair = true, bool VerifyArtifacts = true,
                                        double? ToleranceOverride = null)
{
    public static ConversionSettings Default { get; } = new();
}

public interface IConversionService
{
    Task<ConversionResult> ConvertAsync(string stlPath, ConversionSettings settings,
                                        IProgress<ConversionProgress> progress,
                                        CancellationToken cancellationToken);
}

public sealed class ConversionService : IConversionService
{
    public Task<ConversionResult> ConvertAsync(string stlPath, ConversionSettings settings,
                                               IProgress<ConversionProgress> progress,
                                               CancellationToken cancellationToken) =>
        // The token goes into the pipeline itself, not just into Task.Run: Task.Run only decides
        // whether the work starts, while the pipeline checks it between stages and per region.
        Task.Run(() => ConversionPipeline.Run(stlPath, new ConversionOptions
        {
            RepairMesh = settings.Repair,
            VerifyArtifacts = settings.VerifyArtifacts,
            ToleranceOverride = settings.ToleranceOverride,
            Progress = (stage, fraction) => progress.Report(new ConversionProgress(stage, fraction))
        }, cancellationToken), cancellationToken);
}
