using Meshsmith.App.Mvvm;

namespace Meshsmith.App.ViewModels;

public enum StageState
{
    Pending,
    Active,
    Done,
    Failed
}

/// <summary>One row of the pipeline checklist in the source rail.</summary>
public sealed class PipelineStage : ObservableObject
{
    private StageState _state;

    public PipelineStage(string name) => Name = name;

    public string Name { get; }

    public StageState State
    {
        get => _state;
        set => Set(ref _state, value);
    }
}
