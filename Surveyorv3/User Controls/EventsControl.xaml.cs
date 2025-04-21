using MathNet.Numerics.LinearAlgebra.Factorization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Surveyor.Events;
using Surveyor.Helper;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;


namespace Surveyor.User_Controls
{
    public sealed partial class EventsControl : UserControl
    {

        private DispatcherQueue? dispatcherQueue;
        private ObservableCollection<Event> events = [];

        // Copy of left mediaplayer
        MediaStereoController? mediaStereoController = null;

        private readonly int displayToDecimalPlaces = 2;     // If we start using frame rate of 120fps then we will need to increase this to 3dp

        public EventsControl()
        {
            InitializeComponent();

            // Add listener for theme changes
            var rootElement = (FrameworkElement)Content;
            rootElement.ActualThemeChanged += OnActualThemeChanged;
        }

        internal void SetDispatcherQueue(DispatcherQueue dispatcherQueue)
        {
            this.dispatcherQueue = dispatcherQueue;
        }

        internal void SetEvents(ObservableCollection<Event> eventItems)
        {
            events = eventItems;
            ListViewEvent.ItemsSource = events;
        }

        internal void SetMediaStereoController(MediaStereoController _mediaStereoController)
        {
            mediaStereoController = _mediaStereoController;
        }


        internal ListView GetListView() => ListViewEvent;

        internal ObservableCollection<Event> GetEvents() => events;

        /// <summary>
        /// Add an event to the list
        /// </summary>
        /// <param name="evt"></param>
        public async Task AddEvent(Event evt)
        {
#pragma warning disable CA1868
            if (events.Contains(evt))
#pragma warning restore CA1868
            {
                events.Remove(evt);
                await Task.Delay(100);
            }
            events.Add(evt);

            // Assuming your ListView is named "myListView"
            // Set the SelectedItem property to the newly added event
            ListViewEvent.SelectedItem = evt;
        }


        /// <summary>
        /// Delete an event from the list
        /// </summary>
        /// <param name="evt"></param>
        public void DeleteEvent(Event evt)
        {
            events.Remove(evt);
        }


