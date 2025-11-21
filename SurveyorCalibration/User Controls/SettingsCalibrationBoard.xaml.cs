using Emgu.CV; // for Matrix<> and CvInvoke.Rodrigues
using Emgu.CV.Aruco;
using Emgu.CV.Structure; // for MCvScalar
using Emgu.CV.Util;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using SurveyorCalibrationData; // for CalibrationData
using System;
using System.Drawing; // Size
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using static Emgu.CV.Aruco.Dictionary;

namespace Surveyor.User_Controls
{
    public sealed partial class SettingsCalibrationBoard : UserControl
    {
        private CharucoBoardDefinition? charucoBoardDefinition = null;  // Bound to xaml
        private bool _loaded = false;

        public SettingsCalibrationBoard()
        {
            InitializeComponent();
        }

        public void SetupForProjectSettingWindow(CalibProject? project)
        {
            charucoBoardDefinition = project?.Data.CharucoBoardDefinition;
            DataContext = charucoBoardDefinition;
            UpdateButtons();
            _ = UpdatePreview();
        }

        public void Shutdown()
        {
            charucoBoardDefinition = null;
            PreviewImage.Source = null;
        }

        private void NumberTextBoxPositiveDecimal2DP_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
        {
            string pattern = "^\\d*\\.?\\d{0,2}$";
            if (!Regex.IsMatch(sender.Text, pattern))
            {
                int caretPosition = sender.SelectionStart - 1;
                sender.Text = Regex.Replace(sender.Text, "[^0-9.]", "");
                int firstDotIndex = sender.Text.IndexOf('.');
                if (firstDotIndex != -1)
                {
                    sender.Text = sender.Text.Substring(0, firstDotIndex + 1) + sender.Text.Substring(firstDotIndex + 1).Replace(".", "");
                    int decimalCount = sender.Text.Length - firstDotIndex - 1;
                    if (decimalCount > 2)
                        sender.Text = sender.Text.Substring(0, firstDotIndex + 3);
                }
                sender.SelectionStart = Math.Max(caretPosition, 0);
            }
        }

        private void NumberTextBoxPositiveWholeNumber_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
        {
            string pattern = "^\\d*$";
            if (!Regex.IsMatch(sender.Text, pattern))
            {
                int caretPosition = sender.SelectionStart - 1;
                sender.Text = Regex.Replace(sender.Text, "\\D", "");
                sender.SelectionStart = Math.Max(caretPosition, 0);
            }
        }

        private void UpdateButtons() 
        {

        }


        /// <summary>
        /// Generatess and displays a ChAruco board preview image based on the current settings.
        /// </summary>
        /// <returns></returns>
        private async Task UpdatePreview()
        {
            if (!_loaded || charucoBoardDefinition == null) return;
            try
            {
                // 1. Get / clamp inputs
                int squaresX = Math.Max(1, charucoBoardDefinition.SquaresX);
                int squaresY = Math.Max(1, charucoBoardDefinition.SquaresY);
                float squareLength = Math.Max(0.001f, charucoBoardDefinition.SquareLength);
                float markerLength = Math.Max(0.001f, charucoBoardDefinition.MarkerLength);

                // 2. Create dictionary & board
                var dictionary = new Dictionary(charucoBoardDefinition.PredefinedDictionaryName);
                using var board = new CharucoBoard(squaresX, squaresY, squareLength, markerLength, dictionary);

                // 3. Choose a render size in *pixels*
                //    This is just for on-screen preview, so pick something sensible.
                const int pixelsPerSquare = 80; // adjust as you like
                int imgWidth = squaresX * pixelsPerSquare;
                int imgHeight = squaresY * pixelsPerSquare;

                using var boardMat = new Mat();

                // 4. Render the Charuco board into the Mat
                // marginSize: white border around the board in pixels
                // borderBits: width (in bits) of the marker border
                board.GenerateImage(
                    new System.Drawing.Size(imgWidth, imgHeight),
                    boardMat,
                    marginSize: 20,
                    borderBits: 1);

                // 5. Convert Mat -> PNG bytes (via System.Drawing.Bitmap)
                // --- Mat -> PNG bytes using OpenCV (no System.Drawing) ---
                using var buf = new VectorOfByte();
                Emgu.CV.CvInvoke.Imencode(".png", boardMat, buf);
                byte[] pngBytes = buf.ToArray();

                // --- PNG bytes -> BitmapImage (WinUI 3) ---
                using IRandomAccessStream ras = new InMemoryRandomAccessStream();
                await ras.WriteAsync(pngBytes.AsBuffer());
                ras.Seek(0);

                var bitmapImage = new BitmapImage();
                await bitmapImage.SetSourceAsync(ras);

                // 7. Set it as the source of your <Image>
                PreviewImage.Source = bitmapImage; 
                PreviewCaption1.Text = $"{squaresX} x {squaresY} squares  {charucoBoardDefinition.PredefinedDictionaryName}  Square:{squareLength:F2} Marker:{markerLength:F2}";
                PreviewCaption2.Text = $"This should look exactly the same as your physical board i.e. same number of squares and same markers in the same positions.";
            }
            catch (Exception ex)
            {
                PreviewCaption1.Text = $"Preview failed {ex.Message}";
                PreviewCaption2.Text = $"";
            }
        }

        private void ArucoDictionaryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (charucoBoardDefinition != null && ArucoDictionaryCombo.SelectedItem is Dictionary.PredefinedDictionaryName dict)
                charucoBoardDefinition.PredefinedDictionaryName = dict;
            _ = UpdatePreview();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            _loaded = true;
            ArucoDictionaryCombo.ItemsSource = Enum.GetValues(typeof(Dictionary.PredefinedDictionaryName));
            if (DataContext is CharucoBoardDefinition cbd)
                ArucoDictionaryCombo.SelectedItem = cbd.PredefinedDictionaryName;
            _ = UpdatePreview();
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
                try { return d.ToString(Format); } catch { return d.ToString("F2"); }
            }
            return value.ToString() ?? "";
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is string s && double.TryParse(s, out var d)) return d; return 0.0;
        }
    }

    public class DoubleSubtractConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double d)
            {
                double sub = 0;
                if (parameter is string s && double.TryParse(s, out var p)) sub = p; else if (parameter is double pd) sub = pd;
                var result = d - sub; return result > 0 ? result : 0;
            }
            return value;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
}
