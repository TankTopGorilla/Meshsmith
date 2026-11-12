using System.IO;

namespace Meshsmith.App.Tests;

/// <summary>The viewport used to hard-code its grid colours while a matching token sat unused in the
/// dictionary. Colours must come from the token layer, or editing the palette changes nothing.</summary>
public class GridColourTokenTests
{
    [Fact]
    public void The_grid_colours_come_from_the_token_dictionary()
    {
        var tokens = File.ReadAllText(Path.Combine(RepoRoot.Path, "src", "Meshsmith.App", "Themes", "Tokens.xaml"));

        Assert.Contains("GridMinorColor", tokens);
        Assert.Contains("GridMajorColor", tokens);
        Assert.DoesNotContain("GridLineColor", tokens);

        var panel = File.ReadAllText(
            Path.Combine(RepoRoot.Path, "src", "Meshsmith.App", "Views", "ViewportPanel.xaml.cs"));

        Assert.Contains("FindResource(\"GridMinorColor\")", panel);
        Assert.Contains("FindResource(\"GridMajorColor\")", panel);
        Assert.DoesNotContain("FromRgb(0x19", panel);
        Assert.DoesNotContain("FromRgb(0x27", panel);
    }
}
