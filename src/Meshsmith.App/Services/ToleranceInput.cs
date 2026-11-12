using System.Globalization;

namespace Meshsmith.App.Services;

/// <summary>
/// Reads the tolerance box. Both decimal separators are accepted on purpose: this app runs on Turkish
/// Windows where "0,05" is the local form, while every CAD datasheet and forum answer writes "0.05".
/// A field that rejects one of them is a trap, not a localisation.
/// </summary>
public static class ToleranceInput
{
    public static bool TryParse(string? text, out double value) =>
        TryParse(text, CultureInfo.CurrentCulture, out value);

    /// <summary>The given culture first, then invariant - so "0.05" and "0,05" both work whichever
    /// locale is primary. The culture is a parameter rather than ambient state so the rule can be
    /// tested without touching the machine's locale.</summary>
    public static bool TryParse(string? text, CultureInfo culture, out double value) =>
        Parse(text, culture, out value) ||
        Parse(text, CultureInfo.InvariantCulture, out value) ||
        Parse(text?.Replace(',', '.'), CultureInfo.InvariantCulture, out value);

    private static bool Parse(string? text, CultureInfo culture, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!double.TryParse(text, NumberStyles.Float, culture, out double parsed)) return false;
        if (!(parsed > 0) || double.IsInfinity(parsed)) return false;

        value = parsed;
        return true;
    }
}
