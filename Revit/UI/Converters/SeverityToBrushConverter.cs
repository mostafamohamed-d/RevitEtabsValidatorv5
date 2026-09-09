using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using RevitEtabsValidator.Core.Validation;

namespace RevitEtabsValidator.Revit.UI.Converters;

// Drives the colored severity stripe in the results grid. Matched/Info rows stay
// unmarked so a clean scan of the grid highlights only the rows that need attention.
public sealed class SeverityToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush CriticalBrush = new(Color.FromRgb(0xDC, 0x26, 0x26));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xF9, 0x73, 0x16));
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(0xD9, 0x77, 0x06));
    private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(0xE5, 0xE7, 0xEB));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Severity severity
            ? severity switch
            {
                Severity.Critical => CriticalBrush,
                Severity.Error => ErrorBrush,
                Severity.Warning => WarningBrush,
                _ => InfoBrush
            }
            : InfoBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
