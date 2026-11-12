using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Meshsmith.App.Tests;

/// <summary>A StaticResource typo is invisible at compile time and fatal at first render. This catches it headless.</summary>
public class XamlIntegrityTests
{
    private static readonly Regex Reference = new(@"\{(?:StaticResource|DynamicResource)\s+([^}\s]+)\}",
                                                  RegexOptions.Compiled);

    [Fact]
    public void Every_xaml_file_is_well_formed()
    {
        foreach (var file in RepoRoot.XamlFiles())
            Assert.NotNull(RepoRoot.Load(file));     // throws with the file name if the XML is broken
    }

    [Fact]
    public void Every_static_resource_reference_resolves_to_a_defined_key()
    {
        var files = RepoRoot.XamlFiles().ToList();
        Assert.NotEmpty(files);

        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var element in RepoRoot.Load(file).Descendants())
            {
                var key = element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key")?.Value;
                if (key is not null) defined.Add(key);
            }
        }

        var missing = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match match in Reference.Matches(text))
            {
                var key = match.Groups[1].Value;
                if (!defined.Contains(key)) missing.Add($"{Path.GetFileName(file)} -> {key}");
            }
        }

        Assert.True(missing.Count == 0, "undefined resources: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_view_control_is_wired_into_the_window()
    {
        var window = RepoRoot.Load(Path.Combine(RepoRoot.Path, "src", "Meshsmith.App", "MainWindow.xaml"));
        var text = window.ToString();

        foreach (var control in new[] { "TopBar", "SourcePanel", "ViewportPanel", "InspectorPanel", "StatusBarView" })
            Assert.Contains(control, text);
    }
}
