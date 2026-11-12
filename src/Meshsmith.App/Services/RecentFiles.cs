namespace Meshsmith.App.Services;

/// <summary>The last ten parts, as full paths in newest-first order.
///
/// <para>The array order already is the recency order, so a timestamp per entry would only add
/// clock handling. Paths compare case-insensitively because that is how Windows identifies a file,
/// and a duplicate in the list is a bug the user can see.</para></summary>
public static class RecentFiles
{
    public const int Max = 10;

    /// <summary>Puts <paramref name="path"/> at the top, moves an earlier copy rather than
    /// duplicating it, and drops the tail beyond <paramref name="max"/>.</summary>
    public static List<string> Touch(IReadOnlyList<string> current, string path, int max = Max)
    {
        var updated = new List<string> { path };

        foreach (var entry in current)
        {
            if (!string.Equals(entry, path, StringComparison.OrdinalIgnoreCase)) updated.Add(entry);
        }

        if (updated.Count > max) updated.RemoveRange(max, updated.Count - max);
        return updated;
    }

    /// <summary>Removes <paramref name="path"/>; what is left keeps its order.</summary>
    public static List<string> Without(IReadOnlyList<string> current, string path)
    {
        var kept = new List<string>(current.Count);

        foreach (var entry in current)
        {
            if (!string.Equals(entry, path, StringComparison.OrdinalIgnoreCase)) kept.Add(entry);
        }

        return kept;
    }
}
