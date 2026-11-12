using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Meshsmith.App.Converters;

/// <summary>0..1 share → pixel width, where the target width is the converter parameter.</summary>
public sealed class ShareToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double share = value is double d ? d : 0;
        double total = 0;
        if (parameter is string text) double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out total);
        return Math.Max(0, Math.Min(1, share)) * total;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
