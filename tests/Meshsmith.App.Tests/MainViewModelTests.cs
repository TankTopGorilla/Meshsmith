using System.IO;
using Meshsmith.App.Services;
using Meshsmith.App.ViewModels;

namespace Meshsmith.App.Tests;

public class MainViewModelTests
{
    private static MainViewModel NewViewModel(out FakeConversion conversion, out FakeFileDialogs dialogs,
                                              out FakeMeshProbe probe)
    {
        conversion = new FakeConversion();
        dialogs = new FakeFileDialogs();
        probe = new FakeMeshProbe();
        return new MainViewModel(dialogs, conversion, probe);
    }

    private static MainViewModel Loaded(out FakeConversion conversion)
    {
        var viewModel = NewViewModel(out conversion, out _, out _);
        conversion.Result = Results.Sample(TempFiles.FacesJson());
        Load(viewModel, TempFiles.Stl());
        return viewModel;
    }

    /// <summary>xunit has no SynchronizationContext, so the awaited work finishes on the pool and a
    /// blocking wait cannot deadlock. The app never does this - it awaits LoadFileAsync.</summary>
    private static void Load(MainViewModel viewModel, string path) =>
        viewModel.LoadFileAsync(path).GetAwaiter().GetResult();

    [Fact]
    public void Initial_state_has_no_source_and_cannot_convert()
    {
        var viewModel = NewViewModel(out _, out _, out _);

        Assert.False(viewModel.HasSource);
        Assert.False(viewModel.CanConvert);
        Assert.False(viewModel.CanExport);
        Assert.Equal("READY", viewModel.StageText);
        Assert.Equal("No model loaded", viewModel.StatusRight);
        Assert.All(viewModel.Stages, stage => Assert.Equal(StageState.Pending, stage.State));
    }

    [Fact]
    public void Loading_an_stl_fills_the_source_panel()
    {
        var viewModel = NewViewModel(out _, out _, out var probe);
        probe.Info = new MeshInfo(2048, 60, 60, 10);

        var path = TempFiles.Stl();
        Load(viewModel, path);

        Assert.True(viewModel.HasSource);
        Assert.Equal(Path.GetFileName(path), viewModel.SourceName);
        Assert.Equal("2\u00A0048", viewModel.TrianglesText);
        Assert.Equal("60.0 × 60.0 × 10.0 mm", viewModel.DimensionsText);
        Assert.True(viewModel.CanConvert);
        Assert.Equal("2\u00A0048 triangles", viewModel.StatusRight);
    }

    [Fact]
    public void Loading_a_missing_file_sets_the_error_and_keeps_state()
    {
        var viewModel = NewViewModel(out _, out _, out _);

        Load(viewModel, Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".stl"));

