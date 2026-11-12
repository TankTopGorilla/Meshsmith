using System.IO;
using Meshsmith.App.Services;

namespace Meshsmith.App.Tests;

/// <summary>Persistence is a convenience: every failure below must leave the app with usable
/// defaults, one log line and no exception.</summary>
public class SettingsStoreTests
{
    private static string NewFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meshsmith-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void A_missing_settings_file_loads_the_defaults()
    {
        var log = new List<string>();
        var store = new SettingsStore(Path.Combine(NewFolder(), "settings.json"), log.Add);

        var settings = store.Load();

        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.True(settings.Repair);
        Assert.True(settings.VerifyArtifacts);
        Assert.Null(settings.DefaultOutputFolder);
        Assert.Empty(settings.Recent);
        Assert.Null(settings.Window);
        Assert.Single(log);
    }

    [Fact]
    public void A_corrupt_settings_file_falls_back_to_defaults_and_says_why()
    {
        var path = Path.Combine(NewFolder(), "settings.json");
        File.WriteAllText(path, "not json");
        var log = new List<string>();

        var settings = new SettingsStore(path, log.Add).Load();

        Assert.True(settings.Repair);
        Assert.True(settings.VerifyArtifacts);
        Assert.Contains(log, line => line.Contains("JsonException", StringComparison.Ordinal));
    }

    [Fact]
    public void Settings_survive_a_save_and_load_round_trip()
    {
        var path = Path.Combine(NewFolder(), "settings.json");
        var saved = AppSettings.Default with
        {
            Repair = false,
            VerifyArtifacts = false,
            DefaultOutputFolder = @"C:\out",
            Recent = new List<string> { @"C:\a.stl", @"C:\b.stl" },
            Window = new WindowPlacement(120, 80, 1440, 900, true)
        };

        Assert.True(new SettingsStore(path).Save(saved));

        var loaded = new SettingsStore(path).Load();

        // Members, not whole records: a record with a List member compares by reference.
        Assert.Equal(AppSettings.CurrentVersion, loaded.Version);
        Assert.False(loaded.Repair);
        Assert.False(loaded.VerifyArtifacts);
        Assert.Equal(@"C:\out", loaded.DefaultOutputFolder);
        Assert.Equal(new[] { @"C:\a.stl", @"C:\b.stl" }, loaded.Recent);
        Assert.Equal(new WindowPlacement(120, 80, 1440, 900, true), loaded.Window);
    }

    [Fact]
    public void An_unknown_schema_version_is_ignored_and_the_file_is_not_overwritten()
    {
        var path = Path.Combine(NewFolder(), "settings.json");
        const string future = """{"version":2,"repair":false,"verifyArtifacts":false}""";
        File.WriteAllText(path, future);
        var log = new List<string>();
        var store = new SettingsStore(path, log.Add);

        var settings = store.Load();

        Assert.True(settings.Repair);           // defaults, not the future file's switches
        Assert.True(settings.VerifyArtifacts);
        Assert.False(store.Save(settings));     // read-only for this session
        Assert.Equal(future, File.ReadAllText(path));
        Assert.NotEmpty(log);
    }

    [Fact]
    public void Saving_leaves_no_temporary_file_behind()
    {
        var folder = NewFolder();
        var path = Path.Combine(folder, "settings.json");

        Assert.True(new SettingsStore(path).Save(AppSettings.Default));

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
    }

    [Fact]
    public void A_settings_file_that_cannot_be_written_does_not_throw()
    {
        // A directory sits where the file belongs, so the final move cannot succeed.
        var path = Path.Combine(NewFolder(), "settings.json");
        Directory.CreateDirectory(path);
        var log = new List<string>();

        var saved = new SettingsStore(path, log.Add).Save(AppSettings.Default);

        Assert.False(saved);
        Assert.NotEmpty(log);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void A_zero_size_window_placement_means_no_placement()
    {
        var path = Path.Combine(NewFolder(), "settings.json");
        File.WriteAllText(path,
            """{"version":1,"window":{"x":40,"y":40,"w":0,"h":0,"maximized":false}}""");

        var settings = new SettingsStore(path).Load();

        Assert.Null(settings.Window);

        // A sparse file must not switch the booleans off: the trap a positional record falls into.
        Assert.True(settings.Repair);
        Assert.True(settings.VerifyArtifacts);
    }
}
