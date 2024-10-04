// SurveyorMediaControl  Media Control User Control
// This is a user control that is used to control the playing of the media files 
// 
// Version 1.1
//


using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.System;
using static Surveyor.MediaStereoControllerEventData;
#if !No_MagnifyAndMarkerDisplay
using static Surveyor.User_Controls.MagnifyAndMarkerDisplay;
#endif
using static Surveyor.User_Controls.MediaControlEventData;
using static Surveyor.User_Controls.SurveyorMediaPlayer;


namespace Surveyor.User_Controls
{
    public sealed partial class SurveyorMediaControl : UserControl
    {
        // Reporter
        private Reporter? report = null;

        // Copy of the mediator 
        private SurveyorMediator? mediator;

        // Declare the mediator handler for MediaControl
        private MediaControlHandler? mediaControlHandler;

        // Primary or Secondary Control 
        public enum eControlType { None, Primary, Secondary, Both };
        public eControlType ControlType { get; set; } = eControlType.None;

        // Speed
        private float _speed = 1.0f;

        // Set to true if the Magnifier Window is automatically displayed as the pointer(mouse) moves
        private bool isAutoMagnify = true;    // Must be set to the same initial value as 'isAutoMagnify' in MagnifyAndMarkerDisplay.xaml.cs
        private Microsoft.UI.Xaml.Media.SolidColorBrush? appButtonAutoMagnifyOn = null;
        private Microsoft.UI.Xaml.Media.SolidColorBrush? appButtonAutoMagnifyOff = null;
        private string? appButtonAutoMagnifyTooltip = null;

        // The scaling of the image in the ImageMag where 1 is scales to full image source size 
        private double canvasZoomFactor = 2;    // Must be set to the same initial value as 'canvasZoomFactor' in MagnifyAndMarkerDisplay.xaml.cs

        // Mag Window Size
        private string magWindowSize = "Medium";  // Must be set to the same initial value as in MagnifyAndMarkerDisplay.xaml.cs

        // Layer Types Displayed
#if !No_MagnifyAndMarkerDisplay
        private LayerType layerTypesDisplayed = LayerType.All;    // Must be set to the same initial value as in MagnifyAndMarkerDisplay.xaml.cs
#endif

        // Mute/Unmute - this status is change via a mediator message from MediaPlayer
        public bool Mute { get; set; } = true;

        // Play/Pause - this status is change via a mediator message from MediaPlayer
        public bool PlayOrPause { get; set; } = false;

        // FullScreen - this status is change via a mediator message from MediaPlayer
        private bool _TrueYouAreFullFalseYouAreRestored = false;        
        private eCameraSide? _fullScreenCameraSide = null;

        // Duration of the media (last received from the MediaPlayer)
        private TimeSpan _duration = TimeSpan.Zero;

        // Used to detect if the user is dragging the media position slider
        private bool _userIsInteractingWithSlider = false;

        // Mousewheel calibration and debounce/accumulation 
        private int _lowestMouseWheelDeltaSeen = Int32.MaxValue;
        private DispatcherTimer _wheelEventTimer;
        private int _wheelAccumulatedDelta = 0;
        private const int _wheelDebounceIntervalMs = 50; // Debounce interval in milliseconds

        // Used to indicate if the media is synchronized
        // This is signaled through mediator from the MediaStereoController
        // Is is only updated on the primary media control
        private bool mediaSynchronized = false;