        /// <summary>
        /// Delete all events of a specific type
        /// </summary>
        /// <param name="dataType"></param>
        /// <returns></returns>
        public int DeleteEventOfType(SurveyDataType dataType)
        {
            int count = 0;
            // Remove any existing StereoSyncPoint events
            // Use a reverse loop to avoid issues with changing collection indices when removing items
            for (int i = events.Count - 1; i >= 0; i--)
            {
                if (events[i].EventDataType == dataType)
                {
                    events.RemoveAt(i);
                    count++;
                }
            }

            return count;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="guid"></param>
        /// <returns></returns>
        public Event? FindEvent(Guid guid)
        {
            return events.FirstOrDefault(e => e.Guid == guid);
        }


        /// <summary>
        /// Display Event in a ContentDialog
        /// </summary>
        /// <param name="evt"></param>
        public async void Display(Event evt)
        {
            // Set the content dialog title
            switch (evt.EventDataType)
            {
                case SurveyDataType.SurveyPoint:
                    EventDialog.Title = $"Survey Point";
                    break;
                case SurveyDataType.SurveyStereoPoint:
                    EventDialog.Title = $"Survey Stereo Point";
                    break;
                case SurveyDataType.SurveyMeasurementPoints:
                    EventDialog.Title = $"Survey Measurement Points";
                    break;
                case SurveyDataType.StereoCalibrationPoints:
                    EventDialog.Title = $"Stereo Calibration Points";
                    break;
                case SurveyDataType.StereoSyncPoint:
                    EventDialog.Title = $"Stereo Sync Point";
                    break;
                case SurveyDataType.SurveyStart:
                    EventDialog.Title = $"Survey Start";
                    break;
                case SurveyDataType.SurveyEnd:
                    EventDialog.Title = $"Survey End";
                    break;
                default:
                    EventDialog.Title = $"{evt.EventDataType}";
                    break;
            }
            

            // Create a string to display the event data
            StringBuilder sb = new();

            if (evt.EventData is not null)
            {
                // Get the survey transect marker name for this event (if any)
                string? surveyTransectName = GetTransectMarkerNameForEvent(evt);

                switch (evt.EventDataType)
                {
                    case SurveyDataType.StereoSyncPoint:  // No EventData for this type 

                        sb.AppendLine($"Sync media position: {TimePositionHelper.Format(evt.TimeSpanTimelineController, displayToDecimalPlaces)}");
                        sb.AppendLine($"Left media position: {TimePositionHelper.Format(evt.TimeSpanLeftFrame, displayToDecimalPlaces)}");
                        sb.AppendLine($"Right media position: {TimePositionHelper.Format(evt.TimeSpanRightFrame, displayToDecimalPlaces)}");
                        break;

                    case SurveyDataType.SurveyMeasurementPoints:
                    case SurveyDataType.SurveyStereoPoint:
                        SurveyRulesCalc? surveyRulesCalc = null;
                        if (evt.EventData is SurveyMeasurement surveyMeasurement)
                        {
                            if (surveyTransectName is not null)
                                sb.AppendLine($"Media position: {TimePositionHelper.Format(evt.TimeSpanTimelineController, displayToDecimalPlaces)}  Transect #{surveyTransectName}\r\n");
                            else
                                sb.AppendLine($"Media position: {TimePositionHelper.Format(evt.TimeSpanTimelineController, displayToDecimalPlaces)}\r\n");

                            // Measurement
                            sb.AppendLine($"Species: {surveyMeasurement.SpeciesInfo.Species}\r\n");
                            if (surveyMeasurement.Measurment is not null)
                                sb.AppendLine($"Measurement: {Math.Round((double)surveyMeasurement.Measurment * 1000, 0)}mm");
                            else
                                sb.AppendLine($"Measurement: missing");

                            // Survey Rules
                            string surveyRulesText = !string.IsNullOrWhiteSpace(surveyMeasurement.SurveyRulesCalc.SurveyRulesText) ? surveyMeasurement.SurveyRulesCalc.SurveyRulesText : "No Survey Rules";
                            sb.AppendLine($"Survey Rules: {surveyRulesText}\r\n");

                            // 2D Points
                            sb.AppendLine($"2D Points:");
                            sb.AppendLine($"Set A");
                            sb.AppendLine($"    Left Camera: ({Math.Round(surveyMeasurement.LeftXA,1)}, {Math.Round(surveyMeasurement.LeftYA,1)})");
                            sb.AppendLine($"    Right Camera: ({Math.Round(surveyMeasurement.RightXA, 1)}, {Math.Round(surveyMeasurement.RightYA, 1)})");
                            sb.AppendLine($"Set B");
                            sb.AppendLine($"    Left Camera: ({Math.Round(surveyMeasurement.LeftXB, 1)}, {Math.Round(surveyMeasurement.LeftYB, 1)})");
                            sb.AppendLine($"    Right Camera: ({Math.Round(surveyMeasurement.RightXB, 1)}, {Math.Round(surveyMeasurement.RightYB, 1)})");
                            sb.AppendLine($"");
                            surveyRulesCalc = surveyMeasurement.SurveyRulesCalc;
                        }
                        else if (evt.EventData is SurveyStereoPoint surveyStereoPoint)
                        {
                            sb.AppendLine($"Species: {surveyStereoPoint.SpeciesInfo.Species}\r\n");
                            sb.AppendLine($"Survey Rules: {surveyStereoPoint.SurveyRulesCalc.SurveyRulesText}");
                            sb.AppendLine($"");
                            sb.AppendLine($"2D Points:");
                            sb.AppendLine($"    Left Camera: ({Math.Round(surveyStereoPoint.LeftX, 1)}, {Math.Round(surveyStereoPoint.LeftY, 1)})");
                            sb.AppendLine($"    Right Camera: ({Math.Round(surveyStereoPoint.RightX, 1)}, {Math.Round(surveyStereoPoint.RightY, 1)})");
                            sb.AppendLine($"");
                            surveyRulesCalc = surveyStereoPoint.SurveyRulesCalc;
                        }
                        
                        if (surveyRulesCalc is not null)
                        {
                            if (surveyRulesCalc.RMS is not null)
                            {
                                sb.AppendLine($"RMS Distance Error: {Math.Round((double)surveyRulesCalc.RMS * 1000, 0)}mm");
                                sb.AppendLine($"When a point is selected in the left camera and the corresponding point selected in the right camaera, the 3D point is computed by intersecting the resulting rays in 3D space.");
                                sb.AppendLine($"RMS Distance is the distance of the shortest line, between the intersecting rays. In practice, the intersection is unlikely to ever be perfect (RMS = 0mm).");
                                sb.AppendLine($"");
                            }
                            if (surveyRulesCalc.Range is not null)
                            {
                                sb.AppendLine($"Range: {Math.Round((double)surveyRulesCalc.Range, 2)}m");
                                if (evt.EventData is SurveyMeasurement)
                                    sb.AppendLine($"This is the calculated distance from centre of the camera system to the centre of the measurement points.");
                                else
                                    sb.AppendLine($"This is the calculated distance from centre of the camera system to the 3D point.");
                                sb.AppendLine($"");
                            }
                            if (surveyRulesCalc.XOffset is not null)
                            {
                                sb.Append($"X Offset: {Math.Round((double)surveyRulesCalc.XOffset, 2)}m");
                                if (surveyRulesCalc.XOffset < 0)
                                    sb.AppendLine($" (to the left of the camera system)");
                                if (surveyRulesCalc.XOffset > 0)
                                    sb.AppendLine($" (to the right of the camera system)");
                                else
                                    sb.AppendLine($"");
                            }
                            if (surveyRulesCalc.YOffset is not null)
                            {
                                sb.Append($"Y Offset: {Math.Round((double)surveyRulesCalc.YOffset, 2)}m");
                                if (surveyRulesCalc.YOffset < 0)
                                    sb.AppendLine($" (below the camera system)");
                                if (surveyRulesCalc.YOffset > 0)
                                    sb.AppendLine($" (above the camera system)");
                                else
                                    sb.AppendLine($"");
                            }
                        }
                        break;
                    
                    case SurveyDataType.SurveyPoint:
                        if (evt.EventData is SurveyPoint surveyPoint)
                        {
                            if (surveyTransectName is not null)
                                sb.AppendLine($"Media position: {TimePositionHelper.Format(evt.TimeSpanTimelineController, displayToDecimalPlaces)}  Transect #{surveyTransectName}\r\n");
                            else
                                sb.AppendLine($"Media position: {TimePositionHelper.Format(evt.TimeSpanTimelineController, displayToDecimalPlaces)}\r\n");

                            sb.Append($"Species: {surveyPoint.SpeciesInfo.Species}\r\n");
                            sb.Append($"\r\n");
                            sb.Append($"2D Point:\r\n");
                            string camera = surveyPoint.TrueLeftfalseRight ? "Left" : "Right";
                            sb.Append($"{camera} Camera: ({Math.Round(surveyPoint.X, 1)}, {Math.Round(surveyPoint.Y, 1)})\r\n");
                            sb.Append($"\r\n");
                        }
                        break;

                    case SurveyDataType.SurveyStart:
                    case SurveyDataType.SurveyEnd:
                        if (evt.EventData is TransectMarker transectMarker)
                        {
                            if (transectMarker.MarkerName is not null)
                                sb.AppendLine($"Media position: {TimePositionHelper.Format(evt.TimeSpanTimelineController, displayToDecimalPlaces)}  Transect #{transectMarker.MarkerName}\r\n");
                            else
                                sb.AppendLine($"Media position: {TimePositionHelper.Format(evt.TimeSpanTimelineController, displayToDecimalPlaces)}\r\n");
                        }
                        break;
                }

                sb.Append($"\r\nEvent Created: {evt.DateTimeCreate:dd MMM yyyy HH:mm:ss}");
            }

            // Set the content of the TextBlock inside the dialog
            EventDialogContent.Text = sb.ToString();

            // Show the dialog
            var result = await EventDialog.ShowAsync();


            if (result == ContentDialogResult.Primary)
            {
                // Copy the event data to the clipboard
                var dataPackage = new DataPackage();
                dataPackage.SetText(sb.ToString());
                Clipboard.SetContent(dataPackage);
            }
        }


        /// <summary>
        /// Display status of the SurveyStart/SurveyEnd markers in a ContentDialog
        /// </summary>

        public async void DisplaySurveyStartEndMarkers(Event? newestEventEndMarker)
        {
            // Set the content dialog title
            EventDialog.Title = "Survey Start & End Segment";
            EventDialog.PrimaryButtonText = "Ok";

            Event? newestEventStartMarker = null;
            TransectMarker? newestStartTransectMarker = null;
            TransectMarker? newestEndTransectMarker = null;
                        

            List<Event> startEndEvents = [.. GetEvents().Where(e => e.EventDataType == SurveyDataType.SurveyStart || e.EventDataType == SurveyDataType.SurveyEnd)
                                                        .OrderBy(e => e.TimeSpanTimelineController)];

            // Find the matching newest start marker
            for (int i = startEndEvents.Count - 1; i >= 0; i--)
            {
                // Find where the newest end marker is in the list
                if (startEndEvents[i] == newestEventEndMarker)
                {
                    // Find the matching start marker
                    if (i > 0)
                    {
                        newestEventStartMarker = startEndEvents[i - 1];
                        break;
                    }
                }
            }


            if (newestEventStartMarker is not null)
                newestStartTransectMarker = (TransectMarker)newestEventStartMarker.EventData!;

            if (newestEventEndMarker is not null)
                newestEndTransectMarker = (TransectMarker)newestEventEndMarker.EventData!;
            

            //??? Support full display of all values in all event types
            //??? Add a 'Copy' button using the standard copy gypth and format as tab delimited
            StringBuilder sb = new();
            if (newestEndTransectMarker is not null)
                sb.Append($"A new survey transect start/end: Transect #{newestEndTransectMarker.MarkerName} has been defined\r\n");
            else
                sb.Append($"A new survey transect start/end segment (Not named) has been defined:\r\n");

            if (newestEventStartMarker is not null)
                sb.Append($"Start: {TimePositionHelper.Format(newestEventStartMarker.TimeSpanTimelineController, displayToDecimalPlaces)}\r\n");
            else
                sb.Append($"Start: Missing\r\n");

            if (newestEventEndMarker is not null)
                sb.Append($"End: {TimePositionHelper.Format(newestEventEndMarker.TimeSpanTimelineController, displayToDecimalPlaces)}\r\n");
            else
                sb.Append($"End: Missing\r\n");


            // If there are other survey start/end markers, then display them
            if (startEndEvents.Count > 2)
            {
                sb.Append($"\r\n\r\n");
                sb.Append($"All survey start/end segments:\r\n");

                Event? eventStartMarker = null;
                Event? eventEndMarker = null;
                TransectMarker? startTransectMarker = null;
                TransectMarker? endTransectMarker = null;

                for (int i = 0; i < startEndEvents.Count; i += 2)
                {
                    eventStartMarker = startEndEvents[i];
                    if (i + 1 < startEndEvents.Count)
                    {
                        eventEndMarker = startEndEvents[i + 1];
                    }
                    else
                    {
                        // Odd number of start/end survey transect markers events, so no end marker
                        eventEndMarker = null;
                    }

                    if (eventStartMarker is not null)
                    {
                        startTransectMarker = (TransectMarker)eventStartMarker.EventData!;
                        sb.Append($"{TimePositionHelper.Format(eventStartMarker.TimeSpanTimelineController, displayToDecimalPlaces)} - ");
                    }
                    else
                        sb.Append($"Start missing - ");

                    if (eventEndMarker is not null)
                    {
                        endTransectMarker = (TransectMarker)eventEndMarker.EventData!;
                        sb.Append($"{TimePositionHelper.Format(eventEndMarker.TimeSpanTimelineController, displayToDecimalPlaces)} Transect #{endTransectMarker.MarkerName} ");
                    }
                    else
                        sb.Append($"End missing");

                    sb.Append($"\r\n");
                }
            }


            // Set the content of the TextBlock inside the dialog
            EventDialogContent.Text = sb.ToString();

            // Show the dialog
            var result = await EventDialog.ShowAsync();


            if (result == ContentDialogResult.Primary)
            {
                // Copy the event data to the clipboard
                var dataPackage = new DataPackage();
                dataPackage.SetText(sb.ToString());
                Clipboard.SetContent(dataPackage);
            }
        }



        ///
        /// EVENTS
        ///


        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ViewMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ListViewEvent.SelectedItem is Event selectedItem)
            {
                Display(selectedItem);
            }
        }


