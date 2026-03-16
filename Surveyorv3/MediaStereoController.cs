// Surveyor MediaStereoController
// Used to orchestrate the left and right media players and their media controls
// 
// Version 1.1
// Required a public method MainWindow.MediaBackToWindow() and MediaFullScreen() to return the media players to the stereo layout
// Added support to Save a frame
// Version 1.2  26 Feb 2025
// Add a method to swap the targets is user LeftA mixed up with RightB (and therefore LeftB with RightA
// Version 1.3  09 Mar 2025
// Refactored based on ExampleMediaTimelineController
// Version 1.4  29 Jun 2025
// Handle space bar forwards from any UI component

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Surveyor.Events;
using Surveyor.Helper;
using Surveyor.User_Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media;
using static Surveyor.MediaStereoControllerEventData;
using static Surveyor.User_Controls.MagnifyAndMarkerControlEventData;
using static Surveyor.User_Controls.MagnifyAndMarkerDisplay;
using static Surveyor.User_Controls.SettingsWindowEventData;
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


        // Copy of the left and right MediaInfo controls
        //???private readonly MediaInfo _mediaInfoLeft;
        //???private readonly MediaInfo _mediaInfoRight;

        // Indicates if the two MediaPlayers are operating locked together or independently 
        private bool mediaSynchronized = false;                                // Set if the Left and Right players are now controlled via the MediaTimelineControler
        private TimeSpan mediaSynchronizedFrameOffset = TimeSpan.Zero;         // Position: Left is started before Right. Negative Left is started after Right
        private MediaTimelineController? mediaTimelineController = null;       // The MediaTimelineController that is controlling the Left and Right MediaPlayers
        private TimeSpan maxNaturalDurationForController = TimeSpan.Zero;
        private bool durationSetByThisClass = false;  // If true can't be overridden by receiving duration 
                                                      // via mediator from the Media Players
        private double _frameRate = 0.0;

        // Position of the MediaTimelineController whilst paused and moving frame by frame
        TimeSpan mediaTimelineControllerPositionPausedMode = TimeSpan.Zero;

        // EventControl (existing measurements etc)
        private readonly EventsControl? eventsControl = null;

        // Species Image Cache (stock photos of fish species to help fish ID)
        internal SpeciesImageAndInfoCache speciesImageCache;  // Accessed by SettingsWindow

        // Species Selector dialog class
        internal SpeciesSelector speciesSelector;  // Accessed by SettingsWindow

        // StereoProjection class        
        public StereoProjection stereoProjection;

        // Type of survey that is open
        Survey.SurveyType surveyType = Survey.SurveyType.Unknown;

        // Used to allow the user not to see the dialog that appears if the user adds a
        // Measurement Point, 3D Point or a Single Point and doesn't setup the species info
        private bool justSaveEventDoAsk = false;

        // Cached diagnostic information flag
        private bool diagnosticInformation = false;

        // Only used for settingsWindowEvent.Experimental
        public bool? experimentalEnabled;
        public bool? experimentalFeatureSetAEnabled;
        public bool? experimentalFeatureSetBEnabled;
        public bool? experimentalFeatureSetCEnabled;


        public MediaStereoController(MainWindow _mainWindow, Reporter _report, 
                                     SurveyorMediator _mediator, 
                                     SurveyorMediaPlayer _mediaPlayerLeft, SurveyorMediaPlayer _mediaPlayerRight, 
                                     SurveyorMediaControl _mediaControlPrimary, SurveyorMediaControl _mediaControlSecondary,
                                     EventsControl? _eventsControl,
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

            // Initialize mediator for both media controls
            mediaControlPrimary.InitializeMediator(mediator, mainWindow);
            mediaControlSecondary.InitializeMediator(mediator, mainWindow);


            // Initialize mediator for both media info controls
            //???_mediaInfoLeft.InitializeMediator(mediator, mainWindow);
            //???_mediaInfoRight.InitializeMediator(mediator, mainWindow);

            // Set-up any controls that depend on the diagnostic information state 
            _SetDiagnosticInformation(SettingsManagerLocal.DiagnosticInformation);

            // Load the experimental settings
            _SetExperimental(SettingsManagerLocal.ExperimentalEnabled,
                SettingsManagerLocal.ExperimentalFeatureSetAEnabled, SettingsManagerLocal.ExperimentalFeatureSetBEnabled, SettingsManagerLocal.ExperimentalFeatureSetCEnabled);


            // Remember the StereoProjection class
            stereoProjection = _stereoProjection;
            stereoProjection.SetReporter(report);

            // SpeciesSelector dialog class which contains the Species code list
            speciesSelector = new();
            speciesSelector.SetReporter(report);
            speciesSelector.Load("species.txt", SettingsManagerLocal.ScientificNameOrderEnabled);

            // Initialize the species image cache
            speciesImageCache = new(speciesSelector.speciesCodeList, mainWindow.internetQueue, report);
            _ = speciesImageCache.LoadAsync(SettingsManagerLocal.UseInternetEnabled && SettingsManagerLocal.SpeciesImageCacheEnabled); // Fire and forget / Load persistent SpeciesState from disk

            // Setup the Handler for the MainWindow
            mediaControllerHandler = new MediaControllerHandler(mediator, mainWindow, this);

        }



        ///
        /// PUBLIC METHODS
        ///


        /// <summary>
        /// Shutdown 
        /// </summary>
        public async Task UnloadAsync()
        {
            await mediaPlayerLeft.UnloadAsync();
            await mediaPlayerRight.UnloadAsync();
            await speciesImageCache.UnloadAsync();
            speciesSelector.Unload();
        }

        /// <summary>
        /// Diagnostics dump of class information
        /// </summary>
        public void DumpAllProperties()
        {
            DumpClassPropertiesHelper.DumpAllProperties(this, report, /*ignore*/"mediator,report,mediaControllerHandler,mainWindow,mediaPlayerLeft,mediaPlayerRight,mediaControlPrimary,mediaControlSecondary,magnifyAndMarkerDisplayLeft,magnifyAndMarkerDisplayRight,mediaTimelineController,eventsControl,speciesSelector,stereoProjection");
            DumpClassPropertiesHelper.DumpAllProperties(speciesSelector, report, /*ignore*/"_contentLoaded,<speciesCodeList>k__BackingField,<ImageList>k__BackingField,Bindings");
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
        public async Task<int> MediaOpenAsync(Survey.SurveyType _surveyType, string mediaFileSpecLeft, string mediaFileSpecRight, ObservableCollection<Event>? existingEvents, TimeSpan? timeSpanOffset, uint depthUnderwater)
        {
            CheckIsUIThread();

            int ret = 0;

            // Reset
            mediaSynchronized = false;
            mediaSynchronizedFrameOffset = TimeSpan.Zero;
            mediaTimelineController = null;
            maxNaturalDurationForController = TimeSpan.Zero;
            durationSetByThisClass = false;
            _frameRate = 0.0;
            mediaTimelineControllerPositionPausedMode = TimeSpan.Zero;

            // Remember the survey type
            surveyType = _surveyType;

            // This is an 'are you sure' dialog if the user has not set the species info
            justSaveEventDoAsk = false;


            // Set the depth the video were filmed at for color correction (Feature Set A)
            uint depthUnderwaterPassed = 0;
            if (experimentalEnabled == true && experimentalFeatureSetAEnabled == true)
            {
                depthUnderwaterPassed = depthUnderwater;
            }


            // Open both files        
            if (surveyType == Survey.SurveyType.StereoFish)
            {
                // Open both media files in parallel
                var openLeftTask = string.IsNullOrEmpty(mediaFileSpecLeft)
                    ? Task.CompletedTask
                    : mediaPlayerLeft.OpenAsync(surveyType, mediaFileSpecLeft, depthUnderwaterPassed);

                var openRightTask = string.IsNullOrEmpty(mediaFileSpecRight)
                    ? Task.CompletedTask
                    : mediaPlayerRight.OpenAsync(surveyType, mediaFileSpecRight, 0 /*depthUnderwaterPassed*/);

                // Run both opens concurrently
                await Task.WhenAll(openLeftTask, openRightTask);

                // Wait for media to open
                int tries = 0;
                while (((!string.IsNullOrEmpty(mediaFileSpecLeft) && !mediaPlayerLeft.IsOpen()) ||
                        (!string.IsNullOrEmpty(mediaFileSpecRight) && !mediaPlayerRight.IsOpen())) &&
                       tries < 20)
                {
                    await Task.Delay(100);
                    tries++;
                }

                // If the timeSpanOffset is not null and both media files opened then request the media
                // players to be locked together
                if (timeSpanOffset != null)
                {
                    // Lock the media
                    await Task.Delay(100);
                    await MediaLockMediaPlayersAsync((TimeSpan)timeSpanOffset, existingEvents);

                    // Move in one frame to engage frame server (i.e. display the pause frame in the ImageFrame
                    // instead of the MediaPlayer). This ensure all the code around the canvas and displaying
                    // things like dimensions get setup fully
                    await Task.Delay(100);
                    await FrameMoveAsync(eCameraSide.None, 1);
                }
                else
                {
                    MediaUnlockMediaPlayers();

                    // Move in one frame to engage frame server (i.e. display the pause frame in the ImageFrame
                    // instead of the MediaPlayer). This ensure all the code around the canvas and displaying
                    // things like dimensions get setup fully
                    await Task.Delay(100);
                    await FrameMoveAsync(eCameraSide.Left, 1);
                    await FrameMoveAsync(eCameraSide.Right, 1);
                }

                // Make sure the media players and controls are not in full screen mode
                mainWindow.MediaBackToWindow();
                mediaControlPrimary.FullScreenButtonVisability(true/*trueVisibleIfNeededFalseAlwaysHidden*/);
                mediaControlPrimary.MediaFullScreen(false/*TrueYouAreFullFalseYouAreRestored*/, null);
                mediaControlSecondary.MediaFullScreen(false/*TrueYouAreFullFalseYouAreRestored*/, null);

                // Enable media controls
                mediaControlPrimary.IsEnabled = true;
                mediaControlSecondary.IsEnabled = true;
            }
            else
            {
                // Mono/Benthic survey - open only the left media file
                if (!string.IsNullOrEmpty(mediaFileSpecLeft))
                {
                    await mediaPlayerLeft.OpenAsync(surveyType, mediaFileSpecLeft, depthUnderwaterPassed);
                    // Wait for media to open
                    int tries = 0;
                    while (!mediaPlayerLeft.IsOpen() && tries < 20)
                    {
                        await Task.Delay(100);
                        tries++;
                    }

                    // Recalculate the frame index. They are not saved and if the media is
                    // synchronized that done in MediaLockMediaPlayers( ... ). 
                    CalcFrameIndexesForAllEvents(existingEvents);

                    // Set the events if the players are locked
                    mediaPlayerLeft.SetEvents(existingEvents);

                    // Move in one frame to engage frame server (i.e. display the pause frame in the ImageFrame
                    // instead of the MediaPlayer). This ensure all the code around the canvas and displaying
                    // things like dimensions get setup fully
                    await Task.Delay(100);
                    await FrameMoveAsync(eCameraSide.Left, 1);
                }

                mainWindow.MediaFullScreen(true/*TrueLeftFalseRight*/);
                mainWindow.SetTitleCameraSide("");
                mediaControlPrimary.FullScreenButtonVisability(false/*trueVisibleIfNeededFalseAlwaysHidden*/);
                mediaControlPrimary.MediaFullScreen(true/*TrueYouAreFullFalseYouAreRestored*/, eCameraSide.Left);
                // Disable the other media control we keystroke aren't directed there
                mediaControlPrimary.IsEnabled = true;
                mediaControlSecondary.IsEnabled = false;
            }

            return ret;
        }


        /// <summary>
        /// Close media on eft and right media players (if open). Reset variables
        /// </summary>
        public async Task MediaCloseAsync()
        {
            CheckIsUIThread();

            try
            {
                // This is Pause the media if it is synchronized and playing
                if (mediaSynchronized)
                {
                    if (mediaPlayerLeft.IsOpen() && mediaPlayerRight.IsOpen())
                        await PauseAsync(eCameraSide.None);
                }
                else
                {
                    if (mediaPlayerLeft.IsOpen())
                        await PauseAsync(eCameraSide.Left);
                    if (mediaPlayerRight.IsOpen())
                        await PauseAsync(eCameraSide.Right);
                }

                // Wait to settle
                await Task.Delay(500);


                if (mediaPlayerLeft.IsOpen())
                {
                    await mediaPlayerLeft.CloseAsync();
                    mediaControlPrimary.Clear();
                }
                if (mediaPlayerRight.IsOpen())
                {
                    await mediaPlayerRight.CloseAsync();
                    mediaControlSecondary.Clear();
                }

                // Reset
                mediaSynchronized = false;
                mediaSynchronizedFrameOffset = TimeSpan.Zero;
                mediaTimelineController = null;
                maxNaturalDurationForController = TimeSpan.Zero;
                durationSetByThisClass = false;
                _frameRate = 0.0;
                mediaTimelineControllerPositionPausedMode = TimeSpan.Zero;

                surveyType = Survey.SurveyType.Unknown;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MediaStereoCOntroller.MediaClose: Exception: {ex.Message}");
            }
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
        /// Lock the two media players together at the specified offset position
        /// </summary>
        /// <param name="_lockedMediaPlayersFrameOffset"></param>
        /// <returns></returns>
        public async Task<bool> MediaLockMediaPlayersAsync(TimeSpan? _lockedMediaPlayersFrameOffset, ObservableCollection<Event>? existingEvents)
        {
            CheckIsUIThread();

            bool ret = false;

            if (!mediaSynchronized)
            {
                // Create the MediaTimelineController
                mediaTimelineController = new MediaTimelineController();
                mediaTimelineController.StateChanged += MediaTimelineController_StateChanged;
                mediaTimelineController.PositionChanged += MediaTimelineController_PositionChanged;
                //_mediaTimelineController.Ended += MediaTimelineController_Ended;    // In case we need later
                //_mediaTimelineController.Failed += MediaTimelineController_Failed;  // In case we need later

                // Wait the media players to be ready
                await WaitForPlayersToHaveValidPositionAsync(mediaPlayerLeft, mediaPlayerRight);

                // Remember the current Media Player Position. This is so we can check that the players
                // did actually move to the correct position later in this method
                TimeSpan leftPositionActual = (TimeSpan)mediaPlayerLeft.Position!;
                TimeSpan rightPositionActual = (TimeSpan)mediaPlayerRight.Position!;

                if (_lockedMediaPlayersFrameOffset is null)
                {
                    // Lock in the current player play position.  The user is manually syncing the media players
                    if (mediaPlayerLeft.Position is not null && mediaPlayerRight.Position is not null)
                    {

                        // Correct on frame boundaries (rounding down is OK)
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
                        await Task.Delay(200);

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


                    // Check the media players moved
                    int tries = 0;

                    while ((leftPositionActual == (TimeSpan)mediaPlayerLeft.Position && rightPositionActual == (TimeSpan)mediaPlayerRight.Position) &&
                           tries < 20)
                    {
                        // Sleep 100ms
                        await Task.Delay(100);
                        tries++;
                    }
                }

                // Limit the duration so one media player doesn't play longer than the other
                if (mediaPlayerLeft.NaturalDuration is TimeSpan leftDuration &&
                    mediaPlayerRight.NaturalDuration is TimeSpan rightDuration)
                {
                    TimeSpan leftOffset = mediaSynchronizedFrameOffset > TimeSpan.Zero ? TimeSpan.Zero : -mediaSynchronizedFrameOffset;
                    TimeSpan rightOffset = mediaSynchronizedFrameOffset > TimeSpan.Zero ? mediaSynchronizedFrameOffset : TimeSpan.Zero;

                    var leftRemaining = leftDuration - leftOffset;
                    var rightRemaining = rightDuration - rightOffset;

                    if (leftRemaining < TimeSpan.Zero || rightRemaining < TimeSpan.Zero)
                        Debug.WriteLine($"MediaLockMediaPlayers: Offsets cannot exceed video durations, left remaining:{leftRemaining:hh\\:mm\\:ss\\.ff}, right remaining:{rightRemaining:hh\\:mm\\:ss\\.ff}.");

                    TimeSpan newDuration = TimeSpan.FromTicks(Math.Min(leftRemaining.Ticks, rightRemaining.Ticks));
                    mediaTimelineController!.Duration = newDuration;
                    durationSetByThisClass = true;  // Can't be overridden
                    maxNaturalDurationForController = newDuration;
            
                    // Signal the adjusted media duration via mediator (used by the MediaStereoController and MediaControl)
                    MediaStereoControllerEventData data = new(MediaStereoControllerEventData.eMediaStereoControllerEvent.Duration)
                    {
                        duration = newDuration,
                    };
                    mediaControllerHandler?.Send(data);                    

                    Debug.WriteLine($"MediaLockMediaPlayers: MediaTimelineController.Duration set to {newDuration.TotalSeconds:F2}s");
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


                // Calculate the frame index for each event from the position time span
                CalcFrameIndexesForAllEvents(existingEvents);

                // Set the events if the players are locked
                mediaPlayerLeft.SetEvents(existingEvents);
                mediaPlayerRight.SetEvents(existingEvents);


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
        /// Parse the events and recalculate the frame indexes (they are not saved)
        /// </summary>
        /// <param name=""></param>
        private void CalcFrameIndexesForAllEvents(ObservableCollection<Event>? existingEvents)
        {
            if (existingEvents is not null)
            {
                foreach (Event evt in existingEvents)
                {
                    evt.FrameIndexLeft = mediaPlayerLeft.GetFrameIndexFromPosition(evt.TimeSpanLeftFrame);
                    evt.FrameIndexRight = mediaPlayerRight.GetFrameIndexFromPosition(evt.TimeSpanRightFrame);
                }
            }
        }


        /// <summary>
        /// Wait for the media players to have a valid Position (non-null) and
        /// for the NaturalDuration to be (non-null).
        /// This is used to wait for the media players to be totally ready
        /// </summary>
        /// <param name="mediaPlayerLeft"></param>
        /// <param name="mediaPlayerRight"></param>
        /// <returns></returns>
        private static async Task WaitForPlayersToHaveValidPositionAsync(SurveyorMediaPlayer mediaPlayerLeft, SurveyorMediaPlayer mediaPlayerRight)
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
                var leftDur = mediaPlayerLeft.NaturalDuration;
                var rightDur = mediaPlayerRight.NaturalDuration;

                // Wait until both positions have advanced from zero
                if (leftPos is not null && rightPos is not null &&
                    leftDur is not null && rightDur is not null)
                {
                    Debug.WriteLine($"WaitForPlayersToHaveValidPosition: Ready waited {stopwatch.ElapsedMilliseconds}ms");
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
                // Release the media players to be independent
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
        /// If separate the cameraSide will determine which media player will play
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
        /// Called use other UI components to signal the user press the space bar
        /// Space Bar to used focus component is used to toggle play/pause in media 
        /// locked mode.  If media is not locked the focus as to be on the particular 
        /// mediaControl instance you want to play/pause
        /// </summary>
        public async Task SpaceKeyPressedAsync()
        {
            
            // If the players are locked that pass a space bar keystroke directly to the
            // primary media controller
            if (mediaSynchronized)
            {
                if (MediaIsOpen())
                {
                    if (IsPlaying())
                    {
                        await PauseAsync(eCameraSide.None);
                    }
                    else
                    {
                        Play(eCameraSide.None);
                    }
                }
            }
        }


        /// <summary>
        /// Pause the media. If the media is locked together then both media players will pause
        /// If separate the cameraSide will determine which media player will play
        /// USES 'Internal' to allow Unit Testing
        /// </summary>
        /// <param name="cameraSide"></param>
        internal async Task PauseAsync(eCameraSide cameraSide)
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
                bool paused = await WaitForMediaTimelinePausedAsync(TimeSpan.FromMilliseconds(2000)); // Timeout of 2 seconds

                if (paused)
                {
                    bool forwardFrame = false;

                    // Check media player Positions and MediaTimeLineController position and offset are correct
                    TimeSpan currentMediaOffset = (TimeSpan)mediaPlayerRight.Position! - (TimeSpan)mediaPlayerLeft.Position!;

                    if (currentMediaOffset != mediaSynchronizedFrameOffset)
                    {
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} Both Warning MediaStereoController.Pause: The MediaPlayers position offsets are not correct, open offset:{((TimeSpan)mediaSynchronizedFrameOffset!).TotalSeconds:F3}s, current offset:{currentMediaOffset.TotalSeconds:F3}");
                    }
                    else
                    {
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} Both Info MediaStereoController.Pause: The MediaPlayers position offsets are correct, open offset:{((TimeSpan)mediaSynchronizedFrameOffset!).TotalSeconds:F3}s, current offset:{currentMediaOffset.TotalSeconds:F3}, MediaController Position:{mediaTimelineController.Position.TotalSeconds:F3}, Left Position:{((TimeSpan)mediaPlayerLeft.Position).TotalSeconds:F3}, Right Position:{((TimeSpan)mediaPlayerRight.Position).TotalSeconds:F3}");
                    }

                    // Force a forward frame regardless
                    forwardFrame = true;

                    // Forward frame
                    if (forwardFrame)
                    {
                        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.ff} Both Info PauseControl: Forcing a frame forward to maintain sync");
                        await Task.Delay(10);
                        await FrameMoveAsync(eCameraSide.Left/*Doesn't matter which because we are synchronized*/, 1);


                        // Wait again for pause
                        paused = await WaitForMediaTimelinePausedAsync(TimeSpan.FromMilliseconds(2000));
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
                    mediaPlayerLeft.Pause();
                else
                    mediaPlayerRight.Pause();
            }
        }



        /// <summary>
        /// USed to wait the MediaTimelineController to reach the Paused state
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns></returns>
        private async Task<bool> WaitForMediaTimelinePausedAsync(TimeSpan timeout)
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
        /// Move the media forward(positive) or back(negative) by the time span duration
        /// The function will move both players if they are locked together. If they are not locked together
        /// it will use cameraSide to determine which player to move
        /// USES 'Internal' to allow Unit Testing
        /// </summary>
        /// <param name="cameraSide"></param>
        /// <param name="timeSpan"></param>
        internal async Task FrameMoveAsync(eCameraSide cameraSide, TimeSpan deltaPosition)
        {
            CheckIsUIThread();

            if (IsPlaying())
                await PauseAsync(cameraSide);

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
                    else if (position > maxNaturalDurationForController)
                        position = maxNaturalDurationForController;

                    try
                    {
                        // Move to the relative position
                        mediaTimelineControllerPositionPausedMode = position;
                        mediaTimelineController.Position = mediaTimelineControllerPositionPausedMode;
                    }
                    catch (Exception ex)
                    {
                        report?.Error(cameraSide.ToString(), $"MediaStereoController.FrameJump: Frame failed to jump to PositionOffset: {mediaTimelineControllerPositionPausedMode:hh\\:mm\\:ss\\.ff}, {ex.Message}");
                    }
                }
            }
            else
            {
                if (cameraSide == eCameraSide.Left)
                    mediaPlayerLeft.FrameMove(deltaPosition);
                else
                    mediaPlayerRight.FrameMove(deltaPosition);
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
        internal async Task FrameMoveAsync(eCameraSide cameraSide, int frames)
        {
            CheckIsUIThread();

            if (mediaSynchronized)
            {
                // Use the left media player to calculate the timeSpan for MediaTimelineController
                // This replies on the media in both players having the same frame rate!
                TimeSpan leftTimePerFrame = (TimeSpan)mediaPlayerLeft.TimePerFrame;
                TimeSpan timeSpan = leftTimePerFrame * frames;

                await FrameMoveAsync(cameraSide, timeSpan);
            }
            else
            {
                if (cameraSide == eCameraSide.Left)
                {
                    TimeSpan timePerFrame = (TimeSpan)mediaPlayerLeft.TimePerFrame;
                    TimeSpan timeSpan = timePerFrame * frames;
                    mediaPlayerLeft.FrameMove(timeSpan);
                }
                else
                {
                    TimeSpan timePerFrame = (TimeSpan)mediaPlayerRight.TimePerFrame;
                    TimeSpan timeSpan = timePerFrame * frames;
                    mediaPlayerRight.FrameMove(timeSpan);
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
                else if (position > maxNaturalDurationForController)
                    position = maxNaturalDurationForController;

                if (mediaTimelineController!.State == MediaTimelineControllerState.Paused)
                {
                    // Enable Frame Server
                    mediaPlayerLeft.FrameServerEnable(true);
                    mediaPlayerRight.FrameServerEnable(true);
                }

                try
                {
                    // Move to the absolute position
                    mediaTimelineControllerPositionPausedMode = position;
                    mediaTimelineController.Position = mediaTimelineControllerPositionPausedMode;
                }
                catch (Exception ex)
                {
                    report?.Error(cameraSide.ToString(), $"MediaStereoController.FrameJump: Frame failed to jump to PositionOffset: {mediaTimelineControllerPositionPausedMode:hh\\:mm\\:ss\\.ff}, {ex.Message}");
                }
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
        /// <returns>true if media is synchronized</returns>
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
        /// <param name="sender"></param>
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
            if (maxNaturalDurationForController != TimeSpan.Zero && sender.Position > maxNaturalDurationForController)
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

        // In case we need later
        //private void MediaTimelineController_Failed(MediaTimelineController sender, MediaTimelineControllerFailedEventArgs args)
        //{
        //    Debug.WriteLine($"MediaTimelineController_Failed: ");            
        //}

        // In case we need later
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
        internal void _UpdateDurationAndFrameRateFromMediaPlayer(eCameraSide cameraSide, TimeSpan naturalDuration, double frameRate)
        {
            if (!durationSetByThisClass)
            {
                // Maintain _maxNaturalDurationForController so it is as long as the longest media source.
                // ??? Is this correct, surely we want the shortest of the two durations
                if (naturalDuration > maxNaturalDurationForController)
                    maxNaturalDurationForController = naturalDuration;
            }

            if (cameraSide == eCameraSide.Left)
                _frameRate = frameRate;
        }


        /// <summary>
        /// Received from the MediaPlayer a new frame has been displayed
        /// </summary>
        internal void _NewImageFrame()
        {
            // Reset any target or measurement variables and
            // clear any targets on the magnifier windows
            ClearCachedTargets();
        }


        /// <summary>
        /// Receive a request to edit an existing SpeciesInfo record
        /// </summary>
        /// <param name="eventGuid"></param>
        /// <returns></returns>
        internal async Task _EditSpeciesInfoAsync(Guid eventGuid)
        {
            if (eventsControl is not null)
            {
                Event? evt = eventsControl.FindEvent(eventGuid);

                if (evt is not null)
                {
                    IPointData? pointData = evt.EventData;

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
                        if (await speciesSelector.SpeciesEdit(mainWindow, speciesInfo, speciesImageCache, pointData, mainWindow.GetSurveyRulesClass()) == true)
                        {
                            if (eventsControl is not null)
                                await eventsControl.AddEventAsync(evt);
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Received from the MediaPlayers to delete a Measurement, 3DPoint or SinglePoint 
        /// </summary>
        /// <param name="eventGuid"></param>
        internal void _DeleteMeasure3DPointOrSinglePoint(Guid eventGuid)

        {
            if (eventsControl is not null)
            {
                Event? evt = eventsControl.FindEvent(eventGuid);

                if (evt is not null)
                    eventsControl.DeleteEvent(evt);          
            }
        }


        /// <summary>
        /// The user changes the Diagnostic Information setting
        /// </summary>
        /// <param name="diagnosticInformation"></param>
        internal void _SetDiagnosticInformation(bool _diagnosticInformation)
        {
            diagnosticInformation = _diagnosticInformation;
        }


        /// <summary>
        /// Experimental setting has changed (or is being initially set)
        /// </summary>
        /// <param name="_experimentalEnabled"></param>
        internal void _SetExperimental(bool _experimentalEnabled,
                                       bool _experimentalFeatureSetAEnabled,
                                       bool _experimentalFeatureSetBEnabled,
                                       bool _experimentalFeatureSetCEnabled)
        {
            experimentalEnabled = _experimentalEnabled;
            experimentalFeatureSetAEnabled = _experimentalFeatureSetAEnabled;
            experimentalFeatureSetBEnabled = _experimentalFeatureSetBEnabled;
            experimentalFeatureSetCEnabled = _experimentalFeatureSetCEnabled;
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
            // We are going to assume the left and right media are the same size and only use info from the left camera
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
                            PauseAsync(eCameraSide.Left);
                        else
                            PauseAsync(eCameraSide.Right);
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
            CheckIsUIThread();

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
                        await FrameMoveAsync(eCameraSide.None, framesDelta);
                    }
                    else
                    {
                        if (controlType == SurveyorMediaControl.eControlType.Primary)
                            await FrameMoveAsync(eCameraSide.Left, framesDelta);
                        else
                            await FrameMoveAsync(eCameraSide.Right, framesDelta);
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
                        await FrameMoveAsync(eCameraSide.None, frames);
                    }
                    else
                    {
                        if (controlType == SurveyorMediaControl.eControlType.Primary)
                            await FrameMoveAsync(eCameraSide.Left, frames);
                        else
                            await FrameMoveAsync(eCameraSide.Right, frames);
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
            // Mute/Unmute maybe need a UI thread so to be sure we are on the UI thread
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
                // controls the left player and the secondary (right) media control controls the right player
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
        /// If the media is synchronized, save the frames from both the left and right
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
                    {
                        // Sync check //???--> I think this needs to go into the MediaStereoController.Pause
                        if (mediaPlayerLeft.Position is not null && mediaPlayerRight.Position is not null)
                        {
                            TimeSpan leftPositionActual = (TimeSpan)mediaPlayerLeft.Position;
                            TimeSpan rightPositionActual = (TimeSpan)mediaPlayerRight.Position;

                            // Correct on frame boundaries (rounding down is OK)
                            long leftFrame = (long)(leftPositionActual.TotalMilliseconds * _frameRate / 1000.0);
                            long rightFrame = (long)(rightPositionActual.TotalMilliseconds * _frameRate / 1000.0);
                            TimeSpan leftPositionRounded = TimeSpan.FromMilliseconds(leftFrame * 1000 / _frameRate);
                            TimeSpan rightPositionRounded = TimeSpan.FromMilliseconds(rightFrame * 1000 / _frameRate);
                            TimeSpan mediaSynchronizedFrameOffsetCheck = (TimeSpan)rightPositionRounded - (TimeSpan)leftPositionRounded;

                            // Calculate 1/10 of a frame duration
                            TimeSpan frameTolerance = TimeSpan.FromSeconds(1.0 / (_frameRate * 10));

                            // Calculate absolute difference between the TimeSpans
                            TimeSpan timeDiff = mediaSynchronizedFrameOffsetCheck - mediaSynchronizedFrameOffset;

                            if (timeDiff.Duration() > frameTolerance)
                            {
                                Debug.WriteLine($"MediaStereoController.UserReqSaveFrame: Warning Synchronization Offset: Required:{mediaSynchronizedFrameOffset.TotalMilliseconds / 1000.0:F3}s, Actual:{mediaSynchronizedFrameOffsetCheck.TotalMilliseconds / 1000.0:F3})");

                                //??? TO DO Maybe try to resynchronize the two players with a frame back/forward?
                            }
                        }

                        // Save the current frame
                        await mainWindow.SaveCurrentFrameAsync(SurveyorMediaControl.eControlType.Both);                                    
                    }
                    else if (controlType != SurveyorMediaControl.eControlType.None)
                        await mainWindow.SaveCurrentFrameAsync(controlType);
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
        internal void UserReqMagWindowSizeSelect(bool trueLeftfalseRight, string magWindowSize)
        {
            // MagWindowSizeSelect is thread Safe/UI Check not required

            if (mediaSynchronized || trueLeftfalseRight == true)
                mediaPlayerLeft.MagWindowSizeSelect(magWindowSize);            

            if (mediaSynchronized || trueLeftfalseRight == false)
                mediaPlayerRight.MagWindowSizeSelect(magWindowSize);
        }


        /// <summary>
        /// User requested a magnify window zoom factor change
        /// </summary>
        /// <param name="controlType"></param>
        /// <param name="_canvasZoomFactor"></param>
        internal void UserReqMagZoomSelect(bool trueLeftfalseRight, double canvasZoomFactor)
        {
            // MagWindowZoomFactor is thread Safe/UI Check not required

            if (mediaSynchronized || trueLeftfalseRight == true)
                mediaPlayerLeft.MagWindowZoomFactor(canvasZoomFactor);

            if (mediaSynchronized || trueLeftfalseRight == false)
                mediaPlayerRight.MagWindowZoomFactor(canvasZoomFactor);
        }


        /// <summary>
        /// User requested a change to the layers of information displayed in the canvas frame
        /// </summary>
        /// <param name="controlType"></param>
        /// <param name="layertype"></param>
        internal void UserReqLayersDisplayed(SurveyorMediaControl.eControlType controlType, LayerType layertype)
        {
            if (mediaSynchronized || controlType == SurveyorMediaControl.eControlType.Primary)
                mediaPlayerLeft.SetLayerType(layertype);

            if (mediaSynchronized || controlType == SurveyorMediaControl.eControlType.Secondary)
                mediaPlayerRight.SetLayerType(layertype);
        }


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

        internal void TargetPointSelected(SurveyorMediaPlayer.eCameraSide cameraSide, bool TruePointAFalsePointB, Point? pointA, Point? pointB)
        {
            if (cameraSide == SurveyorMediaPlayer.eCameraSide.Left)
            {
                if (TruePointAFalsePointB)
                {
                    if (pointA is not null)
                        mediaPlayerRight.OtherInstanceTargetSet(true, null);
                    else
                        mediaPlayerRight.OtherInstanceTargetSet(false, null);
                }
                else
                {
                    if (pointB is not null)
                        mediaPlayerRight.OtherInstanceTargetSet(null, true);
                    else
                        mediaPlayerRight.OtherInstanceTargetSet(null, false);
                }
            }
            else if (cameraSide == SurveyorMediaPlayer.eCameraSide.Right)
            {
                if (TruePointAFalsePointB)
                {
                    if (pointA is not null)
                        mediaPlayerLeft.OtherInstanceTargetSet(true, null);
                    else
                        mediaPlayerLeft.OtherInstanceTargetSet(false, null);
                }
                else
                {
                    if (pointB is not null)
                        mediaPlayerLeft.OtherInstanceTargetSet(null, true);
                    else
                        mediaPlayerLeft.OtherInstanceTargetSet(null, false);
                }
            }

            // Remember the target points 
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

                // Calculate the alternative camera
                eCameraSide cameraSideAlt = cameraSide == SurveyorMediaPlayer.eCameraSide.Left ? SurveyorMediaPlayer.eCameraSide.Right : SurveyorMediaPlayer.eCameraSide.Left;
                bool needEpipolarLine = false;
                bool clearEpipolarLine = false;

                // We only need to calculate the Epipolar line if there isn't already a pair of points
                if (cameraSide == SurveyorMediaPlayer.eCameraSide.Left)
                {
                    // Either:
                    // We have just selected a target A on the left camera and there is no selected Target A on the right side
                    // or
                    // We have just selected a target B on the left camera and there is no selected Target B on the right side
                    if ((TruePointAFalsePointB == true && TargetALeft is not null && TargetARight is null) ||
                        (TruePointAFalsePointB == false && TargetBLeft is not null && TargetBRight is null))
                    {
                        needEpipolarLine = true;
                    }
                    // or the point is being removed
                    else if ((TruePointAFalsePointB == true && TargetALeft is null) ||
                             (TruePointAFalsePointB == false && TargetBLeft is null))
                    {
                        clearEpipolarLine = true;
                    }

                }
                else if (cameraSide == SurveyorMediaPlayer.eCameraSide.Right)
                {
                    // Either:
                    // We have just selected a target A on the right camera and there is no selected Target A on the left side
                    // or
                    // We have just selected a target B on the right camera and there is no selected Target B on the left side
                    if ((TruePointAFalsePointB == true && TargetARight is not null && TargetALeft is null) ||
                        (TruePointAFalsePointB == false && TargetBRight is not null && TargetBLeft is null))
                    {
                        needEpipolarLine = true;
                    }
                    // or the point is being removed
                    else if ((TruePointAFalsePointB == true && TargetARight is null) ||
                             (TruePointAFalsePointB == false && TargetBRight is null))
                    {
                        clearEpipolarLine = true;
                    }
                }

                if (point is not null && needEpipolarLine)
                {
                    // *** Epipolar Line Calculation ***
                    if (stereoProjection.CalculateEpipolarLine((bool)TrueLeftFalseRight, (Point)point,
                                out double epiLine_a, out double epiLine_b, out double epiLine_c))
                    {
                        Debug.WriteLine($"{cameraSideAlt} Epipolar for Point({point.Value.X:F2}, {point.Value.Y:F2})  ax+by+c=0: {epiLine_a:F5}x + {epiLine_b:F5}y + {epiLine_c:F5} = 0");
                        Debug.WriteLine($"{cameraSideAlt} Epipolar left border intersect (0, {-epiLine_c / epiLine_b})");


                        // Calculate the points for an epipolar curve
                        if (stereoProjection.CalculateEpipolarCurveDistortedPoints(
                                    (bool)TrueLeftFalseRight,
                                    (double)epiLine_a, (double)epiLine_b, (double)epiLine_c,
                                    out List<Point>? epipolarCurvePoints))
                        {
                            Debug.WriteLine($"{cameraSideAlt} {epipolarCurvePoints!.Count} epipolar Curve points");

                            // Signal to the MagnifyAndMarkerControl to display the epipolar curve
                            MagnifyAndMarkerControlData data = new(MagnifyAndMarkerControlData.MagnifyAndMarkerControlEvent.EpipolarCurve, cameraSideAlt)
                            {
                                TrueEpipolarLinePointAFalseEpipolarLinePointB = (bool)TruePointAFalsePointB,
                                epipolarCurvePoints = epipolarCurvePoints,
                                channelWidth = 0
                            };
                            mediaControllerHandler?.Send(data);
                        }
                    }
                }
                else
                {
                    // This implies that corresponding Targets have been selected (i.e. Left A and Right A or Left B and Right B)
                    // In which case the there is probably a epipolar line on this 'cameraSide' that is no longer needed
                    // Remove Epipolar line from this camera
                    clearEpipolarLine = true;
                }

                if (clearEpipolarLine)
                {
                    // Remove Epipolar Curve from the other camera                       
                    MagnifyAndMarkerControlData data = new(MagnifyAndMarkerControlData.MagnifyAndMarkerControlEvent.EpipolarCurve, cameraSideAlt)
                    {
                        TrueEpipolarLinePointAFalseEpipolarLinePointB = (bool)TruePointAFalsePointB,
                        channelWidth = -1 /* Clear the epipolar line */
                    };
                    mediaControllerHandler?.Send(data);

                }
            }
        }


        /// <summary>
        /// Called dynamically display measurement/range/rms information as the user selects
        /// target points. i.e. before a measurement is added or a 3D point added
        /// </summary>
        internal async Task DisplayDynamicMeasurementAsync(bool trueShowRMSCombinedOnly)
        {
            Stopwatch stopwatch = new();
            stopwatch.Start();

            if (TargetALeft is not null &&
                TargetBLeft is not null &&
                TargetARight is not null &&
                TargetBRight is not null)
            {
                // Enough points for a measurement
                SurveyMeasurement surveyMeasurement = new()
                {
                    LeftXA = TargetALeft!.Value.X,
                    LeftYA = TargetALeft!.Value.Y,
                    LeftXB = TargetBLeft!.Value.X,
                    LeftYB = TargetBLeft!.Value.Y,
                    RightXA = TargetARight!.Value.X,
                    RightYA = TargetARight!.Value.Y,
                    RightXB = TargetBRight!.Value.X,
                    RightYB = TargetBRight!.Value.Y
                };

                // Check that logically Target Left A corresponds to Target Right A and Target Left B is corresponds to Target Right B
                // This is in case the user selected the points the wrong way around. If so the target will be swapped
                //???This function isn't working properly
                //???SurveyMeasurementHelper.EnsureCorrectCorrespondence(surveyMeasurement);

                // Check if suitable calibration data is available for this frame size
                bool isReady = await mainWindow.CheckIfMeasurementSetupIsReadyAsync();
                if (isReady)
                {
                    // This call calculates the distance, range, X & Y offset between the camera system mid-point and the measurement point mid-point
                    if (mainWindow.DoMeasurementAndRulesCalculations(surveyMeasurement))
                    {
                        stopwatch.Stop();
                        Debug.WriteLine($"DisplayDynamicMeasurment: Measurement={surveyMeasurement.Measurement * 1000:F0}mm Range={surveyMeasurement.SurveyRulesCalc.Range:F2}m RMS={surveyMeasurement.SurveyRulesCalc.RMSWorst * 1000:F0}mm, elapsed {stopwatch.ElapsedMilliseconds}ms");

                        mediaControllerHandler?.Send(new MediaStereoControllerEventData(eMediaStereoControllerEvent.DisplayDynamicMeasurement)
                        {
                            showRMSCombinedOnly = trueShowRMSCombinedOnly,
                            measurement = surveyMeasurement.Measurement,
                            rmsCombined = surveyMeasurement.SurveyRulesCalc?.RMSWorst,
                            range = surveyMeasurement.SurveyRulesCalc?.Range,
                            rangeRulePassed = surveyMeasurement.SurveyRulesCalc?.SurveyRuleRange,
                            xOffset = surveyMeasurement.SurveyRulesCalc?.XOffset,
                            horizontalRulePassed = surveyMeasurement.SurveyRulesCalc?.SurveyRuleHoriz,
                            yOffset = surveyMeasurement.SurveyRulesCalc?.YOffset,
                            verticalRulePassed = surveyMeasurement.SurveyRulesCalc?.SurveyRuleVert,
                            rmsTargetA = surveyMeasurement.SurveyRulesCalc?.RMSTargetA,
                            rmsTargetB = surveyMeasurement.SurveyRulesCalc?.RMSTargetB,
                            rmsRulePassed = surveyMeasurement.SurveyRulesCalc?.SurveyRuleRMS
                        });
                    }
                }
            }
            else if (TargetALeft is not null && TargetARight is not null)
            {
                // Enough points for a 3D point
                // Check if suitable calibration data is available for this frame size
                bool isReady = await mainWindow.CheckIfMeasurementSetupIsReadyAsync();
                if (isReady)
                {
                    SurveyStereoPoint surveyStereoPoint = new();

                    if (TargetALeft is not null && TargetARight is not null)
                    {
                        surveyStereoPoint.LeftX = TargetALeft.Value.X;
                        surveyStereoPoint.LeftY = TargetALeft.Value.Y;
                        surveyStereoPoint.RightX = TargetARight.Value.X;
                        surveyStereoPoint.RightY = TargetARight.Value.Y;
                    }
                    else if (TargetBLeft is not null && TargetBRight is not null)
                    {
                        surveyStereoPoint.LeftX = TargetBLeft!.Value.X;
                        surveyStereoPoint.LeftY = TargetBLeft!.Value.Y;
                        surveyStereoPoint.RightX = TargetBRight!.Value.X;
                        surveyStereoPoint.RightY = TargetBRight!.Value.Y;
                    }
                    // This call calculates the distance, range, X & Y offset between
                    // the camera system mid-point and the measurement point mid-point
                    // Note false is returned if there is an error (i.e. it's not that a rules was broken)
                    if (mainWindow.DoRulesCalculations(surveyStereoPoint))
                    {
                        stopwatch.Stop();
                        Debug.WriteLine($"DisplayDynamicMeasurment: 3D Point Range={surveyStereoPoint.SurveyRulesCalc.Range:F2}m RMS={surveyStereoPoint.SurveyRulesCalc.RMSWorst*1000:F0}mm, elapsed {stopwatch.ElapsedMilliseconds}ms");

                        mediaControllerHandler?.Send(new MediaStereoControllerEventData(eMediaStereoControllerEvent.DisplayDynamicMeasurement)
                        {
                            showRMSCombinedOnly = trueShowRMSCombinedOnly,
                            rmsCombined = surveyStereoPoint.SurveyRulesCalc?.RMSWorst,
                            range = surveyStereoPoint.SurveyRulesCalc?.Range,
                            //???rangeRulePassed = surveyStereoPoint.SurveyRulesCalc?.,
                            xOffset = surveyStereoPoint.SurveyRulesCalc?.XOffset,
                            //???horizontalRulePassed = surveyStereoPoint.SurveyRulesCalc?.,
                            yOffset = surveyStereoPoint.SurveyRulesCalc?.YOffset,
                            //???verticalRulePassed = surveyStereoPoint.SurveyRulesCalc?.
                            rmsTargetA = surveyStereoPoint.SurveyRulesCalc?.RMSTargetA,
                            rmsTargetB = surveyStereoPoint.SurveyRulesCalc?.RMSTargetB
                        });
                    }
                }
            }
            else 
            {
                // Clear dynamic measurements
                mediaControllerHandler?.Send(new MediaStereoControllerEventData(eMediaStereoControllerEvent.DisplayDynamicMeasurement));
            }
        }


        /// <summary>
        /// Users requested a new measurement is added
        /// </summary>
        /// <param name="pointA"></param>
        /// <param name="pointB"></param>
        /// <returns></returns>
        internal async Task AddMeasurementRequestAsync(string species)
        {            
            // Check we have two sets of corresponding points from both cameras
            if (TargetALeft is null || TargetBLeft is null || TargetARight is null || TargetBRight is null)
                return;
            else
            {
                SurveyMeasurement surveyMeasurement = new()
                {
                    LeftXA = TargetALeft!.Value.X,
                    LeftYA = TargetALeft!.Value.Y,
                    LeftXB = TargetBLeft!.Value.X,
                    LeftYB = TargetBLeft!.Value.Y,
                    RightXA = TargetARight!.Value.X,
                    RightYA = TargetARight!.Value.Y,
                    RightXB = TargetBRight!.Value.X,
                    RightYB = TargetBRight!.Value.Y
                };

                // Check that logically Target Left A corresponds to Target Right A and Target Left B is corresponds to Target Right B
                // This is in case the user selected the points the wrong way around. If so the target will be swapped
                //???This function isn't working properly
                //???SurveyMeasurementHelper.EnsureCorrectCorrespondence(surveyMeasurement);
                await AddMeasurementOr3DPointOrSinglePointRequestAsync(surveyMeasurement, species);
            }
        }


        /// <summary>
        /// Received a request to add a 3D Point (SurveyStereoPoint)
        /// A SurveyStereoPoint is a corresponding point on both the left and right camera
        /// A SurveyPoint is a point on the left camera only
        /// </summary>
        /// <param name="cameraSide"></param>
        /// <param name="TruePointAFalsePointB"></param>
        /// <returns></returns>
        internal async Task Add3DPointRequestAsync(bool TruePointAFalsePointB, string species)
        {
            SurveyStereoPoint surveyStereoPoint = new();

            if (TruePointAFalsePointB)
            {
                // Check we have a corresponding point from both cameras
                if (TargetALeft is null || TargetARight is null)
                    return;

                surveyStereoPoint.LeftX = TargetALeft.Value.X;
                surveyStereoPoint.LeftY = TargetALeft.Value.Y;
                surveyStereoPoint.RightX = TargetARight.Value.X;
                surveyStereoPoint.RightY = TargetARight.Value.Y;
            }
            else
            {
                // Check we have a corresponding point from both cameras
                if (TargetBLeft is null || TargetBRight is null)
                    return;

                surveyStereoPoint.LeftX = TargetBLeft!.Value.X;
                surveyStereoPoint.LeftY = TargetBLeft!.Value.Y;
                surveyStereoPoint.RightX = TargetBRight!.Value.X;
                surveyStereoPoint.RightY = TargetBRight!.Value.Y;
            }

            await AddMeasurementOr3DPointOrSinglePointRequestAsync(surveyStereoPoint, species);
        }


        /// <summary>
        /// Received a request to add a Single Point (SurveyPoint)
        /// A SurveyPoint is a single point on either left and right camera
        /// </summary>
        /// <param name="cameraSide"></param>
        /// <param name="TruePointAFalsePointB"></param>
        /// <returns></returns>
        internal async Task AddSinglePointRequestAsync(SurveyorMediaPlayer.eCameraSide cameraSide, bool TruePointAFalsePointB, string species)
        {
            if (cameraSide != eCameraSide.None)
            {
                SurveyPoint surveyPoint = new()
                {
                    TrueLeftFalseRight = cameraSide == eCameraSide.Left
                };

                if (cameraSide == eCameraSide.Left && TruePointAFalsePointB)
                {
                    // Check we have a valid point
                    if (TargetALeft is null)
                        return;

                    surveyPoint.X = TargetALeft.Value.X;
                    surveyPoint.Y = TargetALeft.Value.Y;                    
                }
                else if (cameraSide == eCameraSide.Left && !TruePointAFalsePointB)
                {
                    // Check we have a valid point
                    if (TargetBLeft is null)
                        return;

                    surveyPoint.X = TargetBLeft.Value.X;
                    surveyPoint.Y = TargetBLeft.Value.Y;
                }
                else if (cameraSide == eCameraSide.Right && TruePointAFalsePointB)
                {
                    // Check we have a valid point
                    if (TargetARight is null)
                        return;

                    surveyPoint.X = TargetARight.Value.X;
                    surveyPoint.Y = TargetARight.Value.Y;
                }
                else if (cameraSide == eCameraSide.Right && !TruePointAFalsePointB)
                {
                    // Check we have a valid point
                    if (TargetBRight is null)
                        return;

                    surveyPoint.X = TargetBRight.Value.X;
                    surveyPoint.Y = TargetBRight.Value.Y;
                }

                await AddMeasurementOr3DPointOrSinglePointRequestAsync(surveyPoint, species);
            }
        }


        /// <summary>
        /// Users requested a new measurement is added
        /// </summary>
        /// <param name="pointA"></param>
        /// <param name="pointB"></param>
        /// <returns></returns>
        private async Task AddMeasurementOr3DPointOrSinglePointRequestAsync(IPointData pointData, string preSelectedSpecies)
        {
            // Check if the Events is setup and the stereo projection is not null (normally
            // stereoProject is not null if we have calibration data)
            if (eventsControl is not null && stereoProjection is not null)
            {
                // What type is this
                SurveyDataType surveyDataType;
                bool isMeasurement = false;
                bool is3DPoint = false;
                bool isSinglePoint = false;
                if (pointData is SurveyMeasurement)
                {
                    surveyDataType = SurveyDataType.SurveyMeasurementPoints;
                    isMeasurement = true;
                }
                else if (pointData is SurveyStereoPoint)
                {
                    surveyDataType = SurveyDataType.SurveyStereoPoint;
                    is3DPoint = true;
                }
                else if (pointData is SurveyPoint)
                {
                    surveyDataType = SurveyDataType.SurveyPoint;
                    isSinglePoint = true;
                }
                else
                    throw new Exception("MediaStereoController.AddMeasurementOr3DPointRequest: Unknown point data type");

                // Calculations and rules

                // If stereo point do the measurement calculations
                if (isMeasurement || is3DPoint)
                {
                    // Check if suitable calibration data is available for this frame size
                    bool isReady = await mainWindow.CheckIfMeasurementSetupIsReadyAsync();
                    if (isReady)
                    {
                        if (pointData is SurveyMeasurement surveyMeasurement2)
                        {
                            // This call calculates the distance, range, X & Y offset between the camera system mid-point and the measurement point mid-point
                            mainWindow.DoMeasurementAndRulesCalculations(surveyMeasurement2);
                            // No need to check if IsDirty needs to be set as this is a event item and
                            // as such adding to the event list will set the IsDirty flag
                        }
                        else if (pointData is SurveyStereoPoint surveyStereoPoint2)
                        {

                            // This call calculates the distance, range, X & Y offset between
                            // the camera system mid-point and the measurement point mid-point
                            // Note false is returned if there is an error (i.e. it's not that a rules was broken)
                            mainWindow.DoRulesCalculations(surveyStereoPoint2);
                            // No need to check if IsDirty needs to be set as this is a event item and
                            // as such adding to the event list will set the IsDirty flag
                        }
                    }
                    // Note. We still log the event even if no measurement done
                    // Calibration data may not be available at this time but when
                    // calibration data is available all measurements will be recalculated
                }


                // Assigned the species info via a user dialog box
                bool add = true;

                SpeciesInfo specifiesInfo = new(); 
                
                if (await speciesSelector.SpeciesNew(mainWindow, 
                                                    specifiesInfo, 
                                                    preSelectedSpecies, 
                                                    speciesImageCache, 
                                                    pointData,
                                                    mainWindow.GetSurveyRulesClass()) == false)
                {
                    // Species info canceled, check the user still want to add the measurement
                    // (but only if the Survey Rules all passed, this is some we don't encourage bad RMS
                    // measurements to be saved instead of corrected)
                    bool? surveyRulesPassed = false;
                    if (pointData is SurveyMeasurement surveyMeasurement2 && surveyMeasurement2 is not null)
                    {
                        surveyRulesPassed = surveyMeasurement2.SurveyRulesCalc.SurveyRules;
                    }
                    else if (pointData is SurveyStereoPoint surveyStereoPoint2 && surveyStereoPoint2 is not null)
                    {
                        surveyRulesPassed = surveyStereoPoint2.SurveyRulesCalc.SurveyRules;
                    }

                    if (surveyRulesPassed == true)
                    {
                        add = await SpeciesInfoMissingWarningDialogAsync(isMeasurement, is3DPoint, isSinglePoint);
                    }
                    else
                    {
                        add = false;
                    }
                }

                if (add)
                {
                    Event evt = new(surveyDataType)
                    {
                        EventData = pointData
                    };

                    // This maybe empty if the user canceled the species selector dialog
                    // but that is OK.  It maybe that someone is doing the measuring and someone
                    // else the fish ID
                    if (pointData is SurveyMeasurement surveyMeasurement)
                    {
                        surveyMeasurement.SpeciesInfo = specifiesInfo;
                    }
                    else if (pointData is SurveyStereoPoint surveyStereoPoint)
                    {
                        surveyStereoPoint.SpeciesInfo = specifiesInfo;
                    }
                    else if (pointData is SurveyPoint surveyPoint)
                    {
                        surveyPoint.SpeciesInfo = specifiesInfo;
                    }



                    // Add the event to the list and clear down
                    await SetMediaPositionAddEventClearTargetsAsync(evt);

                    // Clear stereo projection calculation class
                    stereoProjection.PointsClear();

                    // Display the missing calibration warning InfoBar if necessary
                    mainWindow.SetInfoBarCalibrationMissing();
                }
            }
        }
        

        /// <summary>
        /// Used by AddMeasurementRequest, Add3DPointRequest and AddSinglePointRequest to do
        /// the work common to all three:
        /// Load the event with the correct media position
        /// Add the event to the event list
        /// Clear the targets in memory and on the screen
        /// </summary>
        /// <param name="evt"></param>
        private async Task SetMediaPositionAddEventClearTargetsAsync(Event evt)
        {
            // Log the event (even if no measurement done)
            if (surveyType == Survey.SurveyType.StereoFish && mediaTimelineController is not null)
            {
                TimeSpan? timelineConntrollerPoistion = mediaTimelineController.Position;
                TimeSpan? leftPosition = mediaPlayerLeft.Position;
                TimeSpan? rightPosition = mediaPlayerRight.Position;

                if (leftPosition is not null && rightPosition is not null)
                {
                    // Add media position information
                    evt.TimeSpanTimelineController = (TimeSpan)timelineConntrollerPoistion;
                    evt.TimeSpanLeftFrame = (TimeSpan)leftPosition;
                    evt.TimeSpanRightFrame = (TimeSpan)rightPosition;

                    // Add frame indexes (not saved, values always calculated)
                    evt.FrameIndexLeft = mediaPlayerLeft.GetFrameIndexFromPosition((TimeSpan)leftPosition);
                    evt.FrameIndexRight = mediaPlayerRight.GetFrameIndexFromPosition((TimeSpan)rightPosition);

                    // Add the species info to the Events list
                    eventsControl?.AddEventAsync(evt);

                    // Remove targets from memory
                    ClearCachedTargets();

                    // Remove targets from the canvas
                    mediaPlayerLeft.SetTargets(null, null);
                    mediaPlayerRight.SetTargets(null, null);
                }
            }
            else if (surveyType == Survey.SurveyType.MonoFish)
            {
                TimeSpan? leftPosition = mediaPlayerLeft.Position;

                if (leftPosition is not null)
                {
                    // Add media position information
                    evt.TimeSpanTimelineController = (TimeSpan)leftPosition;
                    evt.TimeSpanLeftFrame = (TimeSpan)leftPosition;

                    // Add frame indexes (not saved, values always calculated)
                    evt.FrameIndexLeft = mediaPlayerLeft.GetFrameIndexFromPosition((TimeSpan)leftPosition);
                    evt.FrameIndexRight = -1;

                    // Add the species info to the Events list
                    if (eventsControl is not null)
                        await eventsControl.AddEventAsync(evt);

                    // Remove targets from memory
                    ClearCachedTargets();

                    // Remove target from the canvas
                    mediaPlayerLeft.SetTargets(null, null);
                }
            }
            else if (surveyType == Survey.SurveyType.MonoBenthic)
            {
                // To do
            }
        }


        /// <summary>
        /// Called because the user don't setup the species info but may still want to save the 
        /// Measurement Point, 3D Point or Single Point
        /// The user has a do not ask again option.  
        /// </summary>
        /// <param name="isMeasurement"></param>
        /// <param name="is3DPoint"></param>
        /// <param name="isSinglePoint"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private async Task<bool> SpeciesInfoMissingWarningDialogAsync(bool isMeasurement, bool is3DPoint, bool isSinglePoint)
        {
            bool ret = false;

            if (!justSaveEventDoAsk)
            {
                // Prepare the contents text
                string bodyText;
                string doNotAskAgainText;
                if (isMeasurement)
                {
                    bodyText = "Do you want to save the Measurement Point anyway? Species information can be added later.";
                    doNotAskAgainText = "Do not show me this again, just save the measurement points";
                }
                else if (is3DPoint)
                {
                    bodyText = "Do you want to save the 3D Point anyway? Species information can be added later.";
                    doNotAskAgainText = "Do not show me this again, just save the 3D point";
                }
                else if (isSinglePoint)
                {
                    bodyText = "Do you want to save the Single Point anyway? Species information can be added later.";
                    doNotAskAgainText = "Do not show me this again, just save the single point";
                }
                else
                {
                    // Bad parameters three exception
                    throw new ArgumentException("SpeciesInfoMissingWarningDialog: Bad parameters");
                }

                ContentDialog dialog = new()
                {
                    Title = "No Species Selected",
                    PrimaryButtonText = "Yes",
                    CloseButtonText = "No",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = mainWindow.Content.XamlRoot // Ensure dialog attaches to correct visual tree
                };

                var contentText = new TextBlock
                {
                    Text = bodyText,
                    TextWrapping = TextWrapping.WrapWholeWords
                };

                CheckBox suppressCheckbox = new CheckBox
                {
                    Content = doNotAskAgainText
                };

                var stackPanel = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        contentText,
                        suppressCheckbox
                    }
                };

                dialog.Content = stackPanel;

                // Show the dialog and await the result
                var result = await dialog.ShowAsync();

                // Handle the dialog result
                if (result == ContentDialogResult.Primary)
                {
                    ret = true;
                }

                if (suppressCheckbox.IsChecked == true)
                {
                    // User has selected to not be asked again
                    justSaveEventDoAsk = true;
                }
            }
            else
            {
                // Just save the event without asking
                ret = true;
            }

            return ret;
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
                    eventsControl?.AddEventAsync(evt);
                }
            }
        }





        /// <summary>
        /// Clear the targets on the magnifier windows and the remembered copies in this class
        /// </summary>
        private void ClearCachedTargets()
        {
            TargetALeft = null;
            TargetBLeft = null;
            TargetARight = null;
            TargetBRight = null;
        }


        /// <summary>
        /// Used on UI Thread dependent functions to check if the function is being called from the UI thread
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
            Poistion,
            Duration,
            DisplayDynamicMeasurement   // Used to signal dynamic measurement/rms/range calcs as
                                        // target points are setup and moved
        }

        public eMediaStereoControllerEvent mediaStereoControllerEvent;

        // Used for Media synchronized
        public TimeSpan? positionOffset;

        // Used for Duration
        public TimeSpan? duration;

        // Used for DisplayDynamicMeasurement
        public double? measurement;
        public bool? showRMSCombinedOnly; // If true then only display the RMS combined value, false show the element values as well
        public double? rmsCombined;
        public double? rmsTargetA;
        public double? rmsTargetB;
        public bool? rmsRulePassed;
        public double? range;
        public bool? rangeRulePassed;
        public double? xOffset;
        public bool? horizontalRulePassed;
        public double? yOffset;
        public bool? verticalRulePassed;
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
                                mediaStereoController._UpdateDurationAndFrameRateFromMediaPlayer(data.cameraSide, (TimeSpan)data.duration, (double)data.frameRate);
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
                            mediaStereoController.UserReqMoveStep(data.controlType, -10/*old method was time based -(new TimeSpan(0, 0, 10))*/);
                            break;

                        // Move step forward 30 seconds
                        case MediaControlEventData.eMediaControlEvent.UserReqMoveStepForward:
                            mediaStereoController.UserReqMoveStep(data.controlType, 30/*old method was time based (new TimeSpan(0, 0, 30))*/);
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
                        case MediaControlEventData.eMediaControlEvent.UserReqMagWindowSizeSelect:
                            if (data.magWindowSize is not null)
                            {
                                bool trueLeftfalseRight = data.controlType == SurveyorMediaControl.eControlType.Primary ? true : false;
                                mediaStereoController.UserReqMagWindowSizeSelect(trueLeftfalseRight, data.magWindowSize);
                            }
                            break;

                        // Mag window zoom factor change request
                        case MediaControlEventData.eMediaControlEvent.UserReqMagZoomSelect:
                            if (data.canvasZoomFactor is not null)
                            {
                                bool trueLeftfalseRight = data.controlType == SurveyorMediaControl.eControlType.Primary ? true : false;
                                mediaStereoController.UserReqMagZoomSelect(trueLeftfalseRight, (double)data.canvasZoomFactor);
                            }
                            break;

                        // Layer type displayed
                        case MediaControlEventData.eMediaControlEvent.UserReqLayersDisplayed:
                            if (data.layerTypesDisplayed is not null)
                                mediaStereoController.UserReqLayersDisplayed(data.controlType, (LayerType)data.layerTypesDisplayed);
                            break;  
                    }
                }
            }
            else if (message is not null && message is MagnifyAndMarkerControlEventData)
            {
                MagnifyAndMarkerControlEventData data = (MagnifyAndMarkerControlEventData)message;

                switch (data.magnifyAndMarkerControlEvent)
                {
                    case MagnifyAndMarkerControlEvent.TargetPointSelected:
                        if (data.TruePointAFalsePointB is not null)
                        {
                            // Store the selected points, inform the reciprocal media players and request
                            // epipolar lines
                            SafeUICall(() => mediaStereoController.TargetPointSelected(data.cameraSide, (bool)data.TruePointAFalsePointB, data.pointA, data.pointB));

                            // Dynamically display measurement/rms/range info as defined by what points
                            // have been selected. Display in the MainWindow to help the user create
                            // actuate measurements
                            mediaStereoController.DisplayDynamicMeasurementAsync(false/*trueShowAverageRMSOnly*/);

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

                    case MagnifyAndMarkerControlEvent.AddMeasurementRequest:
                        SafeUICall(async () => await mediaStereoController.AddMeasurementRequestAsync(data.species));
                        break;

                    case MagnifyAndMarkerControlEvent.Add3DPointRequest:
                        if (data.TruePointAFalsePointB is not null)
                        {
                            SafeUICall(async () => await mediaStereoController.Add3DPointRequestAsync((bool)data.TruePointAFalsePointB, data.species));
                        }
                        break;

                    case MagnifyAndMarkerControlEvent.AddSinglePointRequest:
                        if (data.TruePointAFalsePointB is not null)
                        {
                            SafeUICall(async () => await mediaStereoController.AddSinglePointRequestAsync(data.cameraSide, (bool)data.TruePointAFalsePointB, data.species));
                        }
                        break;

                    case MagnifyAndMarkerControlEvent.EditSpeciesInfoRequest:
                        if (data.eventGuid is not null)
                        {
                            SafeUICall(async () => await mediaStereoController._EditSpeciesInfoAsync((Guid)data.eventGuid));
                            Debug.WriteLine($"***EditSpeciesInfo");
                        }
                        break;
                    case MagnifyAndMarkerControlEvent.DeleteMeasure3DPointOrSinglePoint:
                        if (data.eventGuid is not null)
                        {
                            SafeUICall(() => mediaStereoController._DeleteMeasure3DPointOrSinglePoint((Guid)data.eventGuid));
                            Debug.WriteLine($"***DeleteMeasure3DPointOrSinglePoint");
                        }
                        break;
                    case MagnifyAndMarkerControlEvent.UserReqMagWindowSizeSelect:
                        if (data.magWindowSize is not null)
                        {
                            // Set the opposite camera side
                            bool trueLeftfalseRight = data.cameraSide == SurveyorMediaPlayer.eCameraSide.Left ? false : true;
                            mediaStereoController.UserReqMagWindowSizeSelect(trueLeftfalseRight, data.magWindowSize);
                        }
                        break;
                    case MagnifyAndMarkerControlEvent.UserReqMagZoomSelect:
                        if (data.canvasZoomFactor is not null)
                        {
                            // Set the opposite camera side
                            bool trueLeftfalseRight = data.cameraSide == SurveyorMediaPlayer.eCameraSide.Left ? false : true;
                            mediaStereoController.UserReqMagZoomSelect(trueLeftfalseRight, (double)data.canvasZoomFactor);
                        }
                        break;
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
                            mediaStereoController._SetDiagnosticInformation((bool)data!.diagnosticInformation);
                        }
                        break;
                    // The user has changed the Experimental settings
                    case eSettingsWindowEvent.Experimental:
                        if (data.experimentalEnabled is not null &&
                            data.experimentalFeatureSetAEnabled is not null &&
                            data.experimentalFeatureSetBEnabled is not null &&
                            data.experimentalFeatureSetCEnabled is not null)
                        {
                            mediaStereoController._SetExperimental((bool)data!.experimentalEnabled,
                                                                     (bool)data.experimentalFeatureSetAEnabled,
                                                                     (bool)data.experimentalFeatureSetBEnabled,
                                                                     (bool)data.experimentalFeatureSetCEnabled);
                        }
                        break;
                }
            }

        }
    }
}