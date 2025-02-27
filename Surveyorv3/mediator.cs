// Version: 1.5
// 19 Feb 2024 Change namespace to Surveyor
// 19 Feb 2024 Changed to work for WinUI (was WPF)
// 09 Mar 2024 Added MainWindow parameter to TListener constructor to enable SafeUICall() to work
// 20 Mar 2024 Added conditional compile OUTPUTMEDIATORTODEBUG to enable outputting of message to the VS dubug window
// 20 Mar 2024 Change MainWindow to Window
// 09 Feb 2024 If DiagnosticInformation is on report listeners being registered and unregistered
// 22 Feb 2025 Removed the use of WeakReference



using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml;
using Surveyor.User_Controls;
#if DEBUG && OUTPUTMEDIATORTODEBUG
using static Surveyor.User_Controls.MediaControlEventData;
using static Surveyor.User_Controls.MediaPlayerEventData;
#endif

namespace Surveyor
{

    public interface IMediator
    {
        void Register(object listener);
        void Unregister(object listener);
        void Send(TListener listenerFrom, object message);
        void SendTo<T>(TListener listenerFrom, object message) where T : TListener;
    }

    public class SurveyorMediator : IMediator
    {
        // Copy of the reporter
        private Reporter? report = null;

        // List of current mediator listeners
        //???WeakRef code private readonly List<WeakReference> _listeners = [];
        private readonly List<TListener> _listeners = [];

