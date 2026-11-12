using System.IO;
using System.Text.RegularExpressions;

namespace Meshsmith.App.Tests;

/// <summary>The legend swatch and the material on the 3D face are two different code paths holding
/// the same colour. A comment asked them to agree; this proves it, so editing one without the other
/// fails the build instead of shipping a legend that lies about the model.</summary>
public class ThemeMatchTests
{
    [Theory]
    [InlineData("Plane")]
    [InlineData("Cylinder")]
    [InlineData("Cone")]
    [InlineData("Faceted")]
    public void The_legend_swatch_matches_the_material(string kind)
    {
        var tokens = File.ReadAllText(
            Path.Combine(RepoRoot.Path, "src", "Meshsmith.App", "Themes", "Tokens.xaml"));
        var preview = File.ReadAllText(
            Path.Combine(RepoRoot.Path, "src", "Meshsmith.App", "Render", "MeshPreview.cs"));

        var swatch = Regex.Match(tokens, $@"x:Key=""Kind{kind}"" Color=""#FF([0-9A-Fa-f]{{6}})""");
        Assert.True(swatch.Success, $"Kind{kind} is missing from Tokens.xaml");

        var material = Regex.Match(preview,
            $@"Color {kind} = Color\.FromRgb\(0x([0-9A-Fa-f]{{2}}), 0x([0-9A-Fa-f]{{2}}), 0x([0-9A-Fa-f]{{2}})\)");
        Assert.True(material.Success, $"{kind} is missing from MeshPreview.cs");

        string rgb = material.Groups[1].Value + material.Groups[2].Value + material.Groups[3].Value;
        Assert.Equal(swatch.Groups[1].Value, rgb, StringComparer.OrdinalIgnoreCase);
    }
}
