// Surveyor MagnifyAndMarkerDisplay
// Used to overlay an existing Image control with a Magnify Window and allow the user to
// set target markers. The Magnify Window is used to display a magnified view of
// the image at the pointer (mouse) location. The user can lock the Magnify Window
// to a location and then set target markers on the image. The control also displays
// Events (e.g. prior measurements) on a Canvas overlaying the Image. The control can
// also be instructed to display a Epipolar lines
//
// These things need to be wired up:
//  MagnifyAndMarkerDisplay.InitializeMediator(...)
//  MagnifyAndMarkerDisplay.Setup(...)
//  MagnifyAndMarkerDisplay.OtherInstanceTargetSet(...)
//  MagnifyAndMarkerDisplay.MagWindowZoomFactor(...)
//  MagnifyAndMarkerDisplay.MagWindowEnlargeSize()
//  MagnifyAndMarkerDisplay.AutoMagnify(...)
//  MagnifyAndMarkerDisplay.SetTargets(...)
//  MagnifyAndMarkerDisplay.SetEvents(...)
//  MagnifyAndMarkerDisplay.SetLayerType(...)
//  MagnifyAndMarkerDisplay.GetLayerType()
//  MagnifyAndMarkerDisplay.Close()
//
//public override void Receive(TListener listenerFrom, object? message)
//{
//    if (message is MagnifyAndMarkerControlEventData data)
//    {
//        switch (data.magnifyAndMarkerControlEvent)
//        {
//            case MagnifyAndMarkerControlEvent.TargetPointSelected:
//                break;
//            case MagnifyAndMarkerControlEvent.SurveyMeasurementPairSelected:
//                break;
//            case MagnifyAndMarkerControlEvent.SurveyPointSelected:
//                break;
//            case MagnifyAndMarkerControlEvent.EditSpeciesInfoRequest:
//                break;
//        }
//    }
//}
//
// Version 1.0
// Version 1.1
// NewImageFrame receives the next frame via a mediator message
// The canvasBitmap is written to an memory stream and portions read out and transformed for the Magnify Window
// Version 1.2 20 May 2025
// Move the MagnifyAndMarkerDisplay to be a child of the MediaPlayer control


using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Surveyor.DesktopWap.Helper;
using Surveyor.Events;
using Surveyor.Helper;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;               // Point class
using Windows.Graphics.Imaging;         // BitmapTransform
using Windows.Storage.Streams;
using static Surveyor.User_Controls.SettingsWindowEventData;



namespace Surveyor.User_Controls
{
    public sealed partial class MagnifyAndMarkerDisplay : UserControl, IAsyncDisposable
    {
        private bool _disposed = false;

        // Copy of MainWindow
        private MainWindow? _mainWindow = null;
        private bool mainWindowActivated = false;

        // Copy of the mediator 
        private SurveyorMediator? _mediator;

        // Declare the mediator handler
        private MagnifyAndMarkerControlHandler? magnifyAndMarkerControlHandler;

        // Reporter
        private Reporter? report = null;

        // Which camera side 
        private SurveyorMediaPlayer.eCameraSide CameraSide = SurveyorMediaPlayer.eCameraSide.None;

        // The Image UIElement control we are serving
        private Image? imageUIElement = null;   // This is a reference to the control we are pigbacking on
        private bool isImageAtFullResolution = false;
        private bool imageLoaded = false;

        // Converted BitmapImage from the Image control
        private IRandomAccessStream? streamSource = null;
        private uint imageSourceWidth = 0;
        private uint imageSourceHeight = 0;

        // Image poistion (which frame in the video as a TimeSpace)
        private TimeSpan? position = null;

        // The scale factor between the actual image in the Image frame and the size of the
        // ImageFrame in the screen. i.e. the actual iamge size could be 3840x2160 and say the 
        // ImageFrame is 1600x900 then the scale factor X would be 1600/3840 = 0.4167
        private double canvasFrameScaleX = -1;
        private double canvasFrameScaleY = -1;
        private double labelScaleFactor = 1;

        // The scaling of the image in the ImageMag where 1 is scales to full image source size 
        private double canvasZoomFactor = 2;    // Must be set to the same initial value as 'canvasZoomFactor' in MediaControl.xaml.cs

        // Indicates if the pointer (mouse) is on this user control of not
        private bool isPointerOnUs = false;

        // Set to true if the Magnifier Window is locked (i.e. the user has clicked the mouse and the Mag Window
        // no longer follows the pointer (mouse))
        private bool isMagLocked = false;
        private Point magLockedCentre = new(0, 0);

        // Dragging support
        private bool isDragging = false;
        private Point draggingInitialPoint;
        private DateTime draggingInitialPressTime;

        // Default Magnifier Window dimensions
        private const uint magWidthDefaultSmall = 350 * 3;
        private const uint magHeightDefaultSmall = 150 * 3;
        private const uint magWidthDefaultMedium = 500 * 3;
        private const uint magHeightDefaultMedium = 250 * 3;
        private const uint magWidthDefaultLarge = 700;
        private const uint magHeightDefaultLarge = 350;

        private uint magWidth = magWidthDefaultLarge;
        private uint magHeight = magHeightDefaultLarge;

        // Marker icons 
        private readonly ImageBrush iconTargetLockA = new();
        private readonly ImageBrush iconTargetLockAMove = new();
        private readonly ImageBrush iconTargetLockB = new();
        private readonly ImageBrush iconTargetLockBMove = new();
        private Point targetIconOffsetToCentre = new();
        private Point targetMagIconOffsetToCentre = new();
        private readonly double targetIconOriginalWidth = -1;
        private readonly double targetIconOriginalHeight = -1;
        private bool? hoveringOverTargetTrueAFalseB = null;
        private Point? hoveringOverTargetPoint = null;

        // Border colours
        private readonly Brush magColourUnlocked = new SolidColorBrush(Microsoft.UI.Colors.Black);
        private readonly Brush magColourLocked = new SolidColorBrush(Microsoft.UI.Colors.Orange);

        // Event graphic colours
        private readonly Brush eventDimensionLineColour = new SolidColorBrush(Microsoft.UI.Colors.Orange);
        private readonly Brush eventDimensionHighLightLineColour = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
        private readonly Brush eventArrowLineColour = new SolidColorBrush(Microsoft.UI.Colors.Orange);
        private readonly Brush eventDimensionTextColour = new SolidColorBrush(ColorHelper.FromArgb(255/*alpha*/, 255/*red*/, 93/*green*/, 89/*blue*/));
        private const double eventFontSize = 14.0;