        /// <summary>
        /// Go to Frame selected via item selection inthe list view 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GoToFrameMenuItem_Click(object? sender, ItemClickEventArgs? e)
        {
            if (ListViewEvent.SelectedItem is Event selectedItem)
            {
                // Attempt to go to the left frame
                if (mediaStereoController is not null)
                {
                    // Jump to frame of this event
                    mediaStereoController.UserReqFrameJump(SurveyorMediaControl.eControlType.Primary, selectedItem.TimeSpanTimelineController);
                }                
            }
        }


        /// <summary>
        /// Delete the selected event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void ListViewEvent_Delete(object? sender, RoutedEventArgs? e)
        {
            if (ListViewEvent.SelectedItem is Event selectedItem)
            {
                if (ListViewEvent.ItemsSource is ObservableCollection<Event> eventItems)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Delete Event",
                        Content = "Are you sure you want to delete the selected event?",
                        PrimaryButtonText = "OK",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = this.Content.XamlRoot
                    };

                    var result = await dialog.ShowAsync();
                    if (result == ContentDialogResult.Primary)
                    {
                        eventItems.Remove(selectedItem);
                    }
                }
            }
        }


        /// <summary>
        /// Right click on the list view to select the item. Select the item and a Context will be displayed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ListViewEvent_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var listView = sender as ListView;
            if (listView != null)
            {
                var element = e.OriginalSource as FrameworkElement;
                if (element?.DataContext is Event selectedItem)
                {
                    ListViewEvent.SelectedItem = selectedItem;
                }
            }
        }


        /// <summary>
        /// Check for user requests via the keyboard in the event list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ListViewEvent_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Delete)
            {
                ListViewEvent_Delete(null, null);
            }
        }


        /// <summary>
        /// Go to Frame selected in the context menu
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GoToFrameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            GoToFrameMenuItem_Click(null, (ItemClickEventArgs?)null);
        }


        /// <summary>
        /// Event raised when the theme is changed in Windows
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void OnActualThemeChanged(FrameworkElement sender, object args)
        {
            // Assuming your ItemsSource is bound to an ObservableCollection
            // This forces the list to "repaint" itself
            var existing = ListViewEvent.ItemsSource;
            ListViewEvent.ItemsSource = null;
            ListViewEvent.ItemsSource = existing;
        }


        /// <summary>
        /// For the passed targetEvent find the closest SurveyStart and SurveyEnd events then therefore return the TransectMarker name
        /// </summary>
        /// <param name="targetEvent"></param>
        /// <returns></returns>

        private string? GetTransectMarkerNameForEvent(Event targetEvent)
        {
            TransectMarker? startMarker = null;
            TransectMarker? endMarker = null;

            if (targetEvent == null || events == null || events.Count == 0)
                return null;

            // Find the index of the target event
            int index = events.IndexOf(targetEvent);
            if (index == -1)
                return null;

            // Search backwards for the closest SurveyStart            
            for (int i = index - 1; i >= 0; i--)
            {
                // If the first marker we see going backwards is an end marker then we are not within a survey
                if (events[i].EventDataType == SurveyDataType.SurveyEnd)
                    break;

                if (events[i].EventDataType == SurveyDataType.SurveyStart &&
                    events[i].EventData is TransectMarker marker)
                {
                    startMarker = marker;
                    break;
                }
            }

            // Search forwards for the closest SurveyEnd            
            for (int i = index + 1; i < events.Count; i++)
            {
                // If the first marker we see going forwards is a start marker then we are not within a survey
                if (events[i].EventDataType == SurveyDataType.SurveyStart)
                    break;

                if (events[i].EventDataType == SurveyDataType.SurveyEnd &&
                    events[i].EventData is TransectMarker marker)
                {
                    endMarker = marker;
                    break;
                }
            }

            // If both exist and match, return the marker name
            if (startMarker != null && endMarker != null &&
                startMarker.MarkerName == endMarker.MarkerName)
            {
                return startMarker.MarkerName;
            }

            // Not within a survey
            return null;
        }
    }


    /// <summary>
    /// Convert TimeSpanToStringConverter to 2DP
    /// </summary>
    public partial class EventTimeSpanToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is TimeSpan timeSpan)
            {
                return TimePositionHelper.Format(timeSpan, 2);
            }
            return "0.00 secs";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }


    /// <summary>
    /// Convert the EventDataType to a string for display
    /// </summary>
    public partial class EventDataTypeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is Surveyor.Events.SurveyDataType eventType)
            {
                switch (eventType)
                {
                    case Surveyor.Events.SurveyDataType.SurveyPoint:
                        return "Survey Point";
                    case Surveyor.Events.SurveyDataType.SurveyStereoPoint:
                        return "Survey 3D Point";
                    case Surveyor.Events.SurveyDataType.SurveyMeasurementPoints:
                        return "Survey Measurement";
                    case Surveyor.Events.SurveyDataType.StereoCalibrationPoints:
                        return "Stereo Calibration Points";
                    case Surveyor.Events.SurveyDataType.StereoSyncPoint:
                        return "Stereo Sync Point";
                    case SurveyDataType.SurveyStart:
                        return "Survey Start";
                    case SurveyDataType.SurveyEnd:
                        return "Survey End";
                    default:
                        return "Unknown";
                }
            }
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }


    /// <summary>
    /// Suitable 
    /// </summary>
    public partial class EventTypeToGlyphConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is Surveyor.Events.SurveyDataType eventType)
            {
                switch (eventType)
                {
                    case Surveyor.Events.SurveyDataType.SurveyPoint:
                        return "\uE139";
                    case Surveyor.Events.SurveyDataType.SurveyStereoPoint:
                        return "\uECAF";
                    case Surveyor.Events.SurveyDataType.SurveyMeasurementPoints:
                        return "\uE1D9";
                    case Surveyor.Events.SurveyDataType.StereoCalibrationPoints:
                        return "\uEB3C";
                    case Surveyor.Events.SurveyDataType.StereoSyncPoint:
                        return "\uE754";
                    case Surveyor.Events.SurveyDataType.SurveyStart:
                        return "\uEA52";
                    case Surveyor.Events.SurveyDataType.SurveyEnd:
                        return "\uE7FD";
                    default:
                        return "Unknown";
                }
            }
            return "Unknown";

        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }


    /// <summary>
    /// Convert the EventDataType to a brush for display
    /// </summary>
    public class EventTypeToBrushConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is Surveyor.Events.SurveyDataType eventType)
            {

                switch (eventType)
                {
                    case Surveyor.Events.SurveyDataType.SurveyPoint:
                    case Surveyor.Events.SurveyDataType.SurveyStereoPoint:
                    case Surveyor.Events.SurveyDataType.SurveyMeasurementPoints:
                    case Surveyor.Events.SurveyDataType.StereoCalibrationPoints:
                    default:
                        // Return system default foreground brush
                        //???return Application.Current.Resources["TextControlForeground"] as Brush;
                        return Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush;

                    case Surveyor.Events.SurveyDataType.StereoSyncPoint:
                    case Surveyor.Events.SurveyDataType.SurveyStart:
                    case Surveyor.Events.SurveyDataType.SurveyEnd:
                        return new SolidColorBrush(Microsoft.UI.Colors.LimeGreen); // Bright green
                }
            }

            return new SolidColorBrush(Microsoft.UI.Colors.Gray); // Default color
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }


    /// <summary>
    /// Make the Event description string
    /// </summary>
    public partial class EventDataToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is Surveyor.Events.Event eventItem)
            {
                StringBuilder sb = new();

                // Combine various properties into a readable string
                if (eventItem.EventData is Surveyor.Events.SurveyMeasurement ||
                    eventItem.EventData is Surveyor.Events.SurveyStereoPoint)
                {
                    // Get the species info/Measurement/Survey rules calcs
                    SpeciesInfo? speciesInfo = null;
                    double? measurment = null;
                    SurveyRulesCalc? surveyRulesCalc = null;

                    if (eventItem.EventData is Surveyor.Events.SurveyMeasurement surveyMeasurement)
                    {
                        speciesInfo = surveyMeasurement.SpeciesInfo;
                        measurment = surveyMeasurement.Measurment;
                        surveyRulesCalc = surveyMeasurement.SurveyRulesCalc;
                    }
                    else if (eventItem.EventData is Surveyor.Events.SurveyStereoPoint surveyStereoPoint)
                    {
                        speciesInfo = surveyStereoPoint.SpeciesInfo;
                        surveyRulesCalc = surveyStereoPoint.SurveyRulesCalc;
                    }


                    // Length of the fish
                    if (measurment is not null && measurment != 0)
                    {
                        if (sb.Length > 0)
                            sb.Append(", ");
                        sb.Append($"{Math.Round((double)measurment * 1000, 0)}mm "); // Round up to the nearest whole number
                    }

                    // Species/Genus/Family
                    if (speciesInfo is not null)
                    {
                        if (!string.IsNullOrEmpty(speciesInfo.Species))
                            sb.Append($"{speciesInfo.Species}");
                        else if (!string.IsNullOrEmpty(speciesInfo.Genus))
                            sb.Append($"{speciesInfo.Genus}");
                        else if (!string.IsNullOrEmpty(speciesInfo.Family))
                            sb.Append($"{speciesInfo.Family}");
                    }

                    // Survey rules passed or failed
                    if (surveyRulesCalc is not null && !string.IsNullOrEmpty(surveyRulesCalc.SurveyRulesText))
                    {
                        sb.Append(", ");
                        sb.Append(surveyRulesCalc.SurveyRulesText);
                    }
                }
                else if (eventItem.EventData is Surveyor.Events.SurveyPoint surveyPoint)
                {
                    sb.Append($"Species: {surveyPoint.SpeciesInfo.Species}");
                }
                else if (eventItem.EventData is Surveyor.Events.TransectMarker transectMarker)
                {
                    sb.Append($"Transect #{transectMarker.MarkerName}");
                }

                // Add other conditions as necessary

                return sb.ToString();
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}

