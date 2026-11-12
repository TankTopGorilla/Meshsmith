using System.Globalization;
using Meshsmith.App.Services;
using Meshsmith.App.ViewModels;

namespace Meshsmith.App.Tests;

/// <summary>Repair, verification and the tolerance override live on one record that each service
/// translates into kernel options. These pin the seam from the view model all the way down, because
/// that seam did not exist: the app had no way to say anything about a run.</summary>
public class ConversionSettingsTests
{
    private static FakeConversion Run(AppSettings preferences, Action<MainViewModel>? arrange = null)
    {
        var conversion = new FakeConversion { Result = Results.Sample(TempFiles.FacesJson()) };
        var viewModel = new MainViewModel(new FakeFileDialogs(), conversion, new FakeMeshProbe(),
                                          preferences: preferences);

        viewModel.LoadFileAsync(TempFiles.Stl()).GetAwaiter().GetResult();
        arrange?.Invoke(viewModel);
        viewModel.ConvertAsync().GetAwaiter().GetResult();

        return conversion;
    }

    [Fact]
    public void The_repair_switch_reaches_the_conversion_settings()
    {
        var conversion = Run(AppSettings.Default with { Repair = false });

        Assert.NotNull(conversion.LastSettings);
        Assert.False(conversion.LastSettings!.Repair);
        Assert.True(conversion.LastSettings.VerifyArtifacts);   // one switch, not both
    }

    [Fact]
    public void The_verify_switch_reaches_the_conversion_settings()
    {
        var conversion = Run(AppSettings.Default,
                             viewModel => viewModel.ApplySettings(
                                 AppSettings.Default with { VerifyArtifacts = false }));

        Assert.NotNull(conversion.LastSettings);
        Assert.False(conversion.LastSettings!.VerifyArtifacts);
        Assert.True(conversion.LastSettings.Repair);
    }

    [Fact]
    public void An_edited_tolerance_reaches_the_conversion_settings()
    {
        var typed = 0.05.ToString(CultureInfo.CurrentCulture);

        var conversion = Run(AppSettings.Default, viewModel => viewModel.ToleranceInput = typed);

        Assert.NotNull(conversion.LastSettings);
        Assert.Equal(0.05, conversion.LastSettings!.ToleranceOverride);
    }

    [Fact]
    public void An_empty_tolerance_box_means_automatic()
    {
        var conversion = Run(AppSettings.Default, viewModel => viewModel.ToleranceInput = "");

        Assert.NotNull(conversion.LastSettings);
        Assert.Null(conversion.LastSettings!.ToleranceOverride);

        // Text that is not a number is the same answer, silently: the pipeline derives it.
        var viewModel2 = new MainViewModel(new FakeFileDialogs(), conversion, new FakeMeshProbe());
        viewModel2.LoadFileAsync(TempFiles.Stl()).GetAwaiter().GetResult();
        viewModel2.ToleranceInput = "not a number";
        viewModel2.ConvertAsync().GetAwaiter().GetResult();

        Assert.Null(conversion.LastSettings!.ToleranceOverride);
    }
}