        // Epipolar line colours
        private readonly Brush epipolarALineColour = new SolidColorBrush(Microsoft.UI.Colors.Red);
        //???private readonly Brush epipolarBLineColour = new SolidColorBrush(Microsoft.UI.Colors.Green);
        private readonly Brush epipolarBLineColour = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 228, 30));


        // Selected targets set by this UserControl instance
        private Point? pointTargetA = null;
        private Point? pointTargetB = null;

        // If targets have been selected on the other UserControl instance
        private bool otherInstanceTargetASet = false;
        private bool otherInstanceTargetBSet = false;

        // Events (existing measurements etc)
        private ObservableCollection<Event>? events = null;

        // Selected Target
        private Rectangle? targetSelected = null;
        private bool? targetSelectedTrueAFalseB = null;
        private Rect rectMagWindowScreen;
        private Rect rectMagWindowSource;
        private Rect rectMagPointerBounds;

        // Target icon types
        private enum TargetIconType
        {
            None,
            Locked,
            Moved
        };

        // Target icon move directions
        private enum TargetIconMove
        {
            None,
            Left,
            Right,
            Up,
            Down
        };

        // Layer types
        [Flags]
        public enum LayerType
        {
            None = 0,
            Events = 1,
            EventsDetail = 2,
            Epipolar = 4,
            All = Events | EventsDetail | Epipolar
        };
        private LayerType layerTypesDisplayed = LayerType.All;

        // Context Menu status        
        private bool canvasMagContextMenuOpen = false;

        // Display Pointer Coords
        public bool DisplayPointerCoords { get; set; } = false;

        // Timer
        private bool isTimerProcessing = false; // Set to true when the timer is processing
        private const int timerInterval = 500; // 1/2 seconds
        private DispatcherTimer? timer = null;

        // Pointer tracking in the Mag Window (time last seen)
        private DateTime lastTimePointerSeenInMagWindow = DateTime.Now;
        private double inactivityMagWindowClose = 2000; // 2 seconds

        // Cached diagnostic information flag
        private bool diagnosticInformation = false;



        public MagnifyAndMarkerDisplay()
        {
            this.InitializeComponent();

            // Add listener for theme changes
            var rootElement = (FrameworkElement)Content;
            rootElement.ActualThemeChanged += OnActualThemeChanged;


            // Load the locked and move markers icon
            iconTargetLockA.ImageSource = new BitmapImage(new Uri("ms-appx:///Assets/targetLockA_Set2.png", UriKind.Absolute));
            iconTargetLockAMove.ImageSource = new BitmapImage(new Uri("ms-appx:///Assets/targetLockA_move_Set2.png", UriKind.Absolute));
            iconTargetLockB.ImageSource = new BitmapImage(new Uri("ms-appx:///Assets/targetLockB_Set2.png", UriKind.Absolute));
            iconTargetLockBMove.ImageSource = new BitmapImage(new Uri("ms-appx:///Assets/targetLockB_move_Set2.png", UriKind.Absolute));            

            // Using the IconTargetA Rectangle and assuming all the target Rectangles
            // on both the CanvasMag and the CanvasFrame are setup to have the same
            // dimensions then calculate the offset to the centre of the icon and save of later use
            targetIconOriginalWidth = TargetA.Width;
            targetIconOriginalHeight = TargetA.Height;
            
            // Start the three second utility timer
            SetupTimer();
        }


        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;

            // Stop the timer and wait for any in-progress tick
            if (timer is not null)
            {
                timer.Stop();

                int attempts = 0;
                while (isTimerProcessing && attempts < 10)
                {
                    await Task.Delay(100); // Yield to allow timer to finish
                    attempts++;
                }

                timer = null;
            }

            // Remove listener for theme changes
            var rootElement = (FrameworkElement)Content;
            rootElement.ActualThemeChanged -= OnActualThemeChanged;

            // Dispose image stream
            streamSource?.Dispose();
            streamSource = null;

            // Unsubscribe from event handlers
            if (events is not null)
            {
                events.CollectionChanged -= OnEventsCollectionChanged;
                events = null;
            }

            if (_mainWindow is not null)
            {
                _mainWindow.Activated -= MainWindow_Activated;
                _mainWindow = null;
            }

            // Clean up mediator handler
            (magnifyAndMarkerControlHandler as IAsyncDisposable)?.DisposeAsync();
            magnifyAndMarkerControlHandler = null;

            _disposed = true;
        }



        /// <summary>
        /// Diags dump of class information
        /// </summary>
        public void DumpAllProperties()
        {
            DumpClassPropertiesHelper.DumpAllProperties(this, report);
        }


        /// <summary>
        /// Initialize mediator handler for SurveyorMediaControl
        /// </summary>
        /// <param name="mediator"></param>
        /// <returns></returns>
        public TListener InitializeMediator(SurveyorMediator __mediator, MainWindow __mainWindow)
        {
            _mediator = __mediator;
            _mainWindow = __mainWindow;

            magnifyAndMarkerControlHandler = new MagnifyAndMarkerControlHandler(_mediator, this, _mainWindow);

            return magnifyAndMarkerControlHandler;
        }


        /// <summary>
        /// Called initial to setup the Image control we are serving and the camera side
        /// This function must be called for the user control to work
        /// The imageFrame is only used so we know where to poisition the magnifier window. The
        /// souece of the magnified image comes from the NewImageFrame() function
        /// </summary>
        /// <param name="imageFrame"></param>
        /// <param name="cameraside"></param>
        public void Setup(Reporter _report, Image imageFrame, SurveyorMediaPlayer.eCameraSide cameraside)
        {
            // Remember the Report
            report = _report;

            // Remember the Image control we are serving
            imageUIElement = imageFrame;
            CameraSide = cameraside;


            // Add handler against the Main Window for Activated and Deavtivated events
            // this is used to hide the Mag Window if the Main Window is deactivated
            // _mainWindow is set in InitializeMediator so made sure that is called first
            Debug.Assert(_mainWindow is not null, "MagnifyAndMarkerControl.InitializeMediator(...) must be called before MagnifyAndMarkerControl.Setup(...)");
            _mainWindow.Activated += MainWindow_Activated;
        }


        /// <summary>
        /// Called after the ImageFrame has cleared
        /// </summary>
        public void Close()
        {
            // Clear position
            position = null;

            _ResetCanvas();

            // Clear values
            ClearEventsAndEpipolar();

            // Assume the image in the ImageFrame is no longer loaded
            imageLoaded = false;

            // Clear in memory image stream
            streamSource?.Dispose();

            // Clear events
            events = null;
        }


        /// <summary>
        /// Set the theme of the application
        /// </summary>
        /// <param name="theme">Dark or Light</param>
        public void SetTheme(ElementTheme theme)
        {

            var rootElement = (FrameworkElement)(Content);

            if (theme == ElementTheme.Dark)
            {
                // Set the RequestedTheme of the root element to Dark
                rootElement.RequestedTheme = ElementTheme.Dark;

                // Use a dark theme icon
                SpeciesEditIcon.UriSource = new Uri($"ms-appx:///Assets/Fish-Dark.png");
            }
            else if (theme == ElementTheme.Light)
            {
                // Set the RequestedTheme of the root element to Light
                rootElement.RequestedTheme = ElementTheme.Light;

                // Use a light theme icon
                SpeciesEditIcon.UriSource = new Uri($"ms-appx:///Assets/Fish-Light.png");
            }
            else
            {
                // Use the default system theme
                rootElement.RequestedTheme = ElementTheme.Default;

                // Get the background colour used by that theme
                if (_mainWindow is not null)
                {
                    var color = TitleBarHelper.ApplySystemThemeToCaptionButtons(_mainWindow) == Colors.White ? "Dark" : "Light";

                    // Based on the background colour select a suitable application icon 
                    if (color == "Dark")
                        SpeciesEditIcon.UriSource = new Uri($"ms-appx:///Assets/Fish-Dark.png");
                    else
                        SpeciesEditIcon.UriSource = new Uri($"ms-appx:///Assets/Fish-Light.png");
                }
            }

            // If the theme has changed, announce the change to the user
            UIHelper.AnnounceActionForAccessibility(rootElement, "Theme changed", "ThemeChangedNotificationActivityId");
        }


        /// <summary>
        /// Set any existing targets. This function must be called after NewIamgeFrame()
        /// </summary>
        /// <param name="existingTargetA"></param>
        /// <param name="existingTargetB"></param>
        public void SetTargets(Point? existingTargetA, Point? existingTargetB)
        {
            pointTargetA = existingTargetA;
            pointTargetB = existingTargetB;

            if (imageLoaded)
            {
                // Transfer any existing Target A & B to the CanvasFrame
                TransferTargetsBetweenVariableAndCanvasFrame(true/*TrueAOnlyFalseBOnly*/, true/*TrueToCanvasFalseFromCanvas*/);
                TransferTargetsBetweenVariableAndCanvasFrame(false/*TrueAOnlyFalseBOnly*/, true/*TrueToCanvasFalseFromCanvas*/);
            }
        }


        /// <summary>
        /// Set any existing events. This function must be called after NewIamgeFrame()
        /// null can passed to indicate no existing Events
        /// </summary>
        /// <param name="existingEvents"></param>
        public void SetEvents(ObservableCollection<Event>? existingEvents)
        {
            if (events is not null)
                // Remove any existing event handlers
                events.CollectionChanged -= OnEventsCollectionChanged;

            if (existingEvents is not null)
            {
                events = existingEvents;
                events.CollectionChanged += OnEventsCollectionChanged;
            }
            else
                events = null;

            if (imageLoaded)
            {
                // Transfer any existing Events
                TransferExistingEvents();
            }
        }



        /// <summary>
        /// Set the zoom factor of the Mag window where 1 is the full resolution of the image
        /// </summary>
        /// <param name="zoomFactor"></param>
        public void MagWindowZoomFactor(double zoomFactor)
        {
            canvasZoomFactor = zoomFactor;
        }


        /// <summary>
        /// Used to turn on or off the indicate layer type for display
        /// </summary>
        /// <param name="layeTypeBit"></param>
        /// <param name="TrueOnFalseOff"></param>
        /// <returns>The current set of layer types being displayer</returns>
        public LayerType SetLayerType(LayerType layeTypeBit, bool TrueOnFalseOff)
        {
            LayerType layerType = GetLayerType();

            if (TrueOnFalseOff)
                layerType |= layeTypeBit;
            else
                layerType &= ~layeTypeBit;

            SetLayerType(layerType);

            return layerType;
        }


        /// <summary>
        /// Used to set the layer type for display (i.e set absolutely the layer)
        /// </summary>
        /// <param name="layeType"></param>
        public void SetLayerType(LayerType layeTyper)
        {
            layerTypesDisplayed = layeTyper;

            // Refresh on screen display
            TransferExistingEvents();

            return;
        }


        /// <summary>
        /// Return the current set of layer types being displayed
        /// </summary>
        /// <returns>LayerType</returns>
        public LayerType GetLayerType()
        {
            return layerTypesDisplayed;
        }


        /// <summary>
        /// Called by the stereo controller to inform this instance of what targets are set on the other instance
        /// </summary>
        /// <param name="targetASet"></param>
        /// <param name="targetBSet"></param>
        public void OtherInstanceTargetSet(bool? targetASet, bool? targetBSet)
        {
            if (targetASet is not null)
                otherInstanceTargetASet = (bool)targetASet;

            if (targetBSet is not null)
                otherInstanceTargetBSet = (bool)targetBSet;
        }


        /// <summary>
        /// User requested a change to the size of the mag window
        /// </summary>
        /// <param name="magWindowSize"></param>
        public void MagWindowSizeSelect(string magWindowSize)
        {
            if (magWindowSize == "Small")
            {
                magWidth = magWidthDefaultSmall;
                magHeight = magHeightDefaultSmall;
            }
            else if (magWindowSize == "Medium")
            {
                magWidth = magWidthDefaultMedium;
                magHeight = magHeightDefaultMedium;
            }
            else if (magWindowSize == "Large")
            {
                magWidth = magWidthDefaultLarge;
                magHeight = magHeightDefaultLarge;
            }
            else
                throw new Exception("MagWindowSizeSelect: Unknown mag window size");
        }



        /// <summary>
        /// ViewPort size changed
        /// </summary>
        /// <param name="newWidth"></param>
        /// <param name="newHeight"></param>
        public void RenderedPixelScreenSizeChanged(double newWidth, double newHeight)
        {
            if (!double.IsNaN(CanvasFrame.ActualWidth))
            {
                labelScaleFactor = CanvasFrame.ActualWidth / newWidth;
            }
            else
            {
                labelScaleFactor = 1;
            }

            if (imageLoaded && newWidth != 0)
                GridSizeChanged();
        }


        ///
        /// EVENTS METHODS
        ///


        /// <summary>
        /// Called when the grid is resized.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (imageLoaded)
                GridSizeChanged();
        }

        
        /// <summary>
        /// Pointer moved on the canvas
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CanvasFrame_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (mainWindowActivated && imageLoaded)
            {
                //???All done in Timer_Tick()
                //// Check if the Mag Window is currently locked
                //if (isMagLocked)
                //{
                //    // Unlock the Mag Window as long is there isn't any unsaved work
                //    MagUnlock();
                //}

                //// Check we are not in Mag Window lock mode still
                //if (!isMagLocked)
                //{
                //    // If auto magnify isn't on then ensure the Mag Window in empty
                //    MagHide();
                //}

                // Remove any existing Event line highlights
                RemoveAnyLineHightLights();

                // If required display the pointer coordinates
                if (DisplayPointerCoords)
                {
                    // Get the pointer position relative to the canvas
                    var position = e.GetCurrentPoint(CanvasFrame).Position;

                    // Update the TextBlock to show the pointer's X and Y coordinates
                    // Change to display in the title bar area
                    _mainWindow?.DisplayPointerCoordinates(position.X, position.Y);
                }
            }
        }


        /// <summary>
        /// Pointer pressed event on the CanvasFrame control (sits on top of the Image control)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CanvasFrame_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (mainWindowActivated && imageLoaded)
            {

                // Get the pointer point relative to the sender (Image control)
                PointerPoint pointerPoint = e.GetCurrentPoint(CanvasFrame);

                // Check if the event was a mouse event
                if (/*pointerPoint.PointerDeviceType == PointerDeviceType.Mouse &&*/
                    pointerPoint.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
                {
                    if (sender is Line || sender is TextBlock)
                    {
                        // Ignore if we are clicking a link or textbox
                        // These are handled by the Line and TextBlock PointerPressed events
                    }
                    else
                    {
                        if (!isMagLocked)
                            MagLockInCurrentPoisition(pointerPoint.Position, pointerPoint.PointerDeviceType);
                    }
                }
                // Display the context menu if the right pointer button is pressed 
                else if (pointerPoint.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
                {
                    // Show the canvas context menu                  
                    DisplayCanvasContextMenu(sender, e);
                }
            }
        }




        /// <summary>
        /// Pointer (mouse) moved event on the CanvasMag control (sit on top of the ImageMag control)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CanvasMag_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (mainWindowActivated)
            {
                // Update the last seen time
                lastTimePointerSeenInMagWindow = DateTime.Now;

                // Get the pointer point relative to the sender (Image control)
                PointerPoint pointerRelativeToCanvasFrame = e.GetCurrentPoint(CanvasFrame);
                
                // In a PointerMove event we use pointerRelativeToImageFrame.Properties.IsLeftButtonPressed
                // to detect if the left mouse button is pressed. 
                if (pointerRelativeToCanvasFrame.Properties.IsLeftButtonPressed &&
                    (Math.Abs(pointerRelativeToCanvasFrame.Position.X - draggingInitialPoint.X) > 10 ||
                     Math.Abs(pointerRelativeToCanvasFrame.Position.Y - draggingInitialPoint.Y) > 10))
                {
                    isDragging = true; // Movement threshold exceeded, start drag operation
                    Debug.WriteLine($"CanvasMag_PointerMoved Dragging detected at ImageFrame Screen Coords:({pointerRelativeToCanvasFrame.Position.X}, {pointerRelativeToCanvasFrame.Position.Y})");
                }

                // If the Mag Window isn't locked (i.e. between mouse clicked) that pass the position of the
                // pointer relative to the ImageFrame (not the ImageMag) to the function that extracts
                // and displays the magnified image
                if (!isMagLocked)
                {
                    MagWindow(pointerRelativeToCanvasFrame.Position);
                }
                // Detect if we are dragging one of the measurement markers
                else if (isDragging && e.OriginalSource is Rectangle rectangle)
                {
                    // Get the pointer point relative to the ImageMag control
                    PointerPoint pointerRelativeToImageMag = e.GetCurrentPoint(ImageMag);

                    // Get the pointer point relative to the CanvasMag control (includes the border)
                    PointerPoint pointerRelativeToCanvasMag = e.GetCurrentPoint(CanvasMag);

                    // Check if we are in bounds of the Mag Window
                    if (rectMagPointerBounds.Contains(pointerRelativeToImageMag.Position))
                    {
                        // Set the target icon on the canvas
                        SetTargetOnCanvasMag(rectangle, pointerRelativeToCanvasMag.Position.X, pointerRelativeToCanvasMag.Position.Y, TargetIconType.Moved);
                        Debug.WriteLine($"CanvasMag_PointerMoved Dragging target...   Image Coords:({pointerRelativeToImageMag.Position.X + rectMagWindowScreen.X:F1}, {pointerRelativeToImageMag.Position.Y + rectMagWindowScreen.Y:F1}),  ImageMag Screen Coords:({pointerRelativeToImageMag.Position.X:F1}, {pointerRelativeToImageMag.Position.Y:F1})");
                    }
                }

                // If we got this message then we are not hovering over either Canvas Frame target A or B
                if (hoveringOverTargetTrueAFalseB is not null)
                {
                    hoveringOverTargetTrueAFalseB = null;
                    Debug.WriteLine($"CanvasMag_PointerMoved: Set hoveringOverTargetTrueAFalseB = null");
                }

                // If required display the pointer coordinates
                if (DisplayPointerCoords)
                {
                    // Get the pointer position relative to the canvas
                    var position = e.GetCurrentPoint(CanvasFrame).Position;

                    // Update the TextBlock to show the pointer's X and Y coordinates
                    // Change to display in the title bar area
                    _mainWindow?.DisplayPointerCoordinates(position.X, position.Y);
                }

                // Stop this event bubbling up to CanvasFrame
                e.Handled = true;
            }
        }


        /// <summary>
        /// Pointer (mouse) button pressed event on the CanvasMag control (sit on top of the ImageMag control)
        /// </summary>
        /// <param name="e"></param>
        private void CanvasMag_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (mainWindowActivated)
            {
                draggingInitialPressTime = DateTime.MinValue;

                // Set the focus to the close button so the keyboard input is routed to the Mag Window
                // This is needed because the Image and Canvas control doesn't support KeyDown events
                // in WinUI3
                ButtonMagClose.Focus(FocusState.Programmatic);

                // Get the pointer point relative to the sender (Image control)
                PointerPoint pointerPoint = e.GetCurrentPoint(CanvasFrame);

                // Check if the event was a mouse event
                if (/*pointerPoint.PointerDeviceType == PointerDeviceType.Mouse &&*/
                    pointerPoint.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
                {
                    if (!isMagLocked)
                    {
                        //???DELETE MagLockInCurrentPoisition(pointerPoint);
                    }
                    else
                    {
                        // Detect if we maybe starting to drag one of the measurement markers
                        if (e.OriginalSource is Rectangle)
                        {
                            // Potental start dragging
                            draggingInitialPoint = pointerPoint.Position;
                            draggingInitialPressTime = DateTime.Now;
                            isDragging = false;     // Set this to false and in PointerMoved event
                                                    // set to true is pointer does move
                            Debug.WriteLine($"CanvasMag_PointerMoved Start detect for dragging at ImageFrame Screen Coords:({pointerPoint.Position.X:F1}, {pointerPoint.Position.Y:F1})");
                        }
                        else
                        {
                            //// Get the pointer point relative to the ImageZoomed control
                            //???DELETEPointerPoint pointerPointMag = e.GetCurrentPoint(ImageMag);

                            //// Set the measurement markers if none or only one target already selected
                            //if (TargetAMag.Visibility == Visibility.Collapsed)
                            //{
                            //    // Set Target A
                            //    SetTargetOnCanvasMag(TargetAMag, pointerPointMag.Position.X, pointerPointMag.Position.Y, TargetIconType.Locked);
                            //    // Clear any selectec Target (if necessary)
                            //    SetSelectedTarget(null);
                            //}
                            //else if (TargetBMag.Visibility == Visibility.Collapsed)
                            //{
                            //    SetTargetOnCanvasMag(TargetBMag, pointerPointMag.Position.X, pointerPointMag.Position.Y, TargetIconType.Locked);
                            //    // Clear any selectec Target (if necessary)
                            //    SetSelectedTarget(null);
                            //}
                        }
                    }
                }
                else if (pointerPoint.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
                {
                    // Show the canvas context menu
                    canvasMagContextMenuOpen = true;
                    DisplayCanvasContextMenu(sender, e);
                }
            }
        }


        /// <summary>
        /// Pointer (mouse) button released event on the CanvasMag (sit on top of the ImageMag control)
        /// </summary>
        /// <param name="e"></param>
        private void CanvasMag_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (mainWindowActivated)
            {

                // Get the pointer point relative to the ImageMag control
                PointerPoint pointerRelativeToImageMag = e.GetCurrentPoint(ImageMag);

                // Stop dragging
                if (isDragging)
                {
                    if (e.OriginalSource is Rectangle rectangle)
                    {
                        // Check if we are in bounds of the Mag Window
                        if (rectMagPointerBounds.Contains(pointerRelativeToImageMag.Position))
                        {
                            // After dragging has stopped switch the icon back to the non-moving version
                            if (rectangle == TargetAMag)
                                SetTargetIconOnCanvas(TargetAMag, TargetIconType.Locked);
                            else if (rectangle == TargetBMag)
                                SetTargetIconOnCanvas(TargetBMag, TargetIconType.Locked);
                        }
                        else
                        {
                            // Drag/Drop is out of bounds, return target to it's original position
                            if (rectangle == TargetAMag)
                                TransferTargetsBetweenVariableAndCanvasMag(true/*TrueAOnlyFalseBOnly*/, true/*TrueToCanvasFalseFromCanvas*/);
                            else if (rectangle == TargetBMag)
                                TransferTargetsBetweenVariableAndCanvasMag(false/*TrueAOnlyFalseBOnly*/, true/*TrueToCanvasFalseFromCanvas*/);
                        }
                    }

                    isDragging = false;
                    Debug.WriteLine($"CanvasMag_PointerReleased Stop dragging at ImageMag Screen Coords:({pointerRelativeToImageMag.Position.X:F1}, {pointerRelativeToImageMag.Position.Y:F1})");
                }
                else if (pointerRelativeToImageMag.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
                {
                    if (!isMagLocked)
                    {
                        // Get the pointer point relative to the sender (Image control)
                        PointerPoint pointerPoint = e.GetCurrentPoint(CanvasFrame);

                        MagLockInCurrentPoisition(pointerPoint.Position, pointerPoint.PointerDeviceType);
                    }
                    else
                    {
                        // Check if it was a single left click (instead of dragging)
                        var duration = DateTime.Now - draggingInitialPressTime;
                        if (draggingInitialPressTime == DateTime.MinValue ||
                            duration.TotalMilliseconds < 500) // Adjust click threshold time as needed
                        {

                            if (e.OriginalSource is Canvas)
                            {
                                // Set the measurement markers if none or only one target already selected
                                if (TargetAMag.Visibility == Visibility.Collapsed)
                                {
                                    // Set Target A
                                    SetTargetOnCanvasMag(TargetAMag, pointerRelativeToImageMag.Position.X, pointerRelativeToImageMag.Position.Y, TargetIconType.Locked);
                                    // Clear any selectec Target (if necessary)
                                    SetSelectedTarget(null);
                                }
                                else if (TargetBMag.Visibility == Visibility.Collapsed)
                                {
                                    SetTargetOnCanvasMag(TargetBMag, pointerRelativeToImageMag.Position.X, pointerRelativeToImageMag.Position.Y, TargetIconType.Locked);
                                    // Clear any selectec Target (if necessary)
                                    SetSelectedTarget(null);
                                }
                            }
                            else if (e.OriginalSource is Rectangle rectangle)
                            {
                                // Check if the target icon was already selected
                                if (targetSelected == rectangle)
                                    // If the target icon was already selected then deselect it
                                    SetSelectedTarget(null);
                                else
                                    // Select the target icon
                                    SetSelectedTarget(rectangle);
                            }
                            else if (targetSelectedTrueAFalseB is not null)
                                SetSelectedTarget(null);

                            Debug.WriteLine($"CanvasMag_PointerReleased Left button click detected ImageMag Screen Coords:({pointerRelativeToImageMag.Position.X:F1}, {pointerRelativeToImageMag.Position.Y:F1})");
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Track if the pointer (mouse) is on the control. This is used to hide the Mag
        /// Window if the pointer stops being on the control. This is done via a timer.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CanvasFrame_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            isPointerOnUs = true;
        }
        private void CanvasMag_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            isPointerOnUs = true;
        }
        private void CanvasMagChild_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            isPointerOnUs = true;
        }

        private void CanvasFrame_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            isPointerOnUs = false;
        }
        private void CanvasMag_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            isPointerOnUs = false;
        }
        private void CanvasMagChild_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            isPointerOnUs = false;
        }


        /// <summary>
        /// These are the Click handlers for the Delete (selected target icon), Ok (Accept selections),
        /// Cancel used in the Mag Window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonMagDelete_Click(object sender, RoutedEventArgs e)
        {
            if (targetSelectedTrueAFalseB is not null && targetSelected is not null)
            {
                ResetTargetOnCanvasMag((Rectangle)targetSelected);
               
                targetSelected = null;
                targetSelectedTrueAFalseB = null;

                Debug.WriteLineIf(pointTargetA is not null, $"After Delete   Target A Centre ({pointTargetA!.Value.X:F1},{pointTargetA!.Value.Y:F1})");
                Debug.WriteLineIf(pointTargetB is not null, $"After Delete   Target B Centre ({pointTargetB!.Value.X:F1},{pointTargetB!.Value.Y:F1})");
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonMagOK_Click(object sender, RoutedEventArgs e)
        {
            // If both targets have been set and no target is currently selected (i.e. user
            // still working on it) then send a mediator message request to add the measurement
            if (pointTargetA is not null && pointTargetB is not null && targetSelected is null)
            {
                MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.AddMeasurementRequest, CameraSide);
                magnifyAndMarkerControlHandler?.Send(data);
            }

            MagHide();
        }


        /// <summary>
        /// Target select mode cursor key handlers to move the selected target icon
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonMagLeft_Click(object sender, RoutedEventArgs e)
        {
            if (targetSelectedTrueAFalseB is not null && targetSelected is not null)
                MoveTargetOnCanvasMag((Rectangle)targetSelected, TargetIconMove.Left);
            Debug.WriteLine("ButtonMagLeft_Click");
        }


        /// <summary>
        /// Target select mode cursor key handlers to move the selected target icon
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonMagRight_Click(object sender, RoutedEventArgs e)
        {
            if (targetSelectedTrueAFalseB is not null && targetSelected is not null)
                MoveTargetOnCanvasMag((Rectangle)targetSelected, TargetIconMove.Right);

            Debug.WriteLine("ButtonMagRight_Click");
        }


        /// <summary>
        /// Target select mode cursor key handlers to move the selected target icon
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonMagUp_Click(object sender, RoutedEventArgs e)
        {
            if (targetSelectedTrueAFalseB is not null && targetSelected is not null)
                MoveTargetOnCanvasMag((Rectangle)targetSelected, TargetIconMove.Up);

            Debug.WriteLine("ButtonMagUp_Click");
        }


        /// <summary>
        /// Target select mode cursor key handlers to move the selected target icon
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonMagDown_Click(object sender, RoutedEventArgs e)
        {
            if (targetSelectedTrueAFalseB is not null && targetSelected is not null)
                MoveTargetOnCanvasMag((Rectangle)targetSelected, TargetIconMove.Down);

            Debug.WriteLine("ButtonMagDown_Click");
        }


        /// <summary>
        /// Close(Escape) button in the Mag Window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonMagClose_Click(object sender, RoutedEventArgs e)
        {
            MagUnlock();
            Debug.WriteLine("ButtonMagClose_Click");
        }


        /// <summary>
        /// Plus button in the Mag Window to increase the size of the MagWindow
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonMagEnlarge_Click(object sender, RoutedEventArgs e)
        {
            MagWindowSizeEnlargeOrReduce(true/*TrueEnargeFalseReduce*/, true/*trueHideIfLocked*/);

            // Message MediaStereoController so the other instance can update the size of the mag window
            MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.UserReqMagWindowSizeSelect, CameraSide)
            {
                magWindowSize = MagWindowGetSizeName()
            };
            magnifyAndMarkerControlHandler?.Send(data);
        }


        /// <summary>
        /// Minus button in the Mag Window to reduce the size of the MagWindow
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonMagReduce_Click(object sender, RoutedEventArgs e)
        {
            MagWindowSizeEnlargeOrReduce(false/*TrueEnargeFalseReduce*/, true/*trueHideIfLocked*/);


            // Message MediaStereoController so the other instance can update the size of the mag window
            MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.UserReqMagWindowSizeSelect, CameraSide )
            {
                magWindowSize = MagWindowGetSizeName()
            };
            magnifyAndMarkerControlHandler?.Send(data);
        }


        /// <summary>
        /// Mag plus magnifier button in the Mag Window to zoom in
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonMagZoomIn_Click(object sender, RoutedEventArgs e)
        {
            MagWindowZoomFactorEnlargeOrReduce(true/*TrueZoomInFalseZoomOut*/);

            // Message MediaStereoController so the other instance can update the zoom factor
            MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.UserReqMagZoomSelect, CameraSide)
            {
                canvasZoomFactor = canvasZoomFactor
            };
            magnifyAndMarkerControlHandler?.Send(data);
        }


        /// <summary>
        /// Mag minus magnifier button in the Mag Window to zoom in
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonMagZoomOut_Click(object sender, RoutedEventArgs e)
        {
            MagWindowZoomFactorEnlargeOrReduce(false/*TrueZoomInFalseZoomOut*/);

            // Message MediaStereoController so the other instance can update the zoom factor
            MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.UserReqMagZoomSelect, CameraSide)
            {
                canvasZoomFactor = canvasZoomFactor
            };
            magnifyAndMarkerControlHandler?.Send(data);
        }


        /// <summary>
        /// Detect and remember if pointer is hovering over either of the CanvasFrame targets
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Target_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            hoveringOverTargetTrueAFalseB = null;

            if (sender is Rectangle rect)
            {
                if (rect == TargetA || rect == TargetAMag)
                    hoveringOverTargetTrueAFalseB = true;
                else if (rect == TargetB || rect == TargetBMag)
                    hoveringOverTargetTrueAFalseB = false;

                // Handle the event
                e.Handled = true;
            }
        }


        /// <summary>
        /// Canvas Context Menu (Add measurement, add 3D point, edit species etc etc)
        /// Use if any of the Canvas Context Menu items are selected
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CanvasFrameContextMenu_Click(object sender, RoutedEventArgs e)
        {
            MenuFlyoutItem? item = sender as MenuFlyoutItem;
            if (item is not null)
            {
                // Add Measurement
                if (item == CanvasFrameMenuAddMeasurement)
                {
                    if (pointTargetA is not null && pointTargetB is not null)
                    {
                        // Request a measurement to be added
                        MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.AddMeasurementRequest, CameraSide);
                        magnifyAndMarkerControlHandler?.Send(data);
                    }
                }

                // Add 3D Point
                else if (item == CanvasFrameMenuAdd3DPoint)
                {
                    bool? TruePointAFalsePointB = null;

                    // Figure out if the request is for Target A or Target B
                    // Target A is set and Target B is not set and also A is set on the other instance
                    if (pointTargetA is not null && pointTargetB is null && otherInstanceTargetASet)
                    {
                        TruePointAFalsePointB = true; // Target A
                    }
                    // Target B is set and Target A is not set and also B is set on the other instance
                    else if (pointTargetB is not null && pointTargetA is null && otherInstanceTargetBSet)
                    {
                        TruePointAFalsePointB = false; // Target B
                    }
                    // Hovering over Target A and their is a corresponding point on the other instance
                    else if (hoveringOverTargetTrueAFalseB == true && pointTargetA is not null && otherInstanceTargetASet)
                    {
                        TruePointAFalsePointB = true; // Target A
                    }
                    // Hovering over Target B and their is a corresponding point on the other instance
                    else if (hoveringOverTargetTrueAFalseB == false && pointTargetB is not null && otherInstanceTargetBSet)
                    {
                        TruePointAFalsePointB = false; // Target B
                    }

                    if (TruePointAFalsePointB is not null)
                    {
                        // Request a 3D Point to be added
                        MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.Add3DPointRequest, CameraSide)
                        {
                            TruePointAFalsePointB = (bool)TruePointAFalsePointB
                        };
                        magnifyAndMarkerControlHandler?.Send(data);
                    }
                }

                // Add Single Point
                else if (item == CanvasFrameMenuAddSinglePoint)
                {
                    bool? TruePointAFalsePointB = null;

                    // Figure out if the request is for Target A or Target B
                    // Target A is set and Target B is not set
                    if (pointTargetA is not null & pointTargetB is null)
                    {
                        TruePointAFalsePointB = true; // Target A
                    }
                    // Target B is set and Target A is not set
                    else if (pointTargetA is null & pointTargetB is not null)
                    {
                        TruePointAFalsePointB = false; // Target B
                    }
                    // Hovering over Target A 
                    else if (hoveringOverTargetTrueAFalseB == true && pointTargetA is not null)
                    {
                        TruePointAFalsePointB = true; // Target A
                    }
                    // Hovering over Target B 
                    else if (hoveringOverTargetTrueAFalseB == false && pointTargetB is not null)
                    {
                        TruePointAFalsePointB = false; // Target B
                    }

                    if (TruePointAFalsePointB is not null)
                    {
                        // Request a Single Point to be added
                        MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.AddSinglePointRequest, CameraSide)
                        {
                            TruePointAFalsePointB = (bool)TruePointAFalsePointB
                        };
                        magnifyAndMarkerControlHandler?.Send(data);
                    }

                }

                // Context menu - Delete the Target on the Canvas Frame we are hoving over
                else if (item == CanvasFrameMenuDeleteTarget) 
                {
                    bool? TruePointAFalsePointB = null;

                    // Figure out if the request is for Target A or Target B
                    // Target A is set and Target B is not set
                    if (pointTargetA is not null & pointTargetB is null)
                    {
                        TruePointAFalsePointB = true; // Target A
                    }
                    // Target B is set and Target A is not set
                    else if (pointTargetA is null & pointTargetB is not null)
                    {
                        TruePointAFalsePointB = false; // Target B
                    }
                    // Hovering over Target A 
                    else if (hoveringOverTargetTrueAFalseB == true && pointTargetA is not null)
                    {
                        TruePointAFalsePointB = true; // Target A
                    }
                    // Hovering over Target B 
                    else if (hoveringOverTargetTrueAFalseB == false && pointTargetB is not null)
                    {
                        TruePointAFalsePointB = false; // Target B
                    }

                    if (TruePointAFalsePointB is not null)
                    {
                        if ((bool)TruePointAFalsePointB)
                        {
                            ResetTargetOnCanvasFrame(TargetA);
                            ResetTargetOnCanvasMag(TargetAMag);
                        }
                        else
                        {
                            ResetTargetOnCanvasFrame(TargetB);
                            ResetTargetOnCanvasMag(TargetBMag);
                        }

                        hoveringOverTargetTrueAFalseB = null;
                    }
                }

                // Delete All Targets
                else if (item == CanvasFrameMenuDeleteAllTargets)
                {
                    // Reset the targets
                    ResetAllTargets();

                    // Signal to the other instance to delete all their targets
                    magnifyAndMarkerControlHandler?.Send(new MagnifyAndMarkerControlEventData(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.TargetDeleteAll, CameraSide));
                }

                // Delete Measurement
                // Delete 3D Point
                // Delete Single Point
                else if (item == CanvasFrameMenuDeleteMeasurement || item == CanvasFrameMenuDelete3DPoint || item == CanvasFrameMenuDeleteSinglePoint) 
                {
                    if (hoveringOverGuid is not null)
                    { 
                        // Signal delete Measurement, 3D point or Single Point
                        magnifyAndMarkerControlHandler?.Send(new MagnifyAndMarkerControlEventData(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.DeleteMeasure3DPointOrSinglePoint, CameraSide)
                        {
                            eventGuid = hoveringOverGuid
                        });
                    }
                }

                // Edit Species Info
                else if (item == CanvasFrameMenuEditSpeciesInfo) 
                {
                    if (hoveringOverGuid is not null)
                    {
                        // Edit species info
                        MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.EditSpeciesInfoRequest, CameraSide)
                        {
                            eventGuid = hoveringOverGuid
                        };
                        magnifyAndMarkerControlHandler?.Send(data);
                    }
                }
            }
        }


        /// <summary>
        /// The Canvas Context menu is closing
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void CanvasContextMenu_Closing(FlyoutBase sender, FlyoutBaseClosingEventArgs args)
        {
            //???canvasFrameContextMenuOpen = false;
            canvasMagContextMenuOpen = false;
        }


        /// <summary>
        /// Teaching tip is closing
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void EpipolarLineTeachingTip_CloseButtonClick(TeachingTip sender, object args)
        {
            SettingsManagerLocal.SetTeachingTipShown("EpipolarLineTeachingTip");
        }

        private void EpipolarPointsTeachingTip_CloseButtonClick(TeachingTip sender, object args)
        {
            SettingsManagerLocal.SetTeachingTipShown("EpipolarPointsTeachingTip");
        }


        /// <summary>
        /// Handler is called when the main window is activated
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState == Microsoft.UI.Xaml.WindowActivationState.Deactivated)
            {
                mainWindowActivated = false;
            }
            else
            {
                mainWindowActivated = true;
            }
        }


        /// <summary>
        /// Handler is called when the main window is deactivated
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_Deactivated(object sender, WindowActivatedEventArgs e)
        {
            // This method is called when the app window loses focus
            //???Debug.WriteLine("App is inactive");
            mainWindowActivated = false;
        }


        /// <summary>
        /// The Events collection has changed and this handler is to redraw the events on the canvas
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnEventsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Replace and redraw
            TransferExistingEvents();
        }


        /// <summary>
        /// Event raised when the theme is changed in Windows
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            // Handle the theme change
            var newTheme = sender.ActualTheme;

            // Optionally, apply additional changes
            SetTheme(newTheme);
        }



        ///
        /// MEDIATOR METHODS (Called by the TListener, always marked as internal)
        ///


        /// <summary>
        /// Clear the canvas of all events and targets
        /// <summary>
        internal void _ResetCanvas()
        {
            CheckIsUIThread();

            // Check the ImageFrame is setup 
            Debug.Assert(imageUIElement is not null, "MagnifyAndMarkerControl.Setup(...) must be called before calling the methods");

            // Reset the Mag Window
            isMagLocked = false;

            // Lines below can cause a GP
            try
            {
                if (ImageMag is not null)
                    ImageMag.Source = null;
                if (BorderMag is not null)
                    BorderMag.BorderBrush = magColourUnlocked;
            }
            catch
            { }


            // Check if mag buttons need to be enabled/disabled
            EnableButtonMag();

            // Reset dragging
            isDragging = false;

            // Reset existing targets
            pointTargetA = null;
            pointTargetB = null;
            ResetTargetIconOnCanvas(TargetAMag);
            ResetTargetIconOnCanvas(TargetBMag);
            ResetTargetOnCanvasFrame(TargetA);
            ResetTargetOnCanvasFrame(TargetB);

            // Cancel any selected targets
            SetSelectedTarget(null);

            // Reset the scaling
            canvasFrameScaleX = -1;
            canvasFrameScaleY = -1;

            // Remove Events and epipolar lines
            RemoveCanvasShapesByTag(CanvasFrame, "Event");
            RemoveCanvasShapesByTag(CanvasFrame, "EpipolarLine");
            RemoveCanvasShapesByTag(CanvasFrame, "EpipolarPoints");
            RemoveCanvasShapesByTag(CanvasMag, "EpipolarLine");
            RemoveCanvasShapesByTag(CanvasMag, "EpipolarPoints");
        }


        /// <summary>        
        /// This function resets all value associated with the previous frame and 
        /// setups up for the new frame.  It is called from the mediator message
        /// Other frame setup functions like SetTargets and SetEvents must be called
        /// AFTER NewImageFrame is called
        /// </summary>       
        internal void _NewImageFrame(IRandomAccessStream? frameStream, TimeSpan _position, uint _imageSourceWidth, uint _imageSourceHeight)
        {
            CheckIsUIThread();

            // Check the ImageFrame is setup 
            Debug.Assert(imageUIElement is not null, $"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: MagnifyAndMarkerControl.Setup(...) must be called before calling the methods");

            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: _NewImageFrame: Position:{_position}, Width:{_imageSourceWidth}, Height:{_imageSourceHeight}");

            // Remember the frame position
            // Used to know what Events are applicable to this frame
            position = _position;
            streamSource = frameStream;
            imageSourceWidth = _imageSourceWidth;
            imageSourceHeight = _imageSourceHeight;

            // Clear the canvas
            _ResetCanvas();

            // Do coordinate need to be displayed
            DisplayPointerCoords = true;   // Force on always not part of SettingsManagerLocal.DiagnosticInformation

            // Set the image loaded flag
            imageLoaded = true;

            // Calulate the scale factor between the actual image and the screen image            
            GridSizeChanged();
        }



        /// <summary>
        /// Check if this MagnifyAndMarkerDisplay control should process the message
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        internal bool _ProcessIfForMe(SurveyorMediaPlayer.eCameraSide _cameraSide)
        {
            if (CameraSide == SurveyorMediaPlayer.eCameraSide.Left && _cameraSide == SurveyorMediaPlayer.eCameraSide.Left)
                return true;
            else if (CameraSide == SurveyorMediaPlayer.eCameraSide.Right && _cameraSide == SurveyorMediaPlayer.eCameraSide.Right)
                return true;
            else
                return false;
        }


        /// <summary>
        /// Check if this MagnifyAndMarkerDisplay control should process the message
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        internal bool _ProcessIfForMe(SurveyorMediaControl.eControlType ControlType)
        {
            if (CameraSide == SurveyorMediaPlayer.eCameraSide.Left && ControlType == SurveyorMediaControl.eControlType.Primary)
                return true;
            else if (CameraSide == SurveyorMediaPlayer.eCameraSide.Right && ControlType == SurveyorMediaControl.eControlType.Secondary)
                return true;
            else if (ControlType == SurveyorMediaControl.eControlType.Both)
                return true;
            else
                return false;
        }



        /// <summary>
        /// The user changes the Diagnostic Information setting
        /// </summary>
        /// <param name="diagnosticInformation"></param>
        internal void _SetDiagnosticInformation(bool _diagnosticInformation)
        {        
            diagnosticInformation = _diagnosticInformation;
        }



        ///
        /// PRIVATE METHODS
        ///


        /// <summary>
        /// Called to displau the Canvas Context Mneu and enable/disable each menu item
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DisplayCanvasContextMenu(object sender, PointerRoutedEventArgs e)
        {
            // Get a list of events than for for the current frame
            // If there is only one event we know all context menu options relate to that event
            // If there are multiple events then we need to check which event the user is hovering over
            List<Event> eventsForThisFrame = GetEventsForThisFrame();

            if (diagnosticInformation)
            {
                // Target points in this instance
                string testPointTargetA = "(null,null)";
                string testPointTargetB = "(null,null)";
                if (pointTargetA is not null) testPointTargetA = $"({pointTargetA.Value.X:F1},{pointTargetA.Value.Y:F1})";
                if (pointTargetB is not null) testPointTargetB = $"({pointTargetB.Value.X:F1},{pointTargetB.Value.Y:F1})";
                report?.Info(CameraSide.ToString(), $"DisplayCanvasContextMenu: PointA:{testPointTargetA}, PointB:{testPointTargetB}");
                // Hovering info
                string texthoveringOverTargetTrueAFalseB = hoveringOverTargetTrueAFalseB?.ToString() ?? "null";
                string textMeasurementEnd = hoveringOverMeasurementEnd?.ToString() ?? "null";                
                string textMeasurementLine = hoveringOverMeasurementLine?.ToString() ?? "null";
                string textDetails = hoveringOverDetails?.ToString() ?? "null";
                string textPoint = hoveringOverPoint?.ToString() ?? "null";
                report?.Info(CameraSide.ToString(), $"DisplayCanvasContextMenu: HoveringOver:  TargetTrueAFalseB={texthoveringOverTargetTrueAFalseB},  MeasurementEnd={textMeasurementEnd}, MeasurementLine={textMeasurementLine}, Details={textDetails}, Point={textPoint}");
                // Other instance info
                report?.Info(CameraSide.ToString(), $"DisplayCanvasContextMenu: OtherInstance: TargetASet={otherInstanceTargetASet},  TargetBSet={otherInstanceTargetBSet}");
                // Events info
                report?.Info(CameraSide.ToString(), $"DisplayCanvasContextMenu: Count event for this frame={eventsForThisFrame.Count}");
                int countMeasurements = eventsForThisFrame.Count(e => e.EventDataType == SurveyDataType.SurveyMeasurementPoints);
                int count3DPoints = eventsForThisFrame.Count(e => e.EventDataType == SurveyDataType.SurveyStereoPoint);
                int countSinglePoints = eventsForThisFrame.Count(e => e.EventDataType == SurveyDataType.SurveyPoint);
                report?.Info(CameraSide.ToString(), $"DisplayCanvasContextMenu: Counts: Measurements={countMeasurements}, 3DPoints={count3DPoints}, SinglePoints={countSinglePoints}");
            }

            // Find the context menu in the resources
            if (this.Resources.TryGetValue("CanvasContextMenu", out object obj) && obj is MenuFlyout menuFlyout)
            {
                // Add Measurement Point
                // Requirement: There is a pair of targets set on this instance and the other instance
                if (pointTargetA is not null && pointTargetB is not null && otherInstanceTargetASet && otherInstanceTargetBSet)
                {
                    CanvasFrameMenuAddMeasurement.IsEnabled = true;
                }
                else
                {
                    CanvasFrameMenuAddMeasurement.IsEnabled = false;
                }

                // Add 3D Point
                // Requirement: There is at least one target set on this instance and the other instance
                //              If their is two target points set on this instance we must be hovering over
                //              one of them (so we know which one to use)
                // Simple case one target set on this instance and the corresponding one on other instance
                if (pointTargetA is not null && pointTargetB is null && otherInstanceTargetASet)
                {
                    CanvasFrameMenuAdd3DPoint.IsEnabled = true;
                }
                else if (pointTargetB is not null && pointTargetA is null && otherInstanceTargetBSet)
                {
                    CanvasFrameMenuAdd3DPoint.IsEnabled = true;
                }
                // Hovering over Target A and their is a corresponding point on the other instance
                else if (hoveringOverTargetTrueAFalseB == true && pointTargetA is not null && otherInstanceTargetASet)
                {
                    CanvasFrameMenuAdd3DPoint.IsEnabled = true;
                }
                // Hovering over Target B and their is a corresponding point on the other instance
                else if (hoveringOverTargetTrueAFalseB == false && pointTargetB is not null && otherInstanceTargetBSet)
                {
                    CanvasFrameMenuAdd3DPoint.IsEnabled = true;
                }
                else
                {
                    CanvasFrameMenuAdd3DPoint.IsEnabled = false;
                }

                // Add Single Point
                // Requirement: There is at least one target set on this instance 
                // Simple case one target set on this instance 
                if (pointTargetA is not null & pointTargetB is null)
                {
                    CanvasFrameMenuAddSinglePoint.IsEnabled = true;
                }
                else if (pointTargetA is null & pointTargetB is not null)
                {
                    CanvasFrameMenuAddSinglePoint.IsEnabled = true;
                }
                // Hovering over Target A 
                else if (hoveringOverTargetTrueAFalseB == true && pointTargetA is not null)
                {
                    CanvasFrameMenuAddSinglePoint.IsEnabled = true;
                }
                // Hovering over Target B 
                else if (hoveringOverTargetTrueAFalseB == false && pointTargetB is not null)
                {
                    CanvasFrameMenuAddSinglePoint.IsEnabled = true;
                }
                else
                {
                    CanvasFrameMenuAddSinglePoint.IsEnabled = false;
                }

                // Delete Target
                // Requirement: There is at least one target set on this instance
                // Simple case one target set on this instance
                if (pointTargetA is not null && pointTargetB is null)
                {
                    CanvasFrameMenuDeleteTarget.IsEnabled = true;
                }
                else if (pointTargetA is null && pointTargetB is not null)
                {
                    CanvasFrameMenuDeleteTarget.IsEnabled = true;
                }
                // Hovering over Target A 
                else if (hoveringOverTargetTrueAFalseB == true && pointTargetA is not null)
                {
                    CanvasFrameMenuDeleteTarget.IsEnabled = true;
                }
                // Hovering over Target B 
                else if (hoveringOverTargetTrueAFalseB == false && pointTargetB is not null)
                {
                    CanvasFrameMenuDeleteTarget.IsEnabled = true;
                }
                else
                {
                    CanvasFrameMenuDeleteTarget.IsEnabled = false;
                }

                // Delete All Targets
                // Requirement: There is at least one target set on this instance or the other instance
                if (pointTargetA is not null || pointTargetB is not null || otherInstanceTargetASet || otherInstanceTargetBSet)
                {
                    CanvasFrameMenuDeleteAllTargets.IsEnabled = true;
                }
                else
                {
                    CanvasFrameMenuDeleteAllTargets.IsEnabled = false;
                }

                // Delete Measurement
                // Reqirement: There is at least one measurement event set on this instance
                // Check events list for an existing measurement event of type Meassurment
                int countMeasurements = eventsForThisFrame.Count(e => e.EventDataType == SurveyDataType.SurveyMeasurementPoints);

                if (countMeasurements > 0)
                {
                    CanvasFrameMenuDeleteMeasurement.IsEnabled = true;
                }
                else
                {
                    CanvasFrameMenuDeleteMeasurement.IsEnabled = false;
                }

                // Delete 3D Point
                // Reqirement: There is at least one 3D Point set on this instance
                int count3DPoints = eventsForThisFrame.Count(e => e.EventDataType == SurveyDataType.SurveyStereoPoint);

                if (count3DPoints > 0)
                {
                    CanvasFrameMenuDelete3DPoint.IsEnabled = true;
                }
                else
                {
                    CanvasFrameMenuDelete3DPoint.IsEnabled = false;
                }

                // Delete Single Point
                // Requirement: There is at least one point set on this instance
                int countSinglePoints = eventsForThisFrame.Count(e => e.EventDataType == SurveyDataType.SurveyPoint);
                if (countSinglePoints > 0)
                {
                    CanvasFrameMenuDeleteSinglePoint.IsEnabled = true;
                }
                else
                {
                    CanvasFrameMenuDeleteSinglePoint.IsEnabled = false;
                }

                // Edit Species Info
                // Requirement: There is at least one Measurement, 3D Point or Single Point set on this instance
                // i.e. any Event that can have a Species Info
                if (countMeasurements > 0 || count3DPoints > 0 || countSinglePoints > 0)
                {
                    CanvasFrameMenuEditSpeciesInfo.IsEnabled = true;
                }
                else
                {
                    CanvasFrameMenuEditSpeciesInfo.IsEnabled = false;
                }


                // Show the context menu
                menuFlyout.ShowAt(sender as FrameworkElement, new FlyoutShowOptions
                {
                    Position = e.GetCurrentPoint(sender as FrameworkElement).Position,
                    Placement = FlyoutPlacementMode.RightEdgeAlignedTop
                });

                // Remember the pointer position in case a 'Add Stereo Point' or 'Add Single Point;
                if (sender is Canvas canvas)
                {
                    Point point = e.GetCurrentPoint(canvas).Position;
                    if (canvas == CanvasFrame)
                        hoveringOverTargetPoint = point;
                    else if (canvas == CanvasMag)
                        // Adjust coords because this is the Mag Window
                        hoveringOverTargetPoint = new Point(point.X + rectMagWindowSource.X, point.Y + rectMagWindowSource.Y);
                }
            }
            else
            {
                // Log or handle the case where the flyout is not found or is of incorrect type
                Debug.WriteLine("DisplayCanvasContextMenu not found or incorrect type!");
            }
        }


        /// <summary>
        /// Called from the size changed event on the Image frame control
        /// This function must be called for this user control to work
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        private void GridSizeChanged()
        {
            // Check if the Image source is being display at it's full resolutions in which
            // case no auto magnify is required
            if (imageUIElement is not null && streamSource is not null)
                isImageAtFullResolution = CheckImageResolution(imageUIElement);
            else
                isImageAtFullResolution = false;  // because we don't know

            // Adjust the CanvasFrame size and scaling
            AdjustCanvasSizeAndScaling();
            
            if (canvasFrameScaleX != -1 && canvasFrameScaleY != -1)
            {
                // Reposition any target and events on the renewly size canvas
                TransferExistingEvents();
                TransferTargetsBetweenVariableAndCanvasFrame(true/*TrueAOnlyFalseBOnly*/, true/*TrueToCanvasFalseFromCanvas*/);
                TransferTargetsBetweenVariableAndCanvasFrame(false/*TrueAOnlyFalseBOnly*/, true/*TrueToCanvasFalseFromCanvas*/);                
            }
        }

        private void AdjustCanvasSizeAndScaling()
        {
            // Calculate the X & Y scale factor between the Image control and the
            // actual image size.  Store this for translating point on the image 
            // between screen between corrdinates to image based coordinates
            if (imageUIElement is not null &&
                imageUIElement.Parent is Grid gridParentImageFrame /*????&& imageUIElement.ActualWidth != 0*/)
            {
                // Calulate the scale factor between the actual image and the screen image
                //canvasFrameScaleX = imageUIElement.ActualWidth / imageSourceWidth;
                //canvasFrameScaleY = imageUIElement.ActualHeight / imageSourceHeight;
                // NEW CODE START
                canvasFrameScaleX = 1;
                canvasFrameScaleY = 1;
                // NEW CODE END

                // Scale Canvas to the same resolution as the actual source image
                //????CanvasFrame.RenderTransform = new ScaleTransform
                //????{
                //????    ScaleX = (double)canvasFrameScaleX,
                //????    ScaleY = (double)canvasFrameScaleY,
                //????};

                //***** WE SHOULD MOVE THIS SETUP STUFF INTO A MESSAGE HANDLE FOR THE FRAMESIZE CHANGE ******

                // Save the CanvasFrame to same dimensions as the actual source image (as
                // opposed to the ImageFrame control dimensions). This keeps the calculations
                // simple
                CanvasFrame.Width = imageSourceWidth;
                CanvasFrame.Height = imageSourceHeight;


                // Discover exactly where the XAML rendering engine placed the ImageFrame (given
                // it was Stretch="Uniform") within it's parent grid
                //????var transformImageFrame = imageUIElement.TransformToVisual(gridParentImageFrame);
                //????Point relativePositionImageFrame = transformImageFrame.TransformPoint(new Point(0, 0));
                //????Debug.WriteLine($"***ImageFrame origin relative to Grid ({relativePositionImageFrame.X:F1},{relativePositionImageFrame.Y:F1})");

                // Doing a ScaleTransform on a Canvas seems to move it origin. We need it to be
                // exactly aligned with the ImageFrame.  If the ImageFrame and the Canvas are
                // within a Grid container the only way to do this is to put a margin either on the
                // left/right or the up/down side as required
                //????CanvasFrame.Margin = new Thickness(relativePositionImageFrame.X, relativePositionImageFrame.Y, relativePositionImageFrame.X, relativePositionImageFrame.Y);

                // Next scale the targets so they take up 31x31 pixels on the screen
                TargetA.Width = targetIconOriginalWidth / canvasFrameScaleX;
                TargetA.Height = targetIconOriginalHeight / canvasFrameScaleY;
                TargetB.Width = targetIconOriginalWidth / canvasFrameScaleX;
                TargetB.Height = targetIconOriginalHeight / canvasFrameScaleY;
                targetIconOffsetToCentre.X = (TargetA.Width - 1) / 2;
                targetIconOffsetToCentre.Y = (TargetA.Height - 1) / 2;

                TargetAMag.Width = targetIconOriginalWidth / canvasZoomFactor;
                TargetAMag.Height = targetIconOriginalHeight / canvasZoomFactor;
                TargetBMag.Width = targetIconOriginalWidth / canvasZoomFactor;
                TargetBMag.Height = targetIconOriginalHeight / canvasZoomFactor;
                targetMagIconOffsetToCentre.X = (TargetAMag.Width - 1) / 2;
                targetMagIconOffsetToCentre.Y = (TargetAMag.Height - 1) / 2;


                // In diags mode display a pink box around the canvas to check for alignment with the media player/ImageFrame
                if (diagnosticInformation)
                {
                    Brush colour = new SolidColorBrush(Microsoft.UI.Colors.PaleVioletRed);
                    CanvasDrawingHelper.DrawLine(CanvasFrame, new Point(0, 0), new Point(0, CanvasFrame.Height - 1), colour, new CanvasTag("", ""), null, null);
                    CanvasDrawingHelper.DrawLine(CanvasFrame, new Point(0, CanvasFrame.Height - 1), new Point(CanvasFrame.Width - 1, CanvasFrame.Height - 1), colour, new CanvasTag("", ""), null, null);
                    CanvasDrawingHelper.DrawLine(CanvasFrame, new Point(CanvasFrame.Width - 1, CanvasFrame.Height - 1), new Point(CanvasFrame.Width - 1, 0), colour, new CanvasTag("", ""), null, null);
                    CanvasDrawingHelper.DrawLine(CanvasFrame, new Point(CanvasFrame.Width - 1, 0), new Point(0, 0), colour, new CanvasTag("", ""), null, null);
                }
            }
            else
            {
                //???
                bool imageUIElementNotNull = false;
                bool imageUIElementParentIsGrid = false;
                double? imageUIElementActualWidth = null;


                if (imageUIElement is not null)
                {
                    imageUIElementNotNull = true;

                    if (imageUIElement.Parent is not null)
                    {
                        if (imageUIElement.Parent is Grid)
                        {
                            imageUIElementParentIsGrid = true;

                            imageUIElementActualWidth = imageUIElement.ActualWidth;

                        }
                    }
                }
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: AdjustCanvasSizeAndScaling: Skipped body. imageUIElement Not Null is {imageUIElementNotNull}, imageUIElementParent is Grid {imageUIElementParentIsGrid}, AcutalWidth = {imageUIElementActualWidth}");
            }

        }


        /// <summary>
        /// Called to set the correct enable/disable state of the Mag Buttons
        /// This function can be called at any time
        /// </summary>
        private void EnableButtonMag()
        {
            try
            {
                // Enable/Disable the 'Delete' MagButton
                if (targetSelected is not null)
                    ButtonMagDelete.IsEnabled = true;
                else
                    ButtonMagDelete.IsEnabled = false;

                // Enable/Disable the 'Ok'/Tick MagButton
                if (pointTargetA is not null && pointTargetB is not null && targetSelected is null)
                    ButtonMagAddMeasurement.IsEnabled = true;
                else
                    ButtonMagAddMeasurement.IsEnabled = false;

                // Enable/Display cursor buttons
                if (targetSelected is not null)
                {
                    ButtonMagLeft.IsEnabled = true;
                    ButtonMagUp.IsEnabled = true;
                    ButtonMagDown.IsEnabled = true;
                    ButtonMagRight.IsEnabled = true;
                }
                else
                {
                    ButtonMagLeft.IsEnabled = false;
                    ButtonMagUp.IsEnabled = false;
                    ButtonMagDown.IsEnabled = false;
                    ButtonMagRight.IsEnabled = false;
                }

                // Enable Mag Enlarge/Reduce Button
                ButtonMagEnlarge.IsEnabled = true;
                ButtonMagReduce.IsEnabled = true;
            }
            catch
            { 
                // Do nothing (sometimes seen at shutdown)
            }

        }


        /// <summary>
        /// Display and lock the Mag Window at its current position indicated
        /// </summary>
        /// <param name="pointerPoint"></param>
        public void MagLockInCurrentPoisition(Point pointerPosition, PointerDeviceType pointerDeviceType)
        {
            // Check if the event was a mouse event
            if (pointerDeviceType == PointerDeviceType.Mouse)
            {
                // Remove any existing target that are outside of the MagWindow
                // We are assuming the user locked the MagWindow to select targets
                // and therefore they don't want any existing targets outside of
                // newly locked MagWindow area                    
                if (pointTargetA is not null)
                {
                    // Check if Target A is in the scope of the Mag Window                        
                    if (!rectMagWindowSource.Contains((Point)pointTargetA))
                        ResetTargetOnCanvasFrame(TargetA);     // Fully removes target
                }
                if (pointTargetB is not null)
                {
                    // Check if Target B is in the scope of the Mag Window
                    if (!rectMagWindowSource.Contains((Point)pointTargetB))
                        ResetTargetOnCanvasFrame(TargetB);     // Fully removes target
                }

                // Update the MagWindow on screen
                MagWindow(pointerPosition);

                // Change the border colour to indicate it is locked
                BorderMag.BorderBrush = magColourLocked;
                isMagLocked = true;
                magLockedCentre = pointerPosition;

                // Show the Mag Window buttons 
                ButtonsMag.Visibility = Visibility.Visible;
                ButtonsMagVertical.Visibility = Visibility.Visible;
            }
        }


        /// <summary>
        /// Unlocks the Mag Window as long as there is unsaved moved target icons
        /// If there is unsaved work this method will flash the OK/Cancel icon instead
        /// </summary>
        /// <returns></returns>
        public bool MagUnlock()
        {
            bool ret = false;

            if (isMagLocked)
            {
                // Otherwise the Mag Window needs to be automatically cancelled
                // (this will set IsMagLocked = false)
                MagHide();
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Unlock Magnify Window and hide");
            }

            return ret;
        }


        private int magIntoImageSquare_EntryCounter = 0;
        /// <summary>
        /// Displays a magnified section of _imageFrame from under the pointer (mouse) position passed in.
        /// The size of the magnifed area is defined by magWidth and magHeight.
        /// The magnification factor is defined by canvasZoomFactor.
        /// </summary>
        /// <param name="pointerPosition">Pointer relative to CanvasFrame</param>
        private async void MagWindow(Point pointerPosition)
        {
            // Check the ImageFrame is setup 
            Debug.Assert(imageUIElement is not null, $"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Error MagnifyAndMarkerControl.Setup(...) must be called before calling the methods");


            // Atomic entry counter
            // Discard this message if there are more queued up
            try
            {
                int entryCounter = Interlocked.Increment(ref magIntoImageSquare_EntryCounter);
                if (entryCounter == 1)
                {

                    // Reset rectMagPointerBounds for safty, not strictly necessary
                    rectMagPointerBounds = new Rect(0, 0, 0, 0);

                    // Check if ImageFrame's source is a BitmapImage and has a valid UriSource.
                    if (imageUIElement is not null && streamSource is not null &&
                        imageUIElement.Parent is Grid gridParentImageFrame)
                    {
                        // Get the BitmapDecoder for the ImageFrame
                        try
                        {
                            streamSource.Seek(0);
                            var decoder = await BitmapDecoder.CreateAsync(streamSource);

                            // Check if the pointer if still on the Image (because the Image maybe not exactly fit the Grid Cell
                            if (pointerPosition.X >= 0 && pointerPosition.Y >= 0 &&
                                pointerPosition.X < CanvasFrame.ActualWidth &&
                                pointerPosition.Y < CanvasFrame.ActualHeight)
                            {
                                // *** TEST CODE START ***
                                {
                                    Debug.WriteLine($"MagnifyAndMarkerDisplay.MagWindow: CanvasFrame Size ({CanvasFrame.ActualWidth:F1}x{CanvasFrame.ActualHeight:F1}, ImageFrame Size:({imageUIElement.ActualWidth}x{imageUIElement.ActualHeight})");
                                    if (imageUIElement.ActualWidth < magWidth || imageUIElement.ActualHeight < magHeight)
                                    {
                                        Debug.WriteLine($"MagnifyAndMarkerDisplay.MagWindow: MagWindow too large, ImageFrame ({imageUIElement.ActualWidth:F1}x{imageUIElement.ActualHeight:F1}), MagWindow ({magWidth}x{magHeight}), first attempt to reduce window size automatically.");

                                        MagWindowSizeEnlargeOrReduce(false/*TrueEnargeFalseReduce*/, false/*trueHideIfLocked*/);

                                        if (imageUIElement.ActualWidth < magWidth || imageUIElement.ActualHeight < magHeight)
                                        {
                                            Debug.WriteLine($"MagnifyAndMarkerDisplay.MagWindow: MagWindow too large, CanvasFrame ({CanvasFrame.ActualWidth:F1}x{CanvasFrame.ActualHeight:F1}), MagWindow ({magWidth}x{magHeight}), second attempt to reduce window size automatically.");

                                            MagWindowSizeEnlargeOrReduce(false/*TrueEnargeFalseReduce*/, false/*trueHideIfLocked*/);

                                            if (imageUIElement.ActualWidth < magWidth || imageUIElement.ActualHeight < magHeight)
                                            {
                                                Debug.WriteLine($"MagnifyAndMarkerDisplay.MagWindow: MagWindow too large, CanvasFrame ({CanvasFrame.ActualWidth:F1}x{CanvasFrame.ActualHeight:F1}), MagWindow ({magWidth}x{magHeight}), can't display MagWindow, try maximising the main window.");
                                            }
                                        }
                                    }

                                    // First check that the mag windows will phyiscally fit in the size the CanvasFrame/ImageFrame has
                                    double magWindowLeftCheck = Math.Clamp(pointerPosition.X - (magWidth / 2), 0/*min*/, CanvasFrame.ActualWidth - magWidth/*max*/);
                                    double magWindowTopCheck = Math.Clamp(pointerPosition.Y - (magHeight / 2), 0/*min*/, CanvasFrame.ActualHeight - magHeight/*max*/);
                                    double magWindowWidthCheck = Math.Min(magWidth, CanvasFrame.ActualWidth - magWindowLeftCheck);
                                    double magWindowHeightCheck = Math.Min(magHeight, decoder.PixelHeight - magWindowTopCheck);

                                    if (imageUIElement.ActualHeight < magWindowHeightCheck ||
                                        imageUIElement.ActualWidth < magWindowWidthCheck)
                                    {
                                        Debug.WriteLine($"MagnifyAndMarkerDisplay.MagWindow: MagWindow too large, ImageFrame ({imageUIElement.ActualWidth}x{imageUIElement.ActualHeight}), MagWindow ({magWindowWidthCheck}x{magWindowHeightCheck})");
                                    }
                                }
                                // *** TEST CODE END ***

                                // Calculate the Mag Window screen rectangle. That is the rectangle that the Mag Window
                                // actually appears within on the CanvasFrame (excluding the border)
                                { // Putting this in braces so that variable go out of scope and not used by mistake
                                    double magWindowLeft = Math.Clamp(pointerPosition.X - (magWidth / 2), 0/*min*/, CanvasFrame.ActualWidth - magWidth/*max*/);
                                    double magWindowTop = Math.Clamp(pointerPosition.Y - (magHeight / 2), 0/*min*/, CanvasFrame.ActualHeight - magHeight/*max*/);
                                    double magWindowWidth = Math.Min(magWidth, CanvasFrame.ActualWidth - magWindowLeft);
                                    double magWindowHeight = Math.Min(magHeight, decoder.PixelHeight - magWindowTop);
                                    rectMagWindowScreen = new Rect(magWindowLeft, magWindowTop, magWindowWidth, magWindowHeight);
                                }

                                // Calcaulte the source rectangle from the ImageFrame that is used to fill
                                // the Mag Window.                         
                                { // Putting this in braces so that variable go out of scope and not used by mistake
                                    double magWidthZoomed = magWidth / canvasZoomFactor;
                                    double magHeightZoomed = magHeight / canvasZoomFactor;
                                    double magSourceLeft = Math.Clamp(pointerPosition.X - (magWidthZoomed / 2), 0/*min*/, CanvasFrame.ActualWidth - magWidthZoomed/*max*/);
                                    double magSourceTop = Math.Clamp(pointerPosition.Y - (magHeightZoomed / 2), 0/*min*/, CanvasFrame.ActualHeight - magHeightZoomed/*max*/);
                                    double magSourceWidth = Math.Min(magWidthZoomed, decoder.PixelWidth - magSourceLeft);
                                    double magSourceHeight = Math.Min(magHeightZoomed, decoder.PixelHeight - magSourceTop);

                                    rectMagWindowSource = new Rect(magSourceLeft, magSourceTop, magSourceWidth, magSourceHeight);
                                }



                                // Definate the ImageMag Rect for pointer bounds checking in 
                                // PointerMoved events
                                rectMagPointerBounds = new Rect(0.0, 0.0, rectMagWindowSource.Width, rectMagWindowSource.Height);


                                //***CHANGE_ORIGNAL START***
                                // Define the magnified portion of the image to extact from the bitmap
                                var transform = new BitmapTransform()
                                {
                                    ScaledWidth = decoder.PixelWidth,
                                    ScaledHeight = decoder.PixelHeight,
                                    Bounds = new BitmapBounds()
                                    {
                                        X = (uint)Math.Round(rectMagWindowSource.X),
                                        Y = (uint)Math.Round(rectMagWindowSource.Y),
                                        Width = (uint)Math.Round(rectMagWindowSource.Width),
                                        Height = (uint)Math.Round(rectMagWindowSource.Height)
                                    }
                                };
                                //***CHANGE_ORIGNAL END***
                                //***CHANGE1 START
                                //var boundsX = Math.Clamp((int)Math.Round(rectMagWindowSource.X), 0, (int)decoder.PixelWidth - 1);
                                //var boundsY = Math.Clamp((int)Math.Round(rectMagWindowSource.Y), 0, (int)decoder.PixelHeight - 1);
                                //var boundsWidth = Math.Clamp((int)Math.Round(rectMagWindowSource.Width), 1, (int)decoder.PixelWidth - boundsX);
                                //var boundsHeight = Math.Clamp((int)Math.Round(rectMagWindowSource.Height), 1, (int)decoder.PixelHeight - boundsY);

                                //var transform = new BitmapTransform()
                                //{
                                //    // If you zoom out too far (zoom < 1), ScaledWidth or ScaledHeight might round to zero.
                                //    // Fix: Clamp to minimum 1:
                                //    ScaledWidth = (uint)Math.Max(1, Math.Round(rectMagWindowScreen.Width)),
                                //    ScaledHeight = (uint)Math.Max(1, Math.Round(rectMagWindowScreen.Height)),

                                //    // The Bounds rectangle must be fully inside the dimensions of the original
                                //    // image(decoder.PixelWidth and decoder.PixelHeight).
                                //    Bounds = new BitmapBounds()
                                //    {
                                //        X = (uint)boundsX,
                                //        Y = (uint)boundsY,
                                //        Width = (uint)boundsWidth,
                                //        Height = (uint)boundsHeight
                                //    }
                                //};
                                //***CHANGE1 END

                                // Debug reporting
                                //???Debug.WriteLine($"Pointer ({pointerPosition.X:F1},{pointerPosition.Y:F1}) ImageFrame cx={imageUIElement.ActualWidth:F1}, cy={imageUIElement.ActualHeight:F1}, Source Image cx,cy {imageSourceWidth},{imageSourceHeight}, Mag Zoom:{canvasZoomFactor:F1}");
                                Debug.WriteLine($"Image size: {decoder.PixelWidth}x{decoder.PixelHeight}");
                                Debug.WriteLine($"Mag Window Coords left,top=({rectMagWindowScreen.X:F1},{rectMagWindowScreen.Y:F1}), cx,cy {rectMagWindowScreen.Width:F1},{rectMagWindowScreen.Height:F1}");
                                Debug.WriteLine($"Mag Window Source Coords left,top=({rectMagWindowSource.X:F1},{rectMagWindowSource.Y:F1}), cx,cy {rectMagWindowSource.Width:F1},{rectMagWindowSource.Height:F1}, Zoom={canvasZoomFactor:F2}");


                                // Get the pixel data for the zoomed region.
                                var pixelProvider = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);

                                // Create a new WriteableBitmap for ImageMag
                                WriteableBitmap magImageBitmap = new WriteableBitmap((int)Math.Round(rectMagWindowSource.Width), (int)Math.Round(rectMagWindowSource.Height));
                                pixelProvider.DetachPixelData().CopyTo(magImageBitmap.PixelBuffer);

                                //***CHANGE_ORIGNAL START***
                                // Update ImageMag's source and scaling
                                ImageMag.Source = magImageBitmap;
                                ImageMag.RenderTransform = new ScaleTransform()
                                {
                                    ScaleX = canvasZoomFactor,
                                    ScaleY = canvasZoomFactor
                                };
                                CanvasMag.RenderTransform = new ScaleTransform()
                                {
                                    ScaleX = canvasZoomFactor,
                                    ScaleY = canvasZoomFactor
                                };
                                //***CHANGE_ORIGNAL END***


                                // Discover exactly where the XAML rendering engine placed the ImageFrame (given
                                // it was Stretch="Uniform") within it's parent grid
                                var transformImageFrame = imageUIElement.TransformToVisual(gridParentImageFrame);
                                Point relativePositionImageFrame = transformImageFrame.TransformPoint(new Point(0, 0));

                                // Constrain the Mag Window with the ImageFrame space
                                double magHalfWidthScreen = rectMagWindowScreen.Width / 2;
                                double magHalfHeightScreen = rectMagWindowScreen.Height / 2;
                                double magCentreXSource = Math.Clamp(pointerPosition.X, magHalfWidthScreen / canvasFrameScaleX, decoder.PixelWidth - (magHalfWidthScreen / canvasFrameScaleX));
                                double magCentreYSource = Math.Clamp(pointerPosition.Y, magHalfHeightScreen / canvasFrameScaleY, decoder.PixelHeight - (magHalfHeightScreen / canvasFrameScaleX));

                                double magOffsetXScreen = (magCentreXSource * canvasFrameScaleX) - magHalfWidthScreen;
                                double magOffsetYScreen = (magCentreYSource * canvasFrameScaleY) - magHalfHeightScreen;


                                //???Debug.WriteLine($"{magIntoImageSquare_EntryCounter}:    MagOffset left,top=({magOffsetXScreen:F1},{magOffsetYScreen:F1})");

                                // Move the CanvasMag to the correct position vs the ImageFrame 
                                // Doing a ScaleTransform on a Canvas seems to move its origin. We need it to be
                                // exactly aligned with the ImageFrame.  If the ImageFrame and the Canvas are
                                // within a Grid container the only way to do this is to put a margin either on the
                                // left/right or the up/down side as required
                                BorderMag.Margin = new Thickness(relativePositionImageFrame.X + magOffsetXScreen - 1,
                                    relativePositionImageFrame.Y + magOffsetYScreen - 1, 0, 0);

                                BorderMag.Width = rectMagWindowScreen.Width + 2;
                                BorderMag.Height = rectMagWindowScreen.Height + 2;
                                BorderMag.Visibility = Visibility.Visible;


                                // Check if Target A is in the scope of the Mag Window                        
                                if (pointTargetA is not null)
                                {
                                    if (rectMagWindowSource.Contains((Point)pointTargetA))
                                    {
                                        // Place Target A on the Mag Window in the correct position
                                        TransferTargetsBetweenVariableAndCanvasMag(true/*TrueAOnlyFalseBOnly*/, true/*TrueToCanvasFalseFromCanvas*/);
                                        //???Debug.WriteLine($"{magIntoImageSquare_EntryCounter}:    Target A Centre ({pointTargetA.Value.X:F1},{pointTargetA.Value.Y:F1})*");
                                    }
                                    else
                                    {
                                        ResetTargetIconOnCanvas(TargetAMag);    // Just hides the MagWindow target icon
                                        //???Debug.WriteLine($"{magIntoImageSquare_EntryCounter}:    Target A Centre ({pointTargetA.Value.X:F1},{pointTargetA.Value.Y:F1})");
                                    }
                                }
                                else
                                    ResetTargetIconOnCanvas(TargetAMag);    // Just hides the MagWindow target icon

                                // Check if Target B is in the scope of the Mag Window                        
                                if (pointTargetB is not null)
                                {
                                    if (rectMagWindowSource.Contains((Point)pointTargetB))
                                    {
                                        // Place Target B on the Mag Window in the correct position                              
                                        TransferTargetsBetweenVariableAndCanvasMag(false/*TrueAOnlyFalseBOnly*/, true/*TrueToCanvasFalseFromCanvas*/);
                                        //???Debug.WriteLine($"{magIntoImageSquare_EntryCounter}:    Target B Centre ({pointTargetB.Value.X:F1},{pointTargetB.Value.Y:F1})*");
                                    }
                                    else
                                    {
                                        ResetTargetIconOnCanvas(TargetBMag);    // Just hides the MagWindow target icon
                                        //???Debug.WriteLine($"{magIntoImageSquare_EntryCounter}:    Target B Centre ({pointTargetB.Value.X:F1},{pointTargetB.Value.Y:F1})");
                                    }
                                }
                                else
                                    ResetTargetIconOnCanvas(TargetBMag);    // Just hides the MagWindow target icon

                                // Check for epipolar line for Target A
                                if (epipolarLineTargetActiveA)
                                {
                                    SetMagWindowEpipolarLine(true/*TrueEpipolarLinePointAFalseEpipolarLinePointB*/,
                                                                rectMagWindowSource,
                                                                0 /*draw line*/);
                                }
                                // Check for epipolar line for Target B
                                if (epipolarLineTargetActiveB)
                                {
                                    SetMagWindowEpipolarLine(false/*TrueEpipolarLinePointAFalseEpipolarLinePointB*/,
                                                                rectMagWindowSource,
                                                                0 /*draw line*/);
                                }
                            }
                            else
                            {
                                MagHide();
                            }
                        }
                        catch (Exception ex)
                        {
                            // Seen BitmapDecoder.CreateAsync(streamSource) cause a COM exception
                            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Error MagWindow display: {ex.Message}");
                        }
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref magIntoImageSquare_EntryCounter); // Atomic
            }

        }


        /// <summary>
        /// Hide the Mag Window and it's target rectangles
        /// </summary>
        private void MagHide()
        {
            BorderMag.BorderBrush = magColourUnlocked;
            BorderMag.Visibility = Visibility.Collapsed;
            isMagLocked = false;

            TargetAMag.Visibility = Visibility.Collapsed;
            TargetBMag.Visibility = Visibility.Collapsed;

            // Cancelled any selected
            targetSelectedTrueAFalseB = null;
            targetSelected = null;

            // Hide the Mag Window button controls
            ButtonsMag.Visibility = Visibility.Collapsed;
            ButtonsMagVertical.Visibility = Visibility.Collapsed;
        }


        /// <summary>
        /// Check if the image is being displayed at full resolution
        /// </summary>
        /// <param name="imageControl"></param>
        private bool CheckImageResolution(Image image)
        {
            bool isDisplayedAtFullResolution = false;

            if (image is not null && image.XamlRoot != null)
            {
                // Get the DPI scaling factor from the XamlRoot
                double dpiScale = image.XamlRoot.RasterizationScale;

                // Adjust the control's ActualWidth and ActualHeight based on the DPI scaling
                double adjustedWidth = image.ActualWidth * dpiScale;
                double adjustedHeight = image.ActualHeight * dpiScale;

                // Compare the adjusted dimensions with the original image dimensions
                isDisplayedAtFullResolution = (imageSourceWidth <= adjustedWidth) && (imageSourceHeight <= adjustedHeight);

                // Output or use the result
                //???Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraLeftRight}: Info CheckImageResolution: Is the image displayed at full resolution? {isDisplayedAtFullResolution}");
            }
            else
            {
                // Handle case where the image source is not a BitmapImage, is not set, or XamlRoot is null
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Error CheckImageResolution: Image source is not a BitmapImage, is not set, or XamlRoot is null.");
            }

            return isDisplayedAtFullResolution;
        }


        /// <summary>
        /// Remember that is Rectangle control has been selected by the user
        /// Pass null to reset the selected target
        /// </summary>
        /// <param name="rectangle"></param>
        private void SetSelectedTarget(Rectangle? rectangle)
        {
            if (rectangle is not null)
            {
                // Reset existing target to non-selected icon
                if (targetSelected is not null)
                    SetTargetIconOnCanvas(targetSelected, TargetIconType.Locked);

                // Remember the selected target and change to the moved icon
                targetSelected = rectangle;
                SetTargetIconOnCanvas(rectangle, TargetIconType.Moved);

                if (targetSelected == TargetAMag)
                {
                    targetSelectedTrueAFalseB = true;
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: SetSelectedTarget Selected Target A");
                }
                else if (targetSelected == TargetBMag)
                {
                    targetSelectedTrueAFalseB = false;
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: SetSelectedTarget Selected Target B");
                }
                else
                    targetSelectedTrueAFalseB = null;
            }
            else
            {
                // Reset existing target to non-selected icon
                if (targetSelected is not null)
                    SetTargetIconOnCanvas(targetSelected, TargetIconType.Locked);

                targetSelected = null;
                targetSelectedTrueAFalseB = null;
            }

            // Check if mag buttons need to be enabled/disabled
            EnableButtonMag();
        }


        /// <summary>
        /// Transfer Target A & B values to and from the CanvasFrame control
        /// </summary>
        /// <param name="TrueToCanvasFalseFromCanvas"></param>
        private void TransferTargetsBetweenVariableAndCanvasFrame(bool TrueAOnlyFalseBOnly, bool TrueToCanvasFalseFromCanvas)
        {
            if (canvasFrameScaleX != -1 && canvasFrameScaleY != -1)
            {
                if (TrueToCanvasFalseFromCanvas)
                {
                    // Transfer from Target A & B variables to the CanvasFrame
                    if (TrueAOnlyFalseBOnly)
                    {
                        if (pointTargetA is not null)
                            SetTargetIconOnCanvas(TargetA, pointTargetA!.Value.X, pointTargetA!.Value.Y, TargetIconType.Locked, true/*TrueCanvasFalseMagCanvas*/);
                        else
                            ResetTargetIconOnCanvas(TargetA);
                    }

                    if (!TrueAOnlyFalseBOnly)
                    {
                        if (pointTargetB is not null)
                            SetTargetIconOnCanvas(TargetB, pointTargetB!.Value.X, pointTargetB!.Value.Y, TargetIconType.Locked, true/*TrueCanvasFalseMagCanvas*/);
                        else
                            ResetTargetIconOnCanvas(TargetB);
                    }
                }
                else
                {
                    // Transfer from the CanvasFrame to Target A variable
                    if (TrueAOnlyFalseBOnly)
                    {
                        if (TargetA.Visibility == Visibility.Visible)
                            pointTargetA = GetTargetIconOnCanvas(TargetA, true/*TrueCanvasFalseMagCanvas*/);
                        else
                            pointTargetA = null;
                    }

                    // Transfer from the CanvasFrame to Target B variable
                    if (!TrueAOnlyFalseBOnly)
                    { 
                        if (TargetB.Visibility == Visibility.Visible)
                            pointTargetB = GetTargetIconOnCanvas(TargetBMag, true/*TrueCanvasFalseMagCanvas*/);
                        else
                            pointTargetB = null;
                    }   
                }
            }
            else
                throw new Exception($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Warning Scale factors (scaleX & scaleY) not setup");
        }




        /// <summary>
        /// This function reflects any changed positioning of the CavnasMag targets A or B into
        /// the targets on the CanvasFrame.  It also updates pointTarget A & B
        /// </summary>
        private void TransferTargetsBetweenVariableAndCanvasMag(bool TrueAOnlyFalseBOnly, bool TrueToCanvasFalseFromCanvas)
        {
            if (TrueToCanvasFalseFromCanvas)
            {
                // Transfer from Target A & B variables to the CanvasMag
                if (TrueAOnlyFalseBOnly)
                {
                    if (pointTargetA is not null)
                        SetTargetIconOnCanvas(TargetAMag,
                                                pointTargetA.Value.X - rectMagWindowSource.X,
                                                pointTargetA.Value.Y - rectMagWindowSource.Y,
                                                TargetIconType.Locked,
                                                false/*TrueCanvasFalseMagCanvas*/);
                    else
                        ResetTargetIconOnCanvas(TargetAMag);                
                }

                if (!TrueAOnlyFalseBOnly)
                {
                    if (pointTargetB is not null)
                        SetTargetIconOnCanvas(TargetBMag,
                                                pointTargetB.Value.X - rectMagWindowSource.X,
                                                pointTargetB.Value.Y - rectMagWindowSource.Y,
                                                TargetIconType.Locked,
                                                false/*TrueCanvasFalseMagCanvas*/);
                    else
                        ResetTargetIconOnCanvas(TargetBMag);
                }
            }
            else
            {
                // Transfer from the CanvasMag to Target A variable
                if (TargetAMag.Visibility == Visibility.Visible)
                {
                    Point? pointTargetRelativeToMagOrgin = GetTargetIconOnCanvas(TargetAMag, false/*TrueCanvasFalseMagCanvas*/);
                    if (pointTargetRelativeToMagOrgin is not null)
                        pointTargetA = new Point(pointTargetRelativeToMagOrgin.Value.X + rectMagWindowSource.X,
                                                    pointTargetRelativeToMagOrgin.Value.Y + rectMagWindowSource.Y);
                }
                else
                    pointTargetA = null;

                // Transfer from the CanvasMag to Target B variable
                if (TargetBMag.Visibility == Visibility.Visible)
                {
                    Point? pointTargetRelativeToMagOrgin = GetTargetIconOnCanvas(TargetBMag, false/*TrueCanvasFalseMagCanvas*/);
                    if (pointTargetRelativeToMagOrgin is not null)
                        pointTargetB = new Point(pointTargetRelativeToMagOrgin.Value.X + rectMagWindowSource.X,
                                                    pointTargetRelativeToMagOrgin.Value.Y + rectMagWindowSource.Y);
                }
                else
                    pointTargetB = null;
            }
        }



        /// <summary>
        /// Transfer Events to the canvas
        /// </summary>
        /// <param name="TrueToCanvasFalseFromCanvas"></param>
        private void TransferExistingEvents()
        {
            // Remove any existing events on the canvas
            RemoveCanvasShapesByTag(CanvasFrame, "Event");

            // Check if the events are to be displayed
            if ((layerTypesDisplayed & LayerType.Events) != 0)
            {
                List<Event> eventsForThisFrame = GetEventsForThisFrame();

                // Loop through the events and draw them on the canvas
                foreach (Event evt in eventsForThisFrame)
                {
                    // Draw the SurveyMeasurement
                    if (evt.EventData is SurveyMeasurement surveyMeasurement)
                    {
                        Point pointA;
                        Point pointB;

                        // Points definition
                        if (CameraSide == SurveyorMediaPlayer.eCameraSide.Left)
                        {
                            pointA = new(surveyMeasurement.LeftXA, surveyMeasurement.LeftYA);
                            pointB = new(surveyMeasurement.LeftXB, surveyMeasurement.LeftYB);
                        }
                        else
                        {
                            pointA = new(surveyMeasurement.RightXA, surveyMeasurement.RightYA);
                            pointB = new(surveyMeasurement.RightXB, surveyMeasurement.RightYB);
                        }


                        if (surveyMeasurement is not null && surveyMeasurement.Measurment is not null)
                            DrawEventStereoMeasurementPoints(evt.Guid, pointA, pointB, surveyMeasurement.SpeciesInfo, (double)surveyMeasurement.Measurment);
                    }
                    // Draw the SurveyStereoPoint
                    else if (evt.EventData is SurveyStereoPoint surveyStereoPoint)
                    {
                        Point point;

                        // Point definition
                        if (CameraSide == SurveyorMediaPlayer.eCameraSide.Left)
                            point = new(surveyStereoPoint.LeftX, surveyStereoPoint.LeftY);
                        else
                            point = new(surveyStereoPoint.RightX, surveyStereoPoint.RightY);


                        if (surveyStereoPoint is not null)
                            DrawEventPoint(evt.Guid, point, surveyStereoPoint.SpeciesInfo);
                    }
                    // Draw the SurveyPoint
                    else if (evt.EventData is SurveyPoint surveyPoint)
                    {
                        // Point definition are camera side specific                        
                        if ((CameraSide == SurveyorMediaPlayer.eCameraSide.Left && surveyPoint.TrueLeftfalseRight) ||
                            (CameraSide == SurveyorMediaPlayer.eCameraSide.Right && !surveyPoint.TrueLeftfalseRight))
                        {
                            Point point = new(surveyPoint.X, surveyPoint.Y);
                            DrawEventPoint(evt.Guid, point, surveyPoint.SpeciesInfo);
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Return a list of events for this frame
        /// </summary>
        /// <returns></returns>
        private List<Event> GetEventsForThisFrame()
        {
            List<Event> eventsForThisFrame = [];

            if (events is not null && events.Count > 0 && position is not null)
            {
                // Loop through the events and draw them on the canvas
                foreach (Event evt in events)
                {
                    // Is the event for this frame?
                    if ((CameraSide == SurveyorMediaPlayer.eCameraSide.Left && evt.TimeSpanLeftFrame == position) ||
                        (CameraSide == SurveyorMediaPlayer.eCameraSide.Right && evt.TimeSpanRightFrame == position))
                    {
                        eventsForThisFrame.Add(evt);
                    }
                }
            }

            return eventsForThisFrame;
        }


        /// <summary>
        /// Position the target icon on the CanvasMag using the source coordinate system. Update
        /// the target position on the CanvasFrame and the pointTargetA or pointTargetB variables.
        /// </summary>
        /// <param name="rectangle"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="targetIconMove"></param>
        private void SetTargetOnCanvasMag(Rectangle rectangle, double x, double y, TargetIconType targetIconType)
        {
            bool? TrueAOnlyFalseBOnly = null;

            if (rectangle == TargetAMag)
                TrueAOnlyFalseBOnly = true;
            else if (rectangle == TargetBMag)
                TrueAOnlyFalseBOnly = false;
            else
                Debug.Assert(false, $"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Error SetTargetOnCanvasMag: Rectangle is not a CanvasMag target icon, programming error!");

            if (TrueAOnlyFalseBOnly is not null)
            {
                // Put the correct target icon on the CanvasMag
                SetTargetIconOnCanvas(rectangle, x, y, targetIconType, false/*TrueCanvasFalseMagCanvas*/);

                // Transfer Target to the target variable and then from the variable to CanvasFrame       
                TransferTargetsBetweenVariableAndCanvasMag((bool)TrueAOnlyFalseBOnly, false /*TrueToCanvasFalseFromCanvas*/);
                TransferTargetsBetweenVariableAndCanvasFrame((bool)TrueAOnlyFalseBOnly, true /*TrueToCanvasFalseFromCanvas*/);

                // Send a mediator message to inform that a target has been set
                // Signal to the MagnifyAndMarkerControl to display the epipolar line
                MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.TargetPointSelected, CameraSide)
                {
                    TruePointAFalsePointB = TrueAOnlyFalseBOnly,
                };
                if (TrueAOnlyFalseBOnly == true)
                    data.pointA = pointTargetA;
                else
                    data.pointB = pointTargetB;
                magnifyAndMarkerControlHandler?.Send(data);

                
            }

            // Check if mag buttons need to be enabled/disabled
            EnableButtonMag();
        }


        /// <summary>
        /// Called to move (normally originated by cursor keys) the target icons in the Mag Window
        /// </summary>
        /// <param name="rectangle"></param>
        /// <param name="targetIconMove"></param>
        private void MoveTargetOnCanvasMag(Rectangle rectangle, TargetIconMove targetIconMove)
        {
            // Check we have a selected target in the Mag Window
            if (targetSelectedTrueAFalseB is not null)
            {
                Point? pointCurrent = GetTargetIconOnCanvas(rectangle, false/*TrueCanvasFalseMagCanvas*/);
                if (pointCurrent is not null)
                {
                    Point pointNew = (Point)pointCurrent;

                    switch (targetIconMove)
                    {
                        case TargetIconMove.Left:
                            pointNew.X -= 1;
                            break;
                        case TargetIconMove.Right:
                            pointNew.X += 1;
                            break;
                        case TargetIconMove.Up:
                            pointNew.Y -= 1;
                            break;
                        case TargetIconMove.Down:
                            pointNew.Y += 1;
                            break;
                    }

                    // Check of Mag Window edge reached
                    pointNew.X = Math.Clamp(pointNew.X, 0, rectMagWindowSource.Width - 1);
                    pointNew.Y = Math.Clamp(pointNew.Y, 0, rectMagWindowSource.Height - 1);


                    SetTargetOnCanvasMag(rectangle, pointNew.X, pointNew.Y, TargetIconType.Moved);
                }
            }
        }


        /// <summary>
        /// Remove the target icon from the CanvasFrame and the main variable
        /// </summary>
        /// <param name="rectangle"></param>
        private void ResetTargetOnCanvasFrame(Rectangle rectangle)
        {
            bool? TrueAOnlyFalseBOnly = null;

            if (rectangle == TargetA)
                TrueAOnlyFalseBOnly = true;
            else if (rectangle == TargetB)
                TrueAOnlyFalseBOnly = false;
            else
                Debug.Assert(false, $"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Error ResetTargetOnCanvasFrame: Rectangle is not a CanvasFrame target icon, programming error!");

            if (TrueAOnlyFalseBOnly is not null)
            {
                // Remove the target icon from the CanvasMag          
                ResetTargetIconOnCanvas(rectangle);

                if ((bool)TrueAOnlyFalseBOnly)
                    pointTargetA = null;
                else
                    pointTargetB = null;
            }            
        }


        /// <summary>
        /// Remove the target icon from the CanvasMag and update the target position on the CanvasFrame
        /// and the main variable
        /// </summary>
        /// <param name="rectangle"></param>
        private void ResetTargetOnCanvasMag(Rectangle rectangle)
        {
            bool? TrueAOnlyFalseBOnly = null;

            if (rectangle == TargetAMag)
                TrueAOnlyFalseBOnly = true;
            else if (rectangle == TargetBMag)
                TrueAOnlyFalseBOnly = false;
            else
                Debug.Assert(false, $"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Error ResetTargetOnCanvasMag: Rectangle is not a CanvasMag target icon, programming error!");

            if (TrueAOnlyFalseBOnly is not null)
            {
                // Remove the target icon from the CanvasMag          
                ResetTargetIconOnCanvas(rectangle);

                // Transfer Target to the target variable and then from the variable to CanvasFrame       
                TransferTargetsBetweenVariableAndCanvasMag((bool)TrueAOnlyFalseBOnly, false /*TrueToCanvasFalseFromCanvas*/);
                TransferTargetsBetweenVariableAndCanvasFrame((bool)TrueAOnlyFalseBOnly, true /*TrueToCanvasFalseFromCanvas*/);

                // Send a mediator message to inform that a target has been reset (use TargetPointSelected but with PointA & B set to null)
                MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.TargetPointSelected, CameraSide)
                {
                    TruePointAFalsePointB = TrueAOnlyFalseBOnly,
                    pointA = null,
                    pointB = null
                };
                magnifyAndMarkerControlHandler?.Send(data);
            }
        }


        /// <summary>
        /// Reset all the target 
        /// Called from this instance and via mediator from the sibling instnace
        /// </summary>
        internal void  ResetAllTargets()
        {
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: ResetAllTargets");

            ResetTargetOnCanvasFrame(TargetA);
            ResetTargetOnCanvasMag(TargetAMag);
            ResetTargetOnCanvasFrame(TargetB);
            ResetTargetOnCanvasMag(TargetBMag);
        }


        /// <summary>
        /// Remove all the CanvasFrame child shapes with the tag e.g. 'Event' or 'EpipolarLine'
        /// The tag format used is 'TagName':Guid
        /// e.g. Event:12345678-1234-1234-1234-1234567890AB
        /// </summary>
        /// <param name="tagRemove"></param>
        private static void RemoveCanvasShapesByTag(Canvas canvas, string tagRemove)
        {
            // Clear the canvas of all children tagged as 'Event'
            for (int i = canvas.Children.Count - 1; i >= 0; i--)
            {
                FrameworkElement? element = canvas.Children[i] as FrameworkElement;
                if (element != null && element.Tag is CanvasTag canvasTag)
                {
                    if (canvasTag.IsTagType(tagRemove))
                        canvas.Children.RemoveAt(i);
                }
            }
        }
        private static void RemoveCanvasShapesByTag(Canvas canvas, CanvasTag canvasTagRemove)
        {
            // Clear the canvas of all children tagged as 'Event'
            for (int i = canvas.Children.Count - 1; i >= 0; i--)
            {
                FrameworkElement? element = canvas.Children[i] as FrameworkElement;
                if (element != null && element.Tag is CanvasTag canvasTag)
                {
                    if (canvasTag.IsTag(canvasTagRemove))
                        canvas.Children.RemoveAt(i);
                }
            }
        }



        /// <summary>
        /// Position the target icon on the canvas using the screen coordinate system
        /// </summary>
        /// <param name="rectangle"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="TrueCanvasFalseMagCanvas">Which canvas to use</param>
        private void SetTargetIconOnCanvas(Rectangle rectangle, double x, double y, TargetIconType targetIconType, bool TrueCanvasFalseMagCanvas)
        {
            double newX;
            double newY;


            // Get the position of the pointer relative to the canvas
            if (TrueCanvasFalseMagCanvas)
            {
                newX = x - targetIconOffsetToCentre.X;
                newY = y - targetIconOffsetToCentre.Y;
            }
            else
            {
                newX = x - targetMagIconOffsetToCentre.X;
                newY = y - targetMagIconOffsetToCentre.Y;
            }

            // Update the position of the rectangle
            Canvas.SetLeft(rectangle, newX);
            Canvas.SetTop(rectangle, newY);

            SetTargetIconOnCanvas(rectangle, targetIconType);
        }


        /// <summary>
        /// Set the target rectangles icon at it current position and ensure it is visable
        /// </summary>
        /// <param name="rectangle"></param>
        /// <param name="TrueLockFalseMove"></param>
        private void SetTargetIconOnCanvas(Rectangle rectangle, TargetIconType targetIconType)
        {
            // Change to the the moving target icon
            if (rectangle == TargetA || rectangle == TargetAMag)
            {
                switch (targetIconType)
                {
                    case TargetIconType.Locked:
                        rectangle.Fill = iconTargetLockA;
                        break;
                    case TargetIconType.Moved:
                        rectangle.Fill = iconTargetLockAMove;
                        break;
                }
            }
            else if (rectangle == TargetB || rectangle == TargetBMag)
            {
                switch (targetIconType)
                {
                    case TargetIconType.Locked:
                        rectangle.Fill = iconTargetLockB;
                        break;
                    case TargetIconType.Moved:
                        rectangle.Fill = iconTargetLockBMove;
                        break;                   
                }
            }

            rectangle.Visibility = Visibility.Visible;
        }



        /// <summary>
        /// Hide the target icon
        /// </summary>
        /// <param name="rectangle"></param>
        private void ResetTargetIconOnCanvas(Rectangle rectangle)
        {
            rectangle.Visibility = Visibility.Collapsed;
        }



        /// <summary>
        /// Find any CanvasFrame child shapes the match the indicated CanvasTag
        /// </summary>
        /// <param name="canvasTagFind"></param>
        /// <returns></returns>
        public FrameworkElement? FindCanvasChildByTag(CanvasTag canvasTagFind)
        {
            FrameworkElement? ret = null;

            for (int i = CanvasFrame.Children.Count - 1; i >= 0; i--)
            {
                FrameworkElement? element = CanvasFrame.Children[i] as FrameworkElement;
                if (element != null && element.Tag is CanvasTag canvasTag)
                {
                    if (canvasTag.IsTag(canvasTagFind))
                        ret = element;
                }
            }

            return ret;
        }


        /// <summary>
        /// Return the position of the target icon on the canvas
        /// </summary>
        /// <param name="rectangle"></param>
        /// <param name="TrueCanvasFalseMagCanvas">Which canvas to use</param>
        /// <returns>Point?</returns>
        private Point? GetTargetIconOnCanvas(Rectangle rectangle, bool TrueCanvasFalseMagCanvas)
        {
            Point? ret = null;

            if (rectangle.Visibility == Visibility.Visible)
            {
                double top = Canvas.GetTop(rectangle);
                double left = Canvas.GetLeft(rectangle);

                if (TrueCanvasFalseMagCanvas)
                    // From main Canvas
                    ret = new Point(left + targetIconOffsetToCentre.X, top + targetIconOffsetToCentre.Y);
                else
                    // From Magnified Canvas
                    ret = new Point(left + targetMagIconOffsetToCentre.X, top + targetMagIconOffsetToCentre.Y);
            }

            return ret;

        }


        /// <summary>
        /// Convert from the XAML screen coordinate system to the image control source coordinates
        /// </summary>
        /// <param name="screenCoords"></param>        
        /// <returns>Point</returns>
        private Point ScreenToImageCoords(Point screenCoords)
        {
            Point ret;

            ret = new Point(screenCoords.X * (double)canvasFrameScaleX!, screenCoords.Y * (double)canvasFrameScaleY!);

            return ret;
        }


        /// <summary>
        /// Convert from the image control source corrdinate system to the XAML screen coordinates
        /// </summary>
        /// <param name="imageCoords"></param>
        /// <returns></returns>
        Point? ImageToScreenCoords(Point? imageCoords)
        {
            Point? ret = null;

            if (imageCoords.HasValue && canvasFrameScaleX != -1 && canvasFrameScaleY != -1)
            {
                ret = new Point(imageCoords.Value.X / (double)canvasFrameScaleX, imageCoords.Value.Y / (double)canvasFrameScaleY);
            }

            return ret;
        }


        /// <summary>
        /// Uses after a screen resise to reposition elements on the canvas
        /// </summary>
        /// <param name="uie"></param>
        /// <param name="scaleOldX"></param>
        /// <param name="scaleOldY"></param>
        private void RePositionUIElementAfterReSize(UIElement uie, double scaleOldX, double scaleOldY)
        {
            double left = Canvas.GetLeft(uie) * scaleOldX / (double)canvasFrameScaleX!;
            double top = Canvas.GetTop(uie) * scaleOldY / (double)canvasFrameScaleY!;

            Canvas.SetLeft(uie, left);
            Canvas.SetTop(uie, top);
        }


        /// <summary>
        /// Utility timer
        /// </summary>
        private void SetupTimer()
        {
            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(timerInterval)
            };
            timer.Tick += Timer_Tick;
            timer.Start();
        }


        /// <summary>
        /// Utility timer fired
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Timer_Tick(object? sender, object e)
        {
            if (isTimerProcessing) return;

            isTimerProcessing = true;
            try
            {
                // Calculate elapsed time since last pointer movement
                TimeSpan elapsed = DateTime.Now - lastTimePointerSeenInMagWindow;

                if (elapsed.TotalMilliseconds >= inactivityMagWindowClose) 
                {
                    if ((!isPointerOnUs && !canvasMagContextMenuOpen) || !mainWindowActivated)
                        // Unlock/Hide the Mag Window
                        MagHide();  // This function will unlock and/or hide the Mag Window
                }
            }
            finally
            {
                isTimerProcessing = false;
            }
        }


        /// <summary>
        /// Returns the size of the current mag window setting as a string
        /// </summary>
        /// <param name="magWidth"></param>
        /// <returns></returns>
        private string MagWindowGetSizeName()
        {
            string magWindowSize;
            if (magWidth == magWidthDefaultSmall)
                magWindowSize = "Small";
            else if (magWidth == magWidthDefaultMedium)
                magWindowSize = "Medium";
            else if (magWidth == magWidthDefaultLarge)
                magWindowSize = "Large";
            else
                magWindowSize = "";
            return magWindowSize;
        }


        /// <summary>
        /// Increase or decrease the size of the mag windiw
        /// </summary>
        /// <param name="TrueEnargeFalseReduce"></param>
        /// <param name="trueHideIfLocked"></param>
        private void MagWindowSizeEnlargeOrReduce(bool TrueEnargeFalseReduce, bool trueHideIfLocked)
        {
            // Get the current mag window size
            string magWindowSize = MagWindowGetSizeName();

            if (TrueEnargeFalseReduce)
            {
                if (magWindowSize == "Medium")
                    MagWindowSizeSelect("Large");
                else if (magWindowSize == "Small")
                    MagWindowSizeSelect("Medium");
            }
            else
            {
                if (magWindowSize == "Large")
                    MagWindowSizeSelect("Medium");
                else if (magWindowSize == "Medium")
                    MagWindowSizeSelect("Small");
            }

            // Next remove and re-display the mag window at the new size
            // Note this method is also called by MagWindow() method with trueHideIfLocked=false
            if (trueHideIfLocked && isMagLocked)
            {
                //??? TO DO get the original mag window centre point (if any)

                //??? TO DO Remember any selected targets (see what gets reset inside MagHide, remember and restore)

                // Hide the existing Mag Window
                MagHide();

                // Show the Mag Window as it new size
                MagLockInCurrentPoisition(magLockedCentre, PointerDeviceType.Mouse);

                //??? TO DO Restore any previously selected targets

            }
        }


        /// <summary>
        /// Used to increase of decrease the zoom factor from inside the MagWindow
        /// </summary>
        /// <param name="TrueZoomInFalseZoomOut"></param>
        private void MagWindowZoomFactorEnlargeOrReduce(bool TrueZoomInFalseZoomOut)
        {
            // Zoom levels 5,3,2,1
            // The logic in this function needs to align with the MediaControl zoom factor menu options
            // see MediaControl.xaml

            if (TrueZoomInFalseZoomOut)
            {
                if (canvasZoomFactor == 0.5)
                    MagWindowZoomFactor(1.0);
                else if (canvasZoomFactor == 1.0)
                    MagWindowZoomFactor(2.0);
                else if (canvasZoomFactor == 2.0)
                    MagWindowZoomFactor(3.0);
                else if (canvasZoomFactor == 3.0)
                    MagWindowZoomFactor(5.0);
            }
            else
            {
                if (canvasZoomFactor == 5.0)
                    MagWindowZoomFactor(3.0);
                else if (canvasZoomFactor == 3.0)
                    MagWindowZoomFactor(2.0);
                else if (canvasZoomFactor == 2.0)
                    MagWindowZoomFactor(1.0);
                // 0.5 current not supported by MagWindow()
                //else if (canvasZoomFactor == 1.0)
                //    MagWindowZoomFactor(0.5);
            }

            // Next remove and re-display the mag window at the new size
            if (isMagLocked)
            {
                //??? TO DO get the original mag window centre point (if any)

                //??? TO DO Remember any selected targets (see what gets reset inside MagHide, remember and restore)

                // Hide the existing Mag Window
                MagHide();

                // Show the Mag Window as it new size
                MagLockInCurrentPoisition(magLockedCentre, PointerDeviceType.Mouse);

                //??? TO DO Restore any previously selected targets

            }
        }


        /// <summary>
        /// Use at the top of the function if that function is intended for use use only on the 
        /// UI Thread.  This is to prevent the function being called from a non-UI thread.
        /// </summary>        
        private void CheckIsUIThread()
        {
            if (!DispatcherQueue.HasThreadAccess)
                throw new InvalidOperationException("This function must be called from the UI thread");
        }



        // ***END OF MagnifyAndMarkerControl***
    }




    /// <summary>
    /// Used to inform the MagnifyAndMarker User Control of changes external to is that it may need to be aware of
    /// </summary>
    public class MagnifyAndMarkerControlData
    {
        public MagnifyAndMarkerControlData(MagnifyAndMarkerControlEvent action, SurveyorMediaPlayer.eCameraSide cameraSide)
        {
            magnifyAndMarkerControlEvent = action;
            this.cameraSide = cameraSide;
        }

        public enum MagnifyAndMarkerControlEvent
        {
            EpipolarLine,
            EpipolarPoints,
            Error
        }
        public MagnifyAndMarkerControlEvent magnifyAndMarkerControlEvent;

        // Used for all
        public readonly SurveyorMediaPlayer.eCameraSide cameraSide;

        // Used for all
        public TimeSpan? frame;

        // Used in EpipolarLine & EpipolarPoints
        public bool TrueEpipolarLinePointAFalseEpipolarLinePointB;

        // Used in EpipolarLine
        // ax+by+c = 0
        // Where:
        // a is the coefficient of x
        // b is the coefficient of y
        // c is the constant term
        public double epipolarLine_a;
        public double epipolarLine_b;
        public double epipolarLine_c;
        public double focalLength;
        public double baseline;
        public double principalXLeft;
        public double principalYLeft;
        public double principalXRight;
        public double principalYRight;

        // Used in EpipolarPoints
        // Because the iamge are distorted and the epipolar line is not straight, we need to define 3 points
        public Point pointNear;
        public Point pointMiddle;
        public Point pointFar;

        // Used in EpipolarLine & EpipolarPoints
        public int channelWidth; /* 1=Line, >1 = channel, -1 remove line*/
    }


    /// <summary>
    /// Used by the MagnifyAndMarker User Control to inform other components on marker changes within MagnifyAndMarkerControl
    /// </summary>
    public class MagnifyAndMarkerControlEventData
    {
        public MagnifyAndMarkerControlEventData(MagnifyAndMarkerControlEvent e, SurveyorMediaPlayer.eCameraSide cameraSide)
        {
            magnifyAndMarkerControlEvent = e;
            this.cameraSide = cameraSide;
        }

        public enum MagnifyAndMarkerControlEvent
        {
            TargetPointSelected,
            TargetDeleteAll,
            DeleteMeasure3DPointOrSinglePoint,
            AddMeasurementRequest,
            Add3DPointRequest,
            AddSinglePointRequest,
            EditSpeciesInfoRequest,
            UserReqMagWindowSizeSelect,
            UserReqMagZoomSelect,
            Error
        }

        public readonly MagnifyAndMarkerControlEvent magnifyAndMarkerControlEvent;

        // Used for all
        public readonly SurveyorMediaPlayer.eCameraSide cameraSide;

        // Used by TargetPointSelected
        public bool? TruePointAFalsePointB;

        // Used for all
        public Point? pointA;

        // Used by MeasurementPairSelected
        public Point? pointB;

        // Used by EditSpeciesInfoRequest or DeleteMeasure3DPointOrSinglePoint
        public Guid? eventGuid;

        // Used by UserReqMagWindowSizeSelect
        public string? magWindowSize;

        // Used by UserReqMagZoomSelect
        public double? canvasZoomFactor;
    }



    public class MagnifyAndMarkerControlHandler : TListener
    {
        private readonly MagnifyAndMarkerDisplay _magnifyAndMarkerControl;

        public MagnifyAndMarkerControlHandler(IMediator mediator, MagnifyAndMarkerDisplay magnifyAndMarkerControl, MainWindow mainWindow) : base(mediator, mainWindow)
        {
            _magnifyAndMarkerControl = magnifyAndMarkerControl;
        }

        public override void Receive(TListener listenerFrom, object message)
        {
            if (message is MagnifyAndMarkerControlData)
            {
                MagnifyAndMarkerControlData data = (MagnifyAndMarkerControlData)message;

                // Proceed if the message for the MediaPlayer is for this MediaControl instance
                if (_magnifyAndMarkerControl._ProcessIfForMe(data.cameraSide))
                {
                    switch (data.magnifyAndMarkerControlEvent)
                    {
                        case MagnifyAndMarkerControlData.MagnifyAndMarkerControlEvent.EpipolarLine:
                            SafeUICall(() => _magnifyAndMarkerControl.SetCanvasFrameEpipolarLine(data.TrueEpipolarLinePointAFalseEpipolarLinePointB,
                                                                                                 data.epipolarLine_a, data.epipolarLine_b, data.epipolarLine_c, 
                                                                                                 data.channelWidth));
                            break;

                        case MagnifyAndMarkerControlData.MagnifyAndMarkerControlEvent.EpipolarPoints:  // Experimental
                            //SafeUICall(() => _magnifyAndMarkerControl.SetEpipolarPoints(data.TrueEpipolarLinePointAFalseEpipolarLinePointB,
                            //                                                            data.pointNear, data.pointMiddle, data.pointFar,
                            //                                                            data.channelWidth));
                            break;                        
                    }
                }
            }
            else if (message is MagnifyAndMarkerControlEventData)
            {
                MagnifyAndMarkerControlEventData data = (MagnifyAndMarkerControlEventData)message;
                {
                    // Message from the sibling control
                    switch (data.magnifyAndMarkerControlEvent)
                    {
                        case MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.TargetDeleteAll:
                            SafeUICall(() => _magnifyAndMarkerControl.ResetAllTargets());
                            break;
                    }
                }
            }
            else if (message is MediaPlayerEventData)
            {
                MediaPlayerEventData data = (MediaPlayerEventData)message;

                // Proceed if the message for the MediaPlayer is for this MediaControl instance
                if (_magnifyAndMarkerControl._ProcessIfForMe(data.cameraSide))
                {
                    switch (data.mediaPlayerEvent)
                    {
                        case MediaPlayerEventData.eMediaPlayerEvent.FrameRendered:
                            if (data.frameStream is not null && data.position is not null)
                                SafeUICall(() => _magnifyAndMarkerControl._NewImageFrame(data.frameStream, (TimeSpan)data.position, data.imageSourceWidth, data.imageSourceHeight));
                            break;
                        case MediaPlayerEventData.eMediaPlayerEvent.Playing:
                            SafeUICall(() => _magnifyAndMarkerControl._ResetCanvas());
                            break;
                    }
                }
            }
            else if (message is SettingsWindowEventData)
            {
                SettingsWindowEventData data = (SettingsWindowEventData)message;

                switch (data.settingsWindowEvent)
                {
                    // The user has changed the Diagnostic Information settings
                    case eSettingsWindowEvent.DiagnosticInformation:
                        if (data.diagnosticInformation is not null)
                        {
                            _magnifyAndMarkerControl._SetDiagnosticInformation((bool)data!.diagnosticInformation);
                        }
                        break;
                }
            }
        }
    }
}

