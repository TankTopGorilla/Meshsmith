using System.IO;
using System.Text.RegularExpressions;
using Meshsmith.App.Services;
using Meshsmith.App.ViewModels;
using Xunit;

namespace Meshsmith.App.Tests;

public class StatusBarEtaTests
{
    [Fact]
    public async Task The_status_bar_shows_the_estimate_next_to_the_percentage()
    {
        var xaml = File.ReadAllText(
            Path.Combine(RepoRoot.Path, "src", "Meshsmith.App", "Views", "StatusBarView.xaml"));

        int percent = xaml.IndexOf("{Binding ProgressText}", StringComparison.Ordinal);
        int estimate = xaml.IndexOf("{Binding EtaText}", StringComparison.Ordinal);
        int activity = xaml.IndexOf("{Binding ActivityText}", StringComparison.Ordinal);

        Assert.True(percent >= 0, "the status bar lost its percentage");
        Assert.True(activity > percent, "the activity line moved ahead of the percentage");
        Assert.True(estimate > percent && estimate < activity,
                    "EtaText must sit between the percentage and the activity line");

        // The binding is only half the claim: the view model has to fill it from the progress stream.
        var viewModel = new MainViewModel(new FakeFileDialogs(), new FakeConversion(), new FakeMeshProbe());
        var shown = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.EtaText)) shown.Add(viewModel.EtaText);
        };

        await viewModel.LoadFileAsync(TempFiles.Stl());
        await viewModel.ConvertAsync();

        var estimateText = Assert.Single(shown.Where(text => text.Length > 0).Take(1),
                                         text => Regex.IsMatch(text, @"^~[0-9]+ (s|min)"));
        Assert.False(string.IsNullOrWhiteSpace(estimateText));
    }
}
