using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Surveyor.Helper
{
    /// <summary>
    /// This converter is used by the XAML to convert a DateTime to a string
    /// </summary>
    public partial class SurveyDateTimeToStringConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is DateTime dateTime && parameter is string format)
            {
                return dateTime.ToString(format);
            }
            return value?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }


    /// <summary>
    /// This converter is used by the XAML to convert a TimeSpan to a string
    /// </summary>
    public partial class SurveyTimeSpanToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value == null || value is not TimeSpan)
                return "";

            if (TimeSpan.TryParse(value.ToString(), out TimeSpan timeSpan))
            {
                string format = parameter as string ?? @"hh\:mm\:ss";
                return timeSpan.ToString(format);
            }

            return "Invalid";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