        // Debug mode
        private bool diagnosticInformation = false;
        public SurveyorMediator()
        {
            diagnosticInformation = SettingsManagerLocal.DiagnosticInformation;
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
        /// Register a new listener
        /// </summary>
        /// <param name="listener"></param>
        public void Register(object listener)
        {
            //???WeakRef code _listeners.Add(new WeakReference(listener));
            _listeners.Add((TListener)listener);

            if (diagnosticInformation)
                report?.Info("", $"+Mediator listeners registered {listener.GetType()}, total={_listeners.Count}");

            Debug.WriteLine($"+Mediator listeners registered {listener.GetType()}, total={_listeners.Count}");
        }


        /// <summary>
        /// Remove an existing listener
        /// </summary>
        /// <param name="listener"></param>
        public void Unregister(object listener)
        {
            //???WeakRef code _listeners.RemoveAll(wr => wr.Target == null || !wr.Target.Equals(listener));
            _listeners.RemoveAll(sr => sr.Equals(listener));

            if (diagnosticInformation)
                report?.Info("", $"-Mediator listeners unregistered {listener.GetType()}, total={_listeners.Count}");

            Debug.WriteLine($"-Mediator listeners unregistered {listener.GetType()}, total={_listeners.Count}");
        }


        /// <summary>
        /// Send a message the current registered listeners
        /// </summary>
        /// <param name="listenerFrom"></param>
        /// <param name="message"></param>
        public void Send(TListener listenerFrom, object message)
        {

#if DEBUG && OUTPUTMEDIATORTODEBUG
            DebugOutputMessage(listenerFrom, message);
#endif
            //???WeakRef code _listeners.Select(wr => wr.Target)
            //???          .OfType<TListener>()
            //???          .Where(listener => listener != null && listener != listenerFrom)
            //???          .ToList()
            //???          .ForEach(listener => listener.Receive(listenerFrom, message));
            _listeners
                .Where(listener => listener != null && listener != listenerFrom) // Filter out null and the sender
                .ToList() // Convert to a list to avoid modifying the collection while iterating
                .ForEach(listener => listener.Receive(listenerFrom, message)); // Send the message to each listener
        }

        public void SendTo<T>(TListener listenerFrom, object message) where T : TListener
        {
            //this._listeners.OfType<T>().ToList().ForEach(m => m.Receive(listenerFrom, message));

            // Not sure if this is correct
            //???WeakRef code _listeners.Where(wr => wr.IsAlive && wr.Target is T && (wr.Target as TListener) != listenerFrom)
            //???          .Select(wr => wr.Target as T)
            //???          .ToList()
            //???          .ForEach(listener => listener?.Receive(listenerFrom, message));
            _listeners
                .OfType<T>() // Filter listeners of type T
                .Where(listener => listener != listenerFrom) // Exclude the sender
                .ToList()
                .ForEach(listener => listener.Receive(listenerFrom, message));
        }

#if DEBUG && OUTPUTMEDIATORTODEBUG
        private void DebugOutputMessage(TListener listenerFrom, object message)
        {
            // MediaControls
            if (message is MediaControlHandlerData mediaControlHandlerData)
            {
                Debug.WriteLine($"*--------->>>MediaControlHandlerData: {mediaControlHandlerData.controlType}, action:{mediaControlHandlerData.mediaControlAction}, [frame={mediaControlHandlerData.frameIndex}, duration={mediaControlHandlerData.duration}, position:{mediaControlHandlerData.position}]");
            } 
            if (message is MediaControlEventData mediaControlEventData)
            {
                Debug.WriteLine($"*--------->>>MediaControlEventData: {mediaControlEventData.controlType}, event:{mediaControlEventData.mediaControlEvent}, [play={mediaControlEventData.playOrPause}, frame ={mediaControlEventData.positionJump:hh\\:mm\\:ss\\.ff}, speed={mediaControlEventData.speed}, mute={mediaControlEventData.mute}]"); 
            }
            // MediaControls
            //if (message is MediaPlayerHandlerData mediaPlayerHandlerData)
            //{

            //}
            if (message is MediaPlayerEventData mediaPlayerEventData)
            {
                if (mediaPlayerEventData.mediaPlayerEvent != eMediaPlayerEvent.Position)   // Too much info
                {
                    Debug.WriteLine($"*--------->>>MediaPlayerEventData: {mediaPlayerEventData.cameraSide}, event:{mediaPlayerEventData.mediaPlayerEvent}, mode:{mediaPlayerEventData.mode}, [media={mediaPlayerEventData.mediaFileSpec}, duration ={mediaPlayerEventData.duration}, position={mediaPlayerEventData.position}, frameIndex={mediaPlayerEventData.frameIndex}]");
                    // Missing: percentage;
                }
            }
            if (message is MediaPlayerInfoData mediaPlayerInfoData)
            {
                Debug.WriteLine($"*--------->>>MediaPlayerInfoData: {mediaPlayerInfoData.cameraSide}, [media={mediaPlayerInfoData.mediaFileSpec}, duration ={mediaPlayerInfoData.duration}]");
                // Missing: empty
                // Missing: mediaSize
                // Missing: frameRate
                // Missing: totalFrames
                // Missing: frameWidth
                // Missing: frameHeight
                // Missing: errorMessage = "";
            }
            //if (message is MediaStereoControllerHandlerData mediaStereoControllerHandlerData)
            //{

            //}
            if (message is MediaStereoControllerEventData mediaStereoControllerEventData)
            {
                Debug.WriteLine($"*--------->>>MediaStereoControllerEventData: event:{mediaStereoControllerEventData.mediaStereoControllerEvent} [positionOffset={mediaStereoControllerEventData.positionOffset}]");
            }
        }
#endif
    }

    public abstract class TListener
    {
        private readonly IMediator _mediator;
        private readonly Window _mainWindow;

        public TListener(IMediator mediator, Window mainWindow)
        {
            _mediator = mediator;
            _mainWindow = mainWindow;
            _mediator.Register(this);
        }

        public abstract void Receive(TListener listenerFrom, object message);

        public void Send(object message)
        {
            this._mediator.Send(this, message);
        }

        public void SendTo<T>(object message) where T : TListener
        {
            this._mediator.SendTo<T>(this, message);
        }

        // Inside your TListener class or wherever the SafeUICall method is defined
        public void SafeUICall(Action action)
        {
            var dispatcher =  _mainWindow.DispatcherQueue;
            if (dispatcher.HasThreadAccess)
            {
                // We are on the UI thread, execute the action directly
                action();
            }
            else
            {
                // We are not on the UI thread, use TryEnqueue
                dispatcher.TryEnqueue(() =>
                {
                    action();
                });
            }
        }

        // Make sure to unregister when the ViewModel is no longer needed
        public void Cleanup()
        {
            _mediator.Unregister(this);
        }

    }
}
