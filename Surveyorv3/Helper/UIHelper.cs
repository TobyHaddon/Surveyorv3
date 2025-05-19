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

        }

        public static bool IsScreenshotMode { get; set; }

        public static IEnumerable<T> GetDescendantsOfType<T>(this DependencyObject start) where T : DependencyObject
        {
            return start.GetDescendants().OfType<T>();
        }


        /// <summary>
        /// Returns a list of visual tree dependants
        /// </summary>
        /// <param name="start"></param>
        /// <returns></returns>
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


        /// <summary>
        /// Find an UIElement in the visual tree:
        /// Example:
        ///     var textBox = FindDescendant<TextBox>(comboBox);
        ///     Finds the child TextBox of a ComboBox
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="d"></param>
        /// <returns></returns>
        public static T? FindDescendant<T>(DependencyObject d) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                if (child is T t)
                    return t;

                var result = FindDescendant<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }


        /// <summary>
        /// Find the UIElement in the visual tree by x:Name
        /// </summary>
        /// <param name="element"></param>
        /// <param name="name"></param>
        /// <returns></returns>
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

            peer?.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted,
                                            AutomationNotificationProcessing.ImportantMostRecent, annoucement, activityID);
        }


        /// <summary>
        /// Used to call the 'Action' in the UI thread if we are not already running from the UI thread
        /// </summary>
        /// <param name="mainWindow"></param>
        /// <param name="action"></param>
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
