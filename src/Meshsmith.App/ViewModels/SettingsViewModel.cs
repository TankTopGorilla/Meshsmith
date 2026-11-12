using Meshsmith.App.Mvvm;
using Meshsmith.App.Services;

namespace Meshsmith.App.ViewModels;

/// <summary>Edits a copy of the three settings the dialog owns. Cancel needs no undo: nothing is
/// written until the caller takes <see cref="Accept"/>, and the recent list and window rectangle —
/// which the dialog never shows — are carried through untouched.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _original;
    private readonly IFileDialogService _dialogs;
    private bool _repair;
    private bool _verifyArtifacts;
    private string _defaultOutputFolder;

    public SettingsViewModel(AppSettings original, IFileDialogService dialogs)
    {
        _original = original;
        _dialogs = dialogs;
        _repair = original.Repair;
        _verifyArtifacts = original.VerifyArtifacts;
        _defaultOutputFolder = original.DefaultOutputFolder ?? "";
        BrowseCommand = new RelayCommand(_ => Browse());
        RestoreDefaultsCommand = new RelayCommand(_ => RestoreDefaults());
    }

    public bool Repair
    {
        get => _repair;
        set => Set(ref _repair, value);
    }

    public bool VerifyArtifacts
    {
        get => _verifyArtifacts;
        set => Set(ref _verifyArtifacts, value);
    }

    public string DefaultOutputFolder
    {
        get => _defaultOutputFolder;
        set => Set(ref _defaultOutputFolder, value);
    }

    public RelayCommand BrowseCommand { get; }
    public RelayCommand RestoreDefaultsCommand { get; }

    /// <summary>The settings to persist: this dialog's three fields on top of everything the
    /// session already had. A blank folder means "no suggestion", which is not the same as an empty
    /// string in the file.</summary>
    public AppSettings Accept() => _original with
    {
        Repair = _repair,
        VerifyArtifacts = _verifyArtifacts,
        DefaultOutputFolder = string.IsNullOrWhiteSpace(_defaultOutputFolder)
            ? null
            : _defaultOutputFolder.Trim()
    };

    private void Browse()
    {
        var picked = _dialogs.PickFolder(string.IsNullOrWhiteSpace(_defaultOutputFolder)
            ? null
            : _defaultOutputFolder);
        if (picked is not null) DefaultOutputFolder = picked;
    }

    /// <summary>Resets the form only. The recent list and the saved rectangle are not settings the
    /// dialog shows, so "defaults" cannot mean clearing them.</summary>
    private void RestoreDefaults()
    {
        var defaults = AppSettings.Default;
        Repair = defaults.Repair;
        VerifyArtifacts = defaults.VerifyArtifacts;
        DefaultOutputFolder = "";
    }
}
