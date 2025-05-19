// SurveyorMediaPlayer  Media Player User Control
// This is a user control that is used to play media files and view frame by frame
// 
// Useful resource for MediaPlayerElement which is the XAML wrapper for MediaPlayer
// https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.controls.mediaplayerelement?view=winrt-22621
// Useful resource for Windows MediaPLayer, MediaTimelineController etc
// https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/play-audio-and-video-with-mediaplayer
// Usewful example of video syncronisation see Scenario2 where two videos are syncronised with a 5.1 sec offset
// https://github.com/microsoft/Windows-universal-samples/tree/main/Samples/VideoPlaybackSynchronization
//
// Version 1.1
// Needs the Microsoft.Grpahics.Win2D NuGet package
// Version 1.2 25 Feb 2025
// Moved to using the inbuilt SaveAsync in CanvasBitmap to save the frame to a file
// Moved all the control of the frame buffer into the VideoFrameManager class (i.e. no direct access to the frame buffer)
// Version 1.3  09 Mar 2025
// Refractored based on ExampleMediaTimelineController
// Moved the setting of 'mode' out of PlaybackSession_PlaybackStateChanged and into where the 
// Play/Pause is requested
// Verion 1.4 13 Mar 2025
// Changed approach to:
// On pause request, pause timeline controller, wait for pause, grab frame directly from media player
// On frame forward/backward request, because the is no pause state change to wait on we use the VideoFrameAvailable event
// approach to grab the frame

using CommunityToolkit.WinUI;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Surveyor.Helper;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Casting;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using Windows.System.Display;

namespace Surveyor.User_Controls
{
    public sealed partial class SurveyorMediaPlayer : UserControl
    {
        // Reporter
        private Reporter? report = null;

        // Copy of MainWindow
        private MainWindow? mainWindow;

        // Copy of the mediator 
        private SurveyorMediator? mediator;

        // Declare the mediator handler for MediaPlayer
        private MediaPlayerHandler? mediaPlayerHandler;

        // Which camera side 'L' or 'R'
        public enum eCameraSide { None, Left, Right };
        public eCameraSide CameraSide { get; set; } = eCameraSide.None;

        
        // Media has been opened
        private bool mediaOpen = false;
        private string mediaUri = "";
        private bool isClosing = false;

        // This can be used to prevent the screen from turning off automatically
        // USeful to stop the screen switching off if media is playing
        private DisplayRequest? appDisplayRequest = null;

        // Used to indicate if the control is in Player mode (i.e. using MediaPlayerElement) or in frame mode (i.e. reading and displaying video frame via FFmpeg)
        public enum Mode { modeNone, modePlayer, modeFrame };
        private Mode mode = Mode.modeNone;
        
        private TimeSpan positionPausedMode = TimeSpan.Zero;

        // Media duration
        private TimeSpan naturalDuration = TimeSpan.Zero;

        // Frame width and height
        private uint frameWidth = 0;   
        private uint frameHeight = 0;  

        // Calculated frame rate
        private double frameRate = -1;
        private TimeSpan frameRateTimeSpan = TimeSpan.Zero;
        private int displayToDecimalPlaces = 2;     // If we start using frame rate of 120fps then we will need to increase this to 3dp

        // Used in the redenering of a frame to the screen
        private readonly VideoFrameManager vidFrameMgr = new();

        // VideoFrameAvailable thread access lock
        private static readonly object lockVideoFrameAvailable = new();

        public SurveyorMediaPlayer()
        {
            this.InitializeComponent();            
        }


        /// <summary>
        /// Diags dump of class information
        /// </summary>
        public void DumpAllProperties()
        {
            DumpClassPropertiesHelper.DumpAllProperties(this, /*ignore*/"report,mainWindow,mediator,mediaPlayerHandler,<CameraSide>k__BackingField,appDisplayRequest,vidFrameMgr,_contentLoaded");
            DumpClassPropertiesHelper.DumpAllProperties(vidFrameMgr, /*ignore*/"<cameraSide>k__BackingField,frameServerDest,canvasImageSource,inputBitmap,taskOneMoreFrameCompletion,_frameLock,<IsSetup>k__BackingField");
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
            mainWindow = _mainWindow;

            mediaPlayerHandler = new MediaPlayerHandler(mediator, this, mainWindow);

            return mediaPlayerHandler;
        }


        /// <summary>
        /// Returned the ImageFrame control
        /// The Magnification control is used to zoom in on the image
        /// </summary>
        public Image GetImageFrame()
        {
            return ImageFrame;
        }


        /// <summary>
        /// Opens the indicated media file in the Media Player
        /// </summary>
        /// <param name="mediaFileSpec"></param>
        internal async Task Open(string mediaFileSpec)
        {
            WinUIGuards.CheckIsUIThread();

            Debug.WriteLine($"{CameraSide}: Open: Enter  (UIThread={DispatcherQueue.HasThreadAccess})");

            // Ensure we are not in a "closing" state when reopening media
            isClosing = false;

            if (!IsOpen() && mediaFileSpec is not null)
            {
                try
                {
                    // Check the file exists either locally of via a URL
                    if (await FileExistsAsync(mediaFileSpec))
                    {
                        // Display the buffering progress ring
                        ProgressRing_Buffering.IsActive = true;

                        // Reset
                        mediaOpen = false;
                        mode = Mode.modeNone;
                        //???_frameIndexCurrent = 0;
                        naturalDuration = TimeSpan.Zero;
                        frameWidth = 0;
                        frameHeight = 0;
                        frameRate = -1;
                        frameRateTimeSpan = TimeSpan.Zero;
                        positionPausedMode = TimeSpan.Zero;

                        // Create a corresponding MediaPlayer for the MediaPlayerElement
                        MediaPlayerElement.SetMediaPlayer(new MediaPlayer());

                        // Event subscriptions MediaPlayer 
                        MediaPlayer mp = MediaPlayerElement.MediaPlayer;
                        mp.MediaEnded += MediaPlayer_MediaEnded;
                        mp.MediaFailed += MediaPlayer_MediaFailed;
                        mp.MediaOpened += MediaPlayer_MediaOpened;
                        mp.MediaPlayerRateChanged += MediaPlayer_MediaPlayerRateChanged;
                        mp.SourceChanged += MediaPlayer_SourceChanged;
                        mp.VideoFrameAvailable += MediaPlayer_VideoFrameAvailable;

                        // Remember the media Uri
                        mediaUri = mediaFileSpec;

                        // Set the media file spec
                        MediaSource mediaSource = MediaSource.CreateFromUri(new System.Uri(mediaFileSpec));
                        MediaPlaybackItem playbackItem = new(mediaSource);
                        MediaPlayerElement.Source = playbackItem;

                        // Event PlaybackSession subscriptions 
                        MediaPlaybackSession playbackSession = MediaPlayerElement.MediaPlayer.PlaybackSession;
                        playbackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;
                        playbackSession.BufferingStarted += PlaybackSession_BufferingStarted;
                        playbackSession.BufferingEnded += PlaybackSession_BufferingEnded;
                        playbackSession.NaturalDurationChanged += PlaybackSession_NaturalDurationChanged;
                        playbackSession.NaturalVideoSizeChanged += PlaybackSession_NaturalVideoSizeChanged;
                        //playbackSession.PlaybackRateChanged += PlaybackSession_PlaybackRateChanged;  // Incase we need later
                        //playbackSession.PlayedRangesChanged += PlaybackSession_PlayedRangesChanged;  // Incase we need later
                        playbackSession.PositionChanged += PlaybackSession_PositionChanged;
                        playbackSession.SeekableRangesChanged += PlaybackSession_SeekableRangesChanged;
                        playbackSession.SeekCompleted += PlaybackSession_SeekCompleted;
                        playbackSession.SupportedPlaybackRatesChanged += PlaybackSession_SupportedPlaybackRatesChanged;
                    }
                    else
                    {
                        // DON'T await this method. I don't understand why but it causes the
                        // dialog to be non-modal
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                        ShowOpenFileNotFoundDialog(mediaFileSpec);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                        Debug.WriteLine($"{CameraSide}: Error SurveyorMediaPlayer.Open  Can't open media: {mediaFileSpec} file not found");
                    }
                }
                catch (Exception ex)
                {
                    // DON'T await this method. I don't understand why but it causes the
                    // dialog to be non-modal
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    ShowOpenFailedDialog(mediaFileSpec, ex);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    Debug.WriteLine($"{CameraSide}: Error SurveyorMediaPlayer.Open  Can't open media: {mediaFileSpec}, {ex.Message}");
                }
            }
            else
                MediaPlayerElement.Source = null;

        }


        /// <summary>
        /// Returns true if the media is open
        /// </summary>
        /// <returns></returns>
        internal bool IsOpen()
        {
            return mediaOpen;
        }


