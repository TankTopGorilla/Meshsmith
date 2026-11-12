using Meshsmith.Core.Fit;
using Meshsmith.Core.Mesh;
using Meshsmith.Core.Pipeline;

namespace Meshsmith.Core.Tests;

public class PipelineCancellationTests
{
    [Fact]
    public void A_cancelled_token_stops_the_pipeline_before_it_does_any_work()
    {
        var stl = StlFixtures.WriteCube(10);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ConversionPipeline.Run(stl, new ConversionOptions(), cancellation.Token));
    }

    [Fact]
    public void A_cancelled_token_stops_surface_recognition()
    {
        var mesh = MeshWelder.Weld(StlReader.Read(StlFixtures.WriteCube(10)), 1e-5);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            PrimitiveClassifier.Classify(mesh, PrimitiveClassifier.Options.Default(0.01), cancellation.Token));
    }

    [Fact]
    public void A_cancel_that_lands_inside_validation_discards_the_result()
    {
        var stl = StlFixtures.WriteCube(10);
        using var cancellation = new CancellationTokenSource();
        var options = new ConversionOptions
        {
            // "validating" is reported at 0.90, immediately before DeviationChecker runs, so this
            // cancels inside the last stage deterministically instead of by sleeping.
            Progress = (stage, _) =>
            {
                if (stage == "validating") cancellation.Cancel();
            }
        };

        Assert.Throws<OperationCanceledException>(() =>
            ConversionPipeline.Run(stl, options, cancellation.Token));
    }
}
