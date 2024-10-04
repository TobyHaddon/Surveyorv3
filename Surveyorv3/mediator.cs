// Version: 1.5
// 19 Feb 2024 Change namespace to Surveyor
// 19 Feb 2024 Changed to work for WinUI (was WPF)
// 09 Mar 2024 Added MainWindow parameter to TListener constructor to enable SafeUICall() to work
// 20 Mar 2024 Added conditional compile OUTPUTMEDIATORTODEBUG to enable outputting of message to the VS dubug window
// 20 Mar 2024 Change MainWindow to Window



using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml;
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
        private readonly List<WeakReference> _listeners = new List<WeakReference>();

        public void Register(object listener)
        {
            _listeners.Add(new WeakReference(listener));
        }

        public void Unregister(object listener)
        {
            _listeners.RemoveAll(wr => wr.Target == null || !wr.Target.Equals(listener));
        }

        public void Send(TListener listenerFrom, object message)
        {
            //foreach (var weakReference in _listeners)
            //{
            //    if (weakReference.Target is TListener listener && weakReference.IsAlive)
            //    {
            //        listener.Receive(listenerFrom, message);
            //    }
            //}
#if DEBUG && OUTPUTMEDIATORTODEBUG
            DebugOutputMessage(listenerFrom, message);
#endif
            _listeners.Select(wr => wr.Target)
                      .OfType<TListener>()
                      .Where(listener => listener != null && listener != listenerFrom)
                      .ToList()
                      .ForEach(listener => listener.Receive(listenerFrom, message));
        }

        public void SendTo<T>(TListener listenerFrom, object message) where T : TListener
        {
            //this._listeners.OfType<T>().ToList().ForEach(m => m.Receive(listenerFrom, message));

            // Not sure if this is correct
            _listeners.Where(wr => wr.IsAlive && wr.Target is T && (wr.Target as TListener) != listenerFrom)
                      .Select(wr => wr.Target as T)
                      .ToList()
                      .ForEach(listener => listener?.Receive(listenerFrom, message));
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
