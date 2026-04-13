using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Surveyor.Converters
{
    /// <summary>
    /// This converter is used by the XAML to hide a whole StackPanel if a string null in one of it's elements is blank or null
    /// </summary>
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


    /// <summary>
    /// XAML Converter 
    /// </summary>
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
}
