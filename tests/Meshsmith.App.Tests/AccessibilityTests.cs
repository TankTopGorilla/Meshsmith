using System.IO;
using System.Text.RegularExpressions;

namespace Meshsmith.App.Tests;

/// <summary>Icon-only controls expose no text at all, and a label wrapped in a StackPanel is not
/// read as a name. Each one carries an explicit AutomationProperties.Name instead, and this keeps
/// a new icon button from arriving without one.</summary>
public class AccessibilityTests
{
    [Theory]
    [InlineData("Views/ViewportPanel.xaml", 3)]
    [InlineData("Views/SourcePanel.xaml", 1)]
    [InlineData("Views/TopBar.xaml", 7)]
    [InlineData("Views/InspectorPanel.xaml", 0)]
    public void Interactive_controls_carry_an_accessible_name(string view, int expected)
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot.Path, "src", "Meshsmith.App", view));

        int names = Regex.Matches(xaml, @"AutomationProperties\.Name=""[^""]+""").Count;
        Assert.True(names >= expected,
                    $"{view}: expected at least {expected} accessible names, found {names}");
    }
}
