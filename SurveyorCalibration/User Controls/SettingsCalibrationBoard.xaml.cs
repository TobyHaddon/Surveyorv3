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
        public void SetupForProjectSettingWindow(CalibProject project)
        {
            // Remember the calibration daata
            charucoBoardDefinition = project.Data.CharucoBoardDefinition;


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
            UpdatePreview();
        }
    }

    public sealed partial class PreferredCalibrationItemVisibilityConverter : IValueConverter
    {
        // The currently preferred row item — set from code-behind
        public CalibrationData? PreferredItem { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            // value is the row’s data item (CalibrationData)
            if (value is CalibrationData row && PreferredItem is not null)
            {
                // reference or Equals — either is fine if your model overrides Equals
                if (ReferenceEquals(row, PreferredItem) || row.Equals(PreferredItem))
                    return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
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

    public class CalibrationDistortionElementConverter : IValueConverter
    {
        // parameter format: "L,index[,format]" or "R,index[,format]" where index maps to [k1,k2,p1,p2,k3,k4,k5,k6]
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string fmt = "F2"; // default
            if (value is CalibrationData cd && parameter is string p)
            {
                try
                {
                    var parts = p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        bool left = parts[0].Equals("L", StringComparison.OrdinalIgnoreCase);
                        int idx = int.Parse(parts[1]);
                        if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
                            fmt = parts[2];

                        var m = left ? cd.LeftCameraCalibration?.Distortion : cd.RightCameraCalibration?.Distortion;
                        if (m is not null && m.Rows > 0 && idx >= 0 && idx < m.Cols)
                        {
                            double d = m[0, idx];
                            return d.ToString(fmt);
                        }
                    }
                }
                catch { }
            }

            // If the binding value is already the Matrix, try fallback for convenience
            if (value is Matrix<double> mat && mat.Rows > 0)
            {
                return mat[0, 0].ToString(fmt);
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public partial class CalibrationIntrinsicElementConverter : IValueConverter
    {
        // parameter format: "L,row,col[,format]" or "R,row,col[,format]"
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string fmt = "F2"; // default
            if (value is CalibrationData cd && parameter is string p)
            {
                try
                {
                    var parts = p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        bool left = parts[0].Equals("L", StringComparison.OrdinalIgnoreCase);
                        int row = int.Parse(parts[1]);
                        int col = int.Parse(parts[2]);
                        if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]))
                            fmt = parts[3];

                        var m = left ? cd.LeftCameraCalibration?.Intrinsic : cd.RightCameraCalibration?.Intrinsic;
                        if (m is not null && row >= 0 && row < m.Rows && col >= 0 && col < m.Cols)
                        {
                            double d = m[row, col];
                            return d.ToString(fmt);
                        }
                    }
                }
                catch { }
            }

            // Fallback: if value is Matrix
            if (value is Matrix<double> mat)
            {
                return mat[0, 0].ToString(fmt);
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public class StereoMatrixVectorConverter : IValueConverter
    {
        // parameter format: "Rot[,format]" or "Trans[,format]"
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string fmt = "F2";
            if (value is not CalibrationData cd || parameter is not string p)
                return string.Empty;

            var parts = p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) fmt = parts[1];

            try
            {
                if (parts[0].Equals("Rot", StringComparison.OrdinalIgnoreCase))
                {
                    var R = cd.StereoCameraCalibration?.Rotation;
                    if (R is null) return string.Empty;

                    // Convert rotation matrix to Rodrigues vector
                    Matrix<double> rvec = new(3, 1);
                    CvInvoke.Rodrigues(R, rvec);
                    return $"[{rvec[0,0].ToString(fmt)}, {rvec[1,0].ToString(fmt)}, {rvec[2,0].ToString(fmt)}]";
                }
                else if (parts[0].Equals("Trans", StringComparison.OrdinalIgnoreCase))
                {
                    var T = cd.StereoCameraCalibration?.Translation;
                    if (T is null) return string.Empty;

                    double x, y, z;
                    if (T.Rows == 1 && T.Cols >= 3)
                    {
                        x = T[0, 0]; y = T[0, 1]; z = T[0, 2];
                    }
                    else if (T.Rows >= 3 && T.Cols == 1)
                    {
                        x = T[0, 0]; y = T[1, 0]; z = T[2, 0];
                    }
                    else
                    {
                        return string.Empty;
                    }

                    return $"[{x.ToString(fmt)}, {y.ToString(fmt)}, {z.ToString(fmt)}]";
                }
            }
            catch { }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
