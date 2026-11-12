using System.Globalization;
using Meshsmith.App.Services;
using Xunit;

namespace Meshsmith.App.Tests;

public class ToleranceInputTests
{
    private static readonly CultureInfo Turkish = new("tr-TR");
    private static readonly CultureInfo English = new("en-US");

    [Fact]
    public void A_dot_decimal_is_read_on_a_comma_locale()
    {
        Assert.True(ToleranceInput.TryParse("0.05", Turkish, out double value));
        Assert.Equal(0.05, value);
    }

    [Fact]
    public void A_comma_decimal_is_read_on_a_comma_locale()
    {
        Assert.True(ToleranceInput.TryParse("0,05", Turkish, out double value));
        Assert.Equal(0.05, value);
    }

    [Fact]
    public void A_comma_decimal_is_read_on_a_dot_locale()
    {
        Assert.True(ToleranceInput.TryParse("0,05", English, out double value));
        Assert.Equal(0.05, value);
    }

    [Fact]
    public void A_dot_decimal_is_read_on_a_dot_locale()
    {
        Assert.True(ToleranceInput.TryParse("0.05", English, out double value));
        Assert.Equal(0.05, value);
    }

    [Fact]
    public void An_empty_box_means_automatic()
    {
        Assert.False(ToleranceInput.TryParse("   ", Turkish, out _));
        Assert.False(ToleranceInput.TryParse(null, Turkish, out _));
    }

    [Fact]
    public void A_zero_or_negative_tolerance_is_not_a_tolerance()
    {
        Assert.False(ToleranceInput.TryParse("0", Turkish, out _));
        Assert.False(ToleranceInput.TryParse("-0,1", Turkish, out _));
        Assert.False(ToleranceInput.TryParse("fast", Turkish, out _));
    }
}
