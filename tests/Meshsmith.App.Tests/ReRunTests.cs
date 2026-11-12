using System.Globalization;
using Meshsmith.App.Services;
using Meshsmith.App.ViewModels;

namespace Meshsmith.App.Tests;

/// <summary>Re-run is Convert without the file dialog, offered only once there is something to
/// re-run: a result or a failure. These pin both halves of that sentence.</summary>
public class ReRunTests
{
    private static (MainViewModel ViewModel, FakeConversion Conversion, FakeFileDialogs Dialogs) Loaded()
    {
        var conversion = new FakeConversion { Result = Results.Sample(TempFiles.FacesJson()) };
        var dialogs = new FakeFileDialogs();
        var viewModel = new MainViewModel(dialogs, conversion, new FakeMeshProbe());
        viewModel.LoadFileAsync(TempFiles.Stl()).GetAwaiter().GetResult();
        return (viewModel, conversion, dialogs);
    }

    /// <summary>The command is fire-and-forget by nature (async void); the fake completes
    /// synchronously, so this returns once the run has actually finished.</summary>
    private static void Execute(MainViewModel viewModel)
    {
        viewModel.ReRunCommand.Execute(null);

        var until = DateTime.UtcNow.AddSeconds(5);
        while (viewModel.IsBusy && DateTime.UtcNow < until) Thread.Sleep(10);
    }

    [Fact]
    public void Re_run_is_disabled_before_anything_has_run()
    {
        var (viewModel, _, _) = Loaded();

        Assert.True(viewModel.CanConvert);
        Assert.False(viewModel.CanRerun);
        Assert.False(viewModel.ReRunCommand.CanExecute(null));
    }

    [Fact]
    public void Re_run_is_enabled_after_a_failure()
    {
        var (viewModel, conversion, _) = Loaded();
        conversion.FailAtStage = conversion.StageToReport;

        viewModel.ConvertAsync().GetAwaiter().GetResult();

        Assert.True(viewModel.HasError);
        Assert.True(viewModel.CanRerun);
        Assert.True(viewModel.ReRunCommand.CanExecute(null));
    }

    [Fact]
    public void Re_run_converts_the_same_file_without_opening_the_dialog()
    {
        var conversion = new FakeConversion { Result = Results.Sample(TempFiles.FacesJson()) };
        var dialogs = new FakeFileDialogs { StlPath = TempFiles.Stl() };
        var viewModel = new MainViewModel(dialogs, conversion, new FakeMeshProbe());

        viewModel.OpenAsync().GetAwaiter().GetResult();
        viewModel.ConvertAsync().GetAwaiter().GetResult();
        Assert.Equal(1, dialogs.PickStlCalls);

        Execute(viewModel);

        Assert.Equal(1, dialogs.PickStlCalls);      // the dialog was not shown a second time
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.HasResult);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public void Re_run_uses_the_edited_tolerance()
    {
        var (viewModel, conversion, _) = Loaded();
        viewModel.ConvertAsync().GetAwaiter().GetResult();
        Assert.Null(conversion.LastSettings!.ToleranceOverride);

        viewModel.ToleranceInput = 0.05.ToString(CultureInfo.CurrentCulture);
        Execute(viewModel);

        Assert.Equal(0.05, conversion.LastSettings!.ToleranceOverride);
    }
}
