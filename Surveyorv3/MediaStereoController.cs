// Surveyor MediaStereoController
// Used to orchestrate the left and right media players and their media controls
// 
// Version 1.1
// Required a public method MainWindow.MediaBackToWindow() and MediaFullScreen() to return the media players to the stereo layout
// Added support to Save a frame
// Version 1.2  26 Feb 2025
// Add a method to swap the targets is user LeftA mixed up with RightB (and therefore LeftB with RightA
// Version 1.3  09 Mar 2025
// Refractored based on ExampleMediaTimelineController

using Surveyor.Events;
using Surveyor.User_Controls;
using System;
using Microsoft.UI.Dispatching;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media;
using static Surveyor.MediaStereoControllerEventData;
using Surveyor.Helper;
using Windows.Media.Playback;



#if !No_MagnifyAndMarkerDisplay
using static Surveyor.User_Controls.MagnifyAndMarkerControlEventData;
using static Surveyor.User_Controls.MagnifyAndMarkerDisplay;
#endif
using static Surveyor.User_Controls.SurveyorMediaPlayer;


namespace Surveyor
{

    internal class MediaStereoController
    {
        // Copy of the Mediator
        private readonly SurveyorMediator? mediator;

        // Copy of the reporter
        private readonly Reporter? report;

        // Declare the mediator handler for MediaStereoController
        private readonly MediaControllerHandler? mediaControllerHandler;

        // Copy of the main window
        private readonly MainWindow mainWindow;
        // Copy of the left and right MediaPlayer controls
        private readonly SurveyorMediaPlayer mediaPlayerLeft;
        private readonly SurveyorMediaPlayer mediaPlayerRight;

        // Copy of the primary and secondary MediaControls
        private readonly SurveyorMediaControl mediaControlPrimary;
        private readonly SurveyorMediaControl mediaControlSecondary;

        // Copy of the left and right Magnify and Marker controls
#if !No_MagnifyAndMarkerDisplay
        private readonly MagnifyAndMarkerDisplay magnifyAndMarkerDisplayLeft;
        private readonly MagnifyAndMarkerDisplay magnifyAndMarkerDisplayRight;
#endif

        // Copy of the left and right MediaInfo controls
        //???private readonly MediaInfo _mediaInfoLeft;
        //???private readonly MediaInfo _mediaInfoRight;

        // Indicates if the two MediaPlayers are operating locked together or indepentently 
        private bool mediaSynchronized = false;                                // Set if the Left and Right players are now controlled via the MediaTimelineControler
        private TimeSpan mediaSynchronizedFrameOffset = TimeSpan.Zero;         // Position: Left is started before Right. Negative Left is started after Right
        private MediaTimelineController? mediaTimelineController = null;       // The MediaTimelineController that is controlling the Left and Right MediaPlayers
        private TimeSpan _maxNaturalDurationForController = TimeSpan.Zero;
        private double _frameRate = 0.0;

        // Position of the MediaTimelineController whilst paused and moving frame by frame
        TimeSpan mediaTimelineControllerPositionPausedMode = TimeSpan.Zero;

        // EventControl (existing measurements etc)
        private readonly EventsControl? eventsControl = null;

        // Species Selector dialog class
        public SpeciesSelector speciesSelector = new();

        // StereoProjection class        
        public StereoProjection stereoProjection;



        public MediaStereoController(MainWindow _mainWindow, Reporter _report, 
                                     SurveyorMediator _mediator, 
                                     SurveyorMediaPlayer _mediaPlayerLeft, SurveyorMediaPlayer _mediaPlayerRight, 
                                     SurveyorMediaControl _mediaControlPrimary, SurveyorMediaControl _mediaControlSecondary,
#if !No_MagnifyAndMarkerDisplay
                                     MagnifyAndMarkerDisplay _magnifyAndMarkerDisplayLeft, MagnifyAndMarkerDisplay _magnifyAndMarkerDisplayRight,
#endif
                                     EventsControl _eventsControl,
                                     StereoProjection _stereoProjection/*,
                                     SurveyorMediaInfo mediaInfoLeft, SurveyorMediaInfo mediaInfoRight*/)
        {
            // Remember the main window
            mainWindow = _mainWindow;

            // Remember the reporter
            report = _report;

            // Remember Mediator
            mediator = _mediator;

            // Remember media player controls and set the reporter
            mediaPlayerLeft = _mediaPlayerLeft;
            mediaPlayerLeft.CameraSide = SurveyorMediaPlayer.eCameraSide.Left;
            mediaPlayerLeft.SetReporter(report);
            mediaPlayerRight = _mediaPlayerRight;
            mediaPlayerRight.CameraSide = SurveyorMediaPlayer.eCameraSide.Right;
            mediaPlayerRight.SetReporter(report);

            // Remember media controls and set the reporter
            mediaControlPrimary = _mediaControlPrimary;
            mediaControlPrimary.ControlType = SurveyorMediaControl.eControlType.Primary;
            mediaControlPrimary.SetReporter(report);
            mediaControlSecondary = _mediaControlSecondary;
            mediaControlSecondary.ControlType = SurveyorMediaControl.eControlType.Secondary;
            mediaControlSecondary.SetReporter(report);

            // Remember the Magnify and Marker controls and set the reporter
#if !No_MagnifyAndMarkerDisplay
            magnifyAndMarkerDisplayLeft = _magnifyAndMarkerDisplayLeft;
            magnifyAndMarkerDisplayRight = _magnifyAndMarkerDisplayRight;
#endif

            // Remember the EventControl
            eventsControl = _eventsControl;

            // Remember media info controls
            //???_mediaInfoLeft = mediaInfoLeft;
            //???_mediaInfoLeft.CameraSide = MediaInfo.eCameraSide.Left;
            //???_mediaInfoRight = mediaInfoRight;
            //???_mediaInfoRight.CameraSide = MediaInfo.eCameraSide.Right;


            // Initialize mediator for both media player controls
            mediaPlayerLeft.InitializeMediator(mediator, mainWindow);
            mediaPlayerRight.InitializeMediator(mediator, mainWindow);

            // Initialize mediator for both mediacontrols
            mediaControlPrimary.InitializeMediator(mediator, mainWindow);
            mediaControlSecondary.InitializeMediator(mediator, mainWindow);

            // Initialize mediator for both Magnify and Marker controls
#if !No_MagnifyAndMarkerDisplay
            magnifyAndMarkerDisplayLeft.InitializeMediator(mediator, mainWindow);
            magnifyAndMarkerDisplayRight.InitializeMediator(mediator, mainWindow);
#endif

            // Initialize mediator for both media info controls
            //???_mediaInfoLeft.InitializeMediator(mediator, mainWindow);
            //???_mediaInfoRight.InitializeMediator(mediator, mainWindow);

            // Remember the StereoProjection class
            stereoProjection = _stereoProjection;
            stereoProjection.SetReporter(report);

            // Setup the Handler for the MainWindow
            mediaControllerHandler = new MediaControllerHandler(mediator, mainWindow, this);

        }


        ///
        /// PUBLIC METHODS
        ///


        /// <summary>
        /// Diags dump of class information
        /// </summary>
        public void DumpAllProperties()
        {
            DumpClassPropertiesHelper.DumpAllProperties(this, /*ignore*/"mediator,report,mediaControllerHandler,mainWindow,mediaPlayerLeft,mediaPlayerRight,mediaControlPrimary,mediaControlSecondary,magnifyAndMarkerDisplayLeft,magnifyAndMarkerDisplayRight,mediaTimelineController,eventsControl,speciesSelector,stereoProjection");
            DumpClassPropertiesHelper.DumpAllProperties(speciesSelector, /*ignore*/"_contentLoaded");
        }


        /// <summary>
        /// Open left and right media files. If timeSpanOffset is null allow media to play independently. If not null, 
        /// lock the media together and offset the right media by the timeSpanOffset
        /// If the timeSpanOffset is positive the left media will start at zero and the right media will start at the 
        /// timeSpanOffset. If the timeSpanOffset is negative the right media will start at zero and the left media will
        /// start at the timeSpanOffset
        /// </summary>
        /// <param name="mediaFileSpecLeft"></param>
        /// <param name="mediaFileSpecRight"></param>
        /// <param name="tempSpanOffset"></param>
        /// <returns></returns>
        public async Task<int> MediaOpen(string mediaFileSpecLeft, string mediaFileSpecRight, TimeSpan? timeSpanOffset)
        {
            CheckIsUIThread();

            int ret = 0;

            // Reset
            mediaSynchronized = false;
            mediaSynchronizedFrameOffset = TimeSpan.Zero;
            mediaTimelineController = null;
            _maxNaturalDurationForController = TimeSpan.Zero;
            _frameRate = 0.0;
            mediaTimelineControllerPositionPausedMode = TimeSpan.Zero;

            // Open both files
            if (!string.IsNullOrEmpty(mediaFileSpecLeft))
                await mediaPlayerLeft.Open(mediaFileSpecLeft);

            if (!string.IsNullOrEmpty(mediaFileSpecRight))
                await mediaPlayerRight.Open(mediaFileSpecRight);

            // If the timeSpanOffset is not null and both media files opened then request the media
            // players to be locked together
            if (timeSpanOffset != null)
            {
                // Wait for media to open
                int tries = 0;
                while ((!string.IsNullOrEmpty(mediaFileSpecLeft) && !mediaPlayerLeft.IsOpen()) &&
                       (!string.IsNullOrEmpty(mediaFileSpecRight) && !mediaPlayerRight.IsOpen()) &&
                       tries < 20)
                {
                    // Sleep 100ms
                    await Task.Delay(250);
                    tries++;
                }

                await Task.Delay(100);
                await MediaLockMediaPlayers((TimeSpan)timeSpanOffset);
            }
            else
            {
                MediaUnlockMediaPlayers();
            }

            // Make sure the media players and controls are not in full screen mode
            mainWindow.MediaBackToWindow();
            mediaControlPrimary.MediaFullScreen(false/*TrueYouAreFullFalseYouAreRestored*/, null);
            mediaControlSecondary.MediaFullScreen(false/*TrueYouAreFullFalseYouAreRestored*/, null);

            return ret;
        }


