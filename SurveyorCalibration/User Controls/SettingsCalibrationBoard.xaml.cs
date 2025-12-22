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
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using static Emgu.CV.Aruco.Dictionary;
using Surveyor.Helper;


namespace Surveyor.User_Controls
{

    public sealed partial class SettingsCalibrationBoard : UserControl
    {
        // Class-level variables
        private CalibrationBoardDefinition? charucoBoardDefinition = null;  // Passed in 
        public CalibrationBoardDefinition? CbdWorking { get; set; }                     // Working on
        public double BoardSizeX { get; set; }
        public double BoardSizeY { get; set; }
        public int PrintDPI { get; set; }
        private bool _loaded = false;
        private readonly DispatcherTimer _previewTimer = new();
        private bool _previewPending = false;


        // Dependency Property to set the 'InstanceType' .xaml attribute (BoardSetup/DefaultsManager)
        public static readonly DependencyProperty InstanceTypeProperty =
        DependencyProperty.Register(nameof(InstanceType), typeof(string), typeof(SettingsCalibrationBoard),
            new PropertyMetadata("BoardSetup", OnInstanceTypeChanged));  // This is the default 'InstanceType'
        public string InstanceType
        {
            get => (string)GetValue(InstanceTypeProperty);
            set => SetValue(InstanceTypeProperty, value);
        }

        private static void OnInstanceTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SettingsCalibrationBoard ctrl)
                ctrl.ApplyMode((string)e.NewValue);
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
                SaveToPDFButton.IsEnabled = false;
            }
            else if (IsDefaultsManagerMode)
            {
                // Show board dimension, dpi and Save to PDF button
                RootGrid.RowDefinitions[6].Height = new GridLength(1, GridUnitType.Star);
                RootGrid.RowDefinitions[7].Height = new GridLength(1, GridUnitType.Star);
                RootGrid.RowDefinitions[8].Height = new GridLength(1, GridUnitType.Star);
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

            // Configure de-bounce timer
            _previewTimer.Interval = TimeSpan.FromMilliseconds(400);
            _previewTimer.Tick += PreviewTimer_Tick;

            // ApplyMode is automatically called if the InstanceType is set
            // to 'DefaultsManager' but not is it is left as default 'BoardSetup'.
            // ApplyMode is called explicitly here to ensure the initial mode is applied.
            ApplyMode(InstanceType); // ensure initial mode applied
        }


        /// <summary>
        /// Must be called to setup the control for use in the Project Settings window.
        /// </summary>
        /// <param name="project"></param>
        public void SetupForProjectSettingWindow(CalibProject? project)
        {
            // Remember what was passed in
            charucoBoardDefinition = project?.Data.ChArUcoBoardDefinition;
            
            // Check if are just managing defaults
            if (IsDefaultsManagerMode)
            {
                // Set default values for later use
                if (!Enum.TryParse<PredefinedDictionaryName>(SettingsManagerLocal.DefaultChArUcoBoard_PredefinedDictionaryName,
                                                             ignoreCase: true,
                                                             out PredefinedDictionaryName predefinedDictionaryName))
                {
                    predefinedDictionaryName = PredefinedDictionaryName.Dict5X5_100; // double default shouldn't be necessary
                }

                CbdWorking = new CalibrationBoardDefinition
                {
                    SquaresX = SettingsManagerLocal.DefaultChArUcoBoard_SquaresX,
                    SquaresY = SettingsManagerLocal.DefaultChArUcoBoard_SquaresY,
                    SquareLength = (float)SettingsManagerLocal.DefaultChArUcoBoard_SquareLength,
                    MarkerLength = (float)SettingsManagerLocal.DefaultChArUcoBoard_MarkerLength,
                    PredefinedDictionaryName = predefinedDictionaryName
                };

                // Setup the Generate Board fields
                BoardSizeX = SettingsManagerLocal.DefaultBoard_SizeX;
                BoardSizeY = SettingsManagerLocal.DefaultBoard_SizeY;
                PrintDPI = SettingsManagerLocal.DefaultBoard_DPI;
            }
            else
            {
                // Setup an actual board this project
                CbdWorking = charucoBoardDefinition;
                if (CbdWorking is null)
                    return;

                // Check if the defaults are required (i.e. first time setup)
                if (CbdWorking.SquaresX == 0)
                {
                    CbdWorking.SquaresX = SettingsManagerLocal.DefaultChArUcoBoard_SquaresX;
                    Debug.WriteLine($"Set CharucoBoard SquaresX to default {CbdWorking.SquaresX}");
                }
                if (CbdWorking.SquaresY == 0)
                {
                    CbdWorking.SquaresY = SettingsManagerLocal.DefaultChArUcoBoard_SquaresY;
                    Debug.WriteLine($"Set CharucoBoard SquaresY to default {CbdWorking.SquaresY}");
                }
                if (CbdWorking.SquareLength == 0)
                {
                    CbdWorking.SquareLength = (float)SettingsManagerLocal.DefaultChArUcoBoard_SquareLength;
                    Debug.WriteLine($"Set CharucoBoard SquareLength to default {CbdWorking.SquareLength}");
                }
                if (CbdWorking.MarkerLength == 0)
                {
                    CbdWorking.MarkerLength = (float)SettingsManagerLocal.DefaultChArUcoBoard_MarkerLength;
                    Debug.WriteLine($"Set CharucoBoard MarkerLength to default {CbdWorking.MarkerLength}");
                }
                if (CbdWorking.PredefinedDictionaryName == 0)
                {
                    if (!Enum.TryParse<PredefinedDictionaryName>(SettingsManagerLocal.DefaultChArUcoBoard_PredefinedDictionaryName,
                                                                 ignoreCase: true,
                                                                 out PredefinedDictionaryName predefinedDictionaryNameDefault))
                    {
                        predefinedDictionaryNameDefault = PredefinedDictionaryName.Dict5X5_100;  // double default shouldn't be necessary
                    }
                    CbdWorking.PredefinedDictionaryName = predefinedDictionaryNameDefault;
                    Debug.WriteLine($"Set CharucoBoard PredefinedDictionaryName to default {CbdWorking.PredefinedDictionaryName}");
                }
            }

            UpdateButtons();
            _ = BoardInputChangedAsync();
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
                    sender.Text = sender.Text[..(firstDotIndex + 1)] + sender.Text[(firstDotIndex + 1)..].Replace(".", "");
                    int decimalCount = sender.Text.Length - firstDotIndex - 1;
                    if (decimalCount > 2)
                        sender.Text = sender.Text[..(firstDotIndex + 3)];
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

        /// <summary>
        /// Number of squares, square size or marker size has changed
        /// Used to update the preview image
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NumericTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // This is to fix the SquareLength and MarkerLength not being updated in the CbdWorking
            // I think it is because of the converter or the TextChanging event
            Bindings.Update();

            SchedulePreviewUpdate();
        }

        /// <summary>
        /// ArUco dictionary changed
        /// Used to update the preview image
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ArucoDictionaryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbdWorking != null && ArucoDictionaryCombo.SelectedItem is PredefinedDictionaryName dict)
            {
                CbdWorking.PredefinedDictionaryName = dict;
                SchedulePreviewUpdate();
            }
        }

        /// <summary>
        /// Board generation text box changed.  Check button status and save defaults
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GenerationTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateButtons();

            // Set any changed defaults if necessary
            if (IsDefaultsManagerMode)
            {          
                // Did the default for Board Size X change?
                if (SettingsManagerLocal.DefaultBoard_SizeX != BoardSizeX)
                {
                    SettingsManagerLocal.DefaultBoard_SizeX = BoardSizeX;
                    Debug.WriteLine($"Updated default Generate Board SizeX to {(BoardSizeX * 1000):F1}mm");
                }

                // Did the default for Board Size Y change?
                if (SettingsManagerLocal.DefaultBoard_SizeY != BoardSizeY)
                {
                    SettingsManagerLocal.DefaultBoard_SizeY = BoardSizeY;
                    Debug.WriteLine($"Updated default Generate Board SizeY to {(BoardSizeY * 1000):F1}mm");
                }

                // Did the default for Print DPI change?
                if (SettingsManagerLocal.DefaultBoard_DPI != PrintDPI)
                {
                    SettingsManagerLocal.DefaultBoard_DPI = PrintDPI;
                    Debug.WriteLine($"Updated default Generate Board DPI to {PrintDPI}");
                }
            }
        }



        /// <summary>
        /// User control loaded.  
        /// Populate ArUco dictionary combo box and setup initial preview.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            _loaded = true;

            try
            {
                // Setup the board types but we only support ChAruco here
                TargetTypeCombo.ItemsSource = Enum.GetValues(typeof(CalibrationBoardDefinition.TargetType));
                TargetTypeCombo.SelectedItem = CalibrationBoardDefinition.TargetType.ChArUco;
                TargetTypeCombo.IsEnabled = false; // fixed to ChAruco for now

                // Setup ArUco Combo values
                ArucoDictionaryCombo.ItemsSource = Enum.GetValues(typeof(PredefinedDictionaryName));

                // Set the current selected item
                ArucoDictionaryCombo.SelectedItem = CbdWorking?.PredefinedDictionaryName;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }

            try
            {
                // Remove the AprilTags (used for AprilGrid, not ChAruco)
                ArucoDictionaryCombo.Items.Remove(PredefinedDictionaryName.DictAprilTag16h5.ToString());
                ArucoDictionaryCombo.Items.Remove(PredefinedDictionaryName.DictAprilTag25h9.ToString());
                ArucoDictionaryCombo.Items.Remove(PredefinedDictionaryName.DictAprilTag36h10.ToString());
                ArucoDictionaryCombo.Items.Remove(PredefinedDictionaryName.DictAprilTag36h11.ToString());
            }
            catch { /*Just eat any errors */ }

            _ = BoardInputChangedAsync();
        }


        /// <summary>
        /// Generate a print shop quality PDF of the current ChAruco board definition.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SaveToPDF_Click(object sender, RoutedEventArgs e) => _ = SaveToPDFAsync();
        private async Task SaveToPDFAsync() 
        {
            if (CbdWorking is null)
                return;

            // Use the hosting SettingsWindow handle, not App.MainWindow
            var hostingWindow = WindowHelper.GetWindowForElement(this); // assuming helper exists

            if (hostingWindow is null)
                return;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(hostingWindow);

            // Show file save picker
            var savePicker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
            };
            savePicker.FileTypeChoices.Add("PDF Document", [".pdf"]);
            savePicker.SuggestedFileName = $"ChArUco Target {CbdWorking.SquaresX}x{CbdWorking.SquaresY} {CbdWorking.PredefinedDictionaryName} Square={(CbdWorking.SquareLength * 1000):F2}mm Marker={(CbdWorking.MarkerLength * 1000):F2}mm.pdf";

            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
            var fileTask = savePicker.PickSaveFileAsync().AsTask();
            await fileTask;
            var file = await fileTask;

            // Restore focus explicitly (defensive)
            hostingWindow?.Activate();

            if (file is null)
                return; // User canceled

            string fileSpec = file.Path;

            // Board Caption
            string caption =
                $"Camera Calibration  {CbdWorking.Description}";

            // Generate the PDF            
            await GeneratePDFBoardAsync(fileSpec,
                                        CbdWorking,
                                        BoardSizeX, BoardSizeY,
                                        PrintDPI,
                                        caption);
        }


        /// <summary>
        /// Update the board preview if necessary
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PreviewTimer_Tick(object? sender, object e) => _ = PreviewTimerAsync();
        private async Task PreviewTimerAsync()
        {
            _previewTimer.Stop();
            if (_previewPending)
            {
                _previewPending = false;
                await BoardInputChangedAsync();
                Debug.WriteLine("Preview Image Updated");
            }
        }


        /// <summary>
        /// Schedules a preview update after a short delay to de-bounce rapid input changes.
        /// </summary>
        private void SchedulePreviewUpdate()
        {
            if (!_loaded || CbdWorking == null) return;
            _previewPending = true;
            _previewTimer.Stop();
            _previewTimer.Start();
        }


        /// <summary>
        /// Update button states based on current inputs
        /// </summary>
        private void UpdateButtons() 
        {
            bool saveToPDFEnabled = BoardSizeX > 0 &&
                                    BoardSizeY > 0 &&
                                    PrintDPI > 0;

            SaveToPDFButton.IsEnabled = saveToPDFEnabled;
        }


        /// <summary>
        /// Generates and displays a ChAruco board preview image based on the current settings.
        /// </summary>
        /// If the IsDefaultsManagerMode is true then we are in a mode where we are setting up
        /// the default board for new projects.  In which we set the defaults to local storage.
        /// </summary>
        /// <returns></returns>
        private async Task BoardInputChangedAsync()
        {
            if (!_loaded || CbdWorking is null) return;

            try
            {
                // 1. Get / clamp inputs
                int squaresX = Math.Max(1, CbdWorking.SquaresX);
                int squaresY = Math.Max(1, CbdWorking.SquaresY);
                float squareLength = Math.Max(0.001f, CbdWorking.SquareLength);
                float markerLength = Math.Max(0.001f, CbdWorking.MarkerLength);

                // 2. Create dictionary & board
                var dictionary = new Dictionary(CbdWorking.PredefinedDictionaryName);
                using var board = new CharucoBoard(squaresX, squaresY, squareLength, markerLength, dictionary);

                // 3. Choose a render size in *pixels*
                //    This is just for on-screen preview, so pick something sensible.
                const int pixelsPerSquare = 80; // adjust as you like
                int imgWidth = squaresX * pixelsPerSquare;
                int imgHeight = squaresY * pixelsPerSquare;

                using var boardMat = new Mat();

                // 4. Render the ChArUco board into the Mat
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
                PreviewCaption1.Text = $"{CbdWorking.Description}";
                PreviewCaption2.Text = $"This should look exactly the same as your physical board i.e. same number of squares and same markers in the same positions.";
            }
            catch (Exception ex)
            {
                PreviewCaption1.Text = $"Preview failed {ex.Message}";
                PreviewCaption2.Text = $"";
            }

            // Set any changed defaults if necessary
            if (IsDefaultsManagerMode)
            {
                // Did the default for SquaresX change?
                if (SettingsManagerLocal.DefaultChArUcoBoard_SquaresX != CbdWorking.SquaresX)
                {
                    SettingsManagerLocal.DefaultChArUcoBoard_SquaresX = CbdWorking.SquaresX;
                    Debug.WriteLine($"Updated default CharucoBoard SquaresX to {CbdWorking.SquaresX}");
                }

                // Did the default for SquaresY change?
                if (SettingsManagerLocal.DefaultChArUcoBoard_SquaresY != CbdWorking.SquaresY)
                {
                    SettingsManagerLocal.DefaultChArUcoBoard_SquaresY = CbdWorking.SquaresY;
                    Debug.WriteLine($"Updated default CharucoBoard SquaresY to {CbdWorking.SquaresY}");
                }

                // Did the default for SquareLength change?
                if (SettingsManagerLocal.DefaultChArUcoBoard_SquareLength != CbdWorking.SquareLength)
                {
                    SettingsManagerLocal.DefaultChArUcoBoard_SquareLength = CbdWorking.SquareLength;
                    Debug.WriteLine($"Updated default CharucoBoard SquareLength to {(CbdWorking.SquareLength * 1000):F2}mm");
                }

                // Did the default for MarkerLength change?
                if (SettingsManagerLocal.DefaultChArUcoBoard_MarkerLength != CbdWorking.MarkerLength)
                {
                    SettingsManagerLocal.DefaultChArUcoBoard_MarkerLength = CbdWorking.MarkerLength;
                    Debug.WriteLine($"Updated default CharucoBoard MarkerLength to {(CbdWorking.MarkerLength * 1000):F2}mm");
                }

                // Did the default for PredefinedDictionaryName change?
                if (SettingsManagerLocal.DefaultChArUcoBoard_PredefinedDictionaryName != CbdWorking.PredefinedDictionaryName.ToString())
                {
                    SettingsManagerLocal.DefaultChArUcoBoard_PredefinedDictionaryName = CbdWorking.PredefinedDictionaryName.ToString();
                    Debug.WriteLine($"Updated default CharucoBoard PredefinedDictionaryName to {CbdWorking.PredefinedDictionaryName}");
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
        /// <param name="caption"></param>
        private async Task GeneratePDFBoardAsync(string fileSpec,
                                                 CalibrationBoardDefinition charucoBoardDefinition,
                                                 double boardWidthMeters,
                                                 double boardHeightMeters,
                                                 int dpi,
                                                 string caption)
        {
            if (dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));
            if (boardWidthMeters <= 0 || boardHeightMeters <= 0)
                throw new ArgumentOutOfRangeException("Board dimensions must be positive.");

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

            // --- 3. Generate ChArUco board image with EMGU.CV (in pixels) ---
            var dictionary = new Dictionary(charucoBoardDefinition.PredefinedDictionaryName);
            using var board = new CharucoBoard(squaresX,
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

            // Caption layout parameters
            const float captionFontSize = 10f;
            const float captionPaddingPoints = 6f; // space above/below text band
            float captionHeight = captionFontSize + captionPaddingPoints; // reserved band at bottom
            // If no room for caption (board exactly fills page), we still draw it overlapping the bottom.
            bool haveCaptionRoom = (pageHeightPoints - patternHeightMeters * 72.0) >= captionHeight;

            try
            {
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
                float patternWidthPointsF = (float)(patternWidthInches * 72.0);
                float patternHeightPointsF = (float)(patternHeightInches * 72.0);

                // Scale the bitmap to match the intended physical ChArUco pattern size
                img.ScaleAbsolute(patternWidthPointsF, patternHeightPointsF);

                // Compute vertical position:
                // Reserve caption band at bottom if space allows; center pattern above that band.
                float availableForPattern = haveCaptionRoom
                    ? pageHeightPoints - captionHeight
                    : pageHeightPoints; // fallback: no reservation, overlap may occur

                float patternOffsetY = haveCaptionRoom
                    ? ((availableForPattern - patternHeightPointsF) / 2f) + captionHeight
                    : (pageHeightPoints - patternHeightPointsF) / 2f;


                // --- 7. Center the ChArUco pattern on the board/page ---
                float offsetX = (pageWidthPoints - patternWidthPointsF) / 2f;

                img.SetFixedPosition(offsetX, patternOffsetY);

                doc.Add(img);

                // Add caption centered at bottom
                if (!string.IsNullOrWhiteSpace(caption))
                {
                    // Draw at vertical center of caption band
                    float captionY = captionFontSize / 2f + (captionPaddingPoints / 2f);
                    // Ensure it stays inside page
                    if (!haveCaptionRoom)
                        captionY = captionFontSize / 2f + 2f; // minimal offset

                    var p = new Paragraph(caption)
                        .SetFontSize(captionFontSize)
                        .SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER);

                    // Use absolute positioning                   
                    doc.ShowTextAligned(p,
                                        pageWidthPoints / 2f,
                                        captionY,
                                        pdf.GetNumberOfPages(),
                                        iText.Layout.Properties.TextAlignment.CENTER,
                                        iText.Layout.Properties.VerticalAlignment.MIDDLE,
                                        0);
                }

                doc.Close();
            }
            catch (Exception ex)
            {
                // Warn the user that the PDF generation fails and report the ex.Message
                ContentDialog dialog = new()
                {
                    Title = "PDF Generation Failed",
                    Content = ex.Message,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();

                Debug.WriteLine($"GeneratePDFBoard: PDF generation failed: {ex.Message}");
            }
        }
    }
}
