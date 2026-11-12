using Meshsmith.Core.Pipeline;
using Xunit;

namespace Meshsmith.Core.Tests;

public class TimingsTests
{
    [Fact]
    public void A_conversion_reports_where_its_time_went()
    {
        var result = ConversionPipeline.Run(StlFixtures.WriteCylinder(20, 30, 256),
            new ConversionOptions { KeepTemporaryFiles = true });

        foreach (string stage in new[] { "reading", "recognising", "building", "sewing", "writing", "validating", "total" })
            Assert.True(result.Timings.ContainsKey(stage), $"missing timing for {stage}");

        Assert.True(result.Timings["total"] > 0);
        Assert.All(result.Timings.Values, seconds => Assert.True(seconds >= 0));

        // The stages are disjoint, so they cannot add up to more than the total the stopwatch saw.
        double sum = result.Timings.Where(kv => kv.Key != "total").Sum(kv => kv.Value);
        Assert.True(sum <= result.Timings["total"] + 0.05, $"sum={sum} total={result.Timings["total"]}");
    }
}