        /// <summary>
        /// Unscribe from the media player events and close the media and set the media open flag to false
        /// </summary>
        internal async Task Close()
        {
            WinUIGuards.CheckIsUIThread();

            // Check the player is actually open
            if (IsOpen())
            {
                try
                {
                    ProgressRing_Buffering.IsActive = true;

                    // Indicate closing in progress
                    isClosing = true;

                    // First detach the TimelineController if necessary
                    // This is so we can control the MediaPlayer directly
                    MediaPlayer? mp = null;
                    try
                    {
                        mp = MediaPlayerElement.MediaPlayer;
                        if (mp.TimelineController is not null)
                            mp.TimelineController = null;
                    }
                    catch // Seen this fail
                    { }

                    // Pause just in case the media is playing
                    if (MediaPlayerElement.MediaPlayer.CurrentState != MediaPlayerState.Paused)
                    {
                        await Pause();
                    }

                    // Event PlaybackSession cancel subscriptions
                    MediaPlaybackSession? playbackSession = null;
                    try
                    {
                        playbackSession = MediaPlayerElement.MediaPlayer.PlaybackSession;
                        playbackSession.PlaybackStateChanged -= PlaybackSession_PlaybackStateChanged;
                        playbackSession.BufferingStarted -= PlaybackSession_BufferingStarted;
                        playbackSession.BufferingEnded -= PlaybackSession_BufferingEnded;
                        playbackSession.NaturalDurationChanged -= PlaybackSession_NaturalDurationChanged;
                        playbackSession.NaturalVideoSizeChanged -= PlaybackSession_NaturalVideoSizeChanged;
                        playbackSession.PositionChanged -= PlaybackSession_PositionChanged;
                        playbackSession.SeekableRangesChanged -= PlaybackSession_SeekableRangesChanged;
                        playbackSession.SeekCompleted -= PlaybackSession_SeekCompleted;
                        playbackSession.SupportedPlaybackRatesChanged -= PlaybackSession_SupportedPlaybackRatesChanged;
                    }
                    catch
                    { }


                    // Event MediaPlayer cancel subscriptions
                    if (mp is not null)
                    {
                        mp.MediaEnded -= MediaPlayer_MediaEnded;
                        mp.MediaFailed -= MediaPlayer_MediaFailed;
                        mp.MediaOpened -= MediaPlayer_MediaOpened;
                        mp.MediaPlayerRateChanged -= MediaPlayer_MediaPlayerRateChanged;
                        mp.SourceChanged -= MediaPlayer_SourceChanged;
                        mp.VideoFrameAvailable -= MediaPlayer_VideoFrameAvailable;

                        MediaPlayerElement.SetMediaPlayer(null);
                        await Task.Delay(300);
                        mp.Dispose();
                    }

                    // Hide the media player and the frame image user control
                    MediaPlayerElement.Visibility = Visibility.Collapsed;
                    ImageFrame.Visibility = Visibility.Collapsed;
                    ImageFrame.Source = null;
                    await Task.Delay(500);

                    // Release the resources used to render the video frame
                    vidFrameMgr.Release();

                    // Reset the variables
                    mediaOpen = false;
                    mediaUri = "";
                    mode = Mode.modeNone;
                    naturalDuration = TimeSpan.Zero;
                    frameWidth = 0;
                    frameHeight = 0;
                    frameRate = -1;
                    frameRateTimeSpan = TimeSpan.Zero;
                    positionPausedMode = TimeSpan.Zero;

                    // Signal the media close event via mediator
                    mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.Closed, CameraSide, mode));
                    Debug.WriteLine($"{CameraSide}: Info SurveyorMediaPlayer.Close");
                }
                finally
                {
                    ProgressRing_Buffering.IsActive = false;
                }
            }
        }


        /// <summary>
        /// Used to sync or unsync media players. Called from the MediaStereoController
        /// </summary>
        /// <param name="mediaTimelineController">Either pass a MediaTimelineController instance or null to disable</param>
        internal void SetTimelineController(MediaTimelineController? mediaTimelineController, TimeSpan offset)
        {
            WinUIGuards.CheckIsUIThread();

            // In a TimelineController scenario, it is important to control the MediaPlayer via a single mechanism.
            MediaPlayerElement.MediaPlayer.CommandManager.IsEnabled = false;

            // Set the offset
            MediaPlayerElement.MediaPlayer.TimelineControllerPositionOffset = offset;

            // Set the TimelineController property to link the media players
            MediaPlayerElement.MediaPlayer.TimelineController = mediaTimelineController;
        }


        /// <summary>
        /// Test of if the MediaTimelineController is exchanged and the media is synchronized
        /// </summary>
        /// <returns>true if synchonized</returns>
        internal bool IsMediaSynchronized()
        {
            return MediaPlayerElement.MediaPlayer.TimelineController is not null ? true : false;
        }

        /// <summary>
        /// Getter/Setter for the Media position 
        /// </summary>
        internal TimeSpan? Position         
        { 
            get 
            {
                WinUIGuards.CheckIsUIThread();

                TimeSpan? ret = null;
                if (IsOpen())
                    if (MediaPlayerElement.MediaPlayer is not null && MediaPlayerElement.MediaPlayer.PlaybackSession is not null)
                        ret = MediaPlayerElement.MediaPlayer.PlaybackSession.Position;

                return ret;
            } 
            set 
            {
                WinUIGuards.CheckIsUIThread();

                if (value is not null && IsOpen())
                    if (MediaPlayerElement.MediaPlayer is not null && MediaPlayerElement.MediaPlayer.PlaybackSession is not null)
                        MediaPlayerElement.MediaPlayer.PlaybackSession.Position = (TimeSpan)value; 
            }
        }

        /// <summary>
        /// Returns the duration of the media (total length). 
        /// null if not ready 
        /// </summary>
        internal TimeSpan? NaturalDuration
        {
            get
            {
                WinUIGuards.CheckIsUIThread();

                TimeSpan? ret = null;
                if (IsOpen())
                    if (MediaPlayerElement.MediaPlayer is not null)
                        ret = MediaPlayerElement.MediaPlayer.NaturalDuration;

                return ret;
            }
        }


        /// <summary>
        /// Return the offset this media player has against the any other media player join via a media timeline controller
        /// </summary>
        internal TimeSpan? TimelineControllerPositionOffset
        {
            get
            {
                WinUIGuards.CheckIsUIThread();

                TimeSpan? ret = null;
                if (IsOpen())
                {
                    if (MediaPlayerElement.MediaPlayer is not null && MediaPlayerElement.MediaPlayer.PlaybackSession is not null)
                        ret = MediaPlayerElement.MediaPlayer.TimelineControllerPositionOffset;
                }
                return ret;
            }

        }

        /// <summary>
        /// Get the current media frame width. Returns -1 if not set
        /// </summary>
        internal int FrameWidth
        {
            get
            {
                return (int)frameWidth;
            }
        }


        /// <summary>
        /// Get the current media frame height. Returns -1 if not set
        /// </summary>
        internal int FrameHeight
        {
            get
            {
                return (int)frameHeight;
            }
        }


        /// <summary>
        /// Getter/Setter for the Media frame duration
        /// </summary>
        internal TimeSpan TimePerFrame
        {
            get
            {
                return frameRateTimeSpan;
            }
        }


        /// <summary>
        /// Check if the media is playing
        /// </summary>
        /// <returns></returns>
        internal bool IsPlaying()
        {
            WinUIGuards.CheckIsUIThread();

            if (IsOpen())
            {
                return MediaPlayerElement.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
            }
            else
                return false;
        }

        /// <summary>
        /// Play the media player
        /// </summary>
        internal void Play()
        {
            WinUIGuards.CheckIsUIThread();

            if (IsOpen())
            {
                // Can only use this function if the media is not synchronized
                if (!IsMediaSynchronized())
                {
                    // Let MediaPlayerElement render the video frame
                    MediaPlayerElement.MediaPlayer.IsVideoFrameServerEnabled = false;

                    // Hide the ImageFrame control
                    ImageFrame.Visibility = Visibility.Collapsed;
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide} Play: Hide the ImageFrame");

                    // Play the media
                    mode = Mode.modePlayer;
                    MediaPlayerElement.MediaPlayer.Play();
                }
                else
                {
                    // Illegal to use this function if the media is synchronized
                    throw new InvalidOperationException("Can't use MediaPlayer.Play() when media is synchronized");
                }
            }
        }


        /// <summary>
        /// Called by MediaTimelineController to set the mode to Player
        /// The media must be synchronized for this to be a valid request
        /// </summary>
        internal void SetPlayMode()
        {
            WinUIGuards.CheckIsUIThread();

            if (IsOpen())
            {
                // Can only use this function if the media is not synchronized
                if (IsMediaSynchronized())
                {
                    // Flag pause media
                    // MediaTimelineController is used to play media and call this method
                    // just to set the mode in the MediaPlayer UserControl to play
                    mode = Mode.modePlayer;

                    // Hide the ImageFrame control
                    ImageFrame.Visibility = Visibility.Collapsed;
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide} SetPlayMode: Hide the ImageFrame");
                }
                else
                {
                    // Illegal to use this function if the media is not synchronized
                    throw new InvalidOperationException("Can't use MediaPlayer.SetPlayMode() when media is not synchronized");
                }
            }
        }


        /// <summary>
        /// Pause the media player
        /// </summary>
        internal async Task Pause()
        {
            WinUIGuards.CheckIsUIThread();

            if (IsOpen() && mode == Mode.modePlayer)
            {
                // Can only use this function if the media is not synchronized
                if (!IsMediaSynchronized())
                {
                    // Start the frame server so we can render the frames
                    FrameServerEnable(true);

                    // Wait for at least one frame so frame is display on the image frame Control
                    // Note the VideoFrameAvailable event shows the image frame control
                    if (vidFrameMgr.RequestOneMoreFrame() == true)
                    {
                        await vidFrameMgr.WaitOneMoreFrame();
                    }

                    // Pause media
                    mode = Mode.modeFrame;
                    MediaPlayerElement.MediaPlayer.Pause();

                    // Settle
                    await Task.Delay(50);

                    //???// Move back one frame and forward one frame. This is a workaround to get the frame
                    //// server to sync with the frame the player. Otherwise the the server frame is typically
                    //// behind the player frame. However sometimes the displayed frame in
                    //// the player sometimes behind the MediaPlaybackSession.Position.  In this case the
                    //// server will sync to that forward position. This is the best we can do currently.
                    //MediaPlayerElement.MediaPlayer.StepBackwardOneFrame();
                    //???MediaPlayerElement.MediaPlayer.StepForwardOneFrame();

                }
                else
                {
                    // Illegal to use this function if the media is synchronized
                    throw new InvalidOperationException("Can't use MediaPlayer.Pause() when media is synchronized");
                }
            }
        }


        /// <summary>
        /// Called by MediaTimelineController to set the mode to Pause
        /// The media must be synchronized for this to be a valid request
        /// </summary>
        internal void SetPauseMode()
        {
            if (IsOpen())
            {
                // Can only use this function if the media is not synchronized
                if (IsMediaSynchronized())
                {
                    // Flag pause media
                    // MediaTimelineController is used to pause media and call this method
                    // just to set the mode inside the MediaPLayer UserControl to Paused
                    mode = Mode.modeFrame;
                }
                else
                {
                    // Illegal to use this function if the media is not synchronized
                    throw new InvalidOperationException("Can't use MediaPlayer.SetPauseMode() when media is not synchronized");
                }
            }
        }


        /// <summary>
        /// Move the media forward(positive) or back(negative) by the timespan duration
        /// The function will move both players if they are locked together. If they are not locked together
        /// it will use cameraSide to determine which player to move
        /// </summary>        
        /// <param name="timeSpan"></param>
        internal async Task FrameMove(TimeSpan deltaPosition)
        {           
            if (IsOpen())
            {
                // Can only use this function if the media is not synchronized
                if (!IsMediaSynchronized())
                {
                    if (mode == Mode.modePlayer)
                        await Pause();

                    // Enable Frame Server
                    FrameServerEnable(true);

                    // Calculate the new position
                    TimeSpan position = positionPausedMode + deltaPosition;

                    // Check move is in bounds
                    if (position < TimeSpan.Zero)
                        position = TimeSpan.Zero;
                    else if (position > naturalDuration)
                        position = naturalDuration;

                    // Move to the relative position
                    positionPausedMode = position;
                    Position = positionPausedMode;

                }
                else
                    // Illegal to use this function if the media is synchronized
                    throw new InvalidOperationException("Can't use MediaPlayer.FrameMove() when media is synchronized");
            }
        }

        /// <summary>
        /// Move the media forward(positive) or back(negative) by the number of frames
        /// The function will move both players if they are locked together. If they are not locked together
        /// it will use cameraSide to determine which player to move
        /// </summary>
        /// <param name="frames">negative move back, positive move forward</param>
        internal async Task FrameMove(int frames)
        {
            WinUIGuards.CheckIsUIThread();

            if (IsOpen())
            {
                // Use the media player frame rate to calculate the timeSpan for the move
                TimeSpan deltaPosition = (TimeSpan)TimePerFrame * frames;

                await FrameMove(deltaPosition);
            }
        }
        

        /// <summary>
        /// Move to the absolute position in the media
        /// </summary>
        /// <param name="timeSpan"></param>
        internal async void FrameJump(TimeSpan position)
        {
            WinUIGuards.CheckIsUIThread();

            if (IsOpen())
            {
                // Can only use this function if the media is not synchronized
                if (!IsMediaSynchronized())
                {
                    if (mode == Mode.modePlayer)
                        //???MediaPlayerElement.MediaPlayer.Pause();  // Was using this but maybe need to use local Pause() instead
                        await Pause();

                    // Check move is in bounds
                    if (position < TimeSpan.Zero)
                        position = TimeSpan.Zero;
                    else if (position > naturalDuration)
                        position = naturalDuration;

                    // Move to the absolute position
                    positionPausedMode = position;
                    Position = positionPausedMode;
                }
                else
                    // Illegal to use this function if the media is synchronized
                    throw new InvalidOperationException("Can't use MediaPlayer.FrameJump() when media is synchronized");
            }
        }

        /// <summary>
        /// Used by the stereo controller to enable and disable the frame server
        /// </summary>
        /// <param name="enabled"></param>
        internal void FrameServerEnable(bool enabled)
        {
            WinUIGuards.CheckIsUIThread();

            MediaPlayerElement.MediaPlayer.IsVideoFrameServerEnabled = enabled;

            if (!enabled)
            {
                ImageFrame.Visibility = Visibility.Collapsed;
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide} FrameServerEnable: Hide the ImageFrame");
            }
        }


        /// <summary>
        /// Setup for a request for at least one frame to be set to the VideoFrameAvailable
        /// event
        /// </summary>
        /// <returns></returns>
        internal bool RequestOneMoreFrame()
        {
            return vidFrameMgr.RequestOneMoreFrame();
        }


        /// <summary>
        /// Wait until at least one more frame has been received by the VideoFrameAvailable
        /// event
        /// </summary>
        /// <returns></returns>
        internal async Task<bool> WaitOneMoreFrame()
        {
            return await vidFrameMgr.WaitOneMoreFrame();
        }


        /// <summary>
        /// Mute the sound
        /// </summary>
        internal void Mute()
        {
            WinUIGuards.CheckIsUIThread();

            // Mute this media player
            MediaPlayerElement.MediaPlayer.IsMuted = true;

            // Signal the Mute was sucessful
            mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.Muted, CameraSide, mode));
            Debug.WriteLine($"{CameraSide}: Info SureyorMediaPlayer.Mute  Muted");
        }


        /// <summary>
        /// Unmute the sound
        /// </summary>
        internal void Unmute()
        {
            WinUIGuards.CheckIsUIThread();

            // Unmute this media player
            MediaPlayerElement.MediaPlayer.IsMuted = false;

            // Signal the Unmuted was sucessful
            mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.Unmuted, CameraSide, mode));
            Debug.WriteLine($"{CameraSide}: Info SureyorMediaPlayer.Mute  Unmuted");
        }


        /// <summary>
        /// Set the media player playback rate
        /// </summary>
        /// <param name="speed"></param>
        internal void SetSpeed(float speed)
        {
            WinUIGuards.CheckIsUIThread();

            MediaPlayerElement.MediaPlayer.PlaybackRate = speed;
            Debug.WriteLine($"{CameraSide}: Info SureyorMediaPlayer.SetSpeed  Playback rate set to {speed:F2}");
        }


        /// <summary>
        /// Used to calculate the frame index from the current position (TimeSpan) using the 
        /// frame rate.  The private variable '_frameRate' is used for the calculation which 
        /// is set in the function 'GetAndStoreTheCurrentFrameRate'
        /// </summary>
        /// <param name="ts"></param>
        /// <returns>-1 if can't calculate (normal because _frameRate is not yet set</returns>
        internal Int64 GetFrameIndexFromPosition(System.TimeSpan ts)
        {
            Int64 frameIndex = -1;

            if (frameRate != -1)
            {
                double frameIndexDouble = ts.Ticks * frameRate / TimeSpan.TicksPerSecond;
                frameIndex = (long)Math.Round(frameIndexDouble, MidpointRounding.AwayFromZero);
            }
            else
                Debug.WriteLine($"{CameraSide}: Error GetFrameIndexFromPosition: Can't calculate frame index, _frameRate == -1");

            return frameIndex;
        }


        /// <summary>
        /// Used to calculate the frame index from the current position (TimeSpan) using the
        /// passed frame rate
        /// </summary>
        /// <param name="ts"></param>
        /// <param name="frameRate"></param>
        /// <returns></returns>
        internal static Int64 GetFrameIndexFromPosition(System.TimeSpan ts, double frameRate)
        {
            Int64 frameIndex = -1;

            if (frameRate > 0)
            {
                double frameIndexDouble = ts.Ticks * frameRate / TimeSpan.TicksPerSecond;
                frameIndex = (long)Math.Round(frameIndexDouble, MidpointRounding.AwayFromZero);
            }
            else
                Debug.WriteLine($"Error GetFrameIndexFromPosition(static version): Can't calculate frame index, frameRate <= 0");

            return frameIndex;
        }

        /// <summary>
        /// Used to cast the media to a casting device
        /// </summary>
        internal void StartCasting()
        {
            WinUIGuards.CheckIsUIThread();

            // Check if casting is supported
            CastingDevicePicker castingPicker = new CastingDevicePicker();
            castingPicker.Filter.SupportsVideo = true; // Assuming you're casting video

            // Handle the DeviceSelected event to cast to the selected device
            castingPicker.CastingDeviceSelected += async (sender, args) =>
            {
                CastingConnection connection = args.SelectedCastingDevice.CreateCastingConnection();

                connection.ErrorOccurred += (s, e) =>
                {
                    // Handle errors (e.g., display a message to the user)
                };

                // Get the casting source from your MediaPlayerElement
                CastingSource? source = MediaPlayerElement.MediaPlayer.GetAsCastingSource();

                // Start casting
                CastingConnectionErrorStatus status = await connection.RequestStartCastingAsync(source);

                if (status == CastingConnectionErrorStatus.Succeeded)
                {
                    // Casting started successfully
                }
                else
                {
                    // Handle casting failure
                }
            };

            // Show the casting picker to the user
            // You need to provide a Rect that represents the area on the screen from which the picker will be shown
            // For simplicity, you might want to use the bounding rectangle of a button that initiates casting
            castingPicker.Show(new Windows.Foundation.Rect(0, 0, 100, 100), Windows.UI.Popups.Placement.Default); // Adjust the Rect as needed
        }


        /// <summary>
        /// Save the current frame to a file in the 'MediaFrameFolder'
        /// This method uses the last frame saved _vfar instance(VideoFrameAvailableResources)
        /// to save the frame to a file.
        /// Note we pass the timeStamp (instead of just using the class Position property) to
        /// allow either the media timeline controller position or the media player position to
        /// be used as the time stamp in the file name
        /// </summary>
        /// <param name="framesPath">Save path</param>
        /// <param name="timeStamp">Time stamp used in the file name</param>
        /// <param name="syncdPair">Is this part of a stereo pair?</param>
        /// <returns></returns>
        internal async Task SaveCurrentFrame(string framesPath, TimeSpan timeStamp, bool syncdPair)
        {
            // Check if the players if loaded and paused
            if (IsOpen() && MediaPlayerElement.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Paused)
            {
                // Create the file name using the media name and the current timespan offset
                string formattedTime = "0000" + TimePositionHelper.Format((TimeSpan)timeStamp, 2);
                string paired = syncdPair ? "_Pair" : "";
                string fileName = Path.GetFileNameWithoutExtension(mediaUri) + paired + $"_{formattedTime.Substring(Math.Max(0, formattedTime.Length - 12))}.png";
                string fileSpec = Path.Combine(framesPath, fileName);

                // Save the frame to .png:
                if (await vidFrameMgr.SaveFrame(fileSpec, CanvasBitmapFileFormat.Png))
                {
                    report?.Info(CameraSide.ToString(), $"Frame saved to: {fileSpec}");                        
                }
                else
                {
                    report?.Error(CameraSide.ToString(), $"SurveyorMediaPlayer.SaveCurrentFrame  Failed to saved to: {fileSpec}");
                }
            }
            else
            {
                report?.Error(CameraSide.ToString(), "SurveyorMediaPlayer.SaveCurrentFrame  Media not open or not in frame mode");
            }
        }


        /// <summary>
        /// Used by MediaStereoController to copy the current paused frame to the the ImageFrame
        /// </summary>
        internal void GrabAndDisplayFrame()
        {
            // ** Called from UI Thread**
            WinUIGuards.CheckIsUIThread();
            
            //???Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Info GrabAndDisplayFrame Enter");


            //  Check if the resources are setup
            if (!vidFrameMgr.IsSetup)
            {
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Error GrabAndDisplayFrame Failed to setup resources");
                return;
            }

            // Copy the frame from the media player to the inputBitmap
            if (vidFrameMgr.GetNextMediaPlayFrame(MediaPlayerElement.MediaPlayer) == false)
            {
                // Allow time to settle
                Task.Delay(50);

                // Retry
                if (vidFrameMgr.GetNextMediaPlayFrame(MediaPlayerElement.MediaPlayer) == false)
                {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Error GrabAndDisplayFrame from GetNextMediaPlayFrame: Second try failed");
                }
            }

            bool lockTaken = false;
            try
            {
                MediaPlayer mp = MediaPlayerElement.MediaPlayer;

                // Ensure only one thread at a time can access this section of code.
                // This is to prevent crashes
                Monitor.TryEnter(lockVideoFrameAvailable, ref lockTaken);
                if (lockTaken)
                {
                    // Check the frame dimensions are as excepted
                    if (vidFrameMgr.FrameWidth != mp.PlaybackSession.NaturalVideoWidth || vidFrameMgr.FrameHeight != mp.PlaybackSession.NaturalVideoHeight)
                        Debug.WriteLine($"{CameraSide}: Warning dimension change, SoftwareBitmap setup at ({vidFrameMgr.FrameWidth},{vidFrameMgr.FrameHeight}), this frame is ({mp.PlaybackSession.NaturalVideoWidth},{mp.PlaybackSession.NaturalVideoHeight})");

                    // Draw the framePrimary: User requested to play
                    vidFrameMgr.DrawFrameOnImageControl();

                    // Make the ImageFrame visible if it is not already
                    if (ImageFrame.Visibility == Visibility.Collapsed)
                    {
                        ImageFrame.Visibility = Visibility.Visible;
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide} Info GrabAndDisplayFrame: Make Image frame visible and collapse player, mode={mode}.");
                    }

#if !No_MagnifyAndMarkerDisplay
                    // Get the image frame in memory for the Magnify Window
                    var (_frameStream, _imageSourceWidth, _imageSourceHeight) = vidFrameMgr.CopyFrameToMemoryStreamAsync();

                    if (_frameStream is not null)
                    {
                        // Signal the frame is ready and pass the reference to the CanvasBitmap
                        mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.FrameRendered, CameraSide, mode)
                        {
                            position = mp.PlaybackSession.Position,
                            frameStream = _frameStream,
                            imageSourceWidth = _imageSourceWidth,
                            imageSourceHeight = _imageSourceHeight
                        });
                    }