        public SurveyorMediaControl()
        {
            this.InitializeComponent();

            // Enable the media controls
            EnableControls(false);
            MuteUmmuteSetIcon();

            // Initialize the debounce timer for the mouse wheel handling
            _wheelEventTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_wheelDebounceIntervalMs)
            };
            _wheelEventTimer.Tick += WheelEventTimer_Tick;
        }


        /// <summary>
        /// Set the Reporter, used to output messages.
        /// Call as early as possible after creating the class instance.
        /// </summary>
        /// <param name="_report"></param>
        public void SetReporter(Reporter _report)
        {
            report = _report;
        }


        /// <summary>
        /// Initialize mediator handler for SurveyorMediaControl
        /// </summary>
        /// <param name="mediator"></param>
        /// <returns></returns>
        public TListener InitializeMediator(SurveyorMediator _mediator, MainWindow _mainWindow)
        {
            mediator = _mediator;

            mediaControlHandler = new MediaControlHandler(mediator, _mainWindow, this);

            return mediaControlHandler;
        }

        /// <summary>
        /// Clear text values, reset the slider
        /// </summary>
        public void Clear()
        {
            ControlPositionText.Text = "";
            ControlDurationText.Text = "";
            ControlSpeedText.Text = "";
            ControlSpeedText.Text = "";
            _duration = TimeSpan.Zero;
            _userIsInteractingWithSlider = false;

            // Turn off and on the event handler to prevent the slider from sending a message to
            // the MediaSteroController as though the user has moved the slider
            ControlPosition.ValueChanged -= ControlSlider_ValueChanged;
            ControlPosition.Value = 0;
            ControlPosition.ValueChanged += ControlSlider_ValueChanged;
        }

        /// <summary>
        /// Enable of disable the controls 
        /// </summary>
        /// <param name="trueEnableFalseDisable"></param>
        internal void EnableControls(bool trueEnableFalseDisable)
        {
            if (trueEnableFalseDisable == true)
            {
                ControlSpeedText.Text = $"x{_speed:F1}";
                ControlFrameBack.IsEnabled = true;
                ControlFrameForward.IsEnabled = true;
                ControlPlayPause.IsEnabled = true;                
                ControlBack10.IsEnabled = true;
                ControlForward30.IsEnabled = true;
                ControlSpeedDecrease.IsEnabled = true;
                ControlSpeedIncrease.IsEnabled = true;
                ControlSpeed.IsEnabled = true;
                ControlFullScreen.IsEnabled = true;
                ControlProperties.IsEnabled = true;
                ControlMuteUnmute.IsEnabled = true;
                ControlEvent.IsEnabled = true;
                ControlSaveFrame.IsEnabled = true;
                ControlCast.IsEnabled = true;
            }
            else
            {
                ControlPositionText.Text = "";
                ControlDurationText.Text = "";
                ControlSpeedText.Text = "";
                ControlSpeedText.Text = "";
                ControlFrameBack.IsEnabled = false;
                ControlFrameForward.IsEnabled = false;
                ControlPlayPause.IsEnabled = false;
                ControlBack10.IsEnabled = false;
                ControlForward30.IsEnabled = false;
                ControlSpeedDecrease.IsEnabled = false;
                ControlSpeedIncrease.IsEnabled = false;
                ControlSpeed.IsEnabled = false;
                ControlFullScreen.IsEnabled = false;
                ControlProperties.IsEnabled = false;
                ControlMuteUnmute.IsEnabled = false;
                ControlEvent.IsEnabled = false;
                ControlSaveFrame.IsEnabled = false;
                ControlCast.IsEnabled = false;

                // Turn off and on the event handler to prevent the slider from sending a message to
                // the MediaSteroController as though the user has moved the slider
                ControlPosition.ValueChanged -= ControlSlider_ValueChanged;
                ControlPosition.Value = 0;
                ControlPosition.ValueChanged += ControlSlider_ValueChanged;
            }
        }


        /// <summary>
        /// Toggle the play/paused button icon
        /// </summary>
        internal void PlayOrPausedSetIcon()
        {
            var fontIcon = ControlPlayPause.Icon as FontIcon;
            if (fontIcon is not null)
            {
                if (PlayOrPause)
                {
                    fontIcon.Glyph = "\uF8AE";  // Pause Icon
                    ToolTipService.SetToolTip(ControlPlayPause, "Pause (Spacebar)");
                }
                else
                {
                    fontIcon.Glyph = "\uF5B0";  // Play Icon (Solid)
                    ToolTipService.SetToolTip(ControlPlayPause, "Play (Spacebar)");
                }
            }
        }


        /// <summary>
        /// Toggle the mute/unmute button icon
        /// Note no tooltip is needs because the because the mute/unmute has a label in the overflow menu
        /// </summary>
        internal void MuteUmmuteSetIcon()
        {
            var fontIcon = ControlMuteUnmute.Icon as FontIcon;
            if (fontIcon is not null)
            {
                // Check mute/unmute button is in sync
                if (Mute == true)
                {
                    fontIcon.Glyph = "\uE198";  // Mute Icon
                }
                else
                {
                    fontIcon.Glyph = "\uE15D";  // UnMute Icon
                }
            }
        }


        /// <summary>
        /// Check if MediaControls should process the message
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        internal bool ProcessIfForMe(MediaPlayerEventData message)
        {
            if (ControlType == eControlType.Primary && message.cameraSide == SurveyorMediaPlayer.eCameraSide.Left)
                return true;
            else if (ControlType == eControlType.Secondary && message.cameraSide == SurveyorMediaPlayer.eCameraSide.Right)
                return true;
            else if (ControlType == eControlType.Both)
            {
                // If the MediaControl is controlling both the left and the right cameras then
                // we want to process all the left camera message and the position message for the right camera
                if (message.cameraSide == SurveyorMediaPlayer.eCameraSide.Left)
                    return true;
                else if (message.mediaPlayerEvent == MediaPlayerEventData.eMediaPlayerEvent.Position)
                    return true;
            } 
            return false;
        }


        /// <summary>
        /// Return true if this instance of the MediaControls is the primary control
        /// </summary>
        /// <returns></returns>
        internal bool ProcessIfWeArePrimary()
        {
            if (ControlType == eControlType.Primary)
                return true;
            else
                return false;
        }


        /// <summary>
        /// Update the duration under the slider on the right side
        /// </summary>
        /// <param name="message"></param>
        internal void UpdateDuration(MediaPlayerEventData message)
        {
            if (message is not null && message.duration is not null)
            {
                _duration = (TimeSpan)message.duration;
                string durationText = $"{_duration:hh\\:mm\\:ss}";

                ControlDurationText.Text = durationText;
            }
        }


        /// <summary>
        /// Update the position under the slider on the left side
        /// </summary>
        /// <param name="message"></param>
        internal void UpdatePositionAndFrame(MediaPlayerEventData message)
        {
            if (message.position is not null)
            {
                // Update the position text time
                string positionText;
                positionText = $"{message.position:hh\\:mm\\:ss\\.ff}";

                // Update the frame index
                if (message.frameIndex != -1)
                    positionText += $" / {message.frameIndex}";

                //??? Temp debug
                //switch (ControlType)
                //{
                //    case eControlType.Primary:
                //        positionText += $" P";
                //        break;
                //    case eControlType.Secondary:
                //        positionText += $" S";
                //        break;
                //    case eControlType.Both:
                //        positionText += $" B";
                //        break;
                //}
                //???

                // If the user isn't currently dragging the slider we are going to
                // update the slider position
                // Turn off and on the event handler to prevent the slider from sending a message to
                // the MediaSteroController as though the user has moved the slider
                if (!_userIsInteractingWithSlider)
                {
                    ControlPosition.ValueChanged -= ControlSlider_ValueChanged;
                    if (_duration != TimeSpan.Zero)
                    {
                        ControlPosition.Value = ((TimeSpan)message.position).TotalSeconds / _duration.TotalSeconds * 100;
                    }
                    else
                    {
                        ControlPosition.Value = 0;
                    }
                    ControlPosition.ValueChanged += ControlSlider_ValueChanged;
                }

                if (ControlType == eControlType.Primary || ControlType == eControlType.Secondary)
                {
                    ControlPositionText.Text = positionText;
                }
                else if (ControlType == eControlType.Both)
                {
                    if (message.cameraSide == SurveyorMediaPlayer.eCameraSide.Left)
                    {
                        ControlPositionText.Text = positionText;
                    }
                    else if (message.cameraSide == SurveyorMediaPlayer.eCameraSide.Right)

                    {
                        //???TextBoxFrame1.Text = $"{message.frameIndex}";
                    }
                }
            }
        }


        /// <summary>
        /// Called to inform the MediaControl User Control that the MediaSteroController has responsed to our
        /// request to display full screen and this media player has the whole grid if True or it has been
        /// restored to its original size if False
        /// </summary>
        /// <param name="TrueYouAreFullFalseYouAreRestored"></param>
        /// <param name="cameraSide">Only used if TrueYouAreFullFalseYouAreRestored = true</param>
        internal void MediaFullScreen(bool TrueYouAreFullFalseYouAreRestored, eCameraSide? cameraSide)
        {
            // Remember if this MediaControl has the ful screen or not
            _TrueYouAreFullFalseYouAreRestored = TrueYouAreFullFalseYouAreRestored;

            // Remember which camera side is full screen if TrueYouAreFullFalseYouAreRestored = true (null otherwise)
            _fullScreenCameraSide = cameraSide;

            // Set the button of the MediaControl accordingly
            SetMediaControlsFullScreenButtons();
        }



        ///
        /// EVENTS
        /// 


        /// <summary>
        /// User has selected to play or pause the media
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlPlayPause_Click(object sender, RoutedEventArgs e)
        {
            // !PlayOrPause is the new state
            string playOrPause = !PlayOrPause == true ? "play" : "pause";

            // Signal eMediaControlEvent.UserReqPlayOrPause with !PlayOrPause
            mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqPlayOrPause, ControlType) { playOrPause = !PlayOrPause });

            string controls = ControlType.ToString();            
            Debug.WriteLine($"{controls}: User requested to {playOrPause}");
        }


        /// <summary>
        /// Users has selected to jump to a new position in the media using the slider
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {            
            double newValue = e.NewValue;
            TimeSpan position = TimeSpan.FromSeconds(newValue / 100 * _duration.TotalSeconds);

            // Signal eMediaControlEvent.UserReqFrameJump with position
            mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqFrameJump, ControlType) { positionJump = position });

            string controls = ControlType.ToString();
            Debug.WriteLine($"{controls}: User requested to jump to position {position:hh\\:mm\\:ss\\.ff}");
        }



        /// <summary>
        /// *** PointerPressed/PointerRelease events never get called in WinUI3 ***
        /// The intetion is to allow the user to freely move the slider without the media player
        /// updating it behind the scenes. 
        /// *** The four function below could be deleted. However maybe future WinUI3 will support them ***
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private bool _wasPlayerBeforeUserInteractingWithSlider = false;
        private void ControlPosition_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _userIsInteractingWithSlider = true;

            if (PlayOrPause)
            {
                // Remember we were playing so we can restore the play state after the user has
                // finished interacting with the slider
                _wasPlayerBeforeUserInteractingWithSlider = true;

                // Pause - Signal eMediaControlEvent.UserReqPlayOrPause 
                mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqPlayOrPause, ControlType) { playOrPause = false });
            }
            else
                _wasPlayerBeforeUserInteractingWithSlider = false;
        }
        private void ControlPosition_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            ControlPosition_InteractionEnded();
        }
        private void ControlPosition_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            ControlPosition_InteractionEnded();
        }
        private void ControlPosition_InteractionEnded()
        {
            _userIsInteractingWithSlider = false;

            if (_wasPlayerBeforeUserInteractingWithSlider)
            {
                // Resume Play - Signal eMediaControlEvent.UserReqPlayOrPause
                mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqPlayOrPause, ControlType) { playOrPause = true });

                _wasPlayerBeforeUserInteractingWithSlider = false;
            }
        }

        /// <summary>
        /// The user has selected to move the media back 10 seconds
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlBack10_Click(object sender, RoutedEventArgs e)
        {
            // Signal eMediaControlEvent.UserReqMoveStepBack 
            mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqMoveStepBack, ControlType));
        }


        /// <summary>
        /// Reduce the playback rate
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlSpeedDecrease_Click(object sender, RoutedEventArgs e)
        {
            // Calculate the new increased speed
            _speed = CalcSpeed(_speed, false/*TrueIncreaseFalseDecrease*/);

            ControlSpeedText.Text = $"x{_speed:F2}";

            // Signal eMediaControlEvent.UserReqSpeedSelect with _speed
            mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqSpeedSelect, ControlType) { speed = _speed });
        }


        /// <summary>
        /// The user has selected to move the media back one frame
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlFrameBack_Click(object sender, RoutedEventArgs e)
        {
            // Signal eMediaControlEvent.UserReqFrameBackward
            mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqFrameBackward, ControlType));
        }


        /// <summary>
        /// The user has selected to move the media forward one frame
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlFrameForward_Click(object sender, RoutedEventArgs e)
        {
            // Signal eMediaControlEvent.UserReqFrameForward
            mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqFrameForward, ControlType));
        }


        /// <summary>
        /// The user has selected to increase the playback rate
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlSpeedIncrease_Click(object sender, RoutedEventArgs e)
        {
            // Calculate the new increased speed
            _speed = CalcSpeed(_speed, true/*TrueIncreaseFalseDecrease*/);

            ControlSpeedText.Text = $"x{_speed:F2}";

            // Signal eMediaControlEvent.UserReqSpeedSelect with _speed
            mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqSpeedSelect, ControlType) { speed = _speed });
        }


        /// <summary>
        /// The user has selected to move the media forward 30 seconds
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlForward30_Click(object sender, RoutedEventArgs e)
        {
            // Signal eMediaControlEvent.UserReqMoveStepForward
            mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqMoveStepForward, ControlType));
        }


        /// <summary>
        /// The Speed select sub-menu is being opened. Put a check mark against the current speed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlSpeed_Opening(object sender, object e)
        {
            // Retrieve the parent MenuFlyout
            MenuFlyout? parentFlyout = sender as MenuFlyout;

            // Create a text string of the current speed of 2 decimal places to compare
            // with the speed of the menu items
            string compareSpeed = _speed.ToString("F2");
            if (parentFlyout is not null)
            {
                foreach (var item in parentFlyout.Items)
                {
                    if (item is ToggleMenuFlyoutItem flyoutItem)
                        if (flyoutItem.Text == compareSpeed)
                            flyoutItem.IsChecked = true;
                        else
                            // Uncheck all other items
                            flyoutItem.IsChecked = false;
                }
            }
        }

        /// <summary>
        /// The user has selected a new speed from the speed select sub-menu
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlSpeed_Click(object sender, RoutedEventArgs e)
        {
            ToggleMenuFlyoutItem? selectedFlyoutItem = sender as ToggleMenuFlyoutItem;

            if (selectedFlyoutItem is not null)
            {
                // Access the MenuFlyout
                MenuFlyout? speedFlyout = ControlSpeed.Flyout as MenuFlyout;

                if (speedFlyout != null)
                {
                    foreach (var item in speedFlyout.Items)
                    {
                        // Check if the item is a ToggleMenuFlyoutItem
                        if (item is ToggleMenuFlyoutItem flyoutItem && flyoutItem != selectedFlyoutItem)
                        {
                            // Uncheck all other items
                            flyoutItem.IsChecked = false;
                        }
                    }
                }

                // Check the selected item (if not already checked)
                selectedFlyoutItem.IsChecked = true;

                // Now figure which speed was clicked on
                // Convert the selected Flyout Item text to a float
                float speed = float.Parse(selectedFlyoutItem.Text);
                if (speed != _speed)
                {
                    _speed = speed;
                    ControlSpeedText.Text = $"x{_speed:F2}";

                    // Send the new speed to the MediaSteroController
                    mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqSpeedSelect, ControlType) { speed = _speed });
                }
            }
        }

        /// <summary>
        /// FullScreen or BackToWindow button pressed, determine which and signal the MediaSteroController
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlFullScreen_Click(object sender, RoutedEventArgs e)
        {
            if (_TrueYouAreFullFalseYouAreRestored == false)
            {
                // We are in the regular stereo player mode and we want to go full screen on one of the media players
                // Check if we are the primary or secondary media control
                if (ControlType == eControlType.Primary)
                {
                    // Note if media is synchronized the this button still means full screen left camera size
                    // (so no need to check if media is synchronized)
                    mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqFullScreen, ControlType)
                    {
                        cameraSide = eCameraSide.Left
                    });
                }
                else
                {
                    mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqFullScreen, ControlType)
                    {
                        cameraSide = eCameraSide.Right
                    });
                }
            }
            else
            {
                if (_fullScreenCameraSide == eCameraSide.Right)
                {
                    // We are in full screen mode and we want to switch media player while staying in full screen mode                    
                    mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqFullScreen, ControlType)
                    {
                        cameraSide = eCameraSide.Left
                    });
                }
                else
                {
                    // We are in the full screen mode on one of the media players and we want to go back to regular stereo player mode
                    mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqBackToWindow, ControlType));
                }
            }
        }
        /// <summary>
        /// Alternative FullScreen or BackToWindow button pressed, determine which and signal the 
        /// MediaSteroController. The alternative button is used if we are in the full screen and 
        /// we want to switch media player while staying in full screen mode or if we are in 
        /// synchronized mode and we want to go full screen on the right media player
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlFullScreenAlternative_Click(object sender, RoutedEventArgs e)
        {
            if (_TrueYouAreFullFalseYouAreRestored == false)
            {
                // We are in the regular stereo player mode and we want to go full screen on one of the media players
                mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqFullScreen, ControlType)
                {
                    cameraSide = eCameraSide.Right
                });
            }
            else
            {
                if (_fullScreenCameraSide == eCameraSide.Left)
                {
                    // We are in full screen mode and we want to switch media player while staying in full screen mode                    
                    mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqFullScreen, ControlType)
                    {
                        cameraSide = eCameraSide.Right
                    });
                }
                else
                {
                    // We are in the full screen mode on one of the media players and we want to go back to regular stereo player mode
                    mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqBackToWindow, ControlType));
                }
            }
        }

        private void ControlProperties_Click(object sender, RoutedEventArgs e)
        {            
            new NotImplementedException("MediaCOntrols.ControlProperties_Click To Do");
        }

        private void ControlEvent_Click(object sender, RoutedEventArgs e)
        {
            new NotImplementedException("MediaCOntrols.ControlEvent_Click To Do");
        }


        /// <summary>
        /// Pause the media if necessary (via a signal) and signal the MediaSteroController to save the frame
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlSaveFrame_Click(object sender, RoutedEventArgs e)
        {
            // Check if media is paused
            if (PlayOrPause == true)
                // Pause the media if necessary
                mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqPlayOrPause, ControlType) { playOrPause = false });

            // Signal the MediaSteroController to save the frame
            mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqSaveFrame, ControlType));
        }


        /// <summary>
        /// The user has selected mute/unmute signal the MediaSteroController
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlMuteUnmute_Click(object sender, RoutedEventArgs e)
        {
            mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqMutedOrUmuted, ControlType) { mute = !this.Mute});
        }


        /// <summary>
        /// The user has selected the casting to device button signal the MediaSteroController
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlCast_Click(object sender, RoutedEventArgs e)
        {
            mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqCasting, ControlType));
        }

        /// <summary>
        /// The user toggled the automatical opening of the Mag Window on or off
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlAutoMag_Click(object sender, RoutedEventArgs e)
        {
            // Initial entry
            if (appButtonAutoMagnifyOn == null && isAutoMagnify == true/*this should always be the case initially*/)
            {
                // Remember the original button color so a) we can restore it, b) we can make a greyer version
                appButtonAutoMagnifyOn = ControlAutoMagIcon.Foreground as Microsoft.UI.Xaml.Media.SolidColorBrush;

                // Rememmber the original tooltip. 
                appButtonAutoMagnifyTooltip = ToolTipService.GetToolTip(ControlAutoMag) as string;

                // Make a greyer version of the original button color
                Windows.UI.Color originalColor = appButtonAutoMagnifyOn!.Color;
                byte grey = (byte)((originalColor.R + originalColor.G + originalColor.B) / 3);
                Windows.UI.Color greyColor = Windows.UI.Color.FromArgb(originalColor.A, grey, grey, grey);
                appButtonAutoMagnifyOff = new Microsoft.UI.Xaml.Media.SolidColorBrush(greyColor);
            }

            isAutoMagnify = !isAutoMagnify;

            if (isAutoMagnify)
            {
                ControlAutoMagIcon.Foreground = appButtonAutoMagnifyOn;
                ToolTipService.SetToolTip(ControlAutoMag, appButtonAutoMagnifyTooltip + "\nSwitch off the auto magnifier window.");
            }
            else
            {
                ControlAutoMagIcon.Foreground = appButtonAutoMagnifyOff;
                ToolTipService.SetToolTip(ControlAutoMag, appButtonAutoMagnifyTooltip + "\nSwitch on the auto magnifier window.");
            }

            // Signal eMediaControlEvent.UserReqAutoMagOnOff with isAutoMagnify
            mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqAutoMagOnOff, ControlType)
            {
                isAutoMagnify = this.isAutoMagnify
            });
        }


        /// <summary>
        /// The MagZoom sub-menu is being opened. Put a check mark against the current zoom level
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlMagZoom_Opening(object sender, object e)
        {
            // Retrieve the parent MenuFlyout
            MenuFlyout? parentFlyout = sender as MenuFlyout;

            // Create a text string of the current speed of 2 decimal places to compare
            // with the speed of the menu items
            string compareZoomFactor = canvasZoomFactor.ToString() + "x";
            if (parentFlyout is not null)
            {
                foreach (var item in parentFlyout.Items)
                {
                    if (item is ToggleMenuFlyoutItem flyoutItem)
                        if (flyoutItem.Text == compareZoomFactor)
                            flyoutItem.IsChecked = true;
                        else
                            // Uncheck all other items
                            flyoutItem.IsChecked = false;
                }
            }
        }


        /// <summary>
        /// The user selected a new Mag Zoom level
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlMagZoom_Click(object sender, RoutedEventArgs e)
        {
            ToggleMenuFlyoutItem? selectedFlyoutItem = sender as ToggleMenuFlyoutItem;

            if (selectedFlyoutItem is not null)
            {
                // Access the MenuFlyout
                MenuFlyout? magZoomFlyout = ControlMagZoom.Flyout as MenuFlyout;

                if (magZoomFlyout != null)
                {
                    foreach (var item in magZoomFlyout.Items)
                    {
                        // Check if the item is a ToggleMenuFlyoutItem
                        if (item is ToggleMenuFlyoutItem flyoutItem && flyoutItem != selectedFlyoutItem)
                        {
                            // Uncheck all other items
                            flyoutItem.IsChecked = false;
                        }
                    }
                }

                // Check the selected item (if not already checked)
                selectedFlyoutItem.IsChecked = true;

                // Now figure which speed was clicked on
                // Convert the selected Flyout Item text to a float
                double _canvasZoomFactor = double.Parse(selectedFlyoutItem.Text.TrimEnd('x'));
                if (canvasZoomFactor != _canvasZoomFactor)
                {
                    canvasZoomFactor =  _canvasZoomFactor;

                    // Send the new speed to the MediaSteroController                    
                    mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqMagZoomSelect, ControlType)
                    { 
                        canvasZoomFactor = this.canvasZoomFactor
                    });
                }
            }
        }


        /// <summary>
        /// Display which layers are currently set on or off
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>

        private void ControlLayers_Opening(object sender, object e)
        {
#if !No_MagnifyAndMarkerDisplay
            // Retrieve the parent MenuFlyout
            MenuFlyout? parentFlyout = sender as MenuFlyout;

            // Create a text string of the current speed of 2 decimal places to compare
            // with the speed of the menu items
            string compareZoomFactor = canvasZoomFactor.ToString() + "x";
            if (parentFlyout is not null)
            {
                foreach (var item in parentFlyout.Items)
                {
                    if (item is ToggleMenuFlyoutItem flyoutItem)
                        if (flyoutItem == ControlLayers_FishPoints && (layerTypesDisplayed & LayerType.Events) != 0)
                            flyoutItem.IsChecked = true;
                    else if (flyoutItem == ControlLayers_SpeciesInfo && (layerTypesDisplayed & LayerType.EventsDetail) != 0)
                        flyoutItem.IsChecked = true;
                        else if (flyoutItem == ControlLayers_Epipolar && (layerTypesDisplayed & LayerType.Epipolar) != 0)
                            flyoutItem.IsChecked = true;
                        else
                            flyoutItem.IsChecked = false;
                }
            }
#endif
        }



        /// <summary>
        /// User requested a change to the layers displayed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>

        private void ControlLayers_Click(object sender, RoutedEventArgs e)
        {
#if !No_MagnifyAndMarkerDisplay
            ToggleMenuFlyoutItem? selectedFlyoutItem = sender as ToggleMenuFlyoutItem;

            if (selectedFlyoutItem is not null)
            {
                LayerType layerTypeNew = LayerType.None;

                // Access the MenuFlyout
                MenuFlyout? magZoomFlyout = ControlMagZoom.Flyout as MenuFlyout;

                if (magZoomFlyout != null)
                {
                    foreach (var item in magZoomFlyout.Items)
                    {
                        // Check if the item is a ToggleMenuFlyoutItem
                        if (item is ToggleMenuFlyoutItem flyoutItem)
                        {
                            if (flyoutItem == ControlLayers_FishPoints)
                            {
                                if (selectedFlyoutItem.IsChecked)
                                    layerTypeNew |= LayerType.Events;
                            }
                            else if (flyoutItem == ControlLayers_SpeciesInfo)
                            {
                                if (selectedFlyoutItem.IsChecked)
                                    layerTypeNew |= LayerType.EventsDetail;
                            }
                            else if (flyoutItem == ControlLayers_Epipolar)
                            {
                                if (selectedFlyoutItem.IsChecked)
                                    layerTypeNew |= LayerType.Epipolar;
                            }
                        }
                    }
                }

                // Now figure which speed was clicked on
                // Convert the selected Flyout Item text to a float
                double _canvasZoomFactor = double.Parse(selectedFlyoutItem.Text.TrimEnd('x'));
                if (layerTypesDisplayed != layerTypeNew)
                {
                    layerTypesDisplayed = layerTypeNew;

                    // Send the new speed to the MediaSteroController                    
                    mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqLayersDisplayed, ControlType)
                    {
                        layerTypesDisplayed = this.layerTypesDisplayed
                    });
                }
            }
