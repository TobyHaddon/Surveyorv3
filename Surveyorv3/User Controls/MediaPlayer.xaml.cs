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

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.Casting;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System.Display;

namespace Surveyor.User_Controls
{
    public sealed partial class SurveyorMediaPlayer : UserControl
    {
        // Reporter
        private Reporter? report = null;

        // Copy of MainWindow
        private MainWindow? _mainWindow;

        // Copy of the mediator 
        private SurveyorMediator? _mediator;

        // Declare the mediator handler for MediaPlayer
        private MediaPlayerHandler? _mediaPlayerHandler;

        // Which camera side 'L' or 'R'
        public enum eCameraSide { None, Left, Right };
        public eCameraSide CameraSide { get; set; } = eCameraSide.None;

        
        // Media has been opened
        private bool _mediaOpen = false;
        private string _mediaUri = "";

        // This can be used to prevent the screen from turning off automatically
        // USeful to stop the screen switching off if media is playing
        private DisplayRequest? appDisplayRequest = null;

        // Used to indicate if the control is in Player mode (i.e. using MediaPlayerElement) or in frame mode (i.e. reading and displaying video frame via FFmpeg)
        public enum eMode { modeNone, modePlayer, modeFrame };
        private eMode _mode = eMode.modeNone;
        private Int64 _frameIndexCurrent = 0;
        private TimeSpan _positionPausedMode = TimeSpan.Zero;

        // Media duration
        private TimeSpan _naturalDuration = TimeSpan.Zero;

        // Frame width and height
        private uint _frameWidth = 0;   //??? Consider removing both of these (not used)
        private uint _frameHeight = 0;  //??? Consider removing both of these (not used)

        // Calculated frame rate
        private double _frameRate = -1;
        private TimeSpan _frameRateTimeSpan = TimeSpan.Zero;

        // Used in the redenering of a frame to the screen
        private readonly VideoFrameAvailableResources _vfar = new();
        //???***private VideoFrameExtractor videoFrameExtractor;

        // VideoFrameAvailable thread access lock
        private static readonly object _lockObject = new();

        public SurveyorMediaPlayer()
        {
            this.InitializeComponent();
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
        public TListener InitializeMediator(SurveyorMediator mediator, MainWindow mainWindow)
        {
            _mediator = mediator;
            _mainWindow = mainWindow;

            _mediaPlayerHandler = new MediaPlayerHandler(_mediator, this, mainWindow);

            return _mediaPlayerHandler;
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
        internal async void Open(string mediaFileSpec)
        {
            Debug.WriteLine($"{CameraSide}: Open: Enter  (UIThread={DispatcherQueue.HasThreadAccess})");

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
                        _mediaOpen = false;
                        _mode = eMode.modeNone;
                        _frameIndexCurrent = 0;
                        _naturalDuration = TimeSpan.Zero;
                        _frameWidth = 0;
                        _frameHeight = 0;
                        _frameRate = -1;
                        _frameRateTimeSpan = TimeSpan.Zero;
                        _positionPausedMode = TimeSpan.Zero;

                        // Create a corresponding MediaPlayer for the MediaPlayerElement
                        MediaPlayerElement.SetMediaPlayer(new MediaPlayer());

                        // Event subscriptions MediaPlayer 
                        MediaPlayer mp = MediaPlayerElement.MediaPlayer;
                        mp.MediaEnded += MediaPlayer_MediaEnded;
                        mp.MediaFailed += MediaPlayer_MediaFailed;
                        mp.MediaOpened += MediaPlayer_MediaOpened;
                        mp.MediaPlayerRateChanged += MediaPlayer_MediaPlayerRateChanged;
                        mp.PlaybackMediaMarkerReached += MediaPlayer_PlaybackMediaMarkerReached;
                        mp.SourceChanged += MediaPlayer_SourceChanged;
                        mp.VideoFrameAvailable += MediaPlayer_VideoFrameAvailable;

                        // Remember the media Uri
                        _mediaUri = mediaFileSpec;

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

                        // This is used to extract frames from MediaPlayer on pause 
                        //???***videoFrameExtractor = new(MediaPlayerElement.MediaPlayer);
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
            return _mediaOpen;
        }


        /// <summary>
        /// Unscribe from the media player events and close the media and set the media open flag to false
        /// </summary>
        internal void Close()
        {
            // Check the player is actually open
            if (IsOpen())
            {
                // First detach the TimelineController if necessary
                // This is so we can control the MediaPlayer directly
                MediaPlayer mp = MediaPlayerElement.MediaPlayer;
                if (mp.TimelineController is not null)
                    mp.TimelineController = null;

                // Pause just in case the media is playing
                if (MediaPlayerElement.MediaPlayer.CurrentState != MediaPlayerState.Paused)
                {
                    Pause();
                }

                // Event PlaybackSession cancel subscriptions
                MediaPlaybackSession playbackSession = MediaPlayerElement.MediaPlayer.PlaybackSession;
                playbackSession.PlaybackStateChanged -= PlaybackSession_PlaybackStateChanged;
                playbackSession.BufferingStarted -= PlaybackSession_BufferingStarted;
                playbackSession.BufferingEnded -= PlaybackSession_BufferingEnded;
                playbackSession.NaturalDurationChanged -= PlaybackSession_NaturalDurationChanged;
                playbackSession.NaturalVideoSizeChanged -= PlaybackSession_NaturalVideoSizeChanged;
                playbackSession.PositionChanged -= PlaybackSession_PositionChanged;
                playbackSession.SeekableRangesChanged -= PlaybackSession_SeekableRangesChanged;
                playbackSession.SeekCompleted -= PlaybackSession_SeekCompleted;
                playbackSession.SupportedPlaybackRatesChanged -= PlaybackSession_SupportedPlaybackRatesChanged;


                // Event MediaPlayer cancel subscriptions
                mp.MediaEnded -= MediaPlayer_MediaEnded;
                mp.MediaFailed -= MediaPlayer_MediaFailed;
                mp.MediaOpened -= MediaPlayer_MediaOpened;
                mp.MediaPlayerRateChanged -= MediaPlayer_MediaPlayerRateChanged;
                mp.PlaybackMediaMarkerReached -= MediaPlayer_PlaybackMediaMarkerReached;
                mp.SourceChanged -= MediaPlayer_SourceChanged;
                mp.VideoFrameAvailable -= MediaPlayer_VideoFrameAvailable;
                mp.Dispose();

                // Release the resources used to render the video frame
                _vfar.Release();

                // Hide the media player and the frame image user control
                MediaPlayerElement.Visibility = Visibility.Collapsed;
                ImageFrame.Visibility = Visibility.Collapsed;
                ImageFrame.Source = null;

                // Reset the variables
                _mediaOpen = false;
                _mediaUri = "";
                _mode = eMode.modeNone;
                _frameIndexCurrent = 0;
                _naturalDuration = TimeSpan.Zero;
                _frameWidth = 0;
                _frameHeight = 0;
                _frameRate = -1;
                _frameRateTimeSpan = TimeSpan.Zero;
                _positionPausedMode = TimeSpan.Zero;

                // Signal the media close event via mediator
                _mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.Closed, CameraSide, _mode));
                Debug.WriteLine($"{CameraSide}: Info SurveyorMediaPlayer.Close");
            }
        }


