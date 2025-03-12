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

using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;               // Point class
using Windows.Graphics.Imaging;         // BitmapTransform
using Windows.Media;
using Windows.Storage.Streams;
using static Surveyor.User_Controls.SettingsWindowEventData;

using Surveyor.Events;
using Surveyor.Helper;


namespace Surveyor.User_Controls
{
    public sealed partial class MagnifyAndMarkerDisplay : UserControl
    {
        // Copy of MainWindow
        private MainWindow? _mainWindow = null;
        private bool mainWindowActivated = false;

        // Copy of the mediator 
        private SurveyorMediator? _mediator;

        // Declare the mediator handler
        private MagnifyAndMarkerControlHandler? _magnifyAndMarkerControlHandler;

        // Which camera side 'L' or 'R'
        public enum CameraSide { None, Left, Right };
        private CameraSide CameraLeftRight = CameraSide.None;

        // Utility timer
        private DispatcherTimer? _timer = null;

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

        // The scaling of the image in the ImageMag where 1 is scales to full image source size 
        private double canvasZoomFactor = 2;    // Must be set to the same initial value as 'canvasZoomFactor' in MediaControl.xaml.cs

        // Indicates if the pointer (mouse) is on this user control of not
        private bool isPointerOnUs = false;

        // Set to true if the Magnifier Window is display as the pointer(mouse) moves
        private bool isAutoMagnify = SettingsManagerLocal.MagnifierWindowAutomatic;    // Must be set to the same initial value as 'isAutoMagnify' in MediaControl.xaml.cs

        // Set to true if the Magnifier Window is locked (i.e. the user has clicked the mouse and the Mag Window
        // no longer follows the pointer (mouse))
        private bool isMagLocked = false;
        private Point magLockedCentre = new(0, 0);

        // Dragging support
        private bool isDragging = false;
        private Point draggingInitialPoint;
        private DateTime draggingInitialPressTime;

        // Default Magnifier Window dimensions
        private const uint magWidthDefaultSmall = 350;
        private const uint magHeightDefaultSmall = 150;
        private const uint magWidthDefaultMedium = 500;
        private const uint magHeightDefaultMedium = 250;
        private const uint magWidthDefaultLarge = 700;
        private const uint magHeightDefaultLarge = 350;

        private uint magWidth = magWidthDefaultMedium;
        private uint magHeight = magHeightDefaultMedium;

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
        private Point? hoveringOverTargetPoint;

        // Border colours
        private readonly Brush magColourUnlocked = new SolidColorBrush(Microsoft.UI.Colors.Black);
        private readonly Brush magColourLocked = new SolidColorBrush(Microsoft.UI.Colors.Orange);

        // Event graphic colours
        private readonly Brush eventDimensionLineColour = new SolidColorBrush(Microsoft.UI.Colors.Orange);
        private readonly Brush eventDimensionHighLightLineColour = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
        private readonly Brush eventArrowLineColour = new SolidColorBrush(Microsoft.UI.Colors.Orange);
        private readonly Brush eventDimensionTextColour = new SolidColorBrush(ColorHelper.FromArgb(255/*alpha*/, 255/*red*/, 93/*green*/, 89/*blue*/));
        private const double eventFontSize = 12.0;

