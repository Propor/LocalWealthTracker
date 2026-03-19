using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace LocalWealthTracker.Converters;

/// <summary>
/// Converts a List of double? (sparkline data) to a PointCollection
/// for rendering as a Polyline in a fixed-size canvas.
/// 
/// Normalizes values to fit within the given canvas dimensions.
/// Null values are treated as 0.
/// </summary>
public sealed class SparklineToPointsConverter : IValueConverter
{
    private const double CanvasWidth = 56;
    private const double CanvasHeight = 18;
    private const double Padding = 1;

    public object? Convert(object? value, Type targetType,
        object? parameter, CultureInfo culture)
    {
        if (value is not List<double?> data || data.Count < 2)
            return null;

        // Convert nulls to 0
        var values = data.Select(v => v ?? 0).ToList();

        double min = values.Min();
        double max = values.Max();
        double range = max - min;

        // Flat line — center it vertically
        if (range == 0) range = 1;

        var points = new PointCollection();
        int count = values.Count;

        for (int i = 0; i < count; i++)
        {
            double x = Padding + (i / (double)(count - 1)) * (CanvasWidth - 2 * Padding);
            double y = Padding + (1 - (values[i] - min) / range) * (CanvasHeight - 2 * Padding);
            points.Add(new Point(x, y));
        }

        return points;
    }

    public object ConvertBack(object? value, Type targetType,
        object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}