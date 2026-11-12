using Meshsmith.App.Services;

namespace Meshsmith.App.Tests;

/// <summary>The recent list is bare strings in newest-first order; these are the only rules it has.</summary>
public class RecentFilesTests
{
    [Fact]
    public void Recent_files_are_newest_first()
    {
        var list = RecentFiles.Touch(new[] { @"C:\a.stl" }, @"C:\b.stl");

        Assert.Equal(new[] { @"C:\b.stl", @"C:\a.stl" }, list);
    }

    [Fact]
    public void The_same_file_twice_moves_to_the_top_without_duplicating()
    {
        var list = RecentFiles.Touch(new[] { @"C:\b.stl", @"C:\a.stl" }, @"C:\a.stl");

        Assert.Equal(new[] { @"C:\a.stl", @"C:\b.stl" }, list);
        Assert.Single(list, entry => entry.Equals(@"C:\a.stl", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_recent_list_stops_at_ten()
    {
        var list = new List<string>();
        for (int i = 0; i < 12; i++) list = RecentFiles.Touch(list, $@"C:\part{i}.stl");

        Assert.Equal(10, list.Count);
        Assert.Equal(@"C:\part11.stl", list[0]);
        Assert.Equal(@"C:\part2.stl", list[9]);
    }

    [Fact]
    public void Paths_compare_case_insensitively()
    {
        var list = RecentFiles.Touch(new[] { @"C:\Parts\A.stl" }, @"c:\parts\a.stl");

        Assert.Single(list);
        Assert.Equal(@"c:\parts\a.stl", list[0]);      // the spelling just used wins

        // The same comparer forgets a path whatever its casing: Windows path identity, not string
        // identity.
        Assert.Empty(RecentFiles.Without(list, @"C:\PARTS\A.STL"));
        Assert.Single(RecentFiles.Without(new[] { @"C:\a.stl", @"C:\b.stl" }, @"C:\a.stl"));
    }
}
