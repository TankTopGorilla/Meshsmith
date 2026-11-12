using Meshsmith.Core.Pipeline;
using Xunit;

namespace Meshsmith.Core.Tests;

public class AcceptanceTests
{
    [Fact]
    public void Cylinder_converts_to_three_faces()
    {
        var r = ConversionPipeline.Run(StlFixtures.WriteCylinder(20, 30, 256),
            new ConversionOptions { KeepTemporaryFiles = true });
        Assert.True(r.IsValidSolid, string.Join("; ", r.Warnings));
        Assert.Equal(1, r.SolidCount);
        Assert.Equal(3, r.FaceCount);
        Assert.Equal(Math.PI * 400 * 30, r.Volume, Math.PI * 400 * 30 * 0.001);
        Assert.True(r.SurfaceDeviation <= r.FitTolerance, $"dev={r.SurfaceDeviation} tol={r.FitTolerance}");
    }

    [Fact]
    public void Washer_converts_to_four_faces_with_a_hole()
    {
        var r = ConversionPipeline.Run(StlFixtures.WriteWasher(30, 10, 10, 256),
            new ConversionOptions { KeepTemporaryFiles = true });
        Assert.True(r.IsValidSolid, string.Join("; ", r.Warnings));
        Assert.Equal(4, r.FaceCount);
        Assert.Equal(Math.PI * (900 - 100) * 10, r.Volume, Math.PI * (900 - 100) * 10 * 0.001);
    }

    [Fact]
    public void Cone_converts_to_three_faces()
    {
        var r = ConversionPipeline.Run(StlFixtures.WriteCone(30, 10, 20, 256),
            new ConversionOptions { KeepTemporaryFiles = true });
        Assert.True(r.IsValidSolid, string.Join("; ", r.Warnings));
        Assert.Equal(3, r.FaceCount);
        Assert.Equal(Math.PI * 20 / 3 * (900 + 300 + 100), r.Volume, Math.PI * 20 / 3 * (900 + 300 + 100) * 0.001);
    }
}