#endif
        }


        /// <summary>
        /// Mark which Mag Window Size is currently selected
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlMagSize_Opening(object sender, object e)
        {
            // Retrieve the parent MenuFlyout
            MenuFlyout? parentFlyout = sender as MenuFlyout;

            if (parentFlyout is not null)
            {
                foreach (var item in parentFlyout.Items)
                {
                    if (item is ToggleMenuFlyoutItem flyoutItem)
                        if (flyoutItem.Text == magWindowSize)
                            flyoutItem.IsChecked = true;
                        else
                            // Uncheck all other items
                            flyoutItem.IsChecked = false;
                }
            }
        }


        /// <summary>
        /// User has selected a new Mag Window Size
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ControlMagSize_Click(object sender, RoutedEventArgs e)
        {
            ToggleMenuFlyoutItem? selectedFlyoutItem = sender as ToggleMenuFlyoutItem;

            if (selectedFlyoutItem is not null)
            {
                // Access the MenuFlyout
                MenuFlyout? magSizeFlyout = ControlMagSize.Flyout as MenuFlyout;

                if (magSizeFlyout != null)
                {
                    foreach (var item in magSizeFlyout.Items)
                    {
                        // Check if the item is a ToggleMenuFlyoutItem
                        if (item is ToggleMenuFlyoutItem flyoutItem && flyoutItem != selectedFlyoutItem)
                        {
                            // Uncheck all other items
                            flyoutItem.IsChecked = false;
                        }
                    }
                }

                // Check the selected item (if not already checked)
                selectedFlyoutItem.IsChecked = true;

                // Now figure which speed was clicked on
                // Convert the selected Flyout Item text to a float
                string magWindowSize = selectedFlyoutItem.Text;
                if (magWindowSize != this.magWindowSize)
                {
                    this.magWindowSize = magWindowSize;

                    // Send the new speed to the MediaSteroController
                    mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqMagWindowSizeSelect, ControlType) 
                    { 
                        magWindowSize = this.magWindowSize 
                    });
                }
            }
        }


        ///
        /// Mouse Wheel Function Public function, Event and private functions
        ///

        /// <summary>
        /// Called from the MouseWheel event normally in the main window
        /// </summary>
        /// <param name="ImageFrame"></param>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void MouseWheelEvent(Object sender, PointerRoutedEventArgs e)
        {
            int delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
            // Delta > 0: wheel moved away from the user; Delta < 0: wheel moved toward the user
            // Handle the event, e.g., by manually scrolling content or performing custom actions

            if (delta != 0)   // I have seen a e.Delta of 0 which creates a divide by zero error so we will ignore this
            {
                // Remember the lowest mouse wheel delta seen. This is used to calculate the number of frames to move
                if (Math.Abs(delta) < _lowestMouseWheelDeltaSeen)
                {
                    _lowestMouseWheelDeltaSeen = Math.Abs(delta);

                    Debug.WriteLine($"{ControlType} MouseWheel notch calculated at a delta of: {_lowestMouseWheelDeltaSeen}");
                }


                if (!_wheelEventTimer.IsEnabled)
                {
                    // Act immediately on the first event to feel responsive
                    _Grid_MouseWheelActOn(delta);

                    // Start the debounce timer
                    _wheelEventTimer.Start();
                }
                else
                {
                    // Accumulate delta values for subsequent events
                    _wheelAccumulatedDelta += delta;
                }
            }
        }


        /// <summary>
        /// Mouse wheel deboune timer event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void WheelEventTimer_Tick(object? sender, object e)
        {
            // Timer elapsed, act on the accumulated delta
            if (_wheelAccumulatedDelta != 0)
            {
                _Grid_MouseWheelActOn(_wheelAccumulatedDelta);
                _wheelAccumulatedDelta = 0;
            }

            // Stop the timer until the next wheel event
            _wheelEventTimer.Stop();
        }


        /// <summary>
        /// Used by the WheelEventTimer_Tick event to act on the accumulated mouse wheel delta
        /// </summary>
        /// <param name="ImageFrame"></param>
        /// <param name="delta"></param>
        /// <param name="CameraSide"></param>
        private void _Grid_MouseWheelActOn(int delta)
        {
            if (delta != 0)   // I have seen a e.Delta of 0 which creates a divide by zero error so we will ignore this
            {
                int frames = delta / _lowestMouseWheelDeltaSeen;

                Debug.WriteLine($"{ControlType} MouseWheel move {frames} frames");

                // If the mouse wheel delta is positive then move forward else move back
                // Signal eMediaControlEvent.UserReqFrameBackward
                mediaControlHandler?.Send(new MediaControlEventData(eMediaControlEvent.UserReqFrameMove, ControlType) 
                { 
                    framesDelta = frames 
                });
            }
        }


        ///
        /// PRIVATE METHODS
        /// 

        static List<float> speedList = new List<float> { 0.25f, 0.5f, 1.0f, 1.5f, 2.0f, 5.0f };
        private float CalcSpeed(float currentSpeed, bool TrueIncreaseFalseDecrease)
        {
            float ret;

            int index = speedList.IndexOf(currentSpeed);

            if (TrueIncreaseFalseDecrease)
            {
                if (index < speedList.Count - 1)
                    ret = speedList[index + 1];
                else
                    ret = currentSpeed;
            }
            else
            {
                if (index > 0)
                    ret = speedList[index - 1];
                else
                    ret = currentSpeed;
            }

            return ret;
        }

        /// <summary>
        /// Used by the TListener to change the state of the MediaControl to understand that the
        /// the media is synchronized. This message came from the MediaStereoController
        /// </summary>
        /// <param name="mediaSynchronized"></param>
        internal void SetMediaSynchronized(bool _mediaSynchronized)
        {
            mediaSynchronized = _mediaSynchronized;

            SetMediaControlsFullScreenButtons();
        }

        private void SetMediaControlsFullScreenButtons()
        {
            var fontIcon = ControlFullScreen.Icon as FontIcon;
            var fontIconAlt = ControlFullScreenAlternative.Icon as FontIcon;

            if (fontIcon is not null && fontIconAlt is not null)
            {
                if (_TrueYouAreFullFalseYouAreRestored)
                {
                    // One media player is enlarged so we need both FullScreen/BackToWindow buttons to be
                    // visible. One button will show the BackToWindow glyph and the other will show the
                    // FullScreen glyph
                    ControlFullScreen.Visibility = Visibility.Visible;
                    ControlFullScreenAlternative.Visibility = Visibility.Visible;

                    if (_fullScreenCameraSide is not null)
                    {
                        if (_fullScreenCameraSide == eCameraSide.Left)
                        {
                            fontIcon.Glyph = "\uE73F";  // Set the BackToWindow Icon so the user can restore the window
                            fontIconAlt.Glyph = "\uE740";  // Set the FullScreen Icon so the user can go full screen
                            ToolTipService.SetToolTip(ControlFullScreen, "Show both media players (Press Esc or L)");
                            ToolTipService.SetToolTip(ControlFullScreenAlternative, "Show only the right media player (Press R)");
                            AppBarButtonReplaceShortcut(ControlFullScreen, VirtualKeyModifiers.None, 
                                VirtualKey.Escape, 
                                VirtualKeyModifiers.None, VirtualKey.L);
                            AppBarButtonReplaceShortcut(ControlFullScreenAlternative, 
                                VirtualKeyModifiers.None, VirtualKey.R);
                        }
                        else if (_fullScreenCameraSide == eCameraSide.Right)
                        {
                            fontIcon.Glyph = "\uE740";  // Set the FullScreen Icon so the user can go full screen
                            fontIconAlt.Glyph = "\uE73F";  // Set the BackToWindow Icon so the user can restore the window
                            ToolTipService.SetToolTip(ControlFullScreen, "Show only the left media player (Press L)");
                            ToolTipService.SetToolTip(ControlFullScreenAlternative, "Show both media players (Press Esc or R)");
                            AppBarButtonReplaceShortcut(ControlFullScreen, VirtualKeyModifiers.None, VirtualKey.L);
                            AppBarButtonReplaceShortcut(ControlFullScreenAlternative, 
                                VirtualKeyModifiers.None, 
                                VirtualKey.Escape, VirtualKeyModifiers.None, VirtualKey.R);
                        }
                    }
                }
                else
                {
                    if (mediaSynchronized)
                    {
                        // We are in regular stereo view and the media is synchronized so we need both
                        // FullScreen/BackToWindow buttons to be visible. Both buttons will show the
                        // FullScreen glyph
                        ControlFullScreen.Visibility = Visibility.Visible;
                        ControlFullScreenAlternative.Visibility = Visibility.Visible;
                        fontIcon.Glyph = "\uE740";  // Set the FullScreen Icon so the user can go full screen
                        fontIconAlt.Glyph = "\uE740";  // Set the FullScreen Icon so the user can go full screen
                        ToolTipService.SetToolTip(ControlFullScreen, "Show only the left media player (Press L)");
                        ToolTipService.SetToolTip(ControlFullScreenAlternative, "Show only the right media player (Press R)");
                        AppBarButtonReplaceShortcut(ControlFullScreen, 
                            VirtualKeyModifiers.None, VirtualKey.L);
                        AppBarButtonReplaceShortcut(ControlFullScreenAlternative, 
                            VirtualKeyModifiers.None, VirtualKey.R);
                    }
                    else
                    {
                        // We are in regular stereo view and the media is not synchronized so we only
                        // need one FullScreen/BackToWindow button to be visible. The button will show the
                        // FullScreen glyph
                        ControlFullScreen.Visibility = Visibility.Visible;
                        ControlFullScreenAlternative.Visibility = Visibility.Collapsed;
                        fontIcon.Glyph = "\uE740";  // Set the FullScreen Icon so the user can go full screen
                        if (ControlType == eControlType.Primary)
                        {
                            ToolTipService.SetToolTip(ControlFullScreen, $"Show only the left media player (Press L)");
                            AppBarButtonReplaceShortcut(ControlFullScreen, VirtualKeyModifiers.None, VirtualKey.L);
                        }
                        else if (ControlType == eControlType.Secondary)
                        {
                            ToolTipService.SetToolTip(ControlFullScreen, $"Show only the right media player (Press R)");
                            AppBarButtonReplaceShortcut(ControlFullScreen, VirtualKeyModifiers.None, VirtualKey.R);
                        }   
                    }
                }
            }
        }


        /// <summary>
        /// Replaces any existing keyboard shortcut on the given AppBarButton with the new one
        /// </summary>
        /// <param name="appBarButton"></param>
        /// <param name="virtualKeyModifiers">Windows.System.VirtualKeyModifiers enum values</param>
        /// <param name="vituralKey">Windows.System.VirtualKey enum values</param>
        private void AppBarButtonReplaceShortcut(AppBarButton appBarButton, VirtualKeyModifiers virtualKeyModifiers, VirtualKey vituralKey)
        {
            // Remove all existing keyboard shortcuts on this button
            appBarButton.KeyboardAccelerators.Clear();

            // Add a new shortcut 
            var newAccelerator = new KeyboardAccelerator()
            {
                Key = vituralKey,
                Modifiers = virtualKeyModifiers
            };            
            appBarButton.KeyboardAccelerators.Add(newAccelerator);
        }
        /// <summary>
        /// Replaces any existing keyboard shortcut on the given AppBarButton with two new ones
        /// </summary>
        /// <param name="appBarButton"></param>
        /// <param name="virtualKeyModifiers">Windows.System.VirtualKeyModifiers enum values</param>
        /// <param name="vituralKey">Windows.System.VirtualKey enum values</param>
        private void AppBarButtonReplaceShortcut(AppBarButton appBarButton, VirtualKeyModifiers virtualKeyModifiers1, VirtualKey vituralKey1, VirtualKeyModifiers virtualKeyModifiers2, VirtualKey vituralKey2)
        {
            // Remove all existing keyboard shortcuts on this button
            appBarButton.KeyboardAccelerators.Clear();

            // Add first shortcut
            var newAccelerator1 = new KeyboardAccelerator()
            {
                Key = vituralKey1,
                Modifiers = virtualKeyModifiers1
            };            
            appBarButton.KeyboardAccelerators.Add(newAccelerator1);
            // Add first shortcut
            var newAccelerator2 = new KeyboardAccelerator()
            {
                Key = vituralKey2,
                Modifiers = virtualKeyModifiers2
            };
            appBarButton.KeyboardAccelerators.Add(newAccelerator2);
        }

    }



    /// <summary>
    /// Used to inform the MediaControl User Control of changes external to is that it may need to be aware of
    /// </summary>
    public class MediaControlHandlerData
    {
        public MediaControlHandlerData(eMediaControlAction action, SurveyorMediaControl.eControlType controlType)
        {             
            mediaControlAction = action;
            this.controlType = controlType;
        }

        public enum eMediaControlAction { FrameIndex, Duration, Position }
        public eMediaControlAction mediaControlAction;

        public readonly SurveyorMediaControl.eControlType controlType;

        // Used only for eMediaControlAction.FrameIndex to inform MediaControls of a change to the Frame Index (i.e. we have moved position in the media)
        public Int64 frameIndex;

        // Used only for eMediaControlAction.Duration to inform MediaControls of the natural diuration of the media
        public TimeSpan duration;

        // Used only for eMediaControlAction.Position to indicate what position of media with the playback
        public TimeSpan position;
    }

    /// <summary>
    /// Used by the MediaControl User Control to inform other components on state changes within MediaControl
    /// </summary>
    public class MediaControlEventData
    {
        public MediaControlEventData(eMediaControlEvent e, SurveyorMediaControl.eControlType controlType)
        {
            mediaControlEvent = e;
            this.controlType = controlType;
        }

        public enum eMediaControlEvent { 
            UserReqPlayOrPause, 
            UserReqFrameForward, 
            UserReqFrameBackward, 
            UserReqMoveStepBack, 
            UserReqMoveStepForward, 
            UserReqFrameJump, 
            UserReqFrameMove, 
            UserReqMutedOrUmuted, 
            UserReqBookmarked, 
            UserReqSaveFrame, 
            UserReqSpeedSelect, 
            UserReqFullScreen, 
            UserReqBackToWindow, 
            UserReqCasting,
            UserReqMagWindowSizeSelect,
            UserReqMagZoomSelect,
            UserReqAutoMagOnOff,
            UserReqLayersDisplayed
        }

        public readonly eMediaControlEvent mediaControlEvent;

        public readonly SurveyorMediaControl.eControlType controlType;

        // Used for UserReqPlayOrPause
        public bool playOrPause;

        // Used for UserReqFrameJump
        public TimeSpan? positionJump;

        // Used for UserReqFrameMove
        public int framesDelta;

        // Used for UserReqSpeedChange playbackSpeed change
        public float? speed;

        // Used for UserReqMagWindowSizeSelect mag window size change
        public string? magWindowSize;

        // Used for UserReqMagZoomSelect to chage the mag window zoom factor
        public double? canvasZoomFactor;

        // Used for UserReqAutoMagOnOff to turn on or off the automatic magnify
        public bool? isAutoMagnify;

        // User for UserReqLayersDisplayed to change the layers displayed
#if !No_MagnifyAndMarkerDisplay
        public LayerType? layerTypesDisplayed;
#endif

        // Used for UserReqMutedOrUmuted true == mute
        public bool? mute;

        // Used for UserReqFullScreen
        public eCameraSide cameraSide;
    }


    public class MediaControlHandler : TListener
    {
        private readonly SurveyorMediaControl _mediaControl;

        public MediaControlHandler(IMediator mediator, MainWindow mainWindow, SurveyorMediaControl mediaControl) : base(mediator, mainWindow)
        {
            _mediaControl = mediaControl;
        }

        public override void Receive(TListener listenerFrom, object message)
        {
            if (message is MediaPlayerEventData mpData)
            {
                // Proceed if the message for the MediaPlayer is for this MediaControl instance
                if (_mediaControl.ProcessIfForMe(mpData))
                {
                    switch (mpData.mediaPlayerEvent)
                    {
                        case MediaPlayerEventData.eMediaPlayerEvent.Opened:
                            SafeUICall(() => _mediaControl.EnableControls(true));
                            break;

                        case MediaPlayerEventData.eMediaPlayerEvent.Closed:
                            SafeUICall(() => _mediaControl.EnableControls(false));
                            break;

                        case MediaPlayerEventData.eMediaPlayerEvent.Playing:
                            _mediaControl.PlayOrPause = true;
                            SafeUICall(() => _mediaControl.PlayOrPausedSetIcon());
                            break;

                        case MediaPlayerEventData.eMediaPlayerEvent.Paused:
                            _mediaControl.PlayOrPause = false;
                            SafeUICall(() => _mediaControl.PlayOrPausedSetIcon());
                            break;

                        case MediaPlayerEventData.eMediaPlayerEvent.EndOfMedia:
                            break;

                        case MediaPlayerEventData.eMediaPlayerEvent.Duration:
                            SafeUICall(() => _mediaControl.UpdateDuration(mpData));
                            break;

                        case MediaPlayerEventData.eMediaPlayerEvent.Position:
                            SafeUICall(() => _mediaControl.UpdatePositionAndFrame(mpData));
                            break;

                        case MediaPlayerEventData.eMediaPlayerEvent.Muted:
                            _mediaControl.Mute = true;
                            SafeUICall(() => _mediaControl.MuteUmmuteSetIcon());
                            break;

                        case MediaPlayerEventData.eMediaPlayerEvent.Unmuted:
                            _mediaControl.Mute = false;
                            SafeUICall(() => _mediaControl.MuteUmmuteSetIcon());
                            break;
                    }
                }
            }
            else if (message is MediaStereoControllerEventData mscData)
            {
                if (_mediaControl.ProcessIfWeArePrimary())
                {
                    switch (mscData.mediaStereoControllerEvent)
                    {
                        case eMediaStereoControllerEvent.MediaSynchronized:
                            _mediaControl.SetMediaSynchronized(true);
                            break;

                        case eMediaStereoControllerEvent.MediaUnsynchronized:                            
                            _mediaControl.SetMediaSynchronized(false);
                            break;

                    }
                }
            }
        }

    }
}
