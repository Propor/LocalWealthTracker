using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LocalWealthTracker.Converters;

public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }
        return new SolidColorBrush(Color.FromRgb(99, 128, 0));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
