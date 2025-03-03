using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Surveyor.Events;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        public EventsControl()
        {
            InitializeComponent();
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
            //???EventDialog.PrimaryButtonText = "Cancel";

            // Create a string to display the event data
            StringBuilder sb = new();
            sb.Append($"Created: {evt.DateTimeCreate:dd MMM yyyy hh:mm:ss}\r\n\r\n");

            if (evt.EventData is not null)
            {
                switch (evt.EventDataType)
                {
                    case SurveyDataType.StereoSyncPoint:  // No EventData for this type 

                        sb.Append($"Sync media position: {evt.TimeSpanTimelineController}\r\n");
                        sb.Append($"Left media position: {evt.TimeSpanLeftFrame}\r\n");
                        sb.Append($"Right media position: {evt.TimeSpanRightFrame}\r\n");
                        break;

                    case SurveyDataType.SurveyMeasurementPoints:
                    case SurveyDataType.SurveyStereoPoint:
                        SurveyRulesCalc? surveyRulesCalc = null;
                        if (evt.EventData is SurveyMeasurement surveyMeasurement)
                        {
                            sb.Append($"Species: {surveyMeasurement.SpeciesInfo.Species}\r\n");
                            if (surveyMeasurement.Measurment is not null)
                                sb.Append($"Measurement: {Math.Round((double)surveyMeasurement.Measurment * 1000, 0)}mm\r\n");
                            else
                                sb.Append($"Measurement: missing\r\n");
                            sb.Append($"Survey Rules: {surveyMeasurement.SurveyRulesCalc.SurveyRulesText}\r\n");
                            sb.Append($"\r\n");
                            sb.Append($"2D Points:\r\n");
                            sb.Append($"Set A\r\n");
                            sb.Append($"Left Camera: ({Math.Round(surveyMeasurement.LeftXA,1)}, {Math.Round(surveyMeasurement.LeftYA,1)})\r\n");
                            sb.Append($"Right Camera: ({Math.Round(surveyMeasurement.RightXA, 1)}, {Math.Round(surveyMeasurement.RightYA, 1)})\r\n");
                            sb.Append($"Set B\r\n");
                            sb.Append($"Left Camera: ({Math.Round(surveyMeasurement.LeftXB, 1)}, {Math.Round(surveyMeasurement.LeftYB, 1)})\r\n");
                            sb.Append($"Right Camera: ({Math.Round(surveyMeasurement.RightXB, 1)}, {Math.Round(surveyMeasurement.RightYB, 1)})\r\n");
                            sb.Append($"\r\n");
                            surveyRulesCalc = surveyMeasurement.SurveyRulesCalc;
                        }
                        else if (evt.EventData is SurveyStereoPoint surveyStereoPoint)
                        {
                            sb.Append($"Species: {surveyStereoPoint.SpeciesInfo.Species}\r\n");
                            sb.Append($"Survey Rules: {surveyStereoPoint.SurveyRulesCalc.SurveyRulesText}\r\n");
                            sb.Append($"\r\n");
                            sb.Append($"2D Points:\r\n");
                            sb.Append($"Left Camera: ({Math.Round(surveyStereoPoint.LeftX, 1)}, {Math.Round(surveyStereoPoint.LeftY, 1)})\r\n");
                            sb.Append($"Right Camera: ({Math.Round(surveyStereoPoint.RightX, 1)}, {Math.Round(surveyStereoPoint.RightY, 1)})\r\n");
                            sb.Append($"\r\n");
                            surveyRulesCalc = surveyStereoPoint.SurveyRulesCalc;
                        }

                        if (surveyRulesCalc is not null)
                        {
                            if (surveyRulesCalc.RMS is not null)
                            {
                                sb.Append($"RMS Distance Error: {Math.Round((double)surveyRulesCalc.RMS * 1000, 0)}mm\r\n");
                                sb.Append($"When a point is selected in the left camera and the corresponding point selected in the right camaera, the 3D point is computed by intersecting the resulting rays in 3D space.\r\n");
                                sb.Append($"RMS Distance is the distance of the shortest line, between the intersecting rays. In practice, the intersection is unlikely to ever be perfect (RMS = 0mm). \r\n");
                                sb.Append($"\r\n");
                            }
                            if (surveyRulesCalc.Range is not null)
                            {
                                sb.Append($"Range: {Math.Round((double)surveyRulesCalc.Range, 2)}m\r\n");
                                if (evt.EventData is SurveyMeasurement)
                                    sb.Append($"This is the calculated distance from centre of the camera system to the centre of the measurement points.\r\n");
                                else
                                    sb.Append($"This is the calculated distance from centre of the camera system to the 3D point.\r\n");
                                sb.Append($"\r\n");
                            }
                            if (surveyRulesCalc.XOffset is not null)
                            {
                                sb.Append($"X Offset: {Math.Round((double)surveyRulesCalc.XOffset, 2)}m");
                                if (surveyRulesCalc.XOffset < 0)
                                    sb.Append($" (to the left of the camera system)\r\n");
                                if (surveyRulesCalc.XOffset > 0)
                                    sb.Append($" (to the right of the camera system)\r\n");
                                else
                                    sb.Append($"\r\n");
                            }
                            if (surveyRulesCalc.YOffset is not null)
                            {
                                sb.Append($"Y Offset: {Math.Round((double)surveyRulesCalc.YOffset, 2)}m");
                                if (surveyRulesCalc.YOffset < 0)
                                    sb.Append($" (below the camera system)\r\n");
                                if (surveyRulesCalc.YOffset > 0)
                                    sb.Append($" (above the camera system)\r\n");
                                else
                                    sb.Append($"\r\n");
                            }
                        }
                        break;
                    
                    case SurveyDataType.SurveyPoint:
                        if (evt.EventData is SurveyPoint surveyPoint)
                        {
                            sb.Append($"Species: {surveyPoint.SpeciesInfo.Species}\r\n");
                            sb.Append($"\r\n");
                            sb.Append($"2D Point:\r\n");
                            string camera = surveyPoint.TrueLeftfalseRight ? "Left" : "Right";
                            sb.Append($"{camera} Camera: ({Math.Round(surveyPoint.X, 1)}, {Math.Round(surveyPoint.Y, 1)})\r\n");
                            sb.Append($"\r\n");
                        }
                        break;
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


        /// <summary>
        /// Display status of the SurveyStart/SurveyEnd markers in a ContentDialog
        /// </summary>

        public async void DisplaySurveyStartEndMarkers(Event? newestEventEndMarker)
        {
            // Set the content dialog title
            EventDialog.Title = "Survey Start & End Segment";
            EventDialog.PrimaryButtonText = "Ok";

            Event? newestEventStartMarker = null;
            SurveyMarker? newestStartSurveyMarker = null;
            SurveyMarker? newestEndSurveyMarker = null;
                        

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
                newestStartSurveyMarker = (SurveyMarker)newestEventStartMarker.EventData!;

            if (newestEventEndMarker is not null)
                newestEndSurveyMarker = (SurveyMarker)newestEventEndMarker.EventData!;
            

            //??? Support full display of all values in all event types
            //??? Display created date as dd MMM yyyy
            //??? Add a 'Copy' button using the standard copy gypth and format as tab delimited
            StringBuilder sb = new();
            if (newestEndSurveyMarker is not null)
                sb.Append($"A new survey start/end segment {newestEndSurveyMarker.MarkerName} has been defined\r\n");
            else
                sb.Append($"A new survey start/end segment (Not named) has been defined:\r\n");

            if (newestEventStartMarker is not null)
                sb.Append($"Start: {newestEventStartMarker.TimeSpanTimelineController.ToString(@"hh\:mm\:ss\.fff")}\r\n");
            else
                sb.Append($"Start: Missing\r\n");

            if (newestEventEndMarker is not null)
                sb.Append($"End: {newestEventEndMarker.TimeSpanTimelineController.ToString(@"hh\:mm\:ss\.fff")}\r\n");
            else
                sb.Append($"End: Missing\r\n");


            // If there are other survey start/end markers, then display them
            if (startEndEvents.Count > 2)
            {
                sb.Append($"\r\n\r\n");
                sb.Append($"All survey start/end segments:\r\n");

                Event? eventStartMarker = null;
                Event? eventEndMarker = null;
                SurveyMarker? startSurveyMarker = null;
                SurveyMarker? endSurveyMarker = null;

                for (int i = 0; i < startEndEvents.Count; i += 2)
                {
                    eventStartMarker = startEndEvents[i];
                    eventEndMarker = startEndEvents[i + 1];

                    if (eventStartMarker is not null)
                    {
                        startSurveyMarker = (SurveyMarker)eventStartMarker.EventData!;
                        sb.Append($"{startSurveyMarker.MarkerName}: {eventStartMarker.TimeSpanTimelineController.ToString(@"hh\:mm\:ss\.fff")} - ");
                    }
                    else
                        sb.Append($"Start missing - ");

                    if (eventEndMarker is not null)
                    {
                        endSurveyMarker = (SurveyMarker)eventEndMarker.EventData!;
                        sb.Append($"{eventEndMarker.TimeSpanTimelineController.ToString(@"hh\:mm\:ss\.fff")}");
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
                return $"{Math.Round(timeSpan.TotalSeconds, 2):F2} seconds";
            }
            return "0.00 seconds";
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
                    /*??? not usedcase Surveyor.Events.DataType.MonoLeftPoint:
                        return "Mono Left Point";
                    case Surveyor.Events.DataType.MonoRightPoint:
                        return "Mono Right Point";
                    case Surveyor.Events.DataType.StereoPoint:
                        return "Stereo Point";
                    case Surveyor.Events.DataType.StereoPairPoints:
                        return "Stereo Pair Points";*/
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
                   /*??? not used  case Surveyor.Events.DataType.MonoLeftPoint:
                        return "L";
                    case Surveyor.Events.DataType.MonoRightPoint:
                        return "R";
                    case Surveyor.Events.DataType.StereoPoint:
                        return "SP";
                    case Surveyor.Events.DataType.StereoPairPoints:
                        return "SPP";*/
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
                    if (surveyRulesCalc is not null)
                    {
                        //if (sb.Length > 0)
                        //    sb.Append(", ");
                        //if (surveyRulesCalc.SurveyRules == true)
                        //    sb.Append("Passed survey rules");
                        //else if (surveyRulesCalc.SurveyRules == false)
                        //{
                        //    sb.Append("Failed survey rules");
                            sb.Append(", ");
                            sb.Append(surveyRulesCalc.SurveyRulesText);
                        //}
                    }
                }
                else if (eventItem.EventData is Surveyor.Events.SurveyPoint surveyPoint)
                {
                    sb.AppendLine($"Species: {surveyPoint.SpeciesInfo.Species}");
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