        // Epipolar line colours
        private readonly Brush epipolarALineColour = new SolidColorBrush(Microsoft.UI.Colors.Red);
        //???private readonly Brush epipolarBLineColour = new SolidColorBrush(Microsoft.UI.Colors.Green);
        private readonly Brush epipolarBLineColour = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 228, 30));


        // Selected measurement markers
        private Point? pointTargetA = null;
        private Point? pointTargetB = null;

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

        // Targets status of the other instance
        private bool otherInstanceTargetASet = false;
        private bool otherInstanceTargetBSet = false;

        // Context Menu status        
        private bool canvasMagContextMenuOpen = false;

        // Display Pointer Coords
        public bool DisplayPointerCoords { get; set; } = false;

        public MagnifyAndMarkerDisplay()
        {
            this.InitializeComponent();

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


        /// <summary>
        /// Initialize mediator handler for SurveyorMediaControl
        /// </summary>
        /// <param name="mediator"></param>
        /// <returns></returns>
        public TListener InitializeMediator(SurveyorMediator __mediator, MainWindow __mainWindow)
        {
            _mediator = __mediator;
            _mainWindow = __mainWindow;

            _magnifyAndMarkerControlHandler = new MagnifyAndMarkerControlHandler(_mediator, this, _mainWindow);

            return _magnifyAndMarkerControlHandler;
        }


        /// <summary>
        /// Called initial to setup the Image control we are serving and the camera side
        /// This function must be called for the user control to work
        /// The imageFrame is only used so we know where to poisition the magnifier window. The
        /// souece of the magnified image comes from the NewImageFrame() function
        /// </summary>
        /// <param name="imageFrame"></param>
        /// <param name="cameraside"></param>
        public void Setup(Image imageFrame, CameraSide cameraside)
        {
            // Remember the Image control we are serving
            imageUIElement = imageFrame;
            CameraLeftRight = cameraside;


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

            // Reset the target 
            pointTargetA = null;
            pointTargetB = null;
            ResetTargetOnCanvasFrame(TargetA);
            ResetTargetOnCanvasFrame(TargetB);

            // Remove any Events from the CanvasFrame
            RemoveCanvasShapesByTag(CanvasFrame, "Event");

            // Remove any Epipolar lines or curves from the CanvasFrame
            RemoveCanvasShapesByTag(CanvasFrame, "EpipolarLine");
            RemoveCanvasShapesByTag(CanvasFrame, "EpipolarPoints");

            // Remove any Epipolar lines or curves from the CanvasMag
            RemoveCanvasShapesByTag(CanvasMag, "EpipolarLine");
            RemoveCanvasShapesByTag(CanvasMag, "EpipolarPoints");

            // Clear values
            ClearEventsAndEpipolar();

            // Clear in memory image stream
            streamSource?.Dispose();

            // Assume the image in the ImageFrame is no longer loaded
            imageLoaded = false;
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
        /// Get the target A & B values.
        /// </summary>
        /// <returns></returns>
        //??? No longer used
        //public ValueTuple<Point?, Point?> GetTargets()
        //{
        //    return (pointTargetA, pointTargetB);
        //}


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
        /// Set the auto magnify mode. If true the Mag Window automatically is display as the pointer(mouse)
        /// passes over the Image frame
        /// </summary>
        /// <param name="enable"></param>
        public void AutoMagnify(bool enable)
        {
            isAutoMagnify = enable;

            if (!isAutoMagnify)
                MagHide();
        }


        /// <summary>
        /// Increase the width and height of the Mag Window
        /// </summary>
        /// <returns>Returns the width and height as a uint tuple</returns>
        //??? no longer used
        //public ValueTuple<uint, uint> MagWindowEnlargeSize()
        //{
        //    if (magWidth < 800)
        //    {
        //        magWidth += 20;
        //        magHeight += 20;
        //    }
        //    return (magWidth, magHeight);
        //}


        /// <summary>
        /// Decrease the width and height of the zoom window
        /// </summary>
        /// <returns>Returns the width and height as a uint tuple</returns>
        //??? no longer used
        //public ValueTuple<uint, uint> MagWindowReduceSize()
        //{
        //    if (magWidth > 20 && magHeight > 20)
        //    {
        //        magWidth -= 20;
        //        magHeight -= 20;
        //    }
        //    return (magWidth, magHeight);
        //}


        /// <summary>
        /// Reset the size of the Mag Window to the default
        /// </summary>
        /// <returns>Returns the width and height as a uint tuple</returns>
        //??? no longer used
        //public ValueTuple<uint, uint> MagWindowResetSize()
        //{
        //    magWidth = magWidthDefaultMedium;
        //    magHeight = magHeightDefaultMedium;
        //    return (magWidth, magHeight);
        //}


        /// <summary>
        /// Set the zoom factor of the Mag window where 1 is the full resolution of the image
        /// </summary>
        /// <param name="_zoomFactor"></param>
        public void MagWindowZoomFactor(double _zoomFactor)
        {
            canvasZoomFactor = _zoomFactor;
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
        public void SetLayerType(LayerType layeType)
        {
            layerTypesDisplayed = layeType;

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
                // Check if the Mag Window is currently locked
                if (isMagLocked)
                {
                    // Unlock the Mag Window as long is there isn't any unsaved work
                    MagUnlock();
                }

                // Check we are not in Mag Window lock mode still
                if (!isMagLocked)
                {
                    // If we are allowed to auto magnify the screen and it isn't that the
                    // image isn't already being display at it's maximum resolution then display 
                    // the Magnify Window at the current pointer (mouse) location
                    if (isAutoMagnify && !isImageAtFullResolution)
                    {
                        // Get the pointer point relative to the sender (Image control)
                        PointerPoint pointerPoint = e.GetCurrentPoint(CanvasFrame);

                        // Create a mag window at the current pointer (mouse) location
                        MagWindow(pointerPoint.Position);
                    }
                    else
                    {
                        // If auto magnify isn't on then ensure the Mag Window in empty
                        MagHide();
                    }
                }

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
        /// Called to displau the Canvas Context Mneu and enable/disable each menu item
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DisplayCanvasContextMenu(object sender, PointerRoutedEventArgs e)
        {
            Debug.WriteLine($"hoveringOver:  TargetTrueAFalseB = {hoveringOverTargetTrueAFalseB},  MeasurementEnd={hoveringOverMeasurementEnd}, MeasurementLine={hoveringOverMeasurementLine}, Details={hoveringOverDetails}, Point={hoveringOverPoint}");
            Debug.WriteLine($"otherInstance TargetASet = {otherInstanceTargetASet},  TargetBSet = {otherInstanceTargetBSet}");

            if (this.Resources.TryGetValue("CanvasContextMenu", out object obj) && obj is MenuFlyout menuFlyout)
            {
                // Enable/Disable menu items
                if (hoveringOverTargetTrueAFalseB is not null)
                {
                    CanvasFrameMenuAddSinglePoint.IsEnabled = true;
                    CanvasFrameMenuDeleteTarget.IsEnabled = true;
                }
                else
                {
                    CanvasFrameMenuAddSinglePoint.IsEnabled = false;
                    CanvasFrameMenuDeleteTarget.IsEnabled = false;
                }

                // Enable the Add Stereo Point menu item if the other instance has a target set
                if ((hoveringOverTargetTrueAFalseB == true && otherInstanceTargetASet == true) || (hoveringOverTargetTrueAFalseB == false && otherInstanceTargetBSet == true))
                    CanvasFrameMenuAdd3DPoint.IsEnabled = true;
                else
                    CanvasFrameMenuAdd3DPoint.IsEnabled = false;

                // Hovering over the dimension line
                if (hoveringOverMeasurementEnd == true || hoveringOverMeasurementLine == true)
                    CanvasFrameMenuDeleteMeasurement.IsEnabled = true;
                else
                    CanvasFrameMenuDeleteMeasurement.IsEnabled = false;

                // Hovering over a point 
                if (hoveringOverPoint == true)
                    CanvasFrameMenuDeleteTarget.IsEnabled = true;
                else
                    CanvasFrameMenuDeleteTarget.IsEnabled = false;

                // Hovering over a measurement line or end
                if (hoveringOverMeasurementEnd == true || hoveringOverMeasurementLine == true)
                    CanvasFrameMenuEditMeasurementTargets.IsEnabled = true;
                else
                    CanvasFrameMenuEditMeasurementTargets.IsEnabled = false;

                // Are we hovering over the species info details
                if (hoveringOverDetails == true)
                {
                    // Enable the Edit Species Info menu item
                    CanvasFrameMenuEditSpeciesInfo.IsEnabled = true;

                    // Check which event data type we are hovering over
                    Event? targetEvent = events?.FirstOrDefault(e => e.Guid == hoveringOverGuid);
                    if (targetEvent is not null)
                    {
                        // If the species info details is a point then enable the delete point menu item
                        if (targetEvent.EventDataType == SurveyDataType.SurveyPoint || targetEvent.EventDataType == SurveyDataType.SurveyStereoPoint)
                            CanvasFrameMenuDeleteTarget.IsEnabled = true;
                        else
                            CanvasFrameMenuDeleteTarget.IsEnabled = false;

                        // If the species info details is a measurement then enable the delete measurement menu item
                        if (targetEvent.EventDataType == SurveyDataType.SurveyMeasurementPoints)
                            CanvasFrameMenuDeleteMeasurement.IsEnabled = true;
                        else
                            CanvasFrameMenuDeleteMeasurement.IsEnabled = false;
                    }
                }
                else
                    CanvasFrameMenuEditSpeciesInfo.IsEnabled = false;

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
        /// Pointer (mouse) moved event on the CanvasMag control (sit on top of the ImageMag control)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CanvasMag_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (mainWindowActivated)
            {

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

                // Set the focus to the back button so the keyboard input is routed to the Mag Window
                // This is needed because the Image and Canvas control doesn't support KeyDown events
                // in WinUI3
                ButtonMagBack.Focus(FocusState.Programmatic);

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
            // still working on it) then send a mediator message to inform that both targets
            // have been set
            if (pointTargetA is not null && pointTargetB is not null && targetSelected is null)
            {
                MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.SurveyMeasurementPairSelected,
                                                            CameraLeftRight == CameraSide.Left ? SurveyorMediaPlayer.eCameraSide.Left : SurveyorMediaPlayer.eCameraSide.Right)
                {
                    pointA = pointTargetA,
                    pointB = pointTargetB
                };
                _magnifyAndMarkerControlHandler?.Send(data);
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
        /// Back(Escape) button in the Mag Window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonMagBack_Click(object sender, RoutedEventArgs e)
        {
            MagUnlock();
            Debug.WriteLine("ButtonMagBack_Click");
        }


        /// <summary>
        /// Plus button in the Mag Window to increase the size of the MagWindow
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonMagEnlarge_Click(object sender, RoutedEventArgs e)
        {
            MagWindowSizeEnlargeOrReduce(true/*TrueEnargeFalseReduce*/);
        }


        /// <summary>
        /// Minus button in the Mag Window to reduce the size of the MagWindow
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonMagReduce_Click(object sender, RoutedEventArgs e)
        {
            MagWindowSizeEnlargeOrReduce(false/*TrueEnargeFalseReduce*/);
        }


        /// <summary>
        /// Mag plus magnifier button in the Mag Window to zoom in
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonMagZoomIn_Click(object sender, RoutedEventArgs e)
        {
            MagWindowZoomFactorEnlargeOrReduce(true/*TrueZoomInFalseZoomOut*/);
        }


        /// <summary>
        /// Mag minus magnifier button in the Mag Window to zoom in
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonMagZoomOut_Click(object sender, RoutedEventArgs e)
        {
            MagWindowZoomFactorEnlargeOrReduce(false/*TrueZoomInFalseZoomOut*/);
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
        /// Canvas Frame Context Menu
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CanvasFrameContextMenu_Click(object sender, RoutedEventArgs e)
        {
            MenuFlyoutItem? item = sender as MenuFlyoutItem;
            if (item is not null)
            {
                // Context menu request a point is added
                if (item == CanvasFrameMenuAddMeasurement || item == CanvasFrameMenuAdd3DPoint || item == CanvasFrameMenuAddSinglePoint)
                {
                    if (hoveringOverTargetTrueAFalseB is not null)
                    {
                        // Add either a SurveyPoint or SurveyStereoPoint (is the matching Target is set on the other camera side
                        MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.SurveyPointSelected, 
                            CameraLeftRight == CameraSide.Left ? SurveyorMediaPlayer.eCameraSide.Left : SurveyorMediaPlayer.eCameraSide.Right)
                        {
                            TruePointAFalsePointB = hoveringOverTargetTrueAFalseB
                        };
                        if (hoveringOverTargetTrueAFalseB == true)
                        {
                            data.pointA = hoveringOverTargetPoint;
                            data.pointB = null;
                        }
                        else if (hoveringOverTargetTrueAFalseB == false)
                        {
                            data.pointA = null;
                            data.pointB = hoveringOverTargetPoint;
                        }
                        _magnifyAndMarkerControlHandler?.Send(data);
                    }
                }
                // Context menu - Delete the Target on the Canvas Frame we are hoving over
                else if (item == CanvasFrameMenuDeleteTarget) 
                {                                       
                    if (hoveringOverTargetTrueAFalseB is not null)
                    {
                        if (hoveringOverTargetTrueAFalseB == true)
                        {
                            ResetTargetOnCanvasFrame(TargetA);
                            ResetTargetOnCanvasMag(TargetAMag);
                        }
                        else
                        {
                            ResetTargetOnCanvasFrame(TargetB);
                            ResetTargetOnCanvasMag(TargetBMag);
                        }

                        if (hoveringOverGuid is not null && events is not null)
                        {
                            // Loop through the events and draw them on the canvas
                            foreach (Event evt in events)
                            {
                                if (evt.Guid == hoveringOverGuid)
                                {
                                    events.Remove(evt);
                                    break;
                                }
                            }
                        }

                        hoveringOverTargetTrueAFalseB = null;
                    }
                }
                // Context menu request to delete a measurement or point from the events list
                else if (item == CanvasFrameMenuDeleteMeasurement || item == CanvasFrameMenuDelete3DPoint || item == CanvasFrameMenuDeleteSinglePoint) 
                {
                    if (hoveringOverGuid is not null && events is not null)
                    { 
                        // Loop through the events and draw them on the canvas
                        foreach (Event evt in events)
                        {
                            if (evt.Guid == hoveringOverGuid)
                            {
                                events.Remove(evt);
                                break;
                            }
                        }
                    }
                }
                // Context Menu request to edit an existing SpeciesInfo
                else if (item == CanvasFrameMenuEditSpeciesInfo) 
                {
                    if (hoveringOverGuid is not null)
                    {
                        // Edit species info
                        MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.EditSpeciesInfoRequest, 
                            CameraLeftRight == CameraSide.Left ? SurveyorMediaPlayer.eCameraSide.Left : SurveyorMediaPlayer.eCameraSide.Right)
                        {
                            eventGuid = hoveringOverGuid
                        };
                        _magnifyAndMarkerControlHandler?.Send(data);
                    }
                }
                else if (item == CanvasFrameMenuEditMeasurementTargets) 
                { 
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
                //???    System.Diagnostics.Debug.WriteLine("App is deactivated");
                mainWindowActivated = false;
            }
            else
            {
                //???    System.Diagnostics.Debug.WriteLine("App is activated");
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



        ///
        /// MEDIATOR METHODS (Called by the TListener, always marked as internal)
        ///


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
            Debug.Assert(imageUIElement is not null, "MagnifyAndMarkerControl.Setup(...) must be called before calling the methods");

            Debug.WriteLine($"_NewImageFrame: Position:{_position}, Width:{_imageSourceWidth}, Height:{_imageSourceHeight}");

            // Remember the frame position
            // Used to know what Events are applicable to this frame
            position = _position;
            streamSource = frameStream;
            imageSourceWidth = _imageSourceWidth;
            imageSourceHeight = _imageSourceHeight;


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

            // Do coordinate need to be displayed
            DisplayPointerCoords = SettingsManagerLocal.DiagnosticInformation;


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
        internal bool _ProcessIfForMe(SurveyorMediaPlayer.eCameraSide cameraSide)
        {
            if (CameraLeftRight == CameraSide.Left && cameraSide == SurveyorMediaPlayer.eCameraSide.Left)
                return true;
            else if (CameraLeftRight == CameraSide.Right && cameraSide == SurveyorMediaPlayer.eCameraSide.Right)
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
            if (CameraLeftRight == CameraSide.Left && ControlType == SurveyorMediaControl.eControlType.Primary)
                return true;
            else if (CameraLeftRight == CameraSide.Right && ControlType == SurveyorMediaControl.eControlType.Secondary)
                return true;
            else if (ControlType == SurveyorMediaControl.eControlType.Both)
                return true;
            else
                return false;
        }


        /// <summary>
        /// Used to change the status of auto magnify. Used by the SettingsWindow to inform the MagnifyAndMarkerDisplay
        /// that the user has changed the auto magnify setting
        /// </summary>
        /// <param name="isAutoMagnify"></param>
        internal void _SetIsAutoMagnify(bool isAutoMagnify)
        {
            this.isAutoMagnify = isAutoMagnify;
        }


        /// <summary>
        /// The user changes the Diagnostic Information setting
        /// </summary>
        /// <param name="diagnosticInformation"></param>
        internal void _SetDiagnosticInformation(bool diagnosticInformation)
        {
            DisplayPointerCoords = diagnosticInformation;
            if (diagnosticInformation)

                // Hide pointer coords
                _mainWindow?.DisplayPointerCoordinates(-1, -1);
        }



        ///
        /// PRIVATE METHODS
        ///


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
                imageUIElement.Parent is Grid gridParentImageFrame && imageUIElement.ActualWidth != 0)
            {
                // Calulate the scale factor between the actual image and the screen image
                canvasFrameScaleX = imageUIElement.ActualWidth / imageSourceWidth;
                canvasFrameScaleY = imageUIElement.ActualHeight / imageSourceHeight;

                // Scale Canvas to the same resolution as the actual source image
                CanvasFrame.RenderTransform = new ScaleTransform
                {
                    ScaleX = (double)canvasFrameScaleX,
                    ScaleY = (double)canvasFrameScaleY,
                };

                // Save the CanvasFrame to same dimensions as the actual source image (as
                // opposed to the ImageFrame control dimensions). This keeps the calculations
                // simple
                CanvasFrame.Width = imageSourceWidth;
                CanvasFrame.Height = imageSourceHeight;

                // Discover exactly where the XAML rendering engine placed the ImageFrame (given
                // it was Stretch="Uniform") within it's parent grid
                var transformImageFrame = imageUIElement.TransformToVisual(gridParentImageFrame);
                Point relativePositionImageFrame = transformImageFrame.TransformPoint(new Point(0, 0));
                //???Debug.WriteLine($"***ImageFrame origin relative to Grid ({relativePositionImageFrame.X:F1},{relativePositionImageFrame.Y:F1})");

                // Doing a ScaleTransform on a Canvas seems to move it origin. We need it to be
                // exactly aligned with the ImageFrame.  If the ImageFrame and the Canvas are
                // within a Grid container the only way to do this is to put a margin either on the
                // left/right or the up/down side as required
                CanvasFrame.Margin = new Thickness(relativePositionImageFrame.X, relativePositionImageFrame.Y, relativePositionImageFrame.X, relativePositionImageFrame.Y);

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
                Debug.WriteLine($"AdjustCanvasSizeAndScaling: Skipped body. imageUIElement Not Null is {imageUIElementNotNull}, imageUIElementParent is Grid {imageUIElementParentIsGrid}, AcutalWidth = {imageUIElementActualWidth}");
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
                    ButtonMagOK.IsEnabled = true;
                else
                    ButtonMagOK.IsEnabled = false;

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
        /// Unlocks the Mag Window as long as there is unsved moved target icons
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
                Debug.WriteLine($"Unlock Magnify Window and hide");
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
            Debug.Assert(imageUIElement is not null, "MagnifyAndMarkerControl.Setup(...) must be called before calling the methods");

            magIntoImageSquare_EntryCounter++;

            // Discard this message if there are more queued up
            if (magIntoImageSquare_EntryCounter == 1)
            {
                // Reset rectMagPointerBounds for safty, not strictly necessary
                rectMagPointerBounds = new Rect(0, 0, 0, 0);

                // Calculate the actual zoom
                double zoom = canvasZoomFactor;

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
                                double magWidthZoomed = magWidth / zoom;
                                double magHeightZoomed = magHeight / zoom;
                                double magSourceLeft = Math.Clamp(pointerPosition.X - (magWidthZoomed / 2), 0/*min*/, CanvasFrame.ActualWidth - magWidthZoomed/*max*/);
                                double magSourceTop = Math.Clamp(pointerPosition.Y - (magHeightZoomed / 2), 0/*min*/, CanvasFrame.ActualHeight - magHeightZoomed/*max*/);
                                double magSourceWidth = Math.Min(magWidthZoomed, decoder.PixelWidth - magSourceLeft);
                                double magSourceHeight = Math.Min(magHeightZoomed, decoder.PixelHeight - magSourceTop);

                                rectMagWindowSource = new Rect(magSourceLeft, magSourceTop, magSourceWidth, magSourceHeight);
                            }


                            // Definate the ImageMag Rect for pointer bounds checking in 
                            // PointerMoved events
                            rectMagPointerBounds = new Rect(0.0, 0.0, rectMagWindowSource.Width, rectMagWindowSource.Height);

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


                            // Get the pixel data for the zoomed region.
                            var pixelProvider = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);

                            // Create a new WriteableBitmap for ImageMag
                            WriteableBitmap magImageBitmap = new WriteableBitmap((int)Math.Round(rectMagWindowSource.Width), (int)Math.Round(rectMagWindowSource.Height));
                            pixelProvider.DetachPixelData().CopyTo(magImageBitmap.PixelBuffer);

                            // Update ImageMag's source and scaling
                            ImageMag.Source = magImageBitmap;
                            ImageMag.RenderTransform = new ScaleTransform()
                            {
                                ScaleX = zoom,
                                ScaleY = zoom
                            };
                            CanvasMag.RenderTransform = new ScaleTransform()
                            {
                                ScaleX = zoom,
                                ScaleY = zoom
                            };


                            // Debug reporting
                            //???Debug.WriteLine($"{magIntoImageSquare_EntryCounter}:Pointer ({pointerPosition.X:F1},{pointerPosition.Y:F1}) ImageFrame cx={imageUIElement.ActualWidth:F1}, cy={imageUIElement.ActualHeight:F1}, Source Image cx,cy {imageSourceWidth},{imageSourceHeight}, Mag Zoom:{canvasZoomFactor:F1}");
                            //???Debug.WriteLine($"{magIntoImageSquare_EntryCounter}:    Mag Window Coords left,top=({rectMagWindowScreen.X:F1},{rectMagWindowScreen.Y:F1}), cx,cy {rectMagWindowScreen.Width:F1},{rectMagWindowScreen.Height:F1}");
                            //???Debug.WriteLine($"{magIntoImageSquare_EntryCounter}:    Mag Window Source Coords left,top=({rectMagWindowSource.X:F1},{rectMagWindowSource.Y:F1}), cx,cy {rectMagWindowSource.Width:F1},{rectMagWindowSource.Height:F1}, Zoom={zoom:F2}");

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
                        Debug.WriteLine($"MagWindow display: {ex.Message}");
                    }
                }
            }

            magIntoImageSquare_EntryCounter--;
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
                Debug.WriteLine($"CheckImageResolution: Is the image displayed at full resolution? {isDisplayedAtFullResolution}");
            }
            else
            {
                // Handle case where the image source is not a BitmapImage, is not set, or XamlRoot is null
                Debug.WriteLine("CheckImageResolution: Image source is not a BitmapImage, is not set, or XamlRoot is null.");
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
                    Debug.WriteLine($"SetSelectedTarget Selected Target A");
                }
                else if (targetSelected == TargetBMag)
                {
                    targetSelectedTrueAFalseB = false;
                    Debug.WriteLine($"SetSelectedTarget Selected Target B");
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
                throw new Exception("Scale factors (scaleX & scaleY) not setup");
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
                if (events is not null && events.Count > 0 && position is not null)
                {
                    // Loop through the events and draw them on the canvas
                    foreach (Event evt in events)
                    {
                        // Is the event for this frame?
                        if ((CameraLeftRight == CameraSide.Left && evt.TimeSpanLeftFrame == position) ||
                            (CameraLeftRight == CameraSide.Right&& evt.TimeSpanRightFrame == position))
                        {
                            // Draw the SurveyMeasurement
                            if (evt.EventData is SurveyMeasurement surveyMeasurement)
                            {
                                Point pointA;
                                Point pointB;

                                // Points definition
                                if (CameraLeftRight == CameraSide.Left)
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
                                if (CameraLeftRight == CameraSide.Left)
                                    point = new(surveyStereoPoint.LeftX, surveyStereoPoint.LeftY);
                                else
                                    point = new(surveyStereoPoint.RightX, surveyStereoPoint.RightY);


                                if (surveyStereoPoint is not null)
                                    DrawEventPoint(evt.Guid, point, surveyStereoPoint.SpeciesInfo);
                            }
                            // Draw the SurveyPoint
                            else if (evt.EventData is SurveyPoint surveyPoint)
                            {
                                // Point definition on left side only
                                if (CameraLeftRight == CameraSide.Left)
                                {
                                    Point point = new(surveyPoint.X, surveyPoint.Y);
                                    DrawEventPoint(evt.Guid, point, surveyPoint.SpeciesInfo);
                                }
                            }
                        }
                    }
                }
            }
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
                Debug.Assert(false, "SetTargetOnCanvasMag: Rectangle is not a CanvasMag target icon, programming error!");

            if (TrueAOnlyFalseBOnly is not null)
            {
                // Put the correct target icon on the CanvasMag
                SetTargetIconOnCanvas(rectangle, x, y, targetIconType, false/*TrueCanvasFalseMagCanvas*/);

                // Transfer Target to the target variable and then from the variable to CanvasFrame       
                TransferTargetsBetweenVariableAndCanvasMag((bool)TrueAOnlyFalseBOnly, false /*TrueToCanvasFalseFromCanvas*/);
                TransferTargetsBetweenVariableAndCanvasFrame((bool)TrueAOnlyFalseBOnly, true /*TrueToCanvasFalseFromCanvas*/);

                // Send a mediator message to inform that a target has been set
                // Signal to the MagnifyAndMarkerControl to display the epipolar line
                MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.TargetPointSelected, 
                    CameraLeftRight == CameraSide.Left ? SurveyorMediaPlayer.eCameraSide.Left : SurveyorMediaPlayer.eCameraSide.Right)
                {
                    TruePointAFalsePointB = TrueAOnlyFalseBOnly,
                };
                if (TrueAOnlyFalseBOnly == true)
                    data.pointA = pointTargetA;
                else
                    data.pointB = pointTargetB;
                _magnifyAndMarkerControlHandler?.Send(data);

                
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
                Debug.Assert(false, "ResetTargetOnCanvasFrame: Rectangle is not a CanvasFrame target icon, programming error!");

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
                Debug.Assert(false, "ResetTargetOnCanvasMag: Rectangle is not a CanvasMag target icon, programming error!");

            if (TrueAOnlyFalseBOnly is not null)
            {
                // Remove the target icon from the CanvasMag          
                ResetTargetIconOnCanvas(rectangle);

                // Transfer Target to the target variable and then from the variable to CanvasFrame       
                TransferTargetsBetweenVariableAndCanvasMag((bool)TrueAOnlyFalseBOnly, false /*TrueToCanvasFalseFromCanvas*/);
                TransferTargetsBetweenVariableAndCanvasFrame((bool)TrueAOnlyFalseBOnly, true /*TrueToCanvasFalseFromCanvas*/);

                // Send a mediator message to inform that a target has been reset (use TargetPointSelected but with PointA & B set to null)
                MagnifyAndMarkerControlEventData data = new(MagnifyAndMarkerControlEventData.MagnifyAndMarkerControlEvent.TargetPointSelected,
                    CameraLeftRight == CameraSide.Left ? SurveyorMediaPlayer.eCameraSide.Left : SurveyorMediaPlayer.eCameraSide.Right)
                {
                    TruePointAFalsePointB = TrueAOnlyFalseBOnly,
                    pointA = null,
                    pointB = null
                };
                _magnifyAndMarkerControlHandler?.Send(data);
            }
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
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }


        /// <summary>
        /// Utility timer fired
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Timer_Tick(object? sender, object e)
        {
            if ((!isPointerOnUs && !canvasMagContextMenuOpen) || !mainWindowActivated)
                // Unlock/Hide the Mag Window
                MagHide();  // This function will unlock and/or hide the Mag Window
        }


        private void MagWindowSizeEnlargeOrReduce(bool TrueEnargeFalseReduce)
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
        /// Used to increase of decrease the zoom factor from inside the MagWindow
        /// </summary>
        /// <param name="TrueZoomInFalseZoomOut"></param>
        private void MagWindowZoomFactorEnlargeOrReduce(bool TrueZoomInFalseZoomOut)
        {
            // Zoom levels 5,3,2,1,0.5
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
                else if (canvasZoomFactor == 1.0)
                    MagWindowZoomFactor(0.5);
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
            SurveyMeasurementPairSelected,
            SurveyPointSelected,
            EditSpeciesInfoRequest,
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

        // Used by EditSpeciesInfoRequest
        public Guid? eventGuid;
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
                    }
                }
            }
            else if (message is SettingsWindowEventData)
            {
                SettingsWindowEventData data = (SettingsWindowEventData)message;

                switch (data.settingsWindowEvent)
                {
                // The user has changed the auto magnify setting
                case eSettingsWindowEvent.MagnifierWindow:
                    SafeUICall(() => _magnifyAndMarkerControl._SetIsAutoMagnify((bool)data!.magnifierWindowAutomatic!));
                    break;

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

