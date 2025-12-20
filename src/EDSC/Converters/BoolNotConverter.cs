using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EDSC.Converters
{
    /// <summary>
    /// Inverts boolean values for bindings.
    /// </summary>
    public class BoolNotConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }

            return true;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }

            return false;
        }
    }
}