        /// <summary>
        /// Close media on eft and right media players (if open). Reset variables
        /// </summary>
        public async Task MediaClose()
        {
            CheckIsUIThread();

            // This is Pause the media if it is synchronized and playing
            if (mediaSynchronized)                
                await Pause(eCameraSide.None);
            else
            {
                await Pause(eCameraSide.Left);
                await Pause(eCameraSide.Right);
            }

            // Wait to settle
            await Task.Delay(500);


            if (mediaPlayerLeft.IsOpen())
            {
                await mediaPlayerLeft.Close();
                mediaControlPrimary.Clear();
            }
            if (mediaPlayerRight.IsOpen())
            {
                await mediaPlayerRight.Close();
                mediaControlSecondary.Clear();
            }

            // Reset
            mediaSynchronized = false;
            mediaSynchronizedFrameOffset = TimeSpan.Zero;
            mediaTimelineController = null;
            _maxNaturalDurationForController = TimeSpan.Zero;
            _frameRate = 0.0;
            mediaTimelineControllerPositionPausedMode = TimeSpan.Zero;
        }


        /// <summary>
        /// Tests if either media players are open
        /// </summary>
        /// <returns>true is either players are open</returns>
        public bool MediaIsOpen()
        {
            CheckIsUIThread();

            return (mediaPlayerLeft.IsOpen() || mediaPlayerRight.IsOpen());
        }
      

        /// <summary>
        /// Lock the two mediaplayer together at their current offset position
        /// </summary>
        /// <returns></returns>
        public async Task<bool> MediaLockMediaPlayers()
        {
            CheckIsUIThread();

            bool ret = false;

            if (!mediaSynchronized)
            {
                // Lock the players at their current position
                ret = await MediaLockMediaPlayers(null);
            }
            return ret;
        }


        /// <summary>
        /// Lock the two mediaplayer together at the specified offset position
        /// </summary>
        /// <param name="_lockedMediaPlayersFrameOffset"></param>
        /// <returns></returns>
        public async Task<bool> MediaLockMediaPlayers(TimeSpan? _lockedMediaPlayersFrameOffset)
        {
            CheckIsUIThread();

            bool ret = false;

            if (!mediaSynchronized)
            {
                // Create the MediaTimelineController
                mediaTimelineController = new MediaTimelineController();
                mediaTimelineController.StateChanged += MediaTimelineController_StateChanged;
                mediaTimelineController.PositionChanged += MediaTimelineController_PositionChanged;
                //_mediaTimelineController.Ended += MediaTimelineController_Ended;    // Incase we need later
                //_mediaTimelineController.Failed += MediaTimelineController_Failed;  // Incase we need later

                // Wait the media players to be ready
                await WaitForPlayersToHaveValidPosition(mediaPlayerLeft, mediaPlayerRight);

                // Remember the current Media Player Position. This is so we can check that the players
                // did actually move to the correct position later in this method
                TimeSpan leftPositionActual = (TimeSpan)mediaPlayerLeft.Position!;
                TimeSpan rightPositionActual = (TimeSpan)mediaPlayerRight.Position!;

                if (_lockedMediaPlayersFrameOffset is null)
                {
                    // Lock in the current player play position.  The user is manually syncing the media players
                    if (mediaPlayerLeft.Position is not null && mediaPlayerRight.Position is not null)
                    {

                        // Correct on frame boundaries (rounding down is ok)
                        long leftFrame = (long)(leftPositionActual.TotalMilliseconds * _frameRate / 1000.0);
                        long rightFrame = (long)(rightPositionActual.TotalMilliseconds * _frameRate / 1000.0);
                        TimeSpan leftPositionRounded = TimeSpan.FromMilliseconds(leftFrame * 1000 / _frameRate);
                        TimeSpan rightPositionRounded = TimeSpan.FromMilliseconds(rightFrame * 1000 / _frameRate);
                        mediaSynchronizedFrameOffset = (TimeSpan)rightPositionRounded - (TimeSpan)leftPositionRounded;

                        // Lock in the current player play position delta
                        TimeSpan leftOffset = mediaSynchronizedFrameOffset > TimeSpan.Zero ? TimeSpan.Zero : (TimeSpan)(-mediaSynchronizedFrameOffset);
                        TimeSpan rightOffset = mediaSynchronizedFrameOffset > TimeSpan.Zero ? (TimeSpan)(mediaSynchronizedFrameOffset) : TimeSpan.Zero;
                        mediaPlayerLeft.SetTimelineController(mediaTimelineController, leftOffset);
                        mediaPlayerRight.SetTimelineController(mediaTimelineController, rightOffset);
                        Debug.WriteLine($"MediaLockMediaPlayers: Lock PositionOffset: (Left:{leftPositionRounded.TotalMilliseconds / 1000.0:F3}, Right:{rightPositionRounded.TotalMilliseconds / 1000.0:F3})");

                        // Let players settle
                        await Task.Delay(100);

                        // Engaging MedaTimelineController will cause the media players to jump to the new start position
                        // of the MediaTimelineController. We want to lock the players but stay at the original point
                        // However once the MediaTimelineController is engaged, the Position can only be move using 
                        // MediaPlayer.TimelineController.Position. 
                        if (mediaSynchronizedFrameOffset >= TimeSpan.Zero)
                            mediaTimelineController.Position = leftPositionRounded;
                        else
                            mediaTimelineController.Position = rightPositionRounded;


                        // Save the sync point as an event so the user can return to this point
                        if (mediaSynchronizedFrameOffset >= TimeSpan.Zero)
                            SurveyStereoSyncPointSelected(leftPositionRounded/*MediaTimelineController*/, leftPositionRounded, rightPositionRounded);
                        else
                            SurveyStereoSyncPointSelected(rightPositionRounded/*MediaTimelineController*/, leftPositionRounded, rightPositionRounded);
                    }
                }
                else
                {
                    // The media has just been opened and we know the offset between the two media players

                    // Set a common controller between the two media players
                    TimeSpan leftOffset = _lockedMediaPlayersFrameOffset > TimeSpan.Zero ? TimeSpan.Zero : (TimeSpan)(-_lockedMediaPlayersFrameOffset);
                    TimeSpan rightOffset = _lockedMediaPlayersFrameOffset > TimeSpan.Zero ? (TimeSpan)(_lockedMediaPlayersFrameOffset) : TimeSpan.Zero;

                    // Remember the offset between the two media players
                    mediaSynchronizedFrameOffset = (TimeSpan)_lockedMediaPlayersFrameOffset;

                    mediaPlayerLeft.SetTimelineController(mediaTimelineController, leftOffset);
                    mediaPlayerRight.SetTimelineController(mediaTimelineController, rightOffset);
                    Debug.WriteLine($"MediaLockMediaPlayers: PositionOffset: (Left:{(leftOffset.TotalMilliseconds / 1000.0):F3}, Right:{(rightOffset.TotalMilliseconds / 1000.0):F3})");


                    // Check the mediaplayers moved
                    int tries = 0;

                    while ((leftPositionActual == (TimeSpan)mediaPlayerLeft.Position && rightPositionActual == (TimeSpan)mediaPlayerRight.Position) &&
                           tries < 20)
                    {
                        // Sleep 100ms
                        await Task.Delay(100);
                        tries++;
                    }
                }

                // Wait to settle players
                await Task.Delay(250);

                // Check the actual media offset is what we need
                // I have seen it not use the provided offset (can be -2 to 2 frames out)
                leftPositionActual = (TimeSpan)mediaPlayerLeft.Position!;
                rightPositionActual = (TimeSpan)mediaPlayerRight.Position!;
                TimeSpan mediaSynchronizedFrameOffsetTest = (TimeSpan)rightPositionActual - (TimeSpan)leftPositionActual;

                if (mediaSynchronizedFrameOffsetTest != mediaSynchronizedFrameOffset)
                    Debug.WriteLine($"MediaLockMediaPlayers: Warning Synchronization Offset: Required:{mediaSynchronizedFrameOffset.TotalMilliseconds / 1000.0:F3}, Actual:{mediaSynchronizedFrameOffsetTest.TotalMilliseconds / 1000.0:F3})");

                    
                // Indicate we have locked the media players
                mediaSynchronized = true;  


                // Signal the media is synchronized (This is used by the MainWindow and the primary MediaControls)
                mediaControllerHandler?.Send(new MediaStereoControllerEventData(eMediaStereoControllerEvent.MediaSynchronized)
                {
                    positionOffset = mediaSynchronizedFrameOffset
                });

                ret = true;

            }

            return ret;
        }


        /// <summary>
        /// Wait for the media players to have a valid Position (non-null). This is used to wait for the media players to
        /// be ready
        /// </summary>
        /// <param name="mediaPlayerLeft"></param>
        /// <param name="mediaPlayerRight"></param>
        /// <returns></returns>
        private static async Task WaitForPlayersToHaveValidPosition(SurveyorMediaPlayer mediaPlayerLeft, SurveyorMediaPlayer mediaPlayerRight)
        {
            const int maxWaitTimeMs = 3000; // Max time to wait
            const int pollIntervalMs = 50;
            int waited = 0;
            Stopwatch stopwatch = new();

            stopwatch.Start();
            while (waited < maxWaitTimeMs)
            {
                var leftPos = mediaPlayerLeft.Position;
                var rightPos = mediaPlayerRight.Position;

                // Wait until both positions have advanced from zero
                if (leftPos is not null && rightPos is not null)
                {
                    Debug.WriteLine($"WaitForPlayersToHaveValidPosition Ready waited {stopwatch.ElapsedMilliseconds}ms");
                    return;
                }

                await Task.Delay(pollIntervalMs);
                waited += pollIntervalMs;
            }
            stopwatch.Stop();
            
            Debug.WriteLine($"WaitForPlayersToHaveValidPosition Timed out waiting {stopwatch.ElapsedMilliseconds}ms for media players to start");
        }


