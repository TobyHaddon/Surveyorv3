using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using SurveyorCalibrationData; // for CalibrationData
using System;
using System.Text.RegularExpressions;
using Emgu.CV; // for Matrix<> and CvInvoke.Rodrigues


namespace Surveyor.User_Controls
{
    public sealed partial class SettingsCalibrationBoard : UserControl
    {
        // Survey Rules
       private CharucoBoardDefinition? charucoBoardDefinition = null;  // Bound to xaml
         
        public SettingsCalibrationBoard()
        {
            this.InitializeComponent();
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="project"></param>
        public void SetupForProjectSettingWindow(CalibProject? project)
        {
            // Remember the calibration data
            charucoBoardDefinition = project?.Data.CharucoBoardDefinition;

            // Bind runtime DataContext so XAML bindings (SquaresX/Y, PredefinedDictionaryName) work
            DataContext = charucoBoardDefinition;

            UpdateButtons();
        }


        /// <summary>
        /// Close resources
        /// </summary>
        public void Shutdown()
        {
            charucoBoardDefinition = null;
        }


        /// <summary>
        /// Control a Textbox to only allow positive decimal numbers to two decimal places
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private static void NumberTextBoxPositiveDecimal2DP_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
        {
            // Regex pattern to match positive numbers with up to two decimal places
            string pattern = @"^\d*\.?\d{0,2}$";

            if (!Regex.IsMatch(sender.Text, pattern))
            {
                int caretPosition = sender.SelectionStart - 1;

                // Allow only digits and one decimal point, with up to two decimal places
                sender.Text = Regex.Replace(sender.Text, @"[^0-9.]", ""); // Remove non-digit and non-dot characters

                // Ensure only one decimal point
                int firstDotIndex = sender.Text.IndexOf('.');
                if (firstDotIndex != -1)
                {
                    // Remove any extra dots
                    sender.Text = sender.Text.Substring(0, firstDotIndex + 1) + sender.Text.Substring(firstDotIndex + 1).Replace(".", "");

                    // Limit to two decimal places
                    int decimalCount = sender.Text.Length - firstDotIndex - 1;
                    if (decimalCount > 2)
                    {
                        sender.Text = sender.Text.Substring(0, firstDotIndex + 3);
                    }
                }

                // Restore cursor position
                sender.SelectionStart = Math.Max(caretPosition, 0);
            }
        }


        /// <summary>
        /// Control a Textbox to only allow positive whole numbers
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private static void NumberTextBoxPositiveWholeNumber_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
        {
            // Allow only digits (0-9)
            string pattern = @"^\d*$";

            if (!Regex.IsMatch(sender.Text, pattern))
            {
                int caretPosition = sender.SelectionStart - 1;

                // Remove all non-numeric characters
                sender.Text = Regex.Replace(sender.Text, @"\D", "");

                // Restore cursor position
                sender.SelectionStart = Math.Max(caretPosition, 0);
            }

        }


        private void UpdateButtons()
        {

        }

        // Minimal preview refresh method; keep no-op if actual implementation lives elsewhere
        private void UpdatePreview()
        {
            // Intentionally left blank; existing preview logic will call into here if present
        }

        private void ArucoDictionaryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Keep model in sync if binding is not yet active
            if (charucoBoardDefinition != null && ArucoDictionaryCombo.SelectedItem is Emgu.CV.Aruco.Dictionary.PredefinedDictionaryName dict)
            {
                charucoBoardDefinition.PredefinedDictionaryName = dict;
            }

            UpdatePreview();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Populate combo with the enum names/values
            var values = Enum.GetValues(typeof(Emgu.CV.Aruco.Dictionary.PredefinedDictionaryName));
            ArucoDictionaryCombo.ItemsSource = values;

            // Ensure selection reflects current model
            if (DataContext is CharucoBoardDefinition cbd)
            {
                ArucoDictionaryCombo.SelectedItem = cbd.PredefinedDictionaryName;
            }
        }
    }


    public partial class DoubleFormatConverter : IValueConverter
    {
        public string? Format { get; set; } = "F2";

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is null) return string.Empty;
            if (value is double d)
            {
                try
                {
                    return d.ToString(Format);
                }
                catch
                {
                    return d.ToString("F2");
                }
            }
            return value.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is string s && double.TryParse(s, out var d))
                return d;
            return 0.0;
        }
    }

    public class DoubleSubtractConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double d)
            {
                double sub = 0;
                if (parameter is string s && double.TryParse(s, out var p)) sub = p;
                else if (parameter is double pd) sub = pd;
                var result = d - sub;
                return result > 0 ? result : 0;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }



}