        /// <summary>
        /// Used to sync or unsync media players. Called from the MediaStereoController
        /// </summary>
        /// <param name="mediaTimelineController">Either pass a MediaTimelineController instance or null to disable</param>
        internal void SetTimelineController(MediaTimelineController? mediaTimelineController, TimeSpan offset)
        {
            CheckIsUIThread();

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
                CheckIsUIThread();

                TimeSpan? ret = null;
                if (IsOpen())
                    if (MediaPlayerElement.MediaPlayer is not null && MediaPlayerElement.MediaPlayer.PlaybackSession is not null)
                        ret = MediaPlayerElement.MediaPlayer.PlaybackSession.Position;

                return ret;
            } 
            set 
            {
                CheckIsUIThread();

                if (value is not null && IsOpen())
                    if (MediaPlayerElement.MediaPlayer is not null && MediaPlayerElement.MediaPlayer.PlaybackSession is not null)
                        MediaPlayerElement.MediaPlayer.PlaybackSession.Position = (TimeSpan)value; 
            }
        }
        
        /// <summary>
        /// Get the current media frame width. Returns -1 if not set
        /// </summary>
        internal int FrameWidth
        {
            get
            {
                return (int)_frameWidth;
            }
        }


        /// <summary>
        /// Get the current media frame height. Returns -1 if not set
        /// </summary>
        internal int FrameHeight
        {
            get
            {
                return (int)_frameHeight;
            }
        }


        /// <summary>
        /// Getter/Setter for the Media frame duration
        /// </summary>
        internal TimeSpan TimePerFrame
        {
            get
            {
                return _frameRateTimeSpan;
            }
        }


        /// <summary>
        /// Check if the media is playing
        /// </summary>
        /// <returns></returns>
        internal bool IsPlaying()
        {
            CheckIsUIThread();

            if (IsOpen())
            {
                return MediaPlayerElement.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
            }
            else
                return false;
        }

        /// <summary>
        /// Play the media
        /// </summary>
        internal void Play()
        {
            CheckIsUIThread();

            if (IsOpen())
            {
                // Can only use this function if the media is not synchronized
                if (!IsMediaSynchronized())
                    MediaPlayerElement.MediaPlayer.Play();
                else
                    // Illegal to use this function if the media is synchronized
                    throw new InvalidOperationException("Can't use MediaPlayer.Play() when media is synchronized");
            }
        }

        /// <summary>
        /// Pause the media
        /// </summary>
        internal void Pause()
        {
            CheckIsUIThread();

            if (IsOpen() && _mode == eMode.modePlayer)
            {
                // Can only use this function if the media is not synchronized
                if (!IsMediaSynchronized())
                {
                    MediaPlayerElement.MediaPlayer.Pause();

                    // Move back one frame and forward one frame. This is a workaround to get the frame
                    // server to sync with the frame the player. Otherwise the the server frame is typically
                    // behind the player frame. However sometimes the displayed frame in
                    // the player sometimes behind the MediaPlaybackSession.Position.  In this case the
                    // server will sync to that forward position. This is the best we can do currently.
                    MediaPlayerElement.MediaPlayer.StepBackwardOneFrame();
                    MediaPlayerElement.MediaPlayer.StepForwardOneFrame();
                }
                else
                    // Illegal to use this function if the media is synchronized
                    throw new InvalidOperationException("Can't use MediaPlayer.Pause() when media is synchronized");
            }
        }


