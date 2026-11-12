using System.Globalization;

namespace Meshsmith.App.ViewModels;

/// <summary>All user-visible numbers go through here so the UI and the tests agree byte for byte.</summary>
public static class Format
{
    private const char Nbsp = '\u00A0';

    public static string Count(int value) =>
        value.ToString("#,##0", CultureInfo.InvariantCulture).Replace(',', Nbsp);

    public static string Volume(double cubicMillimetres) =>
        cubicMillimetres.ToString("#,##0.000", CultureInfo.InvariantCulture).Replace(',', Nbsp);

    public static string Millimetres(double millimetres) =>
        millimetres.ToString("0.0000", CultureInfo.InvariantCulture) + " mm";

    public static string Seconds(double seconds) =>
        seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s";

    public static string Dimensions(double x, double y, double z) =>
        string.Format(CultureInfo.InvariantCulture, "{0:0.0} × {1:0.0} × {2:0.0} mm", x, y, z);
}
