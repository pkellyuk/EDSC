using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EDSC.Converters
{
    /// <summary>
    /// Converts a boolean to a status color.
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        private static readonly IBrush ConnectedBrush = new SolidColorBrush(Color.Parse("#4CAF50"));
        private static readonly IBrush DisconnectedBrush = new SolidColorBrush(Color.Parse("#F44336"));

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue)
            {
                return ConnectedBrush;
            }

            return DisconnectedBrush;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value == ConnectedBrush;
        }
    }
}