        /// <summary>
        /// Move the media forward(positive) or back(negative) by the timespan duration
        /// The function will move both players if they are locked together. If they are not locked together
        /// it will use cameraSide to determine which player to move
        /// </summary>        
        /// <param name="timeSpan"></param>
        internal void FrameMove(TimeSpan deltaPosition)
        {
            CheckIsUIThread();

            if (IsOpen())
            {
                // Can only use this function if the media is not synchronized
                if (!IsMediaSynchronized())
                {
                    if (_mode == eMode.modePlayer)
                        MediaPlayerElement.MediaPlayer.Pause();

                    // Calculate the new position
                    TimeSpan position = _positionPausedMode + deltaPosition;

                    // Check move is in bounds
                    if (position < TimeSpan.Zero)
                        position = TimeSpan.Zero;
                    else if (position > _naturalDuration)
                        position = _naturalDuration;

                    // Move to the relative position
                    _positionPausedMode = position;
                    Position = _positionPausedMode;

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
        internal void FrameMove(int frames)
        {
            CheckIsUIThread();

            if (IsOpen())
            {
                // Use the media player frame rate to calculate the timeSpan for the move
                TimeSpan deltaPosition = (TimeSpan)TimePerFrame * frames;

                FrameMove(deltaPosition);
            }
        }
        

        /// <summary>
        /// Move to the absolute position in the media
        /// </summary>
        /// <param name="timeSpan"></param>
        internal void FrameJump(TimeSpan position)
        {
            CheckIsUIThread();

            if (IsOpen())
            {
                // Can only use this function if the media is not synchronized
                if (!IsMediaSynchronized())
                {
                    if (_mode == eMode.modePlayer)
                       MediaPlayerElement.MediaPlayer.Pause();

                    // Check move is in bounds
                    if (position < TimeSpan.Zero)
                        position = TimeSpan.Zero;
                    else if (position > _naturalDuration)
                        position = _naturalDuration;

                    // Move to the absolute position
                    _positionPausedMode = position;
                    Position = _positionPausedMode;
                }
                else
                    // Illegal to use this function if the media is synchronized
                    throw new InvalidOperationException("Can't use MediaPlayer.FrameJump() when media is synchronized");
            }
        }



        /// <summary>
        /// Mute the sound
        /// </summary>
        internal void Mute()
        {
            CheckIsUIThread();

            // Mute this media player
            MediaPlayerElement.MediaPlayer.IsMuted = true;

            // Signal the Mute was sucessful
            _mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.Muted, CameraSide, _mode));
            Debug.WriteLine($"{CameraSide}: Info SureyorMediaPlayer.Mute  Muted");
        }


        /// <summary>
        /// Unmute the sound
        /// </summary>
        internal void Unmute()
        {
            CheckIsUIThread();

            // Unmute this media player
            MediaPlayerElement.MediaPlayer.IsMuted = false;

            // Signal the Unmuted was sucessful
            _mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.Unmuted, CameraSide, _mode));
            Debug.WriteLine($"{CameraSide}: Info SureyorMediaPlayer.Mute  Unmuted");
        }


        /// <summary>
        /// Set the media player playback rate
        /// </summary>
        /// <param name="speed"></param>
        internal void SetSpeed(float speed)
        {
            CheckIsUIThread();

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

            if (_frameRate != -1)
            {
                double frameIndexDouble = ts.Ticks * _frameRate / TimeSpan.TicksPerSecond;
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
            CheckIsUIThread();

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
        /// to save the frame to a file
        /// </summary>
        internal async void SaveCurrentFrame(string framesPath)
        {
            // Check if the players if loaded and paused
            if (IsOpen() && _mode == eMode.modeFrame)
            {
                if (_vfar.inputBitmap is not null && Position is not null)
                {
                    // Create the file name using the media name and the current timespan offset
                    string formattedTime = ((TimeSpan)Position).ToString("hh\\_mm\\_ss\\_fff");
                    string fileName = Path.Combine(framesPath, Path.GetFileNameWithoutExtension(_mediaUri) + $"_{formattedTime}.png");

                    //???
                    // _vfar.inputBitmap is a CanvasImage and that class has a SaveAsync method
                    // We can adjust this code, lose ConvertCanvasBitmapToSoftwareBitmap and change 
                    // SaveSoftwareBitmapToJPEG to SaveCanvasImageToPNG
                    //???

                    // Convert from a Canvas Bitmap to a SoftwareBitmap for writing to file  
                    SoftwareBitmap bmp = await ConvertCanvasBitmapToSoftwareBitmap(_vfar.inputBitmap);

                    // Save the frame to .jpeg
                    if (await SaveSoftwareBitmapToJPEG(bmp, fileName))
                        Debug.WriteLine($"{CameraSide}: SurveyorMediaPlayer.SaveCurrentFrame  Frame saved to: {fileName}");
                    else
                        report!.Error(CameraSide.ToString(), $"SurveyorMediaPlayer.SaveCurrentFrame  Failed to saved to: {fileName}");
                }
                else
                {
                    Debug.WriteLine($"{CameraSide}: Error SurveyorMediaPlayer.SaveCurrentFrame  The canvas bitmap buffer it null, nothing to write!");
                    report!.Error(CameraSide.ToString(), "SurveyorMediaPlayer.SaveCurrentFrame  The canvas bitmap buffer it null, nothing to write!");
                }
            }
            else
            {
                Debug.WriteLine($"{CameraSide}: Error SurveyorMediaPlayer.SaveCurrentFrame  Media not open or not in frame mode");
                report!.Error(CameraSide.ToString(), "SurveyorMediaPlayer.SaveCurrentFrame  Media not open or not in frame mode");
            }
        }



        ///
        /// EVENTS
        /// 


        /// <summary>
        /// MediaPlayerElement.MediaPlayer.PlaybackSession.PlaybackStateChanged
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private async void PlaybackSession_PlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            Debug.WriteLine($"{CameraSide}: PlaybackSession_PlaybackStateChanged: Enter PlaybackState={((MediaPlaybackSession)sender).PlaybackState}, NaturalDuration:{((MediaPlaybackSession)sender).NaturalDuration:hh\\:mm\\:ss\\.ff}");

            try
            {
                MediaPlaybackSession playbackSession = sender as MediaPlaybackSession;
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
                        try
                        {
                            // Remember we are now in Player mode using the media player to render the video
                            _mode = eMode.modePlayer;
                                
                            // Exit Frame Server mode so the Media Player can render the frame
                            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, () =>
                            {
                                MediaPlayerElement.MediaPlayer.IsVideoFrameServerEnabled = false;

                                // Display the Media Player (Helps with screen refresh issues media player has)
                                if (MediaPlayerElement.Visibility != Visibility.Visible)
                                    MediaPlayerElement.Visibility = Visibility.Visible;

                                // Hide the frame image user control
                                if (ImageFrame.Visibility != Visibility.Collapsed)
                                    ImageFrame.Visibility = Visibility.Collapsed;

                                Debug.WriteLine($"{CameraSide}: Info PlaybackSession_PlaybackStateChanged: Make Player visible and collapse Image Frame.");

                                // Inform the media control that the media is playing
                                _mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.Playing, CameraSide, _mode));

                                Debug.WriteLine($"{CameraSide}: PlaybackSession_PlaybackStateChanged: Playing & video frame event disabled, IsVideoFrameServerEnabled ={MediaPlayerElement.MediaPlayer.IsVideoFrameServerEnabled}");
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"  {CameraSide}: Error PlaybackSession_PlaybackStateChanged  {ex.Message}");
                        }
                        break;

                    case MediaPlaybackState.Paused:
                        // Remember we are now in Frame mode where we are responsible for rendering the frame
                        _mode = eMode.modeFrame;

                        // Capture the frame dimensions if not already known
                        if (_frameWidth == 0 && playbackSession.NaturalVideoWidth != 0)
                        {
                            _frameWidth = playbackSession.NaturalVideoWidth;
                            _frameHeight = playbackSession.NaturalVideoHeight;

                            // Signal the frame size
                            _mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.FrameSize, CameraSide, _mode)
                            {
                                frameWidth = (int?)_frameWidth,
                                frameHeight = (int?)_frameHeight
                            });
                        }

                        // Rememmber the position in paused mode
                        _positionPausedMode = sender.Position;
                        Debug.WriteLine($"MediaTimelineController_StateChanged: PositionOffset: {_positionPausedMode:hh\\:mm\\:ss\\.ff}");
                            
                        // Go into Frame Server mode so we can handle the frame rendering
                        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                        {
                            // Capture the frame rate if not already known
                            if (_frameRate == -1)
                            {
                                GetFrameRateAndTimePerFrame();
                            }

                            MediaPlayerElement.MediaPlayer.IsVideoFrameServerEnabled = true;

                            // Inform the media control that the media is playing
                            _mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.Paused, CameraSide, _mode));

                            Debug.WriteLine($"{CameraSide}: Info PlaybackSession_PlaybackStateChanged: Paused & video frame event enabled");
                        });
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{CameraSide}: Error PlaybackSession_PlaybackStateChanged: Exception: {ex.Message}");
            }
        }


        /// <summary>
        /// Buffering - show the progress ring
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void PlaybackSession_BufferingStarted(MediaPlaybackSession sender, object args)
        {
            _mainWindow!.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, /*async*/ () =>
                ProgressRing_Buffering.IsActive = true);

        }


        /// <summary>
        /// Buffering ended - hide the progress ring
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void PlaybackSession_BufferingEnded(MediaPlaybackSession sender, object args)
        {
            // Hide the progress ring
            _mainWindow!.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, /*async*/ () =>
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

            if (_frameWidth != playbackSession.NaturalVideoWidth || _frameHeight != playbackSession.NaturalVideoHeight)
            {
                _frameWidth = playbackSession.NaturalVideoWidth;
                _frameHeight = playbackSession.NaturalVideoHeight;

                // Signal the frame size
                _mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.FrameSize, CameraSide, _mode)
                {
                    frameWidth = (int?)_frameWidth,
                    frameHeight = (int?)_frameHeight
                });

                Debug.WriteLine($"{CameraSide}: Info PlaybackSession_NaturalVideoSizeChanged: {playbackSession.NaturalVideoWidth}x{playbackSession.NaturalVideoHeight}");
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
            if (_naturalDuration == TimeSpan.Zero)
            {
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {

                    MediaPlaybackSession playbackSession = sender as MediaPlaybackSession;

                    // Signel the media duration event via mediator if known
                    if (playbackSession.NaturalDuration != TimeSpan.Zero)
                    {
                        // Get the frame rate if not already known
                        if (_frameRate == -1)
                            GetFrameRateAndTimePerFrame();
                            

                        // Signal the media duration and frame rate via mediator (used by the MediaStereoController and MediaControl)
                        MediaPlayerEventData data = new(MediaPlayerEventData.eMediaPlayerEvent.DurationAndFrameRate, CameraSide, _mode)
                        {
                            duration = playbackSession.NaturalDuration,
                            frameRate = _frameRate
                        };
                        _mediaPlayerHandler?.Send(data);
                    }

                    Debug.WriteLine($"{CameraSide}: Info PlaybackSession_NaturalDurationChanged: (late discovery) NaturalDuration:{_naturalDuration:hh\\:mm\\:ss\\.ff}");
                });
            }
        }

        private void PlaybackSession_PositionChanged(MediaPlaybackSession sender, object args)
        {
            MediaPlaybackSession playbackSession = sender as MediaPlaybackSession;

            //???_frameIndexCurrent = GetFrameIndexFromPosition(playbackSession.Position);

            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                if (!IsMediaSynchronized())
                {
                    // If the media isn't syncronised signal the position of the MediaPlayer
                    // (If sync'd the timeline controller sends the poisition message)
                    MediaPlayerEventData data = new(MediaPlayerEventData.eMediaPlayerEvent.Position, CameraSide, _mode)
                    {
                        position = playbackSession.Position
                        //???frameIndex = _frameIndexCurrent
                    };
                    _mediaPlayerHandler?.Send(data);
                }
            });
            Debug.WriteLine($"{CameraSide}: Info PlaybackSession_PositionChanged:{sender.Position:hh\\:mm\\:ss\\.ff}");
        }

        private void PlaybackSession_SeekableRangesChanged(MediaPlaybackSession sender, object args)
        {
            MediaPlaybackSession playbackSession = sender as MediaPlaybackSession;
            Debug.WriteLine($"{CameraSide}: Info PlaybackSession_SeekableRangesChanged");
        }

        private void PlaybackSession_SeekCompleted(MediaPlaybackSession sender, object args)
        {
            MediaPlaybackSession playbackSession = sender as MediaPlaybackSession;

            _frameIndexCurrent = GetFrameIndexFromPosition(playbackSession.Position);

            Debug.WriteLine($"{CameraSide}: Info PlaybackSession_SeekCompleted: Total milliseconds:{playbackSession.Position.TotalMilliseconds:F1}ms, Frame Index={GetFrameIndexFromPosition(playbackSession.Position)}");
        }

        private void PlaybackSession_SupportedPlaybackRatesChanged(MediaPlaybackSession sender, object args)
        {
            MediaPlaybackSession playbackSession = sender as MediaPlaybackSession;
            Debug.WriteLine($"{CameraSide}: Info PlaybackSession_SupportedPlaybackRatesChanged: PlaybackRate:{playbackSession.PlaybackRate}");

        }

        private void MediaPlayer_PlaybackMediaMarkerReached(MediaPlayer sender, PlaybackMediaMarkerReachedEventArgs args)
        {

            MediaPlayer mediaPlayer = sender as MediaPlayer;
            Debug.WriteLine($"{CameraSide}: Info MediaPlayer_PlaybackMediaMarkerReached: being depricated instead use MediaPlaybackItem.TimedMetadataTracks");
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
        private /*???async*/ void MediaPlayer_MediaOpened(MediaPlayer sender, object args)
        {
            Debug.WriteLine($"{CameraSide}: MediaPlayer_MediaOpened: Enter  (UIThread={DispatcherQueue.HasThreadAccess})");
            //???await Task.Delay(150); // Found this and not sure it is needed

            try
            {
                _mainWindow!.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, async () =>
                {
                    // Get the natural duration of the media
                    _naturalDuration = sender.PlaybackSession.NaturalDuration - sender.TimelineControllerPositionOffset;

                    if (!IsRemoteFile(_mediaUri))
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
                    _mediaOpen = true;

                    // Signel the media open event via mediator
                    MediaPlayerEventData data = new(MediaPlayerEventData.eMediaPlayerEvent.Opened, CameraSide, _mode)
                    {
                        mediaFileSpec = this._mediaUri
                    };
                    _mediaPlayerHandler?.Send(data);

                    // Signel the media duration and frame rate via mediator if known (used by the MediaStereoController and MediaControl)
                    if (_naturalDuration != TimeSpan.Zero)
                    {
                        // Get the frame rate if not already known
                        if (_frameRate == 0.0)
                            _frameRate = GetCurrentFrameRate(MediaPlayerElement.MediaPlayer);

                        data = new(MediaPlayerEventData.eMediaPlayerEvent.DurationAndFrameRate, CameraSide, _mode)
                        {
                            duration = _naturalDuration,
                            frameRate = _frameRate
                        };
                        _mediaPlayerHandler?.Send(data);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{CameraSide}: Error MediaPlayer_MediaOpened: Exception: {ex.Message}");
            }

            Debug.WriteLine($"{CameraSide}: MediaPlayer_MediaOpened: Exit");
        }


        private void MediaPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            MediaPlayer mediaPlayer = sender as MediaPlayer;

            _mode = eMode.modeNone;

            Debug.WriteLine($"{CameraSide}: Info MediaPlayer_MediaFailed: Media playback error: {args.ErrorMessage}, Extended error code: {args.ExtendedErrorCode.HResult}");
        }

        private void MediaPlayer_MediaEnded(MediaPlayer sender, object args)
        {
            MediaPlayer mediaPlayer = sender as MediaPlayer;

            _mode = eMode.modeNone;

            Debug.WriteLine($"{CameraSide}: Info MediaPlayer_MediaEnded");
        }

        private void MediaPlayer_SourceChanged(MediaPlayer sender, object args)
        {
            MediaPlayer mediaPlayer = sender as MediaPlayer;
            Debug.WriteLine($"{CameraSide}: Info MediaPlayer_SourceChanged:");
        }



        /// <summary>
        /// Class to safely store the resources used to render the video frame
        /// these resources are created when the media is opened and released when the media is closed
        /// </summary>
        class VideoFrameAvailableResources
        {
            public SoftwareBitmap? frameServerDest = null;
            public CanvasImageSource? canvasImageSource = null;
            public CanvasBitmap? inputBitmap = null;
            public uint frameWidthSoftwareBitmap = 0;
            public uint frameHeightSoftwareBitmap = 0;
            public int videoFrameCount = 0;
            public bool IsSetup { get; set; } = false;

            /// <summary>
            /// Called to setup the resource if necessary
            /// </summary>
            /// <param name="mp"></param>
            /// <returns></returns>
            public bool SetupIfNecessary(MediaPlayer mp)
            {
                if (!IsSetup)
                {
                    // Remember the frame width and height used to create the resources
                    frameWidthSoftwareBitmap = mp.PlaybackSession.NaturalVideoWidth;
                    frameHeightSoftwareBitmap = mp.PlaybackSession.NaturalVideoHeight;

                    // Object used by Win2D for rendering graphics
                    CanvasDevice canvasDevice = CanvasDevice.GetSharedDevice();

                    // Creates a new SoftwareBitmap with the specified width, height, and format
                    // This bitmap will be used to hold the video frame data
                    frameServerDest = new SoftwareBitmap(BitmapPixelFormat.Rgba8, (int)frameWidthSoftwareBitmap, (int)frameHeightSoftwareBitmap, BitmapAlphaMode.Ignore);

                    // Creates a new CanvasImageSource with the specified dimensions and DPI (dots per inch)
                    canvasImageSource = new CanvasImageSource(canvasDevice, (float)frameWidthSoftwareBitmap, (float)frameHeightSoftwareBitmap, 96/*DisplayInformation.GetForCurrentView().LogicalDpi*/);//96); 

                    // Create inputBitMap to receive the frame from Media Player
                    inputBitmap = CanvasBitmap.CreateFromSoftwareBitmap(canvasDevice, frameServerDest);

                    // Check all setup ok
                    if (frameServerDest != null && canvasImageSource != null && inputBitmap != null)
                        IsSetup = true;
                    else
                        IsSetup = false;

                }
                return IsSetup;
            }

            /// <summary>
            /// Release all the resources
            /// </summary>
            public void Release()
            {
                if (frameServerDest != null)
                {
                    frameServerDest.Dispose();
                    frameServerDest = null;
                }
                canvasImageSource = null;
                inputBitmap = null;

                frameWidthSoftwareBitmap = 0;
                frameHeightSoftwareBitmap = 0;
                videoFrameCount = 0;

                IsSetup = false;
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
        private void MediaPlayer_VideoFrameAvailable(MediaPlayer mp, object args)
        {           
            // *****************************
            // **Not called from UI Thread**

            // *********************************************************************************************
            // * Check if the media player is in the correct mode to render the frame                      *
            // * Sometimes we observe that MediaPlayerElement.MediaPlayer.IsVideoFrameServerEnabled has    *
            // * been set to false, however we still get this event. So we need to check the mode and set  *
            // * the IsVideoFrameServerEnabled to false again and report                                   *
            // *********************************************************************************************
            if (_mode == eMode.modePlayer)
            {
                _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                {
                    // Force to false again
                    MediaPlayerElement.MediaPlayer.IsVideoFrameServerEnabled = false;
                });

                Debug.WriteLine($"{CameraSide}: MediaPlayer_VideoFrameAvailable: Warning ***************VideoFrameAvailable CALLED WHILE IN MODE PLAYER***************");

                // This call must be done otherwise MediaPlayer_VideoFrameAvailable will be called repeatly
                mp.CopyFrameToVideoSurface(_vfar.inputBitmap);
                return;
            }


            //???Debug.WriteLine($"{CameraSide}: MediaPlayer_VideoFrameAvailable Enter");

            // Reentry counter - increment
            _vfar.videoFrameCount++;

            _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                // Ensure only one thread at a time can access this section of code.
                // This is to prevent crashes
                lock (_lockObject)
                {
                    if (!_vfar.SetupIfNecessary(mp))
                    {
                        Debug.WriteLine($"    {CameraSide}: Error  Failed to setup resources");
                        return;
                    }

                    if (_vfar.frameWidthSoftwareBitmap != mp.PlaybackSession.NaturalVideoWidth || _vfar.frameHeightSoftwareBitmap != mp.PlaybackSession.NaturalVideoHeight)
                        Debug.WriteLine($"    {CameraSide}: Warning dimension change, SoftwareBitmap setup at ({_vfar.frameWidthSoftwareBitmap},{_vfar.frameHeightSoftwareBitmap}), this freame is ({mp.PlaybackSession.NaturalVideoWidth},{mp.PlaybackSession.NaturalVideoHeight})");

                    // Copy the frame from the media player to the inputBitmap
                    // This call must be done otherwise MediaPlayer_VideoFrameAvailable will be called repeatly
                    // With the same frame
                    try
                    {
                        mp.CopyFrameToVideoSurface(_vfar.inputBitmap);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"    {CameraSide}: Error MediaPlayer_VideoFrameAvailable: {ex.Message}");
                        return;
                    }

                    // Draw the frame if there is no frame pending to be drawn
                    if (_vfar.videoFrameCount == 1)
                    {
                        // Draw the frame
                        using (CanvasDrawingSession ds = _vfar.canvasImageSource!.CreateDrawingSession(Microsoft.UI.Colors.Black))
                        {
                            ds.DrawImage(_vfar.inputBitmap);
#if DEBUG // In Debug mode draw camera symbol on the frame so while debugging we can differentiate 
          // between the media player video surface and the image frame

                            // Define the position for the glyph
                            Vector2 glyphPosition = new Vector2(10, 10); // Top-left position of the glyph

                            // Define the font and size for the glyph
                            CanvasTextFormat textFormat = new CanvasTextFormat()
                            {
                                FontFamily = "Segoe MDL2 Assets",
                                FontSize = 48,
                                HorizontalAlignment = CanvasHorizontalAlignment.Left,
                                VerticalAlignment = CanvasVerticalAlignment.Top
                            };

                            // Draw the glyph (E722 is the image symbol in Segoe MDL2 Assets)
                            string pauseGlyph = "\uE158";
                            ds.DrawText(pauseGlyph, glyphPosition, Colors.White, textFormat);
#endif // End of DEBUG
                        }

                        // Make the ImageFrame visible if it is not already and we are not in player mode
                        // Note this is relatively slow function and the player mode is checked at the start
                        // but may have changed by the time we get here.
                        // If we are now in player mode then we mustn't make the ImageFrame visible and collapse the player
                        if (_mode != eMode.modePlayer && ImageFrame.Visibility == Visibility.Collapsed)
                        {
                            ImageFrame.Visibility = Visibility.Visible;
                            MediaPlayerElement.Visibility = Visibility.Collapsed;
                            Debug.WriteLine($"{CameraSide}: Info MediaPlayer_VideoFrameAvailable: Make Image frame visible and collapse player, mode={_mode}.");
                        }

                        // Set ImageFrame.Source last so the old images are not displayed
                        ImageFrame.Source = _vfar.canvasImageSource;

                        // Signal the frame is ready and pass the reference to the CanvasBitmap
                        _mediaPlayerHandler?.Send(new MediaPlayerEventData(MediaPlayerEventData.eMediaPlayerEvent.FrameRendered, CameraSide, _mode)
                        {
                            position = mp.PlaybackSession.Position,
                            canvasBitmap = _vfar.inputBitmap
                        }); 
                    }
                    //???else
                    //???    // Frame skipped
                    //???    Debug.WriteLine($"    {CameraSide}: Warning MediaPlayer_VideoFrameAvailable: ***Frame skipped***. Index from player position:{(mp.PlaybackSession.Position.TotalMilliseconds / 1000.0):F3}");


                    // Reentry counter - decrement
                    _vfar.videoFrameCount--;
                }
            });

           //??? Debug.WriteLine($"{CameraSide}: MediaPlayer_VideoFrameAvailable Exit");
        }


        ///
        /// PRIVATE FUNCTIONS
        ///

        //???***private class VideoFrameExtractor(MediaPlayer mediaPlayer)
        //{
        //    private MediaCapture? mediaCapture;
        //    private readonly MediaPlayer mediaPlayer = mediaPlayer;
        //    private readonly CanvasDevice canvasDevice = new();

        //    public async Task InitializeMediaCaptureAsync()
        //    {
        //        if (mediaCapture == null)
        //        {
        //            mediaCapture = new MediaCapture();

        //            var settings = new MediaCaptureInitializationSettings
        //            {
        //                StreamingCaptureMode = StreamingCaptureMode.Video,
        //                SourceGroup = null // Capture from playback
        //            };

        //            await mediaCapture.InitializeAsync(settings);
        //        }
        //    }

        //    public async Task<CanvasBitmap> CaptureFrameAsync()
        //    {
        //        await InitializeMediaCaptureAsync();

        //        if (mediaCapture == null)
        //        {
        //            throw new InvalidOperationException("MediaCapture is not initialized.");
        //        }

        //        var videoFrame = new Windows.Media.VideoFrame(BitmapPixelFormat.Bgra8,
        //                                                      (int)mediaPlayer.PlaybackSession.NaturalVideoWidth,
        //                                                      (int)mediaPlayer.PlaybackSession.NaturalVideoHeight);

        //        await mediaCapture.GetPreviewFrameAsync(videoFrame);

        //        SoftwareBitmap? softwareBitmap = videoFrame.SoftwareBitmap ?? throw new InvalidOperationException("Failed to capture a frame.");

        //        // Convert to CanvasBitmap
        //        return CanvasBitmap.CreateFromSoftwareBitmap(canvasDevice, softwareBitmap);
        //    }
        //}



        /// <summary>
        /// Function calulates the frame rate of the media and stores it in the private variable '_frameRate'.
        /// This fnction is called from the event 'MediaPlayer_MediaOpened'
        /// </summary>
        /// <param name="mediaPlayer"></param>
        private double GetCurrentFrameRate(MediaPlayer mediaPlayer)
        {
            CheckIsUIThread();

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
                Debug.WriteLine($"{CameraSide}: Info GetCurrentFrameRate: frame rate: {ret:F3}");
            else
                Debug.WriteLine($"{CameraSide}: Warning GetCurrentFrameRate: can't determine the frame rate{subError}");

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
                _mainWindow!.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
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
        /// This method converts a CanvasBitmap to a SoftwareBitmap
        /// it is a relatively slow operation CPU bound operation
        /// </summary>
        /// <param name="canvasBitmap"></param>
        /// <returns></returns>
        private static async Task<SoftwareBitmap> ConvertCanvasBitmapToSoftwareBitmap(CanvasBitmap canvasBitmap)
        {
            // Create a CanvasRenderTarget to draw the CanvasBitmap
            CanvasDevice device = CanvasDevice.GetSharedDevice();
            CanvasRenderTarget renderTarget = new CanvasRenderTarget(device, canvasBitmap.SizeInPixels.Width, canvasBitmap.SizeInPixels.Height, canvasBitmap.Dpi);

            using (var ds = renderTarget.CreateDrawingSession())
            {
                ds.Clear(Colors.Black); // Or any appropriate background color
                ds.DrawImage(canvasBitmap);
            }

            // Now convert the CanvasRenderTarget to a SoftwareBitmap
            SoftwareBitmap softwareBitmap;
            using (var stream = new InMemoryRandomAccessStream())
            {
                await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Bmp);
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
                softwareBitmap = await decoder.GetSoftwareBitmapAsync();
            }

            return softwareBitmap;
        }


        /// <summary>
        /// Distructively saves SoftwareBitmap to a .jpeg file
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        private static async Task<bool> SaveSoftwareBitmapToJPEG(SoftwareBitmap softwareBitmap, string filePath)
        {
            try
            {
                // Get the folder based on the file path and file name
                string? directoryPath = Path.GetDirectoryName(filePath);
                string fileName = Path.GetFileName(filePath);

                if (directoryPath is not null)
                {
                    // Get the storage folder
                    StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(directoryPath);

                    // Create the file, replace if it already exists
                    var storageFile = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);

                    // Open a stream for the file
                    using (IRandomAccessStream stream = await storageFile.OpenAsync(FileAccessMode.ReadWrite))
                    {
                        // Create an encoder for the JPEG format
                        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);

                        // Set the software bitmap to the encoder
                        encoder.SetSoftwareBitmap(softwareBitmap);

                        // Commit the image data to the file
                        await encoder.FlushAsync();
                    }
                }
                else
                {
                    Debug.WriteLine($"Error SaveSoftwareBitmapToJPEG: No path included on the file spec: filePath={filePath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error SaveSoftwareBitmapToJPEG: {ex.Message}");
                return false;
            }

            return true;
        }


        /// <summary>
        /// Called to calculate the frame rate and the time per frame
        /// This is called either when the media opened event or the natural 
        /// duration event are fired
        /// </summary>
        private void GetFrameRateAndTimePerFrame()
        {
            _frameRate = GetCurrentFrameRate(MediaPlayerElement.MediaPlayer);
            if (_frameRate != -1)
            {
                double ticksPerFrameDouble = TimeSpan.TicksPerSecond / _frameRate;
                long ticksPerFrame = (long)Math.Round(ticksPerFrameDouble, MidpointRounding.AwayFromZero);
                _frameRateTimeSpan = TimeSpan.FromTicks(ticksPerFrame);
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

        // ***END OF SurveyorMediaPlayer***
    }




    /// <summary>
    /// Used by the MediaPlayer User Control to inform other components on state changes within MediaPlayer
    /// </summary>
    public class MediaPlayerEventData
    {
        public MediaPlayerEventData(eMediaPlayerEvent e, SurveyorMediaPlayer.eCameraSide cameraSide, SurveyorMediaPlayer.eMode mode)
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
        public readonly SurveyorMediaPlayer.eMode mode;

        // Only used for eMediaPlayerAction.Opened and eMediaPlayerAction.SavedFrame
        public string? mediaFileSpec;

        // Only used for eMediaPlayerAction.DurationAndFrameRate
        public TimeSpan? duration;
        public double? frameRate;

        // Only used for Position & FrameRendered
        public TimeSpan? position;
        //???// Only used for Position
        //???public Int64? frameIndex;

        // Only used for eMediaPlayerAction.Position and eMediaPlayerAction.Buffering
        public float? percentage;

        // Only used for FrameRendered to pass the CanvasBitmap
        public CanvasBitmap? canvasBitmap;

        // Only used for FrameSize
        public int? frameWidth;
        public int? frameHeight;
    }

    public class MediaPlayerInfoData
    {
        public MediaPlayerInfoData(SurveyorMediaPlayer.eCameraSide cameraSide)
        {
            this.cameraSide = cameraSide;
        }
        public readonly SurveyorMediaPlayer.eCameraSide cameraSide;

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
