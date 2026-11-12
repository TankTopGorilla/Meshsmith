using System.IO;
using Meshsmith.App.Services;
using Meshsmith.App.ViewModels;

namespace Meshsmith.App.Tests;

/// <summary>The error card's caption must name what actually failed: a file that never opened is not
/// a failed conversion. Locks the two strings the panel binds to.</summary>
public class ErrorTitleTests
{
    private static MainViewModel NewViewModel(out FakeConversion conversion)
    {
        conversion = new FakeConversion();
        return new MainViewModel(new FakeFileDialogs(), conversion, new FakeMeshProbe());
    }

    [Fact]
    public void A_file_that_cannot_be_opened_says_so()
    {
        var viewModel = NewViewModel(out _);

        viewModel.LoadFileAsync(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N") + ".stl"))
                 .GetAwaiter().GetResult();

        Assert.True(viewModel.HasError);
        Assert.Equal("COULD NOT OPEN FILE", viewModel.ErrorTitle);
    }

    [Fact]
    public void A_probe_failure_says_the_file_could_not_be_opened()
    {
        var viewModel = NewViewModel(out _);
        var probe = new FakeMeshProbe { ShouldThrow = true };
        var path = TempFiles.Stl();

        var withThrowingProbe = new MainViewModel(new FakeFileDialogs(), new FakeConversion(), probe);
        withThrowingProbe.LoadFileAsync(path).GetAwaiter().GetResult();

        Assert.True(withThrowingProbe.HasError);
        Assert.Equal("COULD NOT OPEN FILE", withThrowingProbe.ErrorTitle);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task A_pipeline_failure_says_the_conversion_failed()
    {
        var viewModel = NewViewModel(out var conversion);
        conversion.FailAtStage = "building";
        viewModel.LoadFileAsync(TempFiles.Stl()).GetAwaiter().GetResult();

        await viewModel.ConvertAsync();

        Assert.True(viewModel.HasError);
        Assert.Equal("CONVERSION FAILED", viewModel.ErrorTitle);
    }
}