        /// <summary>
        /// Used to unlock the media players.
        /// </summary>
        public void MediaUnlockMediaPlayers()
        {
            // UI Thread - This itself doesn't need to be on the UI thread, but the function it calls do

            if (mediaSynchronized)
            {
                // Release the media players to be indepentant
                mediaPlayerLeft.SetTimelineController(null, TimeSpan.Zero);
                mediaPlayerRight.SetTimelineController(null, TimeSpan.Zero);

                // Reference the MediaTimelineController
                mediaTimelineController = null;

                // Indicate we have unlocked the media players
                mediaSynchronized = false;
            }

            // Signal the media is not synchronized
            // Signal this even if the media was not synchronized
            mediaControllerHandler?.Send(new MediaStereoControllerEventData(eMediaStereoControllerEvent.MediaUnsynchronized));

        }


        /// <summary>
        /// Check if the media is playing
        /// </summary>
        /// <returns></returns>
        public bool IsPlaying()
        {
            if (mediaSynchronized)
                return mediaTimelineController!.State == MediaTimelineControllerState.Running;
            else
                return mediaPlayerLeft.IsPlaying() || mediaPlayerRight.IsPlaying();
        }


        /// <summary>
        /// Play the media. If the media is locked together then both media players will play
        /// If seperate the cameraSide will determine which media player will play
        /// USES 'Internal' to allow Unit Testing
        /// </summary>
        /// <param name="cameraSide"></param>
        internal void Play(eCameraSide cameraSide)
        {
            CheckIsUIThread();

            if (mediaSynchronized)
            {
                // Play is called on both media players JUST to set the mode to Play. The MediaTimelineController
                // will control the play
                mediaPlayerLeft.SetPlayMode();     
                mediaPlayerRight.SetPlayMode();

                // MediaPlayers are locked together so control via the MediaTimelineController
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} MediaTimelineController Resume requested");
                mediaTimelineController!.Resume();
                Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} MediaTimelineController Resume returned");
            }
            else
            {
                if (cameraSide == eCameraSide.Left)
                    mediaPlayerLeft.Play();
                else
                    mediaPlayerRight.Play();
            }
        }


        /// <summary>
        /// Pause the media. If the media is locked together then both media players will pause
        /// If seperate the cameraSide will determine which media player will play
        /// USES 'Internal' to allow Unit Testing
        /// </summary>
        /// <param name="cameraSide"></param>
        internal async Task Pause(eCameraSide cameraSide)
        {
            CheckIsUIThread();

            if (mediaSynchronized)
            {
                // We Pause the players using the MediaTimelineController
                // We wait for the players to be paused 
                // Once paused to confirm the sync offset is correct and if not we move a frame forward
                // We then grab the frame and display it
                mediaTimelineController!.Pause();


                // Wait for Pause state to be reached
                bool paused = await WaitForMediaTimelinePaused(TimeSpan.FromMilliseconds(2000)); // Timeout of 2 seconds

                if (paused)
                {
                    bool forwardFrame = false;

                    // Check Mediaplayer Poistions and MediaTimeLineController positon and offset are correct
                    TimeSpan currentMediaOffset = (TimeSpan)mediaPlayerRight.Position! - (TimeSpan)mediaPlayerLeft.Position!;

                    if (currentMediaOffset != mediaSynchronizedFrameOffset)
                    {
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} Both Warning MediaStereoController.Pause: The MediaPlayers position offsets are not correct, open offset:{((TimeSpan)mediaSynchronizedFrameOffset!).TotalSeconds:F3}s, current offset:{currentMediaOffset.TotalSeconds:F3}");
                        forwardFrame = true;
                    }
                    else
                    {
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} Both Info MediaStereoController.Pause: The MediaPlayers position offsets are correct, open offset:{((TimeSpan)mediaSynchronizedFrameOffset!).TotalSeconds:F3}s, current offset:{currentMediaOffset.TotalSeconds:F3}, MediaController Position:{mediaTimelineController.Position.TotalSeconds:F3}, Left Position:{((TimeSpan)mediaPlayerLeft.Position).TotalSeconds:F3}, Right Position:{((TimeSpan)mediaPlayerRight.Position).TotalSeconds:F3}");
                    }

                    // Force a forware frame regardless
                    forwardFrame = true;

                    // Forward frame
                    if (forwardFrame)
                    {
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} Both Info PauseControl: Forcing a frame forward to maintain sync");
                        await Task.Delay(10);
                        await FrameMove(eCameraSide.Left/*Doesn't matter which because we are sync'd*/, 1);


                        // Wait again for pause
                        paused = await WaitForMediaTimelinePaused(TimeSpan.FromMilliseconds(2000));
                    }

                    if (paused)
                    {
                        // Read the frame from MediaPlayers and copy to the ImageFrames
                        mediaPlayerLeft.GrabAndDisplayFrame();
                        mediaPlayerRight.GrabAndDisplayFrame();
                    }
                }
                else
                {
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} Both Warning PauseControl: MediaTimelineController did not reach Paused state in time!");
                }
            }
            else
            {
                if (cameraSide == eCameraSide.Left)
                    await mediaPlayerLeft.Pause();
                else
                    await mediaPlayerRight.Pause();
            }
        }



        /// <summary>
        /// USed to wait the MediaTimelineController to reach the Paused state
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns></returns>
        private async Task<bool> WaitForMediaTimelinePaused(TimeSpan timeout)
        {
            if (mediaTimelineController!.State != MediaTimelineControllerState.Paused)
            {
                var tcs = new TaskCompletionSource<bool>();
                void Handler(MediaTimelineController sender, object args)
                {
                    if (sender.State == MediaTimelineControllerState.Paused)
                    {
                        sender.StateChanged -= Handler; //  Unsubscribe to prevent memory leaks
                        tcs.TrySetResult(true); //  Mark as completed
                    }
                }

                mediaTimelineController!.StateChanged += Handler;

                // Wait for either the event OR timeout
                if (await Task.WhenAny(tcs.Task, Task.Delay(timeout)) == tcs.Task)
                {
                    return true; // Successfully paused
                }
                else
                {
                    mediaTimelineController.StateChanged -= Handler; //  Ensure cleanup on timeout
                    Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} MediaTimelineController.WaitForMediaTimelinePaused: Warning Timeout waiting for to reach Paused state, current state is {mediaTimelineController!.State}!");
                    return false; //  Timed out
                }
            }
            else
            {
                return true; // Already paused
            }
        }


        /// <summary>
        /// Move the media forward(positive) or back(negative) by the timespan duration
        /// The function will move both players if they are locked together. If they are not locked together
        /// it will use cameraSide to determine which player to move
        /// USES 'Internal' to allow Unit Testing
        /// </summary>
        /// <param name="cameraSide"></param>
        /// <param name="timeSpan"></param>
        internal async Task FrameMove(eCameraSide cameraSide, TimeSpan deltaPosition)
        {
            CheckIsUIThread();

            if (IsPlaying())
                await Pause(cameraSide);

            if (mediaSynchronized)
            {
                if (mediaTimelineController!.State == MediaTimelineControllerState.Paused)
                {
                    // Enable Frame Server
                    mediaPlayerLeft.FrameServerEnable(true);
                    mediaPlayerRight.FrameServerEnable(true);

                    // Calculate the new position
                    TimeSpan position = mediaTimelineControllerPositionPausedMode + deltaPosition;

                    // Check move is in bounds
                    if (position < TimeSpan.Zero)
                        position = TimeSpan.Zero;
                    else if (position > _maxNaturalDurationForController)
                        position = _maxNaturalDurationForController;

                    // Move to the relative position
                    mediaTimelineControllerPositionPausedMode = position;
                    mediaTimelineController.Position = mediaTimelineControllerPositionPausedMode;

                }
            }
            else
            {
                if (cameraSide == eCameraSide.Left)
                    await mediaPlayerLeft.FrameMove(deltaPosition);
                else
                    await mediaPlayerRight.FrameMove(deltaPosition);
            }
        }


        /// <summary>
        /// Move the media forward(positive) or back(negative) by the number of frames
        /// The function will move both players if they are locked together. If they are not locked together
        /// it will use cameraSide to determine which player to move
        /// USES 'Internal' to allow Unit Testing
        /// </summary>
        /// <param name="cameraSide"></param>
        /// <param name="frames">negative move back, positive move forward</param>
        internal async Task FrameMove(eCameraSide cameraSide, int frames)
        {
            CheckIsUIThread();

            if (mediaSynchronized)
            {
                // Use the left media player to calculate the timeSpan for MediaTimelineController
                // This replies on the media in both players having the same frame rate!
                TimeSpan leftTimePerFrame = (TimeSpan)mediaPlayerLeft.TimePerFrame;
                TimeSpan timeSpan = leftTimePerFrame * frames;

                await FrameMove(cameraSide, timeSpan);
            }
            else
            {
                if (cameraSide == eCameraSide.Left)
                {
                    TimeSpan leftTimePerFrame = (TimeSpan)mediaPlayerLeft.TimePerFrame;
                    TimeSpan timeSpan = leftTimePerFrame * frames;
                    await mediaPlayerLeft.FrameMove(timeSpan);
                }
                else
                {
                    TimeSpan leftTimePerFrame = (TimeSpan)mediaPlayerRight.TimePerFrame;
                    TimeSpan timeSpan = leftTimePerFrame * frames;
                    await mediaPlayerRight.FrameMove(timeSpan);
                }
            }
        }


        /// <summary>
        /// Move to the absolute position in the media
        /// USES 'Internal' to allow Unit Testing
        /// </summary>
        /// <param name="cameraSide"></param>
        /// <param name="timeSpan"></param>
        internal void FrameJump(eCameraSide cameraSide, TimeSpan position)
        {
            if (mediaSynchronized)
            {
                // Check move is in bounds
                if (position < TimeSpan.Zero)
                    position = TimeSpan.Zero;
                else if (position > _maxNaturalDurationForController)
                    position = _maxNaturalDurationForController;

                if (mediaTimelineController!.State == MediaTimelineControllerState.Paused)
                {
                    // Enable Frame Server
                    mediaPlayerLeft.FrameServerEnable(true);
                    mediaPlayerRight.FrameServerEnable(true);
                }

                // Move to the absolute position
                mediaTimelineControllerPositionPausedMode = position;
                mediaTimelineController.Position = mediaTimelineControllerPositionPausedMode;
            }
            else
            {
                if (cameraSide == eCameraSide.Left)
                    mediaPlayerLeft.FrameJump(position);
                else
                    mediaPlayerRight.FrameJump(position);
            }
        }


        /// <summary>
        /// Get the current position of the media
        /// </summary>
        /// <param name="positionTimelineController"></param>
        /// <param name="leftPosition"></param>
        /// <param name="rightPosition"></param>
        /// <returns>true is media in sync'd</returns>
        public bool GetFullMediaPosition(out TimeSpan positionTimelineController, out TimeSpan leftPosition, out TimeSpan rightPosition)
        {
            bool ret = false;

            // Reset
            positionTimelineController = TimeSpan.Zero;
            leftPosition = TimeSpan.Zero;
            rightPosition = TimeSpan.Zero;

            if (mediaSynchronized)
            {
                if (mediaTimelineController is not null && mediaPlayerLeft.Position is not null && mediaPlayerRight.Position is not null)
                {
                    positionTimelineController = mediaTimelineController.Position;
                    leftPosition = (TimeSpan)mediaPlayerLeft.Position;
                    rightPosition = (TimeSpan)mediaPlayerRight.Position;
                    ret = true;
                }
            }
            else
            {
                if (mediaPlayerLeft.Position is not null && mediaPlayerRight.Position is not null)
                {
                    positionTimelineController = TimeSpan.Zero;
                    leftPosition = (TimeSpan)mediaPlayerLeft.Position;
                    rightPosition = (TimeSpan)mediaPlayerRight.Position;
                }
            }

            return ret;
        }


        ///
        /// EVENT HANDLERS
        /// 

        /// <summary>
        /// This is the StateChanged event handler for the MediaTimelineController
        /// It is called when the MediaTimelineController is started, paused, stopped, etc
        /// </summary>
        /// <param name="sender"></param>tryenqueue
        /// <param name="args"></param>
        private void MediaTimelineController_StateChanged(MediaTimelineController sender, object args)
        {
            switch (sender.State)
            {
                case MediaTimelineControllerState.Running:
                    break;

                case MediaTimelineControllerState.Paused:
                    mediaTimelineControllerPositionPausedMode = sender.Position;
                    //???Debug.WriteLine($"MediaTimelineController_StateChanged: PositionOffset: {(mediaTimelineControllerPositionPausedMode.TotalMilliseconds / 1000.0):F3}");
                    break;

                case MediaTimelineControllerState.Error:
                    break;

                case MediaTimelineControllerState.Stalled:
                    break;
            }
        }


        /// <summary>
        /// This is the PositionChanged event handler for the MediaTimelineController
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void MediaTimelineController_PositionChanged(MediaTimelineController sender, object args)
        {
            // Pause when playback reaches the maximum duration.
            if (_maxNaturalDurationForController != TimeSpan.Zero && sender.Position > _maxNaturalDurationForController)
            {
                sender.Pause();
            }

            // Signal to the primary MediaControls the current position          
            mainWindow.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                MediaPlayerEventData data = new(MediaPlayerEventData.eMediaPlayerEvent.Position, eCameraSide.Left, Mode.modeNone)
                {
                    position = sender.Position                    
                };
                mediaControllerHandler?.Send(data);
            });


            //???Debug.WriteLine($"MediaTimelineController_PositionChanged: Position={sender.Position}");
        }

        // Incase we need later
        //private void MediaTimelineController_Failed(MediaTimelineController sender, MediaTimelineControllerFailedEventArgs args)
        //{
        //    Debug.WriteLine($"MediaTimelineController_Failed: ");            
        //}

        // Incase we need later
        //private void MediaTimelineController_Ended(MediaTimelineController sender, object args)
        //{
        //    Debug.WriteLine($"MediaTimelineController_Ended: ");
        //}


        ///
        /// MEDIATOR METHODS (Called by the TListener, always marked as internal)
        ///

        /// <summary>
        /// Received from the MediaPlayers the natural duration of the media
        /// </summary>
        /// <param name="cameraSide">Indicate which player send the information</param>
        /// <param name="naturalDuration">TimeSpan indicating the length of the media</param>
        internal void _UpdateDurationAndFrameRate(eCameraSide cameraSide, TimeSpan naturalDuration, double frameRate)
        {
            // Maintain _maxNaturalDurationForController so it is as long as the longest media source.
            if (naturalDuration > _maxNaturalDurationForController)
                _maxNaturalDurationForController = naturalDuration;

            if (cameraSide == eCameraSide.Left)
                _frameRate = frameRate;
        }


        /// <summary>
        /// Received from the MediaPlayer a new frame has been displayed
        /// </summary>
        internal void _NewImageFrame()
        {
            // Reset any target or measurement variables and
            // clear any targets on the magnifier wondows
            ClearCachedTargets();
        }

