using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Surveyor.Events;
using System.Text;
using System.Runtime.CompilerServices;
using MathNet.Numerics.LinearAlgebra.Factorization;


namespace Surveyor.User_Controls
{
    public sealed partial class EventsControl : UserControl
    {

        private DispatcherQueue? dispatcherQueue;

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

        internal void SetEvents(ObservableCollection<Event> EventItems)
        {
            ListViewEvent.ItemsSource = EventItems;
        }

        internal void SetMediaStereoController(MediaStereoController _mediaStereoController)
        {
            mediaStereoController = _mediaStereoController;
        }


        internal ListView GetListView() => ListViewEvent;

  
        public async void Display(Event evt)
        {
            //??? Support full display of all values in all event types
            //??? Display created date as dd MMM yyyy
            //??? Add a 'Copy' button using the standard copy gypth and format as tab delimited
            StringBuilder sb = new StringBuilder();
            sb.Append($"{evt.EventDataType}:\r\nCreated: {evt.DateTimeCreate:dd MMM yyyy hh:mm:ss}\r\n\r\n");

            if (evt.EventData is not null)
            {
                switch (evt.EventDataType)
                {
                    case DataType.StereoSyncPoint:  // No EventData for this type 

                        sb.Append($"Sync media position: {evt.TimeSpanTimelineController}\r\n");
                        sb.Append($"Left media position: {evt.TimeSpanLeftFrame}\r\n");
                        sb.Append($"Right media position: {evt.TimeSpanRightFrame}\r\n");
                        break;

                    case DataType.SurveyMeasurementPoints:
                    case DataType.SurveyStereoPoint:
                        SurveyRulesCalc? surveyRulesCalc = null;
                        if (evt.EventData is SurveyMeasurement surveyMeasurement)
                        {
                            sb.Append($"Species: {surveyMeasurement.SpeciesInfo.Species}\r\n");
                            sb.Append($"Measurement: {surveyMeasurement.Measurment}m\r\n");
                            sb.Append($"Survey Rules: {surveyMeasurement.SurveyRulesCalc.SurveyRulesText}\r\n");
                            sb.Append($"\r\n");
                            sb.Append($"2D Points:\r\n");
                            sb.Append($"Set A\r\n");
                            sb.Append("Left Camera: ({Math.Round(surveyMeasurement.LeftXA,1)}, {Math.Round(surveyMeasurement.LeftYA,1)})\r\n");
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
                    
                    case DataType.SurveyPoint:
                        if (evt.EventData is SurveyPoint surveyPoint)
                        {
                            sb.Append($"Species: {surveyPoint.SpeciesInfo.Species}\r\n");
                            sb.Append($"\r\n");
                            sb.Append($"2D Point:\r\n");
                            string camera = surveyPoint.trueLeftfalseRight ? "Left" : "Right";
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


            if (result == ContentDialogResult.Secondary)
            {
                // Copy the event data to the clipboard
                var dataPackage = new DataPackage();
                dataPackage.SetText(sb.ToString());
                Clipboard.SetContent(dataPackage);
            }
        }

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
                        SecondaryButtonText = "Cancel",
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
                return $"{Math.Round(timeSpan.TotalSeconds, 2)} seconds";
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
            if (value is Surveyor.Events.DataType eventType)
            {
                switch (eventType)
                {
                    case Surveyor.Events.DataType.MonoLeftPoint:
                        return "Mono Left Point";
                    case Surveyor.Events.DataType.MonoRightPoint:
                        return "Mono Right Point";
                    case Surveyor.Events.DataType.StereoPoint:
                        return "Stereo Point";
                    case Surveyor.Events.DataType.StereoPairPoints:
                        return "Stereo Pair Points";
                    case Surveyor.Events.DataType.SurveyPoint:
                        return "Survey Point";
                    case Surveyor.Events.DataType.SurveyStereoPoint:
                        return "Survey 3D Point";
                    case Surveyor.Events.DataType.SurveyMeasurementPoints:
                        return "Survey Measurement";
                    case Surveyor.Events.DataType.StereoCalibrationPoints:
                        return "Stereo Calibration Points";
                    case Surveyor.Events.DataType.StereoSyncPoint:
                        return "Stereo Sync Point";
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
            if (value is Surveyor.Events.DataType eventType)
            {
                switch (eventType)
                {
                    case Surveyor.Events.DataType.MonoLeftPoint:
                        return "L";
                    case Surveyor.Events.DataType.MonoRightPoint:
                        return "R";
                    case Surveyor.Events.DataType.StereoPoint:
                        return "SP";
                    case Surveyor.Events.DataType.StereoPairPoints:
                        return "SPP";
                    case Surveyor.Events.DataType.SurveyPoint:
                        return "\uE139";
                    case Surveyor.Events.DataType.SurveyStereoPoint:
                        return "\uECAF";
                    case Surveyor.Events.DataType.SurveyMeasurementPoints:
                        return "\uE1D9";
                    case Surveyor.Events.DataType.StereoCalibrationPoints:
                        return "\uEB3C";
                    case Surveyor.Events.DataType.StereoSyncPoint:
                        return "\uE754";
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

                    // Length of the fish
                    if (measurment is not null && measurment != 0)
                    {
                        if (sb.Length > 0)
                            sb.Append(", ");
                        sb.Append($"{Math.Round((double)measurment * 1000, 0)}mm long"); // Round up to the nearest whole number
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

