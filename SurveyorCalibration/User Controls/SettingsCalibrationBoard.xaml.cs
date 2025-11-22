using Emgu.CV; // for Matrix<> and CvInvoke.Rodrigues
using Emgu.CV.Aruco;
using Emgu.CV.Util;
using iText.IO.Image;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using static Emgu.CV.Aruco.Dictionary;

namespace Surveyor.User_Controls
{

    /// <summary>
    /// DataContext for SettingsCalibrationBoard
    /// </summary>
    public partial class DataContextLocal : System.ComponentModel.INotifyPropertyChanged
    {
        private CharucoBoardDefinition? _cbd;
        public CharucoBoardDefinition? Cbd
        {
            get => _cbd;
            set { _cbd = value; OnPropertyChanged(nameof(Cbd)); }
        }

        private double _boardSizeX;
        public double BoardSizeX
        {
            get => _boardSizeX;
            set { _boardSizeX = value; OnPropertyChanged(nameof(BoardSizeX)); }
        }

        private double _boardSizeY;
        public double BoardSizeY
        {
            get => _boardSizeY;
            set { _boardSizeY = value; OnPropertyChanged(nameof(BoardSizeY)); }
        }

        private int _printDPI;
        public int PrintDPI
        {
            get => _printDPI;
            set { _printDPI = value; OnPropertyChanged(nameof(PrintDPI)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    };

    public sealed partial class SettingsCalibrationBoard : UserControl
    {
        // Class-level variables
        private CharucoBoardDefinition? charucoBoardDefinition = null;  // Passed in 
        private CharucoBoardDefinition? cbdWorking;                     // Working on
        private bool _loaded = false;
        private readonly DispatcherTimer _previewTimer = new();
        private bool _previewPending = false;


        // Dependency Property to set the 'InstanceType' .xaml attribute (BoardSetup/DefaultsManager)
        public static readonly DependencyProperty InstanceTypeProperty =
        DependencyProperty.Register(nameof(InstanceType), typeof(string), typeof(SettingsCalibrationBoard),
            new PropertyMetadata("BoardSetup", OnHeadChanged));  // This is the default 'InstanceType'
        public string InstanceType
        {
            get => (string)GetValue(InstanceTypeProperty);
            set => SetValue(InstanceTypeProperty, value);
        }
        private static void OnHeadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SettingsCalibrationBoard ctrl)
            {
                ctrl.ApplyMode((string)e.NewValue);
            }
        }
        private bool IsBoardSetupMode => InstanceType.Equals("BoardSetup", StringComparison.InvariantCultureIgnoreCase);
        private bool IsDefaultsManagerMode => InstanceType.Equals("DefaultsManager", StringComparison.InvariantCultureIgnoreCase);
        private void ApplyMode(string mode)
        {
            if (IsBoardSetupMode)
            {
                // Hide board dimension, dpi and Save to PDF button
                RootGrid.RowDefinitions[6].Height = new GridLength(0);
                RootGrid.RowDefinitions[7].Height = new GridLength(0);
                RootGrid.RowDefinitions[8].Height = new GridLength(0);
                RootGrid.RowDefinitions[9].Height = new GridLength(0);
                SaveToPDFButton.IsEnabled = false;
            }
            else if (IsDefaultsManagerMode)
            {
                // Show board dimension, dpi and Save to PDF button
                RootGrid.RowDefinitions[6].Height = new GridLength(1, GridUnitType.Star);
                RootGrid.RowDefinitions[7].Height = new GridLength(1, GridUnitType.Star);
                RootGrid.RowDefinitions[8].Height = new GridLength(1, GridUnitType.Star);
                RootGrid.RowDefinitions[9].Height = new GridLength(1, GridUnitType.Star);
                SaveToPDFButton.IsEnabled = true;
            }
            else
            {
                throw new InvalidOperationException($"Unknown Type mode: {mode}");
            }
        }


        public SettingsCalibrationBoard()
        {
            InitializeComponent();
            // Configure debounce timer
            _previewTimer.Interval = TimeSpan.FromMilliseconds(400);
            _previewTimer.Tick += PreviewTimer_Tick;
        }


        /// <summary>
        /// Must be called to setup the control for use in the Project Settings window.
        /// </summary>
        /// <param name="project"></param>
        public void SetupForProjectSettingWindow(CalibProject? project)
        {
            // Remember what was passed in
            charucoBoardDefinition = project?.Data.CharucoBoardDefinition;
            
            // Check if are just managing defaults
            if (IsDefaultsManagerMode)
            {
                // Set default values for later use
                if (!Enum.TryParse<PredefinedDictionaryName>(SettingsManagerLocal.DefaultCharucoBoard_PredefinedDictionaryName,
                                                             ignoreCase: true,
                                                             out PredefinedDictionaryName predefinedDictionaryName))
                {
                    predefinedDictionaryName = PredefinedDictionaryName.Dict5X5_100; // double default shouldn't be necessary
                }

                cbdWorking = new CharucoBoardDefinition
                {
                    SquaresX = SettingsManagerLocal.DefaultCharucoBoard_SquaresX,
                    SquaresY = SettingsManagerLocal.DefaultCharucoBoard_SquaresY,
                    SquareLength = (float)SettingsManagerLocal.DefaultCharucoBoard_SquareLength,
                    MarkerLength = (float)SettingsManagerLocal.DefaultCharucoBoard_MarkerLength,
                    PredefinedDictionaryName = predefinedDictionaryName
                };
            }
            else
            {
                // Setup an actual board this project
                cbdWorking = charucoBoardDefinition;
                if (cbdWorking is null)
                    return;

                // Check if the defaults are required (i.e. first time setup)
                if (cbdWorking.SquaresX == 0)
                {
                    cbdWorking.SquaresX = SettingsManagerLocal.DefaultCharucoBoard_SquaresX;
                    Debug.WriteLine($"Set CharucoBoard SquaresX to default {cbdWorking.SquaresX}");
                }
                if (cbdWorking.SquaresY == 0)
                {
                    cbdWorking.SquaresY = SettingsManagerLocal.DefaultCharucoBoard_SquaresY;
                    Debug.WriteLine($"Set CharucoBoard SquaresY to default {cbdWorking.SquaresY}");
                }
                if (cbdWorking.SquareLength == 0)
                {
                    cbdWorking.SquareLength = (float)SettingsManagerLocal.DefaultCharucoBoard_SquareLength;
                    Debug.WriteLine($"Set CharucoBoard SquareLength to default {cbdWorking.SquareLength}");
                }
                if (cbdWorking.MarkerLength == 0)
                {
                    cbdWorking.MarkerLength = (float)SettingsManagerLocal.DefaultCharucoBoard_MarkerLength;
                    Debug.WriteLine($"Set CharucoBoard MarkerLength to default {cbdWorking.MarkerLength}");
                }
                if (cbdWorking.PredefinedDictionaryName == 0)
                {
                    if (!Enum.TryParse<PredefinedDictionaryName>(SettingsManagerLocal.DefaultCharucoBoard_PredefinedDictionaryName,
                                                                 ignoreCase: true,
                                                                 out PredefinedDictionaryName predefinedDictionaryNameDefault))
                    {
                        predefinedDictionaryNameDefault = PredefinedDictionaryName.Dict5X5_100;  // double default shouldn't be necessary
                    }
                    cbdWorking.PredefinedDictionaryName = predefinedDictionaryNameDefault;
                    Debug.WriteLine($"Set CharucoBoard PredefinedDictionaryName to default {cbdWorking.PredefinedDictionaryName}");
                }
            }

            // Setup DataContext for binding to the XAML controls
            DataContext = new DataContextLocal()
            {
                Cbd = cbdWorking,
                BoardSizeX = SettingsManagerLocal.DefaultBoard_SizeX,   // In metres
                BoardSizeY = SettingsManagerLocal.DefaultBoard_SizeY,   // In metres
                PrintDPI = SettingsManagerLocal.DefaultBoardDPI         // In DPI
            };

            UpdateButtons();
            _ = BoardInputChanged();
        }


        public void Shutdown()
        {
            charucoBoardDefinition = null;
            PreviewImage.Source = null;
        }

        /// 
        /// EVENTS
        /// 


        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
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
            SchedulePreviewUpdate();
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
            SchedulePreviewUpdate();
        }

