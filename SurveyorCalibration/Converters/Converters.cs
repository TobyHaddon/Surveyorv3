using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;
using System.Globalization;

namespace Surveyor.Converters
{
    public sealed partial class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string valueString)
                return string.IsNullOrWhiteSpace(valueString) == true ? Visibility.Collapsed : Visibility.Visible;
            else
                return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public sealed partial class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Visible;
            }
            return false;
        }
    }


    /// <summary>
    /// Converts type of double, float or in meters to string in millimeters with specified format.
    /// XAML usage:
    /// 
    ///     <UserControl.Resources>
    ///         <converters:MetersToMillimetersConverter x:Key="MetersToMillimetersConverter2DP" Format="F2" />
    ///         <converters:MetersToMillimetersConverter x:Key="MetersToMillimetersConverter0DP" Format="F0" />
    ///     </UserControl.Resources>
    ///     ...
    ///     <TextBox x:Name="BoardSizeYNumeric" Width="60" 
    ///                      Text="{x:Bind BoardSizeY, 
    ///                      Mode=TwoWay, 
    ///                      Converter={StaticResource MetersToMillimetersConverter0DP}}" 
    ///
    /// </summary>
    public sealed class MetersToMillimetersConverter : IValueConverter
    {
        public string? Format { get; set; } = "F2";

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is null) return string.Empty;

            double meters = value switch
            {
                double d => d,
                float f => f,
                int i => i,
                _ => 0.0
            };

            double mm = meters * 1000.0;
            try
            {
                return mm.ToString(Format);
            }
            catch
            {
                return mm.ToString("F2");
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is not string s)
                return DependencyProperty.UnsetValue;

            s = s.Trim();

            // Intermediate / incomplete user input: keep existing source value
            if (string.IsNullOrEmpty(s) ||
                s.EndsWith(".", StringComparison.Ordinal) ||
                s == "-" ||
                s == "." ||
                s == "0.")
            {
                return DependencyProperty.UnsetValue; // cancels update; source stays unchanged
            }

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out double mm))
            {
                double meters = mm / 1000.0;
                if (targetType == typeof(float))
                    return (float)meters;
                if (targetType == typeof(double))
                    return meters;
                if (targetType == typeof(int))
                    return (int)Math.Round(meters); // unlikely, but provided
                return meters;
            }

            // Parse failed: do not overwrite source with 0
            return DependencyProperty.UnsetValue;
        }
    }
}