        Assert.True(viewModel.HasError);
        Assert.StartsWith("File not found:", viewModel.ErrorText);
        Assert.False(viewModel.HasSource);
        Assert.False(viewModel.CanConvert);
    }

    [Fact]
    public void A_probe_failure_is_surfaced_instead_of_throwing()
    {
        var viewModel = NewViewModel(out _, out _, out var probe);
        probe.ShouldThrow = true;

        Load(viewModel, TempFiles.Stl());

        Assert.True(viewModel.HasError);
        Assert.Equal("unreadable STL", viewModel.ErrorText);
        Assert.False(viewModel.HasSource);
    }

    [Fact]
    public async Task Converting_fills_every_result_field()
    {
        var viewModel = Loaded(out var conversion);

        await viewModel.ConvertAsync();

        Assert.Equal("4", viewModel.FacesText);
        Assert.Equal("1", viewModel.SolidsText);
        Assert.Equal("25\u00A0130.179", viewModel.VolumeText);
        Assert.Equal("0.0024 mm", viewModel.DeviationText);
        Assert.Equal("0.0171 mm", viewModel.ToleranceText);
        Assert.Equal("12", viewModel.EdgesText);
        Assert.Equal("2\u00A0048", viewModel.TriangleCountText);
        Assert.Equal("1.48 s", viewModel.ElapsedText);
        Assert.Equal("PRISMATIC", viewModel.KindText);
        Assert.True(viewModel.IsValid);
        Assert.Equal("VALID SOLID", viewModel.ValidityText);
        Assert.Equal(100, viewModel.Progress);
        Assert.Equal("READY", viewModel.StageText);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanExport);
    }

    [Fact]
    public async Task Converting_marks_all_five_stages_done()
    {
        var viewModel = Loaded(out _);

        await viewModel.ConvertAsync();

        Assert.All(viewModel.Stages, stage => Assert.Equal(StageState.Done, stage.State));
    }

    [Fact]
    public async Task Converting_publishes_the_result_for_the_viewport()
    {
        var viewModel = Loaded(out _);
        Meshsmith.Core.Pipeline.ConversionResult? raised = null;
        viewModel.ResultReady += (_, result) => raised = result;

        await viewModel.ConvertAsync();

        Assert.NotNull(raised);
        Assert.Equal(4, raised!.FaceCount);
    }

    [Fact]
    public async Task A_failed_conversion_marks_the_active_stage_failed_and_stays_convertible()
    {
        var viewModel = Loaded(out var conversion);
        conversion.FailAtStage = "building";

        await viewModel.ConvertAsync();

        Assert.True(viewModel.HasError);
        Assert.Equal("no regions recognised", viewModel.ErrorText);
        Assert.Equal(StageState.Done, viewModel.Stages[0].State);
        Assert.Equal(StageState.Done, viewModel.Stages[1].State);
        Assert.Equal(StageState.Failed, viewModel.Stages[2].State);
        Assert.Equal(StageState.Pending, viewModel.Stages[3].State);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanConvert);
        Assert.False(viewModel.HasResult);
    }

    [Fact]
    public async Task Convert_does_nothing_without_a_source()
    {
        var viewModel = NewViewModel(out _, out _, out _);

        await viewModel.ConvertAsync();

        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasResult);
    }

    [Fact]
    public async Task Export_commands_unlock_only_after_a_result()
    {
        var viewModel = Loaded(out _);
        Assert.False(viewModel.SaveStepCommand.CanExecute(null));
        Assert.False(viewModel.SaveIgesCommand.CanExecute(null));

        await viewModel.ConvertAsync();

        Assert.True(viewModel.SaveStepCommand.CanExecute(null));
        Assert.True(viewModel.SaveIgesCommand.CanExecute(null));
    }

    [Fact]
    public void Clearing_returns_every_panel_to_the_empty_state()
    {
        var viewModel = Loaded(out _);
        viewModel.Clear();

        Assert.False(viewModel.HasSource);
        Assert.False(viewModel.HasResult);
        Assert.Equal("—", viewModel.FacesText);
        Assert.Equal("No model loaded", viewModel.StatusRight);
        Assert.All(viewModel.Stages, stage => Assert.Equal(StageState.Pending, stage.State));
    }

    [Fact]
    public async Task Surface_mix_is_read_from_faces_json()
    {
        var viewModel = NewViewModel(out var conversion, out _, out _);
        conversion.Result = Results.Sample(TempFiles.FacesJson(planes: 3, cylinders: 1));
        Load(viewModel, TempFiles.Stl());

        await viewModel.ConvertAsync();

        Assert.Equal(3, viewModel.PlaneCount);
        Assert.Equal(1, viewModel.CylinderCount);
        Assert.Equal("75%", viewModel.PlanePercentText);
        Assert.Equal("25%", viewModel.CylinderPercentText);
        Assert.True(viewModel.HasMix);
    }

    [Fact]
    public void Show_grid_starts_on_and_raises_change_notifications()
    {
        var viewModel = NewViewModel(out _, out _, out _);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.ShowGrid = false;

        Assert.False(viewModel.ShowGrid);
        Assert.Contains(nameof(MainViewModel.ShowGrid), changed);
    }
}