#endif
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Error GrabAndDisplayFrame: {ex.Message}");
            }
            finally
            {
                if (lockTaken)
                {
                    Monitor.Exit(lockVideoFrameAvailable);
                }
            }

            //???Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: GrabAndDisplayFrame Exit");
        }


        ///
        /// EVENTS
        /// 


        /// <summary>
        /// MediaPlayerElement.MediaPlayer.PlaybackSession.PlaybackStateChanged
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void PlaybackSession_PlaybackStateChanged(MediaPlaybackSession playbackSession, object args)
        {
            //???Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: PlaybackSession_PlaybackStateChanged: Enter PlaybackState={playbackSession.PlaybackState}, NaturalDuration:{playbackSession.NaturalDuration:hh\\:mm\\:ss\\.ff}");

            try
            {
                if (playbackSession != null && playbackSession.NaturalVideoHeight != 0)
                {
                    if (playbackSession.PlaybackState == MediaPlaybackState.Playing)
                        // Prevent the screen from turning off automatically
                        await EnableDisplayTimoutAsync(false);
                    else// PlaybackState is Buffering, None, Opening, or Paused.
                        // Allow the screen to turn off automatically
                        await EnableDisplayTimoutAsync(true);


                    switch (playbackSession.PlaybackState)
                    {
                    case MediaPlaybackState.Playing:
                        // Inform the MediaControl that the media is playing
                        _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, () =>
                        {
                            try
                            {
                                // Inform the media control that the media is playing
                                mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.Playing, CameraSide, mode));
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Error PlaybackSession_PlaybackStateChanged  {ex.Message}");
                            }
                        });
                        break;

                    case MediaPlaybackState.Paused:
                        // Rememmber the position in paused mode
                        positionPausedMode = playbackSession.Position;
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} PlaybackSession_PlaybackStateChanged: PositionOffset: {positionPausedMode:hh\\:mm\\:ss\\.ff}");
                            
                        // Go into Frame Server mode so we can handle the frame rendering
                        _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                        {
                            // Capture the frame rate if not already known
                            if (frameRate == -1)
                            {
                                GetFrameRateAndTimePerFrame();
                            }

                            // Inform the media control that the media is playing
                            mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.Paused, CameraSide, mode));

                            //???Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Info PlaybackSession_PlaybackStateChanged: Paused & video frame event enabled");
                        });
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Error PlaybackSession_PlaybackStateChanged: Exception: {ex.Message}");
            }
        }


        /// <summary>
        /// Buffering - show the progress ring
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void PlaybackSession_BufferingStarted(MediaPlaybackSession sender, object args)
        {
            WinUIGuards.CheckIsUIThread();

            mainWindow!.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, /*async*/ () =>
                ProgressRing_Buffering.IsActive = true);

        }


        /// <summary>
        /// Buffering ended - hide the progress ring
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void PlaybackSession_BufferingEnded(MediaPlaybackSession sender, object args)
        {
            WinUIGuards.CheckIsUIThread();

            // Hide the progress ring
            mainWindow!.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, /*async*/ () =>
                ProgressRing_Buffering.IsActive = false);
        }


        /// <summary>
        /// Capture and store the frame dimensions
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void PlaybackSession_NaturalVideoSizeChanged(MediaPlaybackSession sender, object args)
        {
            MediaPlaybackSession playbackSession = sender as MediaPlaybackSession;

            // Check the frame size if known (via MediaOpened) has not changed
            if (frameWidth != 0 && 
                (frameWidth != playbackSession.NaturalVideoWidth || 
                frameHeight != playbackSession.NaturalVideoHeight))
            {
                //???frameWidth = playbackSession.NaturalVideoWidth;
                //???frameHeight = playbackSession.NaturalVideoHeight;

                //// Signal the frame size
                //??? Because we need the frame size to setup the video frame manager buffers we wait in the MediaOpened
                // event for the frame size to become available.  Therefore receiving it here is uneccessary
                //mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.FrameSize, CameraSide, mode)
                //{
                //    frameWidth = (int?)frameWidth,
                //    frameHeight = (int?)frameHeight
                //});

                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Error PlaybackSession_NaturalVideoSizeChanged: {playbackSession.NaturalVideoWidth}x{playbackSession.NaturalVideoHeight} buffer will be mismatched!");
            }
        }

        /// <summary>
        /// Something the durection is not know at MediaOpened event time.
        /// If the duration is not known at MediaOpened event time then use this event 
        /// to get the duration and signal it via mediator
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void PlaybackSession_NaturalDurationChanged(MediaPlaybackSession sender, object args)
        {
            if (naturalDuration == TimeSpan.Zero)
            {
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {

                    MediaPlaybackSession playbackSession = sender as MediaPlaybackSession;

                    // Signel the media duration event via mediator if known
                    if (playbackSession.NaturalDuration != TimeSpan.Zero)
                    {
                        // Get the frame rate if not already known
                        if (frameRate == -1)
                            GetFrameRateAndTimePerFrame();
                            

                        // Signal the media duration and frame rate via mediator (used by the MediaStereoController and MediaControl)
                        MediaPlayerEventData data = new(MediaPlayerEventData.eMediaPlayerEvent.DurationAndFrameRate, CameraSide, mode)
                        {
                            duration = playbackSession.NaturalDuration,
                            frameRate = frameRate
                        };
                        mediaPlayerHandler?.Send(data);
                    }

                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Info PlaybackSession_NaturalDurationChanged: (late discovery) NaturalDuration:{TimePositionHelper.Format(naturalDuration, displayToDecimalPlaces)}");
                });
            }
        }

        private void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args)
        {
            MediaPlaybackSession playbackSession = sender as MediaPlaybackSession;

            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                if (!IsMediaSynchronized())
                {
                    // If the media isn't syncronised signal the position of the MediaPlayer
                    // (If sync'd the timeline controller sends the poisition message)
                    MediaPlayerEventData data = new(MediaPlayerEventData.eMediaPlayerEvent.Position, CameraSide, mode)
                    {
                        position = playbackSession.Position                       
                    };
                    mediaPlayerHandler?.Send(data);
                }
            });

            //??? Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Info PlaybackSession_PositionChanged:{TimePositionHelper.Format(sender.Position, displayToDecimalPlaces)}, mode {mode}");
        }

        private void PlaybackSession_SeekableRangesChanged(MediaPlaybackSession sender, object args)
        {
            MediaPlaybackSession playbackSession = sender as MediaPlaybackSession;
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Info PlaybackSession_SeekableRangesChanged");
        }

        private void PlaybackSession_SeekCompleted(MediaPlaybackSession sender, object args)
        {
            MediaPlaybackSession playbackSession = sender as MediaPlaybackSession;

            //???_frameIndexCurrent = GetFrameIndexFromPosition(playbackSession.Position);

            //???Debug.WriteLine($"{CameraSide}: Info PlaybackSession_SeekCompleted: Total milliseconds:{playbackSession.Position.TotalMilliseconds:F1}ms, Frame Index={GetFrameIndexFromPosition(playbackSession.Position)}");
        }

        private void PlaybackSession_SupportedPlaybackRatesChanged(MediaPlaybackSession sender, object args)
        {
            MediaPlaybackSession playbackSession = sender as MediaPlaybackSession;
            Debug.WriteLine($"{CameraSide}: Info PlaybackSession_SupportedPlaybackRatesChanged: PlaybackRate:{playbackSession.PlaybackRate}");

        }

        private void MediaPlayer_MediaPlayerRateChanged(MediaPlayer sender, MediaPlayerRateChangedEventArgs args)
        {
            MediaPlayer mediaPlayer = sender as MediaPlayer;
            Debug.WriteLine($"{CameraSide}: Info MediaPlayer_MediaPlayerRateChanged: being depricated instead, use the MediaPlayer.PlaybackSession property to get a MediaPlaybackSession");
        }


        /// <summary>
        /// MediaPlayerElement.MediaPlayer.MediaOpened 
        /// New media has been opened, get and source the media Uri
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void MediaPlayer_MediaOpened(MediaPlayer sender, object args)
        {
            //???Debug.WriteLine($"{CameraSide}: MediaPlayer_MediaOpened: Enter  (UIThread={DispatcherQueue.HasThreadAccess})");

            mainWindow!.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, async () =>
            {
                try
                {
                    // Set the ImageFrame in the video frame manager
                    vidFrameMgr.SetImage(CameraSide, ImageFrame);

                    // Set the resource to be used by the VideoFrameAvailable event
                    bool setupOk = await vidFrameMgr.SetupIfNecessary(MediaPlayerElement.MediaPlayer);
                    if (!setupOk)
                    { 
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: MediaPlayer_MediaOpened vidFrameMgr.SetupIfNecessary failed to setup resources");
                        return;
                    }

                    // Capture the frame dimensions
                    frameWidth = MediaPlayerElement.MediaPlayer.PlaybackSession.NaturalVideoWidth;
                    frameHeight = MediaPlayerElement.MediaPlayer.PlaybackSession.NaturalVideoHeight;

                    // Signal the frame size
                    mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.FrameSize, CameraSide, mode)
                    {
                        frameWidth = (int?)frameWidth,
                        frameHeight = (int?)frameHeight
                    });


                    // Get the natural duration of the media
                    naturalDuration = sender.PlaybackSession.NaturalDuration - sender.TimelineControllerPositionOffset;

                    if (!IsRemoteFile(mediaUri))
                    {
                        // If the media is local then we can move one frame forward and back to
                        // get a frame to display                            
                        MediaPlayerElement.MediaPlayer.StepForwardOneFrame();
                        await Task.Delay(150);
                        MediaPlayerElement.MediaPlayer.StepBackwardOneFrame();
                    }


                    // Set mute because normally we don't want the sound
                    Mute();

                    // Show the media player (the frame image user control to shown when the media is paused)
                    MediaPlayerElement.Visibility = Visibility.Visible;

                    // Remove the buffering progress ring
                    ProgressRing_Buffering.IsActive = false;

                    // This flag is used to indicate to the class that the media is open
                    mediaOpen = true;

                    // Signel the media open event via mediator
                    MediaPlayerEventData data = new(MediaPlayerEventData.eMediaPlayerEvent.Opened, CameraSide, mode)
                    {
                        mediaFileSpec = this.mediaUri
                    };
                    mediaPlayerHandler?.Send(data);

                    // Signel the media duration and frame rate via mediator if known (used by the MediaStereoController and MediaControl)
                    if (naturalDuration != TimeSpan.Zero)
                    {
                        // Get the frame rate if not already known
                        if (frameRate == 0.0)
                            frameRate = GetCurrentFrameRate(MediaPlayerElement.MediaPlayer);

                        data = new(MediaPlayerEventData.eMediaPlayerEvent.DurationAndFrameRate, CameraSide, mode)
                        {
                            duration = naturalDuration,
                            frameRate = frameRate
                        };
                        mediaPlayerHandler?.Send(data);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Error MediaPlayer_MediaOpened: Exception: {ex.Message}");
                }
            });

            //???Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: MediaPlayer_MediaOpened: Exit");
        }


        private void MediaPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            MediaPlayer mediaPlayer = sender as MediaPlayer;

            mode = Mode.modeNone;

            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Info MediaPlayer_MediaFailed: Media playback error: {args.ErrorMessage}, Extended error code: {args.ExtendedErrorCode.HResult}");
        }

        private void MediaPlayer_MediaEnded(MediaPlayer sender, object args)
        {
            MediaPlayer mediaPlayer = sender as MediaPlayer;

            mode = Mode.modeFrame;

            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Info MediaPlayer_MediaEnded");
        }

        private void MediaPlayer_SourceChanged(MediaPlayer sender, object args)
        {
            MediaPlayer mediaPlayer = sender as MediaPlayer;
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Info MediaPlayer_SourceChanged:");
        }



        /// <summary>
        /// Class to safely store the resources used to render the video frame
        /// these resources are created when the media is opened and released when the media is closed
        /// </summary>
        private class VideoFrameManager
        {
            // Image Control
            private eCameraSide cameraSide { get; set; } = eCameraSide.None;
            private Image? imageFrame = null;

            // Bitmaps and Soruces  
            private SoftwareBitmap? frameServerDest = null;
            private CanvasImageSource? canvasImageSource = null;
            private CanvasBitmap? inputBitmap = null;
            private uint frameWidthSoftwareBitmap = 0;
            private uint frameHeightSoftwareBitmap = 0;
            private int videoFrameCount = 0;

            // Wait for one more frame 
            private bool waitForOneMoreFrame = false;
            private TaskCompletionSource<bool>? taskOneMoreFrameCompletion = null;
            private readonly object _frameLock = new(); // Lock for synchronization


            /// <summary>
            /// Set the Image control
            /// </summary>
            /// <param name="_imageFrame"></param>
            public void SetImage(eCameraSide _cameraSide, Image _imageFrame)
            {
                cameraSide = _cameraSide;
                imageFrame = _imageFrame;
            }

            public bool IsSetup { get; set; } = false;


            /// <summary>
            /// Setup the resources if necessary
            /// </summary>
            /// <param name="mp"></param>
            /// <returns></returns>
            public async Task<bool> SetupIfNecessary(MediaPlayer mp)
            {
                WinUIGuards.CheckIsUIThread();

                Debug.Assert(imageFrame != null, $"{DateTime.Now:HH:mm:ss.ff} Must call SetImage before calling SetupIfNecessary.");

                if (!IsSetup)
                {
                    // Wait the frame width and heights to be available (5 sec max)
                    await WaitForNonZeroFrameSizeAsync(mp);

                    // Remember the frame width and height used to create the resources
                    frameWidthSoftwareBitmap = mp.PlaybackSession.NaturalVideoWidth;
                    frameHeightSoftwareBitmap = mp.PlaybackSession.NaturalVideoHeight;


                    // Object used by Win2D for rendering graphics
                    // Do this (once per SurveyorMediaPlayer instance, and store it):
                    //???CanvasDevice canvasDevice = CanvasDevice.GetSharedDevice(); // Do not use shared singleton GPU device provided by Win2D
                    CanvasDevice canvasDevice = new();


                    // Creates a new SoftwareBitmap with the specified width, height, and format
                    // This bitmap will be used to hold the video frame data
                    frameServerDest = new SoftwareBitmap(BitmapPixelFormat.Rgba8, (int)frameWidthSoftwareBitmap, (int)frameHeightSoftwareBitmap, BitmapAlphaMode.Ignore);

                    // Creates a new CanvasImageSource with the specified dimensions and DPI (dots per inch)
                    canvasImageSource = new CanvasImageSource(canvasDevice, (float)frameWidthSoftwareBitmap, (float)frameHeightSoftwareBitmap, 96/*DisplayInformation.GetForCurrentView().LogicalDpi*/);//96); 

                    // Create inputBitMap to receive the frame from Media Player
                    inputBitmap = CanvasBitmap.CreateFromSoftwareBitmap(canvasDevice, frameServerDest);

                    // Assign the canvasImageSource to the Image.Source
                    imageFrame.Source = canvasImageSource;

                    // Check all setup ok
                    if (frameServerDest != null && canvasImageSource != null && inputBitmap != null)
                        IsSetup = true;
                    else
                        IsSetup = false;
                }
                return IsSetup;
            }


            /// <summary>
            /// Wait for the frame size to become non-zero. Sometimes NaturalVideoWidth/Height are zero when
            /// the MediaOpened event is raised.  If we wait a little while, the values will be set.
            /// </summary>
            /// <param name="mp"></param>
            /// <param name="timeoutMs"></param>
            /// <param name="checkIntervalMs"></param>
            /// <returns></returns>
            private async Task WaitForNonZeroFrameSizeAsync(MediaPlayer mp, int timeoutMs = 5000, int checkIntervalMs = 50)
            {
                WinUIGuards.CheckIsUIThread();

                var tcs = new TaskCompletionSource<bool>();

                var cancellationTokenSource = new CancellationTokenSource(timeoutMs);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (mp.PlaybackSession.NaturalVideoWidth == 0 || mp.PlaybackSession.NaturalVideoHeight == 0)
                        {
                            if (cancellationTokenSource.Token.IsCancellationRequested)
                            {
                                tcs.TrySetCanceled();
                                return;
                            }

                            await Task.Delay(checkIntervalMs); // Non-blocking wait
                        }

                        // Values are non-zero — signal completion
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }, cancellationTokenSource.Token);

                try
                {
                    // Wait for the task to complete or timeout
                    await tcs.Task;
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide}: Frame size detected: {mp.PlaybackSession.NaturalVideoWidth}x{mp.PlaybackSession.NaturalVideoHeight}");
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide}: Timeout waiting for frame size to become non-zero.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide}: Error waiting for frame size: {ex.Message}");
                }
            }


            /// <summary>
            /// Release the resources
            /// </summary>
            public void Release()
            {
                WinUIGuards.CheckIsUIThread();

                if (frameServerDest is not null)
                {
                    frameServerDest.Dispose();
                    frameServerDest = null;
                }


                canvasImageSource = null;

                if (inputBitmap is not null)
                {
                    inputBitmap.Dispose();
                    inputBitmap = null;
                }
                if (imageFrame is not null)
                    imageFrame.Source = null;

                frameWidthSoftwareBitmap = 0;
                frameHeightSoftwareBitmap = 0;
                videoFrameCount = 0;

                IsSetup = false;
            }


            /// <summary>
            /// Used to manage MediaPlayer_VideoFrameAvailable reentry
            /// </summary>
            public void VideoFrameAvailableEnter()
            {
                videoFrameCount++;
            }


            /// <summary>
            /// Used to manage MediaPlayer_VideoFrameAvailable reentry
            /// </summary>
            public void VideoFrameAvailableExit()
            {
                videoFrameCount--;
            }


            /// <summary>
            /// Check if there are multiple frames pending
            /// </summary>
            /// <returns>true is multiple frames are pending</returns>
            public bool MultipleFramesPending()
            {
                return videoFrameCount > 1;
            }


            /// <summary>
            /// Return the entry count in the MediaPlayer_VideoFrameAvailable event
            /// </summary>
            public int GetEntryCount { get { return videoFrameCount; } }


            /// <summary>
            /// Get the frame dimensions
            /// </summary>
            public uint FrameWidth { get { return frameWidthSoftwareBitmap; } }
            public uint FrameHeight { get { return frameHeightSoftwareBitmap; } }


            /// <summary>
            /// Setup the request for a signal from the frame server that one more frame has
            /// been rendered in the UI Image control.
            /// This function must be used in conjunction with WaitOneMoreFrame() which actually
            /// does the waiting for the signel.
            /// ** This method is called on the UI side (waiting side) **
            /// </summary>
            /// <param name="cameraSide"></param>
            /// <param name="_surveyorMediaPlayer">Pause request handler or can be null</param>
            /// <returns></returns>
            public bool RequestOneMoreFrame()
            {
                bool ret = false;

                lock (_frameLock)
                {
                    if (!waitForOneMoreFrame)
                    {
                        taskOneMoreFrameCompletion = new TaskCompletionSource<bool>();
                        waitForOneMoreFrame = true;

                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide} (Wait side) Requested one more frame notification");
                        ret = true;
                    }
                    else
                    {
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide} (Wait side) Requested one more frame FAILED");
                    }
                }
                return ret;
            }


            /// <summary>
            /// Wait for the frame server to signal that one more frame has been received and rendered in the 
            /// UI image control. RequestOneMoreFrame() must be call before this method to setup the request
            /// /// ** This method is called on the UI side (waiting side) **
            /// </summary>
            /// <param name="cameraSide"></param>
            /// <returns></returns>
            public async Task<bool> WaitOneMoreFrame()
            {
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide} WaitOneMoreFrame (Wait side) Waiting...");

                lock (_frameLock)
                {
                    if (!waitForOneMoreFrame)
                    {
                        return false; // Avoid waiting on an uninitialized task
                    }
                }

                if (taskOneMoreFrameCompletion is not null)
                {
                    if (await Task.WhenAny(taskOneMoreFrameCompletion.Task, Task.Delay(2000)) == taskOneMoreFrameCompletion.Task)
                    {
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide} WaitOneMoreFrame (Wait side) Successfully waited for one more frame");
                        return true;
                    }
                    else
                    {
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide} WaitOneMoreFrame (Wait side) Timeout waiting for one more frame");
                        waitForOneMoreFrame = false;
                        return false;
                    }
                }
                else
                {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide} WaitOneMoreFrame (Wait side) Error: taskOneMoreFrameCompletion is null");
                    waitForOneMoreFrame = false;
                    return false;
                }
            }


            /// <summary>
            /// Called inside the frame server after a new frame has been rendered into the IU
            /// image control.  The methed check if the UI side was waiting for one more frame and
            /// if so signals to the waiting thread.
            /// The purpose of this is has the MediaPlayer move from playing to pause that at least
            /// one frame comes via the frame serve so there is an up-to-date frame in the Image UI 
            /// control which is displayed as the media player element is hidden.
            /// ** This method is called on the frame server side (MediaPlayer thread) **
            /// </summary>
            /// <param name="cameraSide"></param>
            public void DoWeNeedToSignalForOneMoreFrameReceived()
            {
                lock (_frameLock)
                {
                    if (waitForOneMoreFrame)
                    {
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide} (Frame Side) Signalling for one more frame received");
                        if (taskOneMoreFrameCompletion is not null)
                        {
                            taskOneMoreFrameCompletion.TrySetResult(true);

                            waitForOneMoreFrame = false;
                        }
                        else
                        {
                            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide} (Frame Side) Error: taskOneMoreFrameCompletion is null");
                            waitForOneMoreFrame = false;
                        }
                    }
                }
            }


            /// <summary>
            /// THIS IS TEST CODE
            /// If we are waiting to signal and we have one more frame and we are skipping frames
            /// then we maybe overloaded and need to stop for frame server
            /// </summary>
            /// <param name="cameraSide"></param>
            public bool DoWeNeedToSignalForOneMoreFrameReceivedButSkippingFrames()
            {
                bool ret = false;
                lock (_frameLock)
                {
                    if (waitForOneMoreFrame)
                    {
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide} (Frame Side) Waiting for frame and Frame Skipping - So shutdown the frame server");
                        ret = true;
                    }
                }

                return ret;
            }


            /// <summary>
            /// Get the next frame from the media player into the inputBitmap
            /// </summary>
            /// <param name="mp"></param>
            public bool GetNextMediaPlayFrame(MediaPlayer mp)
            {
                // Lightweight check 
                if (IsSetup == false)
                {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide} VideoFrameManager.GetNextMediaPlayFrame: Not setup");
                    return false;
                }

                bool ret = false;
                try
                {
                    if (inputBitmap is not null)
                    {
                        // Copies the current video frame from the MediaPlayer to the provided IDirect3DSurface
                        mp.CopyFrameToVideoSurface(inputBitmap);

                        ret = !IsImageBlank();

                        if (!ret)
                        {
                            Debug.WriteLine($"{cameraSide} VideoFrameManager.GetNextMediaPlayFrame: Frame is blank");
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.Write($"{cameraSide} VideoFrameManager.GetNextMediaPlayFrame Exception:{e.Message}");
                }

                return ret;
            }


            /// <summary>
            /// Draw the frame on the image control
            /// </summary>
            public bool DrawFrameOnImageControl()
            {
                WinUIGuards.CheckIsUIThread();

                bool ret = false;

                try
                {
                    if (canvasImageSource == null || inputBitmap == null)
                        return ret; // Avoid null reference issues

                    // Draw the frame
                    ret = true;  // Assume success
                    try
                    {
                        using (CanvasDrawingSession ds = canvasImageSource!.CreateDrawingSession(Microsoft.UI.Colors.Black))
                        {
                            ds.DrawImage(inputBitmap);
                            //???Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Info MediaPlayer_VideoFrameAvailable: Frame drawn. Index from player position:{(mp.PlaybackSession.Position.TotalMilliseconds / 1000.0):F3}");

                            // In Debug mode, draw a camera symbol on the frame for differentiation
                            // between the media player video surface and the image frame
//??? Susprended for now
///#if DEBUG
                            // Define the position for the glyph
                            Vector2 glyphPosition = new(10, 10); // Top-left position of the glyph

                            // Define the font and size for the glyph
                            CanvasTextFormat textFormat = new()
                            {
                                FontFamily = "Segoe Fluent Icons",
                                FontSize = 48,
                                HorizontalAlignment = CanvasHorizontalAlignment.Left,
                                VerticalAlignment = CanvasVerticalAlignment.Top
                            };

                            // Draw the glyph (E158 is the pause symbol in Segoe Fluent Icons)
                            string pauseGlyph = "\uE158";
                            ds.DrawText(pauseGlyph, glyphPosition, Colors.White, textFormat);
///#endif // End of DEBUG
                        }
                    }
                    catch (COMException ex) when ((uint)ex.HResult == 0x88980801)
                    {
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide}: DirectX context lost or unsupported operation in CreateDrawingSession: {ex.Message}-----------*****************");
                        ret = false;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide}: Exception during frame rendering: {ex.Message}");
                        ret = false;
                    }

                    ret = true;
                }
                catch (Exception e)
                {
                    Debug.Write($"{DateTime.Now:HH:mm:ss.ff} {cameraSide}: VideoFrameManager.DrawFrameOnImageControl Exception:{e.Message}");
                }

                return ret;
            }


            /// <summary>
            /// Copy the frame to a memory stream this is used by MagnifyAndMarker
            /// </summary>
            /// <returns></returns>
            /// <exception cref="InvalidOperationException"></exception>
            public (IRandomAccessStream? stream, uint imageSourceWidth, uint imageSourceHeight) CopyFrameToMemoryStreamAsync()
            {
                WinUIGuards.CheckIsUIThread();

                // Lightweight check 
                if (IsSetup == false)
                {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide} VideoFrameManager.CopyFrameToMemoryStreamAsync: Not setup");
                    return (null, 0, 0);
                }

                if (inputBitmap is null)
                {
                    throw new InvalidOperationException("CopyFrameToMemoryStreamAsync: inputBitmap is not initialized.");
                }

                // Get image dimensions
                uint imageSourceWidth = (uint)inputBitmap.SizeInPixels.Width;
                uint imageSourceHeight = (uint)inputBitmap.SizeInPixels.Height;

                // Create a memory stream
                InMemoryRandomAccessStream streamSource = new();

                try
                {
                    // Save the CanvasBitmap into the memory stream
                    _ = inputBitmap.SaveAsync(streamSource, CanvasBitmapFileFormat.Bmp);
                    return (streamSource, imageSourceWidth, imageSourceHeight); // Return the stream with the image data
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide}: CopyFrameToMemoryStreamAsync Failed to save frame to memory stream: {ex.Message}");
                    throw; // Rethrow the exception so caller can handle it
                }
            }


            /// <summary>
            /// Save the frame to a file
            /// Many format available including:
            ///     CanvasBitmapFileFormat.Jpeg
            ///     CanvasBitmapFileFormat.Png
            ///     CanvasBitmapFileFormat.Bmp
            /// </summary>
            /// <param name=""></param>
            /// <param name=""></param>
            /// <returns></returns>
            public async Task<bool> SaveFrame(string fileSpec, CanvasBitmapFileFormat fileFormat)
            {
                WinUIGuards.CheckIsUIThread();

                bool ret = false;

                if (inputBitmap is not null)
                {
                    try
                    {
                        await inputBitmap.SaveAsync(fileSpec, fileFormat);
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide}: SaveFrame Saved: {fileSpec}");
                        ret = true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {cameraSide}: SaveFrame: failed to write:{fileSpec} to format:{fileFormat}  {ex.Message}");
                    }
                }

                return ret;
            }


            /// <summary>
            /// Test if the image is blank.  This is to test if media player returned a blank frame
            /// </summary>
            /// <returns></returns>
            public bool IsImageBlank()
            {
                if (inputBitmap == null)
                    return false;

                int width = (int)inputBitmap.SizeInPixels.Width;
                int height = (int)inputBitmap.SizeInPixels.Height;

                var pixels = inputBitmap.GetPixelBytes();

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = (y * width + x) * 4; // BGRA format

                        byte b = pixels[index];

                        if (b != 0)
                            return false; // Found a non-black pixel

                        byte g = pixels[index + 1];

                        if (g != 0)
                            return false; // Found a non-black pixel    

                        byte r = pixels[index + 2];

                        if (r != 0)
                            return false; // Found a non-black pixel

                        //??? byte a = pixels[index + 3];

                        //??? if (a != 0)
                        //???     return false; // Found a non-black pixel
                    }
                }

                return true; // All pixels are pure black
            }
        }


        /// <summary>
        /// MediaPlayerElement.MediaPlayer.VideoFrameAvailable
        /// Event called when a frame is ready to display when the Media Player is in 'Frame Server' mode,
        /// because we want to interact with the frame when we pause it to zoom in we need access to the
        /// frame image and take control of rendering the image while in the paused state.
        /// When the player is paused or frame forward or back an event MediaPlayer.PlaybackSession.PlaybackStateChanged
        /// is raised. In the event is put the player into and out of 'Frame Server' mode.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void MediaPlayer_VideoFrameAvailable(MediaPlayer mp, object args)
        {
            // *****************************
            // **Not called from UI Thread**

            // Work only want one frame
            mp.IsVideoFrameServerEnabled = false;

            // Flag to indicate the buffers need reseting (used if we get an exception)
            bool reset = false;

            // Check if media is being closed
            if (isClosing)
            {
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Skipping MediaPlayer_VideoFrameAvailable - Player is closing.");
                return;
            }


            //???Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: MediaPlayer_VideoFrameAvailable Enter");

            // Reentry counter
            vidFrameMgr.VideoFrameAvailableEnter();


            //  Check if the resources are setup
            if (!vidFrameMgr.IsSetup)
            {
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Error  Failed to setup resources");
                vidFrameMgr.VideoFrameAvailableExit();
                return;
            }

            // Copy the frame from the media player to the inputBitmap
            // This call must be done otherwise MediaPlayer_VideoFrameAvailable will be called repeatly
            // With the same frame
            Stopwatch watch = new();
            watch.Start();
            if (vidFrameMgr.GetNextMediaPlayFrame(mp) == false)
            {
                // Allow time to settle
                await Task.Delay(50);

                // Retry
                if (vidFrameMgr.GetNextMediaPlayFrame(mp) == false)
                {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Error MediaPlayer_VideoFrameAvailable from GetNextMediaPlayFrame: Second try failed");
                    reset = true;
                }
            }
            Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: GetNextMediaPlayFrame Time={watch.ElapsedMilliseconds}m/s");
            watch.Stop();

            if (reset == false)
            {

                _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                //await DispatcherQueue.EnqueueAsync(() =>
                {
                    //???Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: MediaPlayer_VideoFrameAvailable Into UI Thread {functionTime.ElapsedMilliseconds}m/s.");

                    bool lockTaken = false;
                    try
                    {
                        // Ensure only one thread at a time can access this section of code.
                        Monitor.TryEnter(lockVideoFrameAvailable, ref lockTaken);
                        if (lockTaken)
                        {                            
                            // Check the frame dimensions are as excepted
                            if (vidFrameMgr.FrameWidth != mp.PlaybackSession.NaturalVideoWidth || vidFrameMgr.FrameHeight != mp.PlaybackSession.NaturalVideoHeight)
                                Debug.WriteLine($"{CameraSide}: Warning dimension change, SoftwareBitmap setup at ({vidFrameMgr.FrameWidth},{vidFrameMgr.FrameHeight}), this frame is ({mp.PlaybackSession.NaturalVideoWidth},{mp.PlaybackSession.NaturalVideoHeight})");

                            // Draw the frame if there is no frame pending to be drawn
                            // i.e. waste if time if there is already a frame pending
                            if (!vidFrameMgr.MultipleFramesPending())
                            {
                                // Draw the framePrimary: User requested to play
                                if (vidFrameMgr.DrawFrameOnImageControl() == false)
                                    reset = true;

                                if (reset == false)
                                {

                                    // Make the ImageFrame visible if it is not already
                                    if (ImageFrame.Visibility == Visibility.Collapsed)
                                    {
                                        ImageFrame.Visibility = Visibility.Visible;
                                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide} Info MediaPlayer_VideoFrameAvailable: Make Image frame visible and collapse player, mode={mode}.");
                                    }

#if !No_MagnifyAndMarkerDisplay
                                    // Get the image frame in memory for the Magnify Window
                                    var (_frameStream, _imageSourceWidth, _imageSourceHeight) = vidFrameMgr.CopyFrameToMemoryStreamAsync();

                                    if (_frameStream is not null)
                                    {
                                        // Signal the frame is ready and pass the reference to the CanvasBitmap
                                        mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.FrameRendered, CameraSide, mode)
                                        {
                                            position = mp.PlaybackSession.Position,
                                            frameStream = _frameStream,
                                            imageSourceWidth = _imageSourceWidth,
                                            imageSourceHeight = _imageSourceHeight
                                        });
                                    }
#endif

                                    //???Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: MediaPlayer_VideoFrameAvailable Before DoWeNeedToSignalForOneMoreFrameReceived {functionTime.ElapsedMilliseconds}m/s.");

                                    // Signal for one more frame received if it was requested
                                    vidFrameMgr.DoWeNeedToSignalForOneMoreFrameReceived();
                                }
                            }
                            else
                            {
                                // Frame skipped
                                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Warning MediaPlayer_VideoFrameAvailable: ***Frame skipped***. Index from player position:{(mp.PlaybackSession.Position.TotalMilliseconds / 1000.0):F3}");
                                if (vidFrameMgr.DoWeNeedToSignalForOneMoreFrameReceivedButSkippingFrames())
                                {
                                    FrameServerEnable(false);  // Shouldn't get here because the frame server should be disabled
                                                               // on entry to this function
                                }
                            }
                        }

                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Error MediaPlayer_VideoFrameAvailable: {ex.Message}");
                    }
                    finally
                    {
                        if (lockTaken)
                        {
                            Monitor.Exit(lockVideoFrameAvailable);
                        }

                        // Reentry counter - decrement
                        vidFrameMgr.VideoFrameAvailableExit();
                    }
                }/*, DispatcherQueuePriority.Normal*/);
            }

            // Failure - try to reset the the buffer before the next event
            if (reset)
            {
                vidFrameMgr.IsSetup = false;  // Do this quickly to avoid reentry
                await DispatcherQueue.EnqueueAsync(async () =>
                {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide} CanvasImageSource about to reset after DirectX context loss");
                    await Task.Delay(100);

                    //  Reset the frame server mode to recover                                    
                    mp.IsVideoFrameServerEnabled = false;

                    // Optionally reset CanvasImageSource
                    await Task.Delay(100);
                    vidFrameMgr.Release();

                    await Task.Delay(100);                  
                    await vidFrameMgr.SetupIfNecessary(mp);

                    await Task.Delay(100);
                    mp.IsVideoFrameServerEnabled = true;

                    vidFrameMgr.IsSetup = true;

                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide} CanvasImageSource reset after DirectX context loss");
                });
            }

            //???Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: MediaPlayer_VideoFrameAvailable Exit");
        }


        ///
        /// PRIVATE FUNCTIONS
        ///


        /// <summary>
        /// Function calulates the frame rate of the media and stores it in the private variable '_frameRate'.
        /// This fnction is called from the event 'MediaPlayer_MediaOpened'
        /// </summary>
        /// <param name="mediaPlayer"></param>
        private double GetCurrentFrameRate(MediaPlayer mediaPlayer)
        {
            WinUIGuards.CheckIsUIThread();

            double ret = -1;
            string subError = "";

            // Assuming mediaPlayerElement is your MediaPlayerElement            
            if (mediaPlayer != null && mediaPlayer.Source != null)
            {
                if (mediaPlayer.Source is MediaPlaybackItem playbackItem)
                {
                    var videoTracks = playbackItem.VideoTracks;
                    if (videoTracks != null)
                    {
                        if (videoTracks.Count > 0)
                        {

                            var videoTrack = videoTracks.First();

                            // Get the video encoding properties
                            var encodingProperties = videoTrack.GetEncodingProperties();
                            var videoEncodingProperties = encodingProperties as VideoEncodingProperties;

                            if (videoEncodingProperties != null)
                            {
                                uint frameRateNumerator = videoEncodingProperties.FrameRate.Numerator;
                                uint frameRateDenominator = videoEncodingProperties.FrameRate.Denominator;

                                if (frameRateDenominator != 0)
                                {
                                    ret = (double)frameRateNumerator / frameRateDenominator;
                                }
                            }
                        }
                        else
                            subError = " ,videoTracks.Count = 0";
                    }
                    else
                        subError = " ,videoTracks.Count = 0";
                }
                else
                    subError = " , MediaPlayer.Source is not of type MediaPlaybackItem";
            }

            if (ret != -1)
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Info GetCurrentFrameRate: frame rate: {ret:F3}");
            else
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} {CameraSide}: Warning GetCurrentFrameRate: can't determine the frame rate{subError}");

            return ret;
        }


        /// <summary>
        /// *Thread-Safe*
        /// Call with 'True' to allow the the display (computer screen) to naturally timeout and 
        /// switch off or with 'False' to prevent the display from timing out and switching off.
        /// If we are watching a video and not interacting with the computer we don't want the
        /// display to switch off.
        /// </summary>
        /// <param name="enable"></param>
        /// <returns></returns>
        private async Task EnableDisplayTimoutAsync(bool enable)
        {
            await Task.Run(() =>
            {
                mainWindow!.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {
                    if (enable)
                    {
                        if (appDisplayRequest is null)
                        {
                            appDisplayRequest = new DisplayRequest();
                            appDisplayRequest.RequestActive();
                        }
                    }
                    else
                    {
                        if (appDisplayRequest is not null)
                        {
                            appDisplayRequest.RequestRelease();
                            appDisplayRequest = null;
                        }
                    }
                });
            });
        }


        /// <summary>
        /// Used to test of a local or remote file exists
        /// </summary>
        /// <param name="pathOrUrl"></param>
        /// <returns></returns>
        private static async Task<bool> FileExistsAsync(string pathOrUrl)
        {
            // Check if the pathOrUrl is a valid URI
            if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out Uri? uriResult))
            {
                // If it's a local file with a file URI scheme
                if (uriResult.IsFile)
                {
                    return File.Exists(uriResult.LocalPath);
                }
                else // It's probably a remote file
                {
                    return await RemoteFileExistsAsync(pathOrUrl);
                }
            }
            else // It's not a valid URI, so assume it's a local path
            {
                return File.Exists(pathOrUrl);
            }
        }


        /// <summary>
        /// Test is the passed string is a remote file or a local file
        /// </summary>
        /// <param name="pathOrUrl"></param>
        /// <returns>true is remote, false if local</returns>
        private static bool IsRemoteFile(string pathOrUrl)
        {
            // Check if the pathOrUrl is a valid URI
            if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out Uri? uriResult))
            {
                // If it's a local file with a file URI scheme
                if (uriResult.IsFile)
                {
                    return false;
                }
                else // It's probably a remote file
                {
                    return true;
                }
            }
            else // It's not a valid URI, so assume it's a local path
            {
                return false;
            }
        }


        /// <summary>
        /// Used to test of a remote file exists e.g. "http://example.com/path_to_your_remote_file.mp4"
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private static async Task<bool> RemoteFileExistsAsync(string url)
        {
            using var httpClient = new HttpClient();
            try
            {
                // Only get the header information -- don't download the entire file
                var request = new HttpRequestMessage(HttpMethod.Head, url);
                var response = await httpClient.SendAsync(request);

                // Return true if the status code is success (200 OK)
                return response.IsSuccessStatusCode;
            }
            catch
            {
                // Handle exceptions (for example, if the request is not allowed on the server)
                return false;
            }
        }

        /// <summary>
        /// Used to display any exceptions during the Open() function
        /// </summary>
        /// <param name="mediaFileSpec"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        private async Task ShowOpenFailedDialog(string mediaFileSpec, Exception e)
        {
            WinUIGuards.CheckIsUIThread();

            ContentDialog confirmationDialog = new()
            {
                Title = "Failed to open media",
                Content = $"Failed to open the media:\n\n{mediaFileSpec}\n\n{e.Message}",
                CloseButtonText = "OK",

                XamlRoot = this.XamlRoot // Associate the dialog with the current XamlRoot
            };

            // Display the dialog
            await confirmationDialog.ShowAsync();
        }


        /// <summary>
        /// Used to display a dialog when the media file is not found
        /// </summary>
        /// <param name="mediaFileSpec"></param>
        /// <returns></returns>
        private async Task ShowOpenFileNotFoundDialog(string mediaFileSpec)
        {
            WinUIGuards.CheckIsUIThread();

            ContentDialog confirmationDialog = new()
            {
                Title = "Failed to open media",
                Content = $"The media file was not found:\n\n{mediaFileSpec}",
                CloseButtonText = "OK",
                
                XamlRoot = this.XamlRoot // Associate the dialog with the current XamlRoot
            };
            // Display the dialog
            await confirmationDialog.ShowAsync();
        }



        /// <summary>
        /// Called to calculate the frame rate and the time per frame
        /// This is called either when the media opened event or the natural 
        /// duration event are fired
        /// </summary>
        private void GetFrameRateAndTimePerFrame()
        {
            frameRate = GetCurrentFrameRate(MediaPlayerElement.MediaPlayer);
            if (frameRate != -1)
            {
                double ticksPerFrameDouble = TimeSpan.TicksPerSecond / frameRate;
                long ticksPerFrame = (long)Math.Round(ticksPerFrameDouble, MidpointRounding.AwayFromZero);
                frameRateTimeSpan = TimeSpan.FromTicks(ticksPerFrame);
            }
        }


        // ***END OF SurveyorMediaPlayer***
    }




    /// <summary>
    /// Used by the MediaPlayer User Control to inform other components on state changes within MediaPlayer
    /// </summary>
    public class MediaPlayerEventData
    {
        public MediaPlayerEventData(eMediaPlayerEvent e, SurveyorMediaPlayer.eCameraSide cameraSide, SurveyorMediaPlayer.Mode mode)
        {
            mediaPlayerEvent = e;
            this.cameraSide = cameraSide;
            this.mode = mode;
        }

        public enum eMediaPlayerEvent { 
            Opened, 
            Closed, 
            Buffering,
            DurationAndFrameRate, 
            Playing, 
            Paused, 
            Stopped, 
            Position, 
            FrameForwarded, 
            FrameBacked, 
            MovedToStart, 
            MovedToEnd, 
            MoveSteppedBack, 
            MoveSteppedForward, 
            VolumeChanged, 
            Muted, 
            Unmuted, 
            Bookmarked, 
            SavedFrame, 
            EndOfMedia,
            MeasurementPointSelected, 
            MeasurementPairSelected, 
            Error,
            FrameRendered,
            FrameSize}

        public readonly eMediaPlayerEvent mediaPlayerEvent;

        // Used for all
        public readonly SurveyorMediaPlayer.eCameraSide cameraSide;
        public readonly SurveyorMediaPlayer.Mode mode;

        // Only used for eMediaPlayerAction.Opened and eMediaPlayerAction.SavedFrame
        public string? mediaFileSpec;

        // Only used for eMediaPlayerAction.DurationAndFrameRate
        public TimeSpan? duration;
        public double? frameRate;

        // Only used for Position & FrameRendered
        public TimeSpan? position;

        // Only used for eMediaPlayerAction.Position and eMediaPlayerAction.Buffering
        public float? percentage;

        // Only used for FrameRendered to pass the CanvasBitmap
        public IRandomAccessStream? frameStream;
        public uint imageSourceWidth;
        public uint imageSourceHeight;

        // Only used for FrameSize
        public int? frameWidth;
        public int? frameHeight;
    }

    public class MediaPlayerInfoData(SurveyorMediaPlayer.eCameraSide cameraSide)
    {
        public readonly SurveyorMediaPlayer.eCameraSide cameraSide = cameraSide;

        // Set to true to clear the MediaInfo WFT Control
        public bool empty = true;
        public string? mediaFileSpec;
        public Int64 mediaSize = 0;
        public double frameRate = 0;
        public TimeSpan? duration = null;
        public Int64 totalFrames = 0;
        public int frameWidth = 0;
        public int frameHeight = 0;

        // Any errors opening the media, this is set if .empty == true
        public string errorMessage = "";
    }

    public class MediaPlayerHandler : TListener
    {
        private readonly SurveyorMediaPlayer _mediaPlayer;

        public MediaPlayerHandler(IMediator mediator, SurveyorMediaPlayer mediaPlayer, MainWindow mainWindow) : base(mediator, mainWindow)
        {
            _mediaPlayer = mediaPlayer;
        }

        public override void Receive(TListener listenerFrom, object message)
        {
            // In case we need later
        }

    }

}
