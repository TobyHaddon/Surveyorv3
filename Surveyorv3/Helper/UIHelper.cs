using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Microsoft.UI.Input;

namespace Surveyor.Helper
{
    public static class UIHelper
    {
        static UIHelper()
        {
            //??? need helper/WinowsHelper.cs for this  ScreenshotStorageFolder = WindowHelper.GetAppLocalFolder();
        }

        public static bool IsScreenshotMode { get; set; }

        //??? public static StorageFolder ScreenshotStorageFolder { get; set; }
        public static IEnumerable<T> GetDescendantsOfType<T>(this DependencyObject start) where T : DependencyObject
        {
            return start.GetDescendants().OfType<T>();
        }

        public static IEnumerable<DependencyObject> GetDescendants(this DependencyObject start)
        {
            var queue = new Queue<DependencyObject>();
            var count1 = VisualTreeHelper.GetChildrenCount(start);

            for (int i = 0; i < count1; i++)
            {
                var child = VisualTreeHelper.GetChild(start, i);
                yield return child;
                queue.Enqueue(child);
            }

            while (queue.Count > 0)
            {
                var parent = queue.Dequeue();
                var count2 = VisualTreeHelper.GetChildrenCount(parent);

                for (int i = 0; i < count2; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    yield return child;
                    queue.Enqueue(child);
                }
            }
        }

        static public UIElement? FindElementByName(UIElement element, string name)
        {
            if (element is not null && element.XamlRoot is not null && element.XamlRoot.Content is FrameworkElement frameworkElement)
            {
                var ele = frameworkElement.FindName(name);
                if (ele != null)
                {
                    return ele as UIElement;
                }
            }
            return null;
        }

        // Confirmation of Action
        static public void AnnounceActionForAccessibility(UIElement ue, string annoucement, string activityID)
        {
            var peer = FrameworkElementAutomationPeer.FromElement(ue);
            if (peer is not null)
                peer.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted,
                                            AutomationNotificationProcessing.ImportantMostRecent, annoucement, activityID);
        }



        static public void SafeUICall(MainWindow mainWindow, Action action)
        {
            var dispatcher = mainWindow.DispatcherQueue;
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

    }
}
