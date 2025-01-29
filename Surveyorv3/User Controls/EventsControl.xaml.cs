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

  
        public async void Display(Event item)
        {
            var display = $"Level: {item.EventDataType.ToString()}:\r\nCreated: {item.DateTimeCreate}\r\n\r\n";

            var dataPackage = new DataPackage();
            dataPackage.SetText(display);  //??? Change this to be useful pastable data like the coordincated and distance in tabulated form
            Clipboard.SetContent(dataPackage);

            var messageDialog = new ContentDialog
            {
                Title = "Event",
                Content = display,
                CloseButtonText = "OK",

                // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                XamlRoot = this.Content.XamlRoot
            };

            await messageDialog.ShowAsync();
        }

        private void ViewMenuItem_Click(object sender, ItemClickEventArgs e)
        {
            //??? Never receive this message - investigate
            if (ListViewEvent.SelectedItem is Event selectedItem)
            {
                Display(selectedItem);
            }
        }

        private void GoToFrameMenuItem_Click(object sender, ItemClickEventArgs e)
        {
            if (ListViewEvent.SelectedItem is Event selectedItem)
            {
                // Attempt to go to the left frame
                if (selectedItem.TimeSpanTimelineController != TimeSpan.Zero && mediaStereoController is not null)
                {
                    // Jump to frame of this event
                    mediaStereoController.UserReqFrameJump(SurveyorMediaControl.eControlType.Primary, selectedItem.TimeSpanTimelineController);
                }                
            }
        }

        private void ViewMenuItem_Click(object sender, RightTappedRoutedEventArgs e)
        {

        }


        /// <summary>
        /// Check for user requests via the keyboard in the event list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void ListViewEvent_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Delete)
            {
                if (ListViewEvent.SelectedItem is Event selectedItem)
                {
                    if (ListViewEvent.ItemsSource is ObservableCollection<Event> eventItems)
                    {
                        // Create the ContentDialog instance
                        var dialog = new ContentDialog
                        {
                            Title = $"Delete Event",
                            Content = "Are you sure you want to delete the selecged event?",
                            PrimaryButtonText = "OK",
                            SecondaryButtonText = "Cancel",
                            DefaultButton = ContentDialogButton.Primary, // Set "OK" as the default button

                            // XamlRoot must be set in the case of a ContentDialog running in a Desktop app
                            XamlRoot = this.Content.XamlRoot
                        };

                        // Show the dialog and await the result
                        var result = await dialog.ShowAsync();

                        // Handle the dialog result
                        if (result == ContentDialogResult.Primary)
                            eventItems.Remove(selectedItem);
                    }
                }
            }
        }
    }


    /// <summary>
    /// Convert TimeSpanToStringConverter to 2DP
    /// </summary>
    public class EventTimeSpanToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is TimeSpan timeSpan)
            {
                return $"{timeSpan.TotalSeconds:F2} seconds";
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
    public class EventDataTypeToStringConverter : IValueConverter
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
    public class EventTypeToGlyphConverter : IValueConverter
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
                        sb.Append($"{measurment * 1000:F0}mm long");
                    }

                    // Range from the camera 
                    if (surveyRulesCalc is not null && surveyRulesCalc.Range is not null && surveyRulesCalc.Range > 0)
                    {
                        if (sb.Length > 0)
                            sb.Append(", ");
                        sb.Append($"{surveyRulesCalc.Range:F2}m away");
                    }

                    // XOffset
                    if (surveyRulesCalc is not null && surveyRulesCalc.XOffset is not null && surveyRulesCalc.XOffset != 0)
                    {
                        if (sb.Length > 0)
                            sb.Append(", ");
                        if (surveyRulesCalc.XOffset == 0)
                            sb.Append($"Horzontially central");
                        else if (surveyRulesCalc.XOffset > 0)
                            sb.Append($"{surveyRulesCalc.XOffset:F2}m to the right");
                        else if (surveyRulesCalc.XOffset < 0)
                            sb.Append($"{-surveyRulesCalc.XOffset:F2}m to the left");
                    }

                    // YOffset
                    if (surveyRulesCalc is not null && surveyRulesCalc.YOffset is not null && surveyRulesCalc.YOffset != 0)
                    {
                        if (sb.Length > 0)
                            sb.Append(", ");
                        if (surveyRulesCalc.YOffset == 0)
                            sb.Append($"Horzontially central");
                        else if (surveyRulesCalc.YOffset > 0)
                            sb.Append($"{surveyRulesCalc.YOffset:F2}m below");
                        else if (surveyRulesCalc.YOffset < 0)
                            sb.Append($"{-surveyRulesCalc.YOffset:F2}m above");
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

