using System.IO;
using Meshsmith.App.ViewModels;

namespace Meshsmith.App.Tests;

public class FaceMixTests
{
    [Fact]
    public void Empty_has_no_mix_and_no_shares()
    {
        Assert.False(FaceMix.Empty.Total > 0);
        Assert.Equal(0, FaceMix.Empty.PlaneShare);
        Assert.Equal(0, FaceMix.Empty.CylinderShare);
        Assert.Equal(0, FaceMix.Empty.ConeShare);
    }

    [Fact]
    public void Shares_are_the_fraction_of_all_exact_faces()
    {
        var mix = new FaceMix(3, 1, 0, 0);

        Assert.Equal(4, mix.Total);
        Assert.Equal(0.75, mix.PlaneShare);
        Assert.Equal(0.25, mix.CylinderShare);
        Assert.Equal(0.0, mix.ConeShare);
    }

    [Fact]
    public void From_file_reads_the_summary_block()
    {
        var mix = FaceMix.FromFile(TempFiles.FacesJson(planes: 2, cylinders: 1, cones: 1, faceted: 7));

        Assert.Equal(2, mix.Planes);
        Assert.Equal(1, mix.Cylinders);
        Assert.Equal(1, mix.Cones);
        Assert.Equal(7, mix.FacetedTriangles);
    }

    [Fact]
    public void A_missing_file_returns_empty_instead_of_throwing()
    {
        var mix = FaceMix.FromFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));

        Assert.Equal(FaceMix.Empty, mix);
    }

    [Fact]
    public void Corrupt_json_returns_empty_instead_of_throwing() =>
        Assert.Equal(FaceMix.Empty, FaceMix.FromFile(TempFiles.Json("{ not json ]")));
}
