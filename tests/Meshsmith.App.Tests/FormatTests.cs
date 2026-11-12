using Meshsmith.App.ViewModels;

namespace Meshsmith.App.Tests;

public class FormatTests
{
    [Fact]
    public void Count_uses_a_non_breaking_space_between_thousands() =>
        Assert.Equal("2\u00A0048", Format.Count(2048));

    [Fact]
    public void Volume_keeps_three_decimals_and_thousands_separator() =>
        Assert.Equal("25\u00A0130.179", Format.Volume(25130.179));

    [Fact]
    public void Millimetres_keeps_four_decimals() =>
        Assert.Equal("0.0024 mm", Format.Millimetres(0.002365));

    [Fact]
    public void Seconds_keeps_two_decimals() =>
        Assert.Equal("1.48 s", Format.Seconds(1.4791));

    [Fact]
    public void Dimensions_are_invariant_culture() =>
        Assert.Equal("60.0 × 60.0 × 10.0 mm", Format.Dimensions(60, 60, 10));
}