        // TextChanged fallback in case binding updates only after change
        private void NumericTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SchedulePreviewUpdate();
        }

        private void ArucoDictionaryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (charucoBoardDefinition != null && ArucoDictionaryCombo.SelectedItem is Dictionary.PredefinedDictionaryName dict)
                charucoBoardDefinition.PredefinedDictionaryName = dict;
            SchedulePreviewUpdate();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            _loaded = true;
            ArucoDictionaryCombo.ItemsSource = Enum.GetValues(typeof(Dictionary.PredefinedDictionaryName));
            if (DataContext is CharucoBoardDefinition cbd)
                ArucoDictionaryCombo.SelectedItem = cbd.PredefinedDictionaryName;
            _ = BoardInputChanged();
        }

        private void SaveToPDF_Click(object sender, RoutedEventArgs e)
        {

        }


        /// 
        /// PRIVATE
        /// 

        /// <summary>
        /// Timer used to refresh the board preview image
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PreviewTimer_Tick(object? sender, object e)
        {
            _previewTimer.Stop();
            if (_previewPending)
            {
                _previewPending = false;
                _ = BoardInputChanged();
            }
        }

        private void SchedulePreviewUpdate()
        {
            if (!_loaded || charucoBoardDefinition == null) return;
            _previewPending = true;
            _previewTimer.Stop();
            _previewTimer.Start();
        }
        private void UpdateButtons() 
        {

        }


        /// <summary>
        /// Generates and displays a ChAruco board preview image based on the current settings.
        /// If the IsDefaultsManagerMode is true then we are in a mode where we are setting up
        /// the default board for new projects.  In which we set the defaults to local storage.
        /// </summary>
        /// <returns></returns>
        private async Task BoardInputChanged()
        {
            if (!_loaded || cbdWorking is null) return;

            try
            {
                // 1. Get / clamp inputs
                int squaresX = Math.Max(1, cbdWorking.SquaresX);
                int squaresY = Math.Max(1, cbdWorking.SquaresY);
                float squareLength = Math.Max(0.001f, cbdWorking.SquareLength);
                float markerLength = Math.Max(0.001f, cbdWorking.MarkerLength);

                // 2. Create dictionary & board
                var dictionary = new Dictionary(cbdWorking.PredefinedDictionaryName);
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
                using InMemoryRandomAccessStream ras = new();
                await ras.WriteAsync(pngBytes.AsBuffer());
                ras.Seek(0);

                var bitmapImage = new BitmapImage();
                await bitmapImage.SetSourceAsync(ras);

                // 7. Set it as the source of your <Image>
                PreviewImage.Source = bitmapImage;
                PreviewCaption1.Text = $"{squaresX} x {squaresY} squares  {cbdWorking.PredefinedDictionaryName}  Square:{squareLength:F2}mm Marker:{markerLength:F2}mm";
                PreviewCaption2.Text = $"This should look exactly the same as your physical board i.e. same number of squares and same markers in the same positions.";
            }
            catch (Exception ex)
            {
                PreviewCaption1.Text = $"Preview failed {ex.Message}";
                PreviewCaption2.Text = $"";
            }

            // Set any changed defauls if necessary
            if (IsDefaultsManagerMode)
            {
                // Did the default for SquaresX change?
                if (SettingsManagerLocal.DefaultCharucoBoard_SquaresX != cbdWorking.SquaresX)
                {
                    SettingsManagerLocal.DefaultCharucoBoard_SquaresX = cbdWorking.SquaresX;
                    Debug.WriteLine($"Updated default CharucoBoard SquaresX to {cbdWorking.SquaresX}");
                }

                // Did the default for SquaresY change?
                if (SettingsManagerLocal.DefaultCharucoBoard_SquaresY != cbdWorking.SquaresY)
                {
                    SettingsManagerLocal.DefaultCharucoBoard_SquaresY = cbdWorking.SquaresY;
                    Debug.WriteLine($"Updated default CharucoBoard SquaresY to {cbdWorking.SquaresY}");
                }

                // Did the default for SquareLength change?
                if (SettingsManagerLocal.DefaultCharucoBoard_SquareLength != cbdWorking.SquareLength)
                {
                    SettingsManagerLocal.DefaultCharucoBoard_SquareLength = cbdWorking.SquareLength;
                    Debug.WriteLine($"Updated default CharucoBoard SquareLength to {cbdWorking.SquareLength}");
                }

                // Did the default for MarkerLength change?
                if (SettingsManagerLocal.DefaultCharucoBoard_MarkerLength != cbdWorking.MarkerLength)
                {
                    SettingsManagerLocal.DefaultCharucoBoard_MarkerLength = cbdWorking.MarkerLength;
                    Debug.WriteLine($"Updated default CharucoBoard MarkerLength to {cbdWorking.MarkerLength}");
                }

                // Did the default for PredefinedDictionaryName change?
                if (SettingsManagerLocal.DefaultCharucoBoard_PredefinedDictionaryName != cbdWorking.PredefinedDictionaryName.ToString())
                {
                    SettingsManagerLocal.DefaultCharucoBoard_PredefinedDictionaryName = cbdWorking.PredefinedDictionaryName.ToString();
                    Debug.WriteLine($"Updated default CharucoBoard PredefinedDictionaryName to {cbdWorking.PredefinedDictionaryName}");
                }

                // Access the board gneration settings from DataContext
                DataContextLocal dcl = (DataContextLocal)DataContext;

                // Did the default for Board Size X change?
                if (SettingsManagerLocal.DefaultBoard_SizeX != dcl.BoardSizeX)
                {
                    SettingsManagerLocal.DefaultBoard_SizeX = dcl.BoardSizeX;
                    Debug.WriteLine($"Updated default Board SizeX to {dcl.BoardSizeX}");
                }

                // Did the default for Board Size Y change?
                if (SettingsManagerLocal.DefaultBoard_SizeY != dcl.BoardSizeY)
                {
                    SettingsManagerLocal.DefaultBoard_SizeY = dcl.BoardSizeY;
                    Debug.WriteLine($"Updated default Board SizeY to {dcl.BoardSizeY}");
                }

                // Did the default for Print DPI change?
                if (SettingsManagerLocal.DefaultBoardDPI != dcl.PrintDPI)
                {
                    SettingsManagerLocal.DefaultBoardDPI = dcl.PrintDPI;
                    Debug.WriteLine($"Updated default Board DPI to {dcl.PrintDPI}");
                }
            }
        }

        /// <summary>
        /// Generates a PDF containing a single page of size boardWidth x boardHeight (in meters)
        /// and draws a ChArUco board centered on that page. The printed square size will match
        /// charucoBoardDefinition.SquareLength.
        /// </summary>
        /// <param name="fileSpec">Full path to the PDF to create.</param>
        /// <param name="charucoBoardDefinition">Definition with SquaresX/Y, SquareLength, MarkerLength.</param>
        /// <param name="boardWidthMeters">Full board width in meters (e.g., 0.6 for 600 mm).</param>
        /// <param name="boardHeightMeters">Full board height in meters (e.g., 0.4 for 400 mm).</param>
        /// <param name="dpi">Target print DPI (e.g., 1200).</param>
        public static void GeneratePDFBoard(string fileSpec,
                                            CharucoBoardDefinition charucoBoardDefinition,
                                            double boardWidthMeters,
                                            double boardHeightMeters,
                                            int dpi)
        {
            if (dpi <= 0)
                throw new ArgumentOutOfRangeException(nameof(dpi), "DPI must be positive.");
            if (boardWidthMeters <= 0 || boardHeightMeters <= 0)
                throw new ArgumentOutOfRangeException("Board dimensions must be positive (in meters).");

            const double metersPerInch = 0.0254;

            // --- 1. Read inputs and compute physical pattern size (in meters) ---
            int squaresX = Math.Max(1, charucoBoardDefinition.SquaresX);
            int squaresY = Math.Max(1, charucoBoardDefinition.SquaresY);
            double squareLengthMeters = Math.Max(0.0001, charucoBoardDefinition.SquareLength);
            double markerLengthMeters = Math.Max(0.0001, charucoBoardDefinition.MarkerLength);

            double patternWidthMeters = squaresX * squareLengthMeters;
            double patternHeightMeters = squaresY * squareLengthMeters;

            // --- 2. Convert physical pattern size -> pixels (true resolution) ---
            double patternWidthInches = patternWidthMeters / metersPerInch;
            double patternHeightInches = patternHeightMeters / metersPerInch;

            int patternWidthPx = Math.Max(1, (int)Math.Round(patternWidthInches * dpi));
            int patternHeightPx = Math.Max(1, (int)Math.Round(patternHeightInches * dpi));

            // --- 3. Generate ChArUco board image with Emgu (in pixels) ---
            var dictionary = new Dictionary(PredefinedDictionaryName.Dict5X5_100);
            using var board = new CharucoBoard(
                squaresX,
                squaresY,
                (float)squareLengthMeters,
                (float)markerLengthMeters,
                dictionary);

            using var boardMat = new Mat();

            // marginSize = 0 here because we want the image itself to be pattern-only.
            board.GenerateImage(
                new System.Drawing.Size(patternWidthPx, patternHeightPx),
                boardMat,
                marginSize: 0,
                borderBits: 1);

            // --- 4. Encode Mat -> PNG bytes (no System.Drawing.Bitmap) ---
            using var buf = new VectorOfByte();
            CvInvoke.Imencode(".png", boardMat, buf);
            byte[] pngBytes = buf.ToArray();

            // --- 5. Set up PDF page size (board size in points) ---
            double pageWidthInches = boardWidthMeters / metersPerInch;
            double pageHeightInches = boardHeightMeters / metersPerInch;

            float pageWidthPoints = (float)(pageWidthInches * 72.0);
            float pageHeightPoints = (float)(pageHeightInches * 72.0);

            var pageSize = new PageSize(pageWidthPoints, pageHeightPoints);

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(fileSpec))!);

            using var writer = new PdfWriter(fileSpec);
            using var pdf = new PdfDocument(writer);
            pdf.SetDefaultPageSize(pageSize);

            using var doc = new Document(pdf, pageSize);
            doc.SetMargins(0, 0, 0, 0);

            // --- 6. Create iText image from PNG ---
            var imageData = ImageDataFactory.Create(pngBytes);
            var img = new iText.Layout.Element.Image(imageData);

            // Physical size of pattern on the page (in points), preserving 40 mm (or whatever) squares.
            double patternWidthPoints = patternWidthInches * 72.0;
            double patternHeightPoints = patternHeightInches * 72.0;

            float patternWidthPointsF = (float)patternWidthPoints;
            float patternHeightPointsF = (float)patternHeightPoints;

            // Scale the bitmap to match the intended physical ChArUco pattern size
            img.ScaleAbsolute(patternWidthPointsF, patternHeightPointsF);

            // --- 7. Center the ChArUco pattern on the board/page ---
            float offsetX = (pageWidthPoints - patternWidthPointsF) / 2f;
            float offsetY = (pageHeightPoints - patternHeightPointsF) / 2f;

            img.SetFixedPosition(offsetX, offsetY);

            doc.Add(img);
            doc.Close();
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
