using System.IO;
using System.Windows.Media;
using System.Xml.Linq;

namespace Meshsmith.App.Tests;

/// <summary>WCAG 2.1 contrast on the real token values, so "looks nice" has a floor that cannot regress.</summary>
public class ContrastTests
{
    private static readonly Dictionary<string, Color> Tokens = LoadTokens();

    private static Dictionary<string, Color> LoadTokens()
    {
        var path = Path.Combine(RepoRoot.Path, "src", "Meshsmith.App", "Themes", "Tokens.xaml");
        var document = XDocument.Load(path);
        var result = new Dictionary<string, Color>(StringComparer.Ordinal);

        foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "SolidColorBrush"))
        {
            var key = element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key")?.Value;
            var value = element.Attribute("Color")?.Value;
            if (key is not null && value is not null) result[key] = Parse(value);
        }

        return result;
    }

    private static Color Parse(string hex)
    {
        var text = hex.TrimStart('#');
        if (text.Length == 8) text = text.Substring(2);   // drop alpha, this test only checks opaque pairs
        return Color.FromRgb(Convert.ToByte(text.Substring(0, 2), 16),
                             Convert.ToByte(text.Substring(2, 2), 16),
                             Convert.ToByte(text.Substring(4, 2), 16));
    }

    private static double Channel(byte value)
    {
        double s = value / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(Color colour) =>
        0.2126 * Channel(colour.R) + 0.7152 * Channel(colour.G) + 0.0722 * Channel(colour.B);

    private static double Ratio(Color a, Color b)
    {
        double first = Luminance(a), second = Luminance(b);
        double lighter = Math.Max(first, second), darker = Math.Min(first, second);
        return (lighter + 0.05) / (darker + 0.05);
    }

    [Theory]
    [InlineData("TextPrimary", "Bg0")]
    [InlineData("TextPrimary", "Bg1")]
    [InlineData("TextPrimary", "Bg2")]
    [InlineData("TextPrimary", "Bg3")]
    [InlineData("TextSecondary", "Bg1")]
    [InlineData("TextSecondary", "Bg2")]
    [InlineData("TextMuted", "Bg1")]
    [InlineData("TextMuted", "Bg2")]
    [InlineData("Accent", "Bg1")]
    [InlineData("OnAccent", "Accent")]
    [InlineData("OnAccent", "AccentHover")]
    public void Body_text_meets_wcag_aa(string foreground, string background)
    {
        Assert.True(Tokens.ContainsKey(foreground), $"missing token {foreground}");
        Assert.True(Tokens.ContainsKey(background), $"missing token {background}");

        double ratio = Ratio(Tokens[foreground], Tokens[background]);
        Assert.True(ratio >= 4.5, $"{foreground} on {background} is {ratio:0.00}:1, needs 4.5:1");
    }

    [Theory]
    [InlineData("Success", "Bg1")]
    [InlineData("Warning", "Bg1")]
    [InlineData("Danger", "Bg1")]
    [InlineData("KindPlane", "Bg1")]
    [InlineData("KindCylinder", "Bg1")]
    [InlineData("KindCone", "Bg1")]
    [InlineData("KindFaceted", "Bg1")]
    public void Non_text_marks_meet_wcag_aa_for_graphics(string foreground, string background)
    {
        double ratio = Ratio(Tokens[foreground], Tokens[background]);
        Assert.True(ratio >= 3.0, $"{foreground} on {background} is {ratio:0.00}:1, needs 3.0:1");
    }

    [Fact]
    public void The_three_surfaces_are_ordered_dark_to_light()
    {
        Assert.True(Luminance(Tokens["Bg0"]) < Luminance(Tokens["Bg1"]));
        Assert.True(Luminance(Tokens["Bg1"]) < Luminance(Tokens["Bg2"]));
        Assert.True(Luminance(Tokens["Bg2"]) < Luminance(Tokens["Bg3"]));
    }
}