//=========================
        /// <summary>
        /// Receive a request to edit an existing SpeciesInfo record
        /// </summary>
        /// <param name="eventGuid"></param>
        /// <returns></returns>
        internal async Task _EditSpeciesInfo(Guid eventGuid)
        {
            if (eventsControl is not null)
            {
                Event? evt = eventsControl.FindEvent(eventGuid);

                if (evt is not null)
                {
                    SpeciesInfo? speciesInfo = null;
                    if (evt.EventData is SurveyMeasurement surveyMeasurement)
                        speciesInfo = surveyMeasurement.SpeciesInfo;
                    else if (evt.EventData is SurveyStereoPoint surveyStereoPoint)
                        speciesInfo = surveyStereoPoint.SpeciesInfo;
                    else if (evt.EventData is SurveyPoint surveyPoint)
                        speciesInfo = surveyPoint.SpeciesInfo;


                    if (speciesInfo is not null)
                    {
                        // Assigned the species info via a user dialog box
                        if (await speciesSelector.SpeciesEdit(mainWindow, speciesInfo) == true)
                        {
                            eventsControl?.AddEvent(evt);
                        }
                    }
                }
            }
        }



        ///
        /// PRIVATE METHODS
        ///



        /// <summary>
        /// Received from the MediaPlayers to inform of the frame size of the media.  This is important so the correct calibration data can be used
        /// </summary>
        /// <param name=""></param>
        /// <param name=""></param>
        /// <param name=""></param>
        internal void SetFrameSize(eCameraSide cameraSide, int frameWidth, int frameHeight)
        {
            // We are going to assume the left and right media are the same size and only use info fromt the left camera
            if (cameraSide == eCameraSide.Left)
            {
                stereoProjection.SetFrameSize(frameWidth, frameHeight);
                mainWindow.SetCalibratedIndicator(frameWidth, frameHeight);
            }
        }

        /// <summary>
        /// Used by the TListener to call back the MediaPlayers to play/pause
        /// TListener is not async so we need to call the MediaPlayers on the UI thread
        /// </summary>
        /// <param name="controlType"></param>
        /// <param name="mute"></param>
        internal void UserReqPlayOrPause(SurveyorMediaControl.eControlType controlType, bool playOrPause)
        {
            
            DispatcherQueue.GetForCurrentThread()?.TryEnqueue(async () =>
            {
                try
                {
                    CheckIsUIThread();

                    if (playOrPause)
                    {
                        if (controlType == SurveyorMediaControl.eControlType.Primary)
                            Play(eCameraSide.Left);
                        else
                            Play(eCameraSide.Right);
                    }
                    else
                    {
                        if (controlType == SurveyorMediaControl.eControlType.Primary)
                            await Pause(eCameraSide.Left);
                        else
                            await Pause(eCameraSide.Right);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MediaStereoController.UserReqPlayOrPause: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Used by the TListener to call the MediaPlayers to jump to a specific position in the media
        /// </summary>
        /// <param name="controlType"></param>
        /// <param name="positionJump"></param>
        internal void UserReqFrameJump(SurveyorMediaControl.eControlType controlType, TimeSpan positionJump)
        {
            if (mediaSynchronized)
            {
                FrameJump(eCameraSide.None, positionJump);
            }
            else
            {
                if (controlType == SurveyorMediaControl.eControlType.Primary)
                    FrameJump(eCameraSide.Left, positionJump);
                else
                    FrameJump(eCameraSide.Right, positionJump);
            }
        }


        /// <summary>
        /// Used by the TListener to call the MediaPlayers to jump to a relative position in the media
        /// </summary>
        /// <param name="controlType"></param>
        /// <param name="framesDelta"></param>
        internal void UserReqFrameMove(SurveyorMediaControl.eControlType controlType, int framesDelta)
        {
            DispatcherQueue.GetForCurrentThread()?.TryEnqueue(async () =>
            {
                try
                {
                    CheckIsUIThread();

                    if (mediaSynchronized)
                    {
                        await FrameMove(eCameraSide.None, framesDelta);
                    }
                    else
                    {
                        if (controlType == SurveyorMediaControl.eControlType.Primary)
                            await FrameMove(eCameraSide.Left, framesDelta);
                        else
                            await FrameMove(eCameraSide.Right, framesDelta);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MediaStereoController.UserReqFrameMove: {ex.Message}");
                }
            });
        }


        /// <summary>
        /// Used by the TListener to call the MediaPlayers to step forward/back in blocks of time 
        /// i.e 10 frame back, 30 frames forward
        /// </summary>
        /// <param name="controlType"></param>
        internal void UserReqMoveStep(SurveyorMediaControl.eControlType controlType, int frames)
        {
            DispatcherQueue.GetForCurrentThread()?.TryEnqueue(async () =>
            {
                try
                {
                    if (mediaSynchronized)
                    {
                        await FrameMove(eCameraSide.None, frames);
                    }
                    else
                    {
                        if (controlType == SurveyorMediaControl.eControlType.Primary)
                            await FrameMove(eCameraSide.Left, frames);
                        else
                            await FrameMove(eCameraSide.Right, frames);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MediaStereoController.UserReqMoveStep: {ex.Message}");
                }
            });
        }


        /// <summary>
        /// Used by the TListener to call back the MediaPlayers to mute/unmute
        /// If the players are locked together, Mute/Unmute the left media player
        /// If the players are not locked together, Mute/Unmute the player that requested the mute/unmute
        /// Ensure only one player is unmuted 
        /// </summary>
        /// <param name="controlType"></param>
        /// <param name="mute"></param>
        internal void UserReqMutedOrUmuted(SurveyorMediaControl.eControlType controlType, bool mute)
        {
            // Mute/Umute maybe need a UI thread so to be sure we are on the UI thread
            DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
            {
                try
                {
                    if (mediaSynchronized || controlType == SurveyorMediaControl.eControlType.Primary)
                    {
                        if (mute)
                            mediaPlayerLeft.Mute();
                        else
                        {
                            mediaPlayerLeft.Unmute();
                            mediaPlayerRight.Mute();
                        }
                    }
                    else
                    {
                        if (mute)
                            mediaPlayerRight.Mute();
                        else
                        {
                            mediaPlayerRight.Unmute();
                            mediaPlayerLeft.Mute();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MediaStereoController.UserReqMutedOrUmuted: {ex.Message}");
                }
            });
        }


        /// <summary>
        /// Set the media players speed 
        /// If the players are locked together, set the speed for both
        /// If the players are not locked together, set the speed for the player that requested the speed change
        /// </summary>
        /// <param name="controlType"></param>
        /// <param name="speed"></param>
        internal void UserReqSetSpeed(SurveyorMediaControl.eControlType controlType, float speed)
        {
            DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
            {
                try
                {
                    if (mediaSynchronized)
                    {
                        // Set the ClockRate on the MediaTimelineController
                        mediaTimelineController!.ClockRate = speed;
                    }
                    else
                    {
                        if (controlType == SurveyorMediaControl.eControlType.Primary)
                            mediaPlayerLeft.SetSpeed(speed);
                        else
                            mediaPlayerRight.SetSpeed(speed);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MediaStereoController.UserReqSetSpeed: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Set the media control that requested full screen to use the XAML whole grid
        /// </summary>
        /// <param name="controlType"></param>
        internal void UserReqFullScreen(SurveyorMediaControl.eControlType controlType, eCameraSide cameraSide)
        {
            if (mediaSynchronized)
            {
                // In synchronized mode, when the in full screen mode, the primary (left) media control
                if (controlType == SurveyorMediaControl.eControlType.Primary)
                {
                    if (cameraSide == eCameraSide.Left)
                    {
                        mainWindow.MediaFullScreen(true/*TrueLeftFalseRight*/);
                        mainWindow.SetTitleCameraSide("Left Camera View");                        
                    }
                    else
                    {
                        mainWindow.MediaFullScreen(false/*TrueLeftFalseRight*/);
                        mainWindow.SetTitleCameraSide("Right Camera View");
                    }

                    mediaControlPrimary.MediaFullScreen(true/*TrueYouAreFullFalseYouAreRestored*/, cameraSide);
                }
            }
            else 
            {
                // In non-synchronized mode, when the in full screen mode, the primary (left) media control
                // controls the left player and the secondart (right) media control controls the right player
                if (cameraSide == eCameraSide.Left)
                {
                    mainWindow.MediaFullScreen(true/*TrueLeftFalseRight*/);
                    mainWindow.SetTitleCameraSide("Left Camera View");
                    mediaControlPrimary.MediaFullScreen(true/*TrueYouAreFullFalseYouAreRestored*/, eCameraSide.Left);
                    // Disable the other media control we keystroke aren't directed there
                    mediaControlPrimary.IsEnabled = true;
                    mediaControlSecondary.IsEnabled = false;
                }
                else
                {
                    mainWindow.MediaFullScreen(false/*TrueLeftFalseRight*/);
                    mainWindow.SetTitleCameraSide("Right Camera View");
                    mediaControlSecondary.MediaFullScreen(true/*TrueYouAreFullFalseYouAreRestored*/, eCameraSide.Right);
                    // Disable the other media control we keystroke aren't directed there
                    mediaControlPrimary.IsEnabled = false;
                    mediaControlSecondary.IsEnabled = true;
                }
            }
        }


        /// <summary>
        /// Restore the media players to the regular stereo screen layout
        /// </summary>
        /// <param name="controlType"></param>
        internal void UserReqBackToWindow()
        {
            mainWindow.MediaBackToWindow();
            mediaControlPrimary.MediaFullScreen(false/*TrueYouAreFullFalseYouAreRestored*/, null);
            mediaControlSecondary.MediaFullScreen(false/*TrueYouAreFullFalseYouAreRestored*/, null);
            mainWindow.SetTitleCameraSide("");

            if (!mediaSynchronized)
            {
                // Enable both media controls
                mediaControlPrimary.IsEnabled = true;
                mediaControlSecondary.IsEnabled = true;
            }
        }

        /// <summary>
        /// Cast the media to a remote device
        /// </summary>
        /// <param name="controlType"></param>
        internal void UserReqCasting(SurveyorMediaControl.eControlType controlType)
        {
            if (controlType == SurveyorMediaControl.eControlType.Primary)
                mediaPlayerLeft.StartCasting();
            else
                mediaPlayerRight.StartCasting();
        }


        /// <summary>
        /// User requested to save the current frame. 
        /// If themedia is synchronized, save the frames from both the left and right
        /// players. 
        /// </summary>
        /// <param name="controlType"></param>
        internal void UserReqSaveFrame(SurveyorMediaControl.eControlType controlType)
        {
            DispatcherQueue.GetForCurrentThread()?.TryEnqueue(async () =>
            {
                CheckIsUIThread();

                try
                {
                    // Call to the MainWindow so a) we have access to the Projectclass and b) we
                    // can do some UI work if necessary (no UI world in this class)
                    if (mediaSynchronized || controlType == SurveyorMediaControl.eControlType.Both)
                        await mainWindow.SaveCurrentFrame(SurveyorMediaControl.eControlType.Both);
                    else if (controlType != SurveyorMediaControl.eControlType.None)
                        await mainWindow.SaveCurrentFrame(controlType);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MediaStereoController.UserReqSaveFrame: {ex.Message}");
                }
            });
        }


        /// <summary>
        /// User requested a magnify window size change
        /// </summary>
        /// <param name="controlType"></param>
        /// <param name="magWindowSize"></param>
#if !No_MagnifyAndMarkerDisplay
        internal void UserReqMagWindowSizeSelect(SurveyorMediaControl.eControlType controlType, string magWindowSize)
        {
            // Thread Safe/UI Check not required
            if (mediaSynchronized || controlType == SurveyorMediaControl.eControlType.Primary)
                magnifyAndMarkerDisplayLeft.MagWindowSizeSelect(magWindowSize);

            if (mediaSynchronized || controlType == SurveyorMediaControl.eControlType.Secondary)
                magnifyAndMarkerDisplayRight.MagWindowSizeSelect(magWindowSize);
        }
#endif


        /// <summary>
        /// User requested a magnify window zoom factor change
        /// </summary>
        /// <param name="controlType"></param>
        /// <param name="_canvasZoomFactor"></param>
#if !No_MagnifyAndMarkerDisplay
        internal void UserReqMagZoomSelect(SurveyorMediaControl.eControlType controlType, double canvasZoomFactor)
        {
            // Thread Safe/UI Check not 

            if (mediaSynchronized || controlType == SurveyorMediaControl.eControlType.Primary)
                magnifyAndMarkerDisplayLeft.MagWindowZoomFactor(canvasZoomFactor);

            if (mediaSynchronized || controlType == SurveyorMediaControl.eControlType.Secondary)
                magnifyAndMarkerDisplayRight.MagWindowZoomFactor(canvasZoomFactor);
        }
#endif


        /// <summary>
        /// User requested a change to the layers of information displayed in the canvas frame
        /// </summary>
        /// <param name="controlType"></param>
        /// <param name="layertype"></param>
#if !No_MagnifyAndMarkerDisplay
        internal void UserReqLayersDisplayed(SurveyorMediaControl.eControlType controlType, LayerType layertype)
        {
            if (mediaSynchronized || controlType == SurveyorMediaControl.eControlType.Primary)
                magnifyAndMarkerDisplayLeft.SetLayerType(layertype);

            if (mediaSynchronized || controlType == SurveyorMediaControl.eControlType.Secondary)
                magnifyAndMarkerDisplayRight.SetLayerType(layertype);
        }
#endif


        /// <summary>
        /// Received message from one instance of the MagnifyAndMarkerDisplay and this function's job
        /// is to inform the other instance of the target points selected
        /// </summary>
        /// <param name="TruePointAFalsePointB"></param>
        /// <param name="pointA"></param>
        /// <param name="pointB"></param>
        /// 

        private Point? TargetALeft = null;
        private Point? TargetBLeft = null;
        private Point? TargetARight = null;
        private Point? TargetBRight = null;

#if !No_MagnifyAndMarkerDisplay
        internal void TargetPointSelected(SurveyorMediaPlayer.eCameraSide cameraSide, bool TruePointAFalsePointB, Point? pointA, Point? pointB)
        {
            if (cameraSide == SurveyorMediaPlayer.eCameraSide.Left)
            {
                if (TruePointAFalsePointB)
                {
                    if (pointA is not null)
                        magnifyAndMarkerDisplayRight.OtherInstanceTargetSet(true, null);
                    else
                        magnifyAndMarkerDisplayRight.OtherInstanceTargetSet(false, null);
                }
                else
                {
                    if (pointB is not null)
                        magnifyAndMarkerDisplayRight.OtherInstanceTargetSet(null, true);
                    else
                        magnifyAndMarkerDisplayRight.OtherInstanceTargetSet(null, false);
                }
            }
            else if (cameraSide == SurveyorMediaPlayer.eCameraSide.Right)
            {
                if (TruePointAFalsePointB)
                {
                    if (pointA is not null)
                        magnifyAndMarkerDisplayLeft.OtherInstanceTargetSet(true, null);
                    else
                        magnifyAndMarkerDisplayLeft.OtherInstanceTargetSet(false, null);
                }
                else
                {
                    if (pointB is not null)
                        magnifyAndMarkerDisplayLeft.OtherInstanceTargetSet(null, true);
                    else
                        magnifyAndMarkerDisplayLeft.OtherInstanceTargetSet(null, false);
                }
            }

            // Rememmber the target points 
            if (cameraSide == SurveyorMediaPlayer.eCameraSide.Left)
            {
                if (TruePointAFalsePointB)
                    TargetALeft = pointA;
                else
                    TargetBLeft = pointB;
            }
            else if (cameraSide == SurveyorMediaPlayer.eCameraSide.Right)
            {
                if (TruePointAFalsePointB)
                    TargetARight = pointA;
                else
                    TargetBRight = pointB;
            }

            // Calculate the Epipolar line on the alternative camera
            bool? TrueLeftFalseRight = cameraSide == SurveyorMediaPlayer.eCameraSide.Left ? true : cameraSide == SurveyorMediaPlayer.eCameraSide.Right ? false : null;
            Point? point;            

            if (TrueLeftFalseRight is not null)
            {
                // Single out the point
                if (TruePointAFalsePointB)
                    point = pointA;
                else
                    point = pointB;

                // Calc the alternative camera
                eCameraSide cameraSideAlt = cameraSide == SurveyorMediaPlayer.eCameraSide.Left ? SurveyorMediaPlayer.eCameraSide.Right : SurveyorMediaPlayer.eCameraSide.Left;


                if (point is not null)
                {                    
                    // We only need to calculate the Epipolar line if there isn't already a pair of points
                    bool needEpipolarLine = false;
                    if (cameraSide == SurveyorMediaPlayer.eCameraSide.Left)
                    {
                        // Either:
                        // We have just selected a target A on the left camera and there is no selected Target A on the right side
                        // We have just selected a target B on the left camera and there is no selected Target B on the right side
                        if ((TruePointAFalsePointB == true && TargetARight is null) || (TruePointAFalsePointB == false && TargetBRight is null))
                            needEpipolarLine = true;
                    }
                    else if (cameraSide == SurveyorMediaPlayer.eCameraSide.Right)
                    {
                        // Either:
                        // We have just selected a target A on the right camera and there is no selected Target A on the left side
                        // We have just selected a target B on the right camera and there is no selected Target B on the left side
                        if ((TruePointAFalsePointB == true && TargetALeft is null) || (TruePointAFalsePointB == false && TargetBLeft is null))
                            needEpipolarLine = true;
                    }

                    if (needEpipolarLine)
                    {
                        // *** Epipolar Line Calculation - Approach 1 ***
                        // Display Epipolar line
                        if (stereoProjection.CalculateEpipilorLine((bool)TrueLeftFalseRight, (Point)point,
                                    out double epiLine_a, out double epiLine_b, out double epiLine_c, out double _focalLength, out double _baseline,
                                    out double _principalXLeft, out double _principalYLeft, out double _principalXRight, out double _principalYRight))
                        {
                            Debug.WriteLine($"{cameraSideAlt} Epipolar for Point({point.Value.X:F2}, {point.Value.Y:F2})  ax+by+c=0: {epiLine_a:F5}x + {epiLine_b:F5}y + {epiLine_c:F5} = 0");
                            Debug.WriteLine($"{cameraSideAlt} Epipolar left border intersect (0, {-epiLine_c / epiLine_b})");

                            // Signal to the MagnifyAndMarkerControl to display the epipolar line
                            MagnifyAndMarkerControlData data = new(MagnifyAndMarkerControlData.MagnifyAndMarkerControlEvent.EpipolarLine, cameraSideAlt)
                            {
                                TrueEpipolarLinePointAFalseEpipolarLinePointB = (bool)TruePointAFalsePointB,
                                epipolarLine_a = (double)epiLine_a,
                                epipolarLine_b = (double)epiLine_b,
                                epipolarLine_c = (double)epiLine_c,
                                focalLength = _focalLength,
                                baseline = _baseline,
                                principalXLeft = _principalXLeft,
                                principalYLeft = _principalYLeft,
                                principalXRight = _principalXRight,
                                principalYRight = _principalYRight,
                                channelWidth = 0
                            };
                            mediaControllerHandler?.Send(data);
                        }

#if DEBUG   // Approach is under test (NOT WORKING CURRENTLY)
                        //// *** Epipolar Line Calculation - Approach 2 ***
                        //// Calculate the near, far and middle points on the epipolar line from the SurveyRules
                        //// if not SurveyRules for range in place then use 1m,10m and 5.5m
                        //if(stereoProjection.CalculateEpipolarPoints((bool)TrueLeftFalseRight, (Point)point, out Point _pointNear, out Point _pointMiddle, out Point _pointFar))
                        //{
                        //    Debug.WriteLine($"{cameraSideAlt} Epipolar Points for Point({point.Value.X:F2}, {point.Value.Y:F2})  {cameraSide} Near({_pointNear.X:F2}, {_pointNear.Y:F2}), Middle({_pointMiddle.X:F2}, {_pointMiddle.Y:F2}), Far({_pointFar.X:F2}, {_pointFar.Y:F2})");

                        //    // Signal to the MagnifyAndMarkerControl to display the epipolar points
                        //    MagnifyAndMarkerControlData data = new(MagnifyAndMarkerControlData.MagnifyAndMarkerControlEvent.EpipolarPoints, cameraSideAlt)
                        //    {
                        //        TrueEpipolarLinePointAFalseEpipolarLinePointB = (bool)TruePointAFalsePointB,
                        //        pointNear = _pointNear,
                        //        pointMiddle = _pointMiddle,
                        //        pointFar = _pointFar,
                        //        channelWidth = 0
                        //    };
                        //    mediaControllerHandler?.Send(data);
                        //}
#endif
                    }
                    else
                    {
                        // This implies that corresponding Targets have been selected (i.e. Left A and Right A or Left B and Right B)
                        // In which case the there is probably a epipolar line on this 'cameraSide' that is no longer need
                        // Remove Epipolar line from this camera                       
                        MagnifyAndMarkerControlData data = new(MagnifyAndMarkerControlData.MagnifyAndMarkerControlEvent.EpipolarLine, cameraSide)
                        {
                            TrueEpipolarLinePointAFalseEpipolarLinePointB = (bool)TruePointAFalsePointB,
                            channelWidth = -1 /* Clear the epipolar line */
                        };
                        mediaControllerHandler?.Send(data);


                        // Signal to the MagnifyAndMarkerControl to display the epipolar points
                        MagnifyAndMarkerControlData data2 = new(MagnifyAndMarkerControlData.MagnifyAndMarkerControlEvent.EpipolarPoints, cameraSideAlt)
                        {
                            TrueEpipolarLinePointAFalseEpipolarLinePointB = (bool)TruePointAFalsePointB,
                            channelWidth = -1 /* Clear the epipolar points */
                        };
                        mediaControllerHandler?.Send(data2);
                    }
                }
                else
                {
                    // Remove Epipolar line from the other camera                       
                    MagnifyAndMarkerControlData data = new(MagnifyAndMarkerControlData.MagnifyAndMarkerControlEvent.EpipolarLine, cameraSideAlt)
                    {
                        TrueEpipolarLinePointAFalseEpipolarLinePointB = (bool)TruePointAFalsePointB,
                        channelWidth = -1 /* Clear the epipolar line */
                    }; 
                    mediaControllerHandler?.Send(data);
                }
            }
        }
#endif


        /// <summary>
        /// Mediator massage received for the stereo controler 
        /// to indicate that a pair of targets have been selected on that instance
        /// </summary>
        /// <param name="pointA"></param>
        /// <param name="pointB"></param>
        /// <returns></returns>
        internal async Task SurveyMeasurementPairSelected(SurveyorMediaPlayer.eCameraSide cameraSide, Point pointA, Point pointB)
        {
            // Check if the Events list is null and the stereo projection is not null (normally
            // stereoProject is not null if we have calibration data)
            if (eventsControl is not null && stereoProjection is not null)
            {
                bool fullSetOfMeasurementPoints = false;
                SurveyMeasurement surveyMeasurement = new();

                // Check if we have a complete set of measurement points from both cameras
                if (cameraSide == SurveyorMediaPlayer.eCameraSide.Left)
                {
                    // We just received a pair of points from the left camera so check
                    // if we previously have a pair from the right camera
                    if (TargetARight is null || TargetBRight is null)
                        return;
                    else
                    {
                        surveyMeasurement.LeftXA = pointA.X;
                        surveyMeasurement.LeftYA = pointA.Y;
                        surveyMeasurement.LeftXB = pointB.X;
                        surveyMeasurement.LeftYB = pointB.Y;
                        surveyMeasurement.RightXA = TargetARight!.Value.X;
                        surveyMeasurement.RightYA = TargetARight!.Value.Y;
                        surveyMeasurement.RightXB = TargetBRight!.Value.X;
                        surveyMeasurement.RightYB = TargetBRight!.Value.Y;
                        fullSetOfMeasurementPoints = true;
                    }
                }
                else if (cameraSide == SurveyorMediaPlayer.eCameraSide.Right)
                {
                    // We just received a pair of points from the right camera so check
                    // if we previously have a pair from the left camera
                    if (TargetALeft is null || TargetBLeft is null)
                        return;
                    else
                    {
                        surveyMeasurement.LeftXA = TargetALeft!.Value.X;
                        surveyMeasurement.LeftYA = TargetALeft!.Value.Y;
                        surveyMeasurement.LeftXB = TargetBLeft!.Value.X;
                        surveyMeasurement.LeftYB = TargetBLeft!.Value.Y;
                        surveyMeasurement.RightXA = pointA.X;
                        surveyMeasurement.RightYA = pointA.Y;
                        surveyMeasurement.RightXB = pointB.X;
                        surveyMeasurement.RightYB = pointB.Y;
                        fullSetOfMeasurementPoints = true;
                    }
                }

                if (fullSetOfMeasurementPoints)
                {
                    // Check that logically Target Left A correponds to Target Right A and Target Left B is correponds to Target Right B
                    // This is incase the user selected the points the wrong way around. If so the target will be swapped
                    SurveyMeasurementHelper.EnsureCorrectCorrespondence(surveyMeasurement);

                    // Assigned the species info via a user dialog box
                    SpeciesInfo specifiesInfo = new();
                    if (await speciesSelector.SpeciesNew(mainWindow, specifiesInfo) == true)
                    {
                        Event evt = new(SurveyDataType.SurveyMeasurementPoints)
                        {
                            EventData = surveyMeasurement
                        };

                        surveyMeasurement.SpeciesInfo = specifiesInfo;

                        // Check if suitable calibration data is available for this frame size
                        bool isReady = await mainWindow.CheckIfMeasurementSetupIsReady();
                        if (isReady)
                        {

                            // This call calculates the distance, range, X & Y offset between the camera system mid-point and the measurement point mid-point
                            if (mediaTimelineController is not null && mainWindow.DoMeasurementAndRulesCalculations(surveyMeasurement) == true)
                            {
                                TimeSpan? timelineConntrollerPoistion = mediaTimelineController.Position;
                                TimeSpan? leftPosition = mediaPlayerLeft.Position;
                                TimeSpan? rightPosition = mediaPlayerRight.Position;

                                if (surveyMeasurement.Measurment != 0 && leftPosition is not null && rightPosition is not null)
                                {
                                    evt.TimeSpanTimelineController = (TimeSpan)timelineConntrollerPoistion;
                                    evt.TimeSpanLeftFrame = (TimeSpan)leftPosition;
                                    evt.TimeSpanRightFrame = (TimeSpan)rightPosition;

                                    // Add the species info to the Events list
                                    eventsControl?.AddEvent(evt);

                                    // Remove targets
                                    ClearCachedTargets();
#if !No_MagnifyAndMarkerDisplay
                                    magnifyAndMarkerDisplayLeft.SetTargets(null, null);
                                    magnifyAndMarkerDisplayRight.SetTargets(null, null);
#endif
                                }

                                stereoProjection.PointsClear();
                            }
                        }
                    }
                }
            }
        }



        /// <summary>
        /// Received a request to add either a SurveyStereoPoint or a SurveyPoint
        /// A SurveyStereoPoint is a corresponding point on both the left and right camera
        /// A SurveyPoint is a point on the left camera only
        /// </summary>
        /// <param name="cameraSide"></param>
        /// <param name="TruePointAFalsePointB"></param>
        /// <param name="pointA"></param>
        /// <param name="pointB"></param>
        /// <returns></returns>
        internal async Task SurveyPointSelected(SurveyorMediaPlayer.eCameraSide cameraSide, bool TruePointAFalsePointB, Point? pointA, Point? pointB)
        {
            if (eventsControl is not null && stereoProjection is not null)
            {
                SurveyDataType eventDataType = SurveyDataType.SurveyPoint;   // Default event type to a single point until we confirm there are stereo point

                if (cameraSide == SurveyorMediaPlayer.eCameraSide.Left)
                {
                    if (TruePointAFalsePointB == true && TargetARight is not null)
                        eventDataType = SurveyDataType.SurveyStereoPoint;
                    else if (TruePointAFalsePointB == false && TargetBRight is not null)
                        eventDataType = SurveyDataType.SurveyStereoPoint;
                }
                else if (cameraSide == SurveyorMediaPlayer.eCameraSide.Right)
                {
                    if (TruePointAFalsePointB == true && TargetALeft is not null)
                        eventDataType = SurveyDataType.SurveyStereoPoint;
                    else if (TruePointAFalsePointB == false && TargetBLeft is not null)
                        eventDataType = SurveyDataType.SurveyStereoPoint;
                }

                // Declare a new event with the right event data type
                Event evt = new();
                evt.SetData(eventDataType);

                // Setup the coord of a Survey Stereo Point (corresponding point on the left and right camera)
                if (eventDataType == SurveyDataType.SurveyStereoPoint && evt.EventData is not null)
                {
                    SurveyStereoPoint surveyStereoPoint = (SurveyStereoPoint)evt.EventData;

                    if (cameraSide == SurveyorMediaPlayer.eCameraSide.Left)
                    {
                        surveyStereoPoint.LeftX = TruePointAFalsePointB ? pointA!.Value.X : pointB!.Value.X;
                        surveyStereoPoint.LeftY = TruePointAFalsePointB ? pointA!.Value.Y : pointB!.Value.Y;
                        surveyStereoPoint.RightX = TruePointAFalsePointB ? TargetARight!.Value.X : TargetBRight!.Value.X;
                        surveyStereoPoint.RightY = TruePointAFalsePointB ? TargetARight!.Value.Y : TargetBRight!.Value.Y;
                    }
                    else
                    {
                        surveyStereoPoint.LeftX = TruePointAFalsePointB ? TargetALeft!.Value.X : TargetBLeft!.Value.X;
                        surveyStereoPoint.LeftY = TruePointAFalsePointB ? TargetALeft!.Value.Y : TargetBLeft!.Value.Y;
                        surveyStereoPoint.RightX = TruePointAFalsePointB ? pointA!.Value.X : pointB!.Value.X;
                        surveyStereoPoint.RightY = TruePointAFalsePointB ? pointA!.Value.Y : pointB!.Value.Y;
                    }                    
                }
                // Setup the coord of a Survey Point (point on left camera only)
                else if (eventDataType == SurveyDataType.SurveyPoint && evt.EventData is not null)
                {
                    SurveyPoint surveyPoint = (SurveyPoint)evt.EventData;

                    surveyPoint.X = TruePointAFalsePointB ? pointA!.Value.X : pointB!.Value.X;
                    surveyPoint.Y = TruePointAFalsePointB ? pointA!.Value.Y : pointB!.Value.Y;                    
                }


                // Assigned the species info via a user dialog box
                SpeciesInfo specifiesInfo = new();
                if (await speciesSelector.SpeciesNew(mainWindow, specifiesInfo) == true)
                {
                    if (eventDataType == SurveyDataType.SurveyStereoPoint && evt.EventData is not null)
                    {
                        SurveyStereoPoint surveyStereoPoint = (SurveyStereoPoint)evt.EventData;
                        surveyStereoPoint.SpeciesInfo = specifiesInfo;

                        // Check if suitable calibration data is available for this frame size
                        bool isReady = await mainWindow.CheckIfMeasurementSetupIsReady();
                        if (isReady)
                        {

                            // This call calculates the distance, range, X & Y offset between the camera system mid-point and the measurement point mid-point
                            if (mediaTimelineController is not null && mainWindow.DoRulesCalculations(surveyStereoPoint) == true)
                            {

                                TimeSpan? timelineConntrollerPoistion = mediaTimelineController.Position;
                                TimeSpan? leftPosition = mediaPlayerLeft.Position;
                                TimeSpan? rightPosition = mediaPlayerRight.Position;

                                if (leftPosition is not null && rightPosition is not null)
                                {
                                    evt.TimeSpanTimelineController = (TimeSpan)timelineConntrollerPoistion;
                                    evt.TimeSpanLeftFrame = (TimeSpan)leftPosition;
                                    evt.TimeSpanRightFrame = (TimeSpan)rightPosition;

                                    // Add the species info to the Events list
                                    //EventCo
                                    eventsControl?.AddEvent(evt);

                                    // Remove targets
                                    ClearCachedTargets();
#if !No_MagnifyAndMarkerDisplay
                                    magnifyAndMarkerDisplayLeft.SetTargets(null, null);
                                    magnifyAndMarkerDisplayRight.SetTargets(null, null);
#endif
                                }

                                stereoProjection.PointsClear();
                            }
                        }

                    }
                    else if (eventDataType == SurveyDataType.SurveyPoint && evt.EventData is not null)
                    {
                        SurveyPoint surveyPoint = (SurveyPoint)evt.EventData;
                        surveyPoint.SpeciesInfo = specifiesInfo;
                    }

                }                
            }
        }



        /// <summary>
        /// Create a Event record for a StereoSyncPoint at the current locked media position
        /// </summary>
        private void SurveyStereoSyncPointSelected(TimeSpan timelineConntrollerPoistion, TimeSpan leftPosition, TimeSpan rightPosition)
        {
            // Check if the Events list is not null 
            if (eventsControl is not null)
            {
                if (mediaTimelineController is not null)
                {
                    // Remove any existing StereoSyncPoint events
                    eventsControl?.DeleteEventOfType(SurveyDataType.StereoSyncPoint);

                    // Create the StereoSyncPoint event
                    Event evt = new(SurveyDataType.StereoSyncPoint)
                    {
                        EventData = null,
                        TimeSpanTimelineController = timelineConntrollerPoistion,
                        TimeSpanLeftFrame = leftPosition,
                        TimeSpanRightFrame = rightPosition
                    };

                    // Add the species info to the Events list
                    eventsControl?.AddEvent(evt);
                }
            }
        }





        /// <summary>
        /// Clear the targets on the magnifier windows and the remembered copys in this class
        /// </summary>
        private void ClearCachedTargets()
        {
            TargetALeft = null;
            TargetBLeft = null;
            TargetARight = null;
            TargetBRight = null;
        }


        /// <summary>
        /// Used on UI Thread dependant functions to check if the function is being called from the UI thread
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void CheckIsUIThread()
        {
            if (!mainWindow.DispatcherQueue.HasThreadAccess)
                throw new InvalidOperationException("This function must be called from the UI thread");
        }

        // ***END OF MediaStereoController***
    }



    // In case we need later
    //public class MediaControllerHandlerData
    //{
    //    public MediaControllerHandlerData(eMediaControllerHandlerAction action) => mediaControllerAction = action;
    //    public enum eMediaControllerAction { None }
    //    public eMediaControllerAction mediaControlsAction;
    //    // Add any support variables here
    //}



    /// <summary>
    /// Used by the MediaStereoController to inform other components on state changes within MediaStereoController
    /// </summary>
    public class MediaStereoControllerEventData
    {
        public MediaStereoControllerEventData(eMediaStereoControllerEvent e) => mediaStereoControllerEvent = e;
        public enum eMediaStereoControllerEvent
        {
            MediaSynchronized,
            MediaUnsynchronized,
            Poistion
        }

        public eMediaStereoControllerEvent mediaStereoControllerEvent;

        // Used for Mediasynchronized
        public TimeSpan? positionOffset;
    }




    internal class MediaControllerHandler : TListener
    {
        private readonly MediaStereoController mediaStereoController;


        internal MediaControllerHandler(IMediator mediator, MainWindow mainWindow, MediaStereoController _mediaStereoController) : base(mediator, mainWindow)
        {
            mediaStereoController = _mediaStereoController;
        }

        public override void Receive(TListener listenerFrom, object message)
        {

            if (message is not null && message is MediaPlayerEventData)
            {
                MediaPlayerEventData data = (MediaPlayerEventData)message;
                if (data is not null)
                {
                    switch (((MediaPlayerEventData)message).mediaPlayerEvent)
                    {
                        case MediaPlayerEventData.eMediaPlayerEvent.Opened:
                            break;

                        case MediaPlayerEventData.eMediaPlayerEvent.Closed:
                            mediaStereoController.SetFrameSize(eCameraSide.Left, -1, -1);
                            mediaStereoController.SetFrameSize(eCameraSide.Right, -1, -1);
                            break;

                        case MediaPlayerEventData.eMediaPlayerEvent.DurationAndFrameRate:
                            if (data.duration is not null && data.frameRate is not null)
                                mediaStereoController._UpdateDurationAndFrameRate(data.cameraSide, (TimeSpan)data.duration, (double)data.frameRate);
                            break;

                        case MediaPlayerEventData.eMediaPlayerEvent.EndOfMedia:
                            break;

                        case MediaPlayerEventData.eMediaPlayerEvent.FrameRendered:
                            mediaStereoController._NewImageFrame();  // Clear any selected targets
                            break;

                        case MediaPlayerEventData.eMediaPlayerEvent.FrameSize:
                            if (data.frameWidth is not null && data.frameHeight is not null)
                                SafeUICall(() => mediaStereoController.SetFrameSize(data.cameraSide, (int)data.frameWidth, (int)data.frameHeight));
                           break;
                    }
                }
            }
            else if (message is not null && message is MediaControlEventData)
            {
                MediaControlEventData data = (MediaControlEventData)message;
                if (data is not null)
                {
                    switch (data.mediaControlEvent)
                    {
                        // Play or Pause
                        case MediaControlEventData.eMediaControlEvent.UserReqPlayOrPause:
                            mediaStereoController.UserReqPlayOrPause(data.controlType, data.playOrPause);
                            break;

                        // Jump to an absolute position in the media
                        case MediaControlEventData.eMediaControlEvent.UserReqFrameJump:
                            if (data.positionJump is not null)
                                mediaStereoController.UserReqFrameJump(data.controlType, (TimeSpan)data.positionJump);
                            break;

                        // Move to a relative position in the media
                        case MediaControlEventData.eMediaControlEvent.UserReqFrameMove:
                            mediaStereoController.UserReqFrameMove(data.controlType, data.framesDelta);
                            break;

                        // Frame back
                        case MediaControlEventData.eMediaControlEvent.UserReqFrameBackward:
                            mediaStereoController.UserReqFrameMove(data.controlType, -1);
                            break;

                        // Frame forward
                        case MediaControlEventData.eMediaControlEvent.UserReqFrameForward:
                            mediaStereoController.UserReqFrameMove(data.controlType, 1);
                            break;

                        // Move step back 10 seconds
                        case MediaControlEventData.eMediaControlEvent.UserReqMoveStepBack:
                            mediaStereoController.UserReqMoveStep(data.controlType, -10/*old methed was time based -(new TimeSpan(0, 0, 10))*/);
                            break;

                        // Move step forward 30 seconds
                        case MediaControlEventData.eMediaControlEvent.UserReqMoveStepForward:
                            mediaStereoController.UserReqMoveStep(data.controlType, 30/*old methed was time based (new TimeSpan(0, 0, 30))*/);
                            break;

                        // Mute or Unmute   
                        case MediaControlEventData.eMediaControlEvent.UserReqMutedOrUmuted:
                            if (data.mute is not null)
                                mediaStereoController.UserReqMutedOrUmuted(data.controlType, (bool)data.mute);
                            break;

                        // Set speed
                        case MediaControlEventData.eMediaControlEvent.UserReqSpeedSelect:
                            if (data.speed is not null)
                                mediaStereoController.UserReqSetSpeed(data.controlType, (float)data.speed);
                            break;

                        // Full screen
                        case MediaControlEventData.eMediaControlEvent.UserReqFullScreen:
                            mediaStereoController.UserReqFullScreen(data.controlType, data.cameraSide);
                            break;

                        // Back to window
                        case MediaControlEventData.eMediaControlEvent.UserReqBackToWindow:
                            mediaStereoController.UserReqBackToWindow();
                            break;

                        // Cast to device
                        case MediaControlEventData.eMediaControlEvent.UserReqCasting:
                            mediaStereoController.UserReqCasting(data.controlType);
                            break;

                        // Save frame request
                        case MediaControlEventData.eMediaControlEvent.UserReqSaveFrame:
                            mediaStereoController.UserReqSaveFrame(data.controlType);
                            break;

                        // Mag Window size change request
#if !No_MagnifyAndMarkerDisplay
                        case MediaControlEventData.eMediaControlEvent.UserReqMagWindowSizeSelect:
                            if (data.magWindowSize is not null)
                                mediaStereoController.UserReqMagWindowSizeSelect(data.controlType, data.magWindowSize);
                            break;
#endif

                        // Mag window zoom factor change request
#if !No_MagnifyAndMarkerDisplay
                        case MediaControlEventData.eMediaControlEvent.UserReqMagZoomSelect:
                            if (data.canvasZoomFactor is not null)
                                mediaStereoController.UserReqMagZoomSelect(data.controlType, (double)data.canvasZoomFactor);
                            break;
#endif

#if !No_MagnifyAndMarkerDisplay
                        // Layer type displayed
                        case MediaControlEventData.eMediaControlEvent.UserReqLayersDisplayed:
                            if (data.layerTypesDisplayed is not null)
                                mediaStereoController.UserReqLayersDisplayed(data.controlType, (LayerType)data.layerTypesDisplayed);
                            break;  
#endif
                    }
                }
            }
#if !No_MagnifyAndMarkerDisplay
            else if (message is not null && message is MagnifyAndMarkerControlEventData)
            {
                MagnifyAndMarkerControlEventData data = (MagnifyAndMarkerControlEventData)message;

                switch (data.magnifyAndMarkerControlEvent)
                {
                    case MagnifyAndMarkerControlEvent.TargetPointSelected:
                        if (data.TruePointAFalsePointB is not null)
                        {
                            SafeUICall(() => mediaStereoController.TargetPointSelected(data.cameraSide, (bool)data.TruePointAFalsePointB, data.pointA, data.pointB));
                            if ((bool)data.TruePointAFalsePointB)
                            {
                                if (data.pointA is not null)
                                    Debug.WriteLine($"***TargetPointSelected Camera:{data.cameraSide} Point A: x={Math.Round(data.pointA.Value.X,2)}, y={Math.Round(data.pointA.Value.Y,2)}");
                                else
                                    Debug.WriteLine($"***TargetPointSelected Camera:{data.cameraSide} Point A: Reset");
                            }
                            else
                            {
                                if (data.pointB is not null)
                                    Debug.WriteLine($"***TargetPointSelected Camera:{data.cameraSide} Point B: x={Math.Round(data.pointB.Value.X, 2)}, y={Math.Round(data.pointB.Value.Y, 2)}");
                                else
                                    Debug.WriteLine($"***TargetPointSelected Camera:{data.cameraSide} Point B: Reset");
                            }
                        }
                        break;

                    case MagnifyAndMarkerControlEvent.SurveyMeasurementPairSelected:
                        if (data.pointA is not null && data.pointB is not null)
                        {
                            SafeUICall(async () => await mediaStereoController.SurveyMeasurementPairSelected(data.cameraSide, (Point)data.pointA, (Point)data.pointB));
                            Debug.WriteLine($"***SurveyMeasurementPairSelected");
                        }
                        break;

                    case MagnifyAndMarkerControlEvent.SurveyPointSelected:
                        if (data.TruePointAFalsePointB is not null)
                        {
                            SafeUICall(async () => await mediaStereoController.SurveyPointSelected(data.cameraSide, (bool)data.TruePointAFalsePointB, data.pointA, data.pointB));
                            Debug.WriteLine($"***SurveyPointSelected");
                        }
                        break;

                    case MagnifyAndMarkerControlEvent.EditSpeciesInfoRequest:
                        if (data.eventGuid is not null)
                        {
                            SafeUICall(async () => await mediaStereoController._EditSpeciesInfo((Guid)data.eventGuid));
                            Debug.WriteLine($"***EditSpeciesInfo");
                        }
                        break;
                }
            }
#endif
        }
    }
}