// Used to manage the survey transect start/stop markers in the events list
// Markers are just inserted at a points and this class looks through
// the list of events to figure out if a marker should be a start or a stop 
// marker. 
//
// Version 1.0 13 Feb 2025
//
// Version 1.1 13 Apr 2025
// Rename from SurveryMarkerManager to TransectMarkerManager
// Version 1.2 14 Apr 2026
// Added support for field trip control transect naming. This means the user selects
// a transect name from the layout for the site and this is then used as the transect
// marker name. If no layout is provided then the user can enter any transect name they want.
// This is instead of the old auto transect numbering system. 
// ???TO DO After SelectTransectName is integrated review the remaining TransectMarkerManager methods

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Surveyor.Events;
using Surveyor.User_Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;


namespace Surveyor
{
    class TransectMarkerManager
    {
        public TransectMarkerManager() { }


        /// <summary>
        /// Selects an available transect name from the specified layout that is not present in the list of used
        /// replicate names.
        /// If ReplicatesLayoutClass is null then the user and enter any transect name
        /// </summary>
        /// <param name="layout">The layout containing the available transect names to select from.</param>
        /// <param name="UsedReplicateNames">A list of replicate names that have already been used. The selected transect name will not be in this list.</param>
        /// <returns>A transect name that is available in the layout and not present in the used replicate names list; or null if
        /// no such name is available.</returns>
        public static async Task<string?> SelectTransectNameAsync(EventsControl eventsControl, FieldTrip? fieldTrip, string siteNameOrCode, List<string> allowedReplicateNames)
        {
            string? replicateNameSelected = null;

            if (fieldTrip is not null)
            {
                ObservableCollection<string> usedReplicateNames = [.. eventsControl.GetEvents()
                                    .Where(e => e.EventDataType == SurveyDataType.SurveyStart)
                                    .OrderBy(e => e.TimeSpanTimelineController)
                                    .Select(e => (TransectMarker)e.EventData!)
                                    .Select(m => m.MarkerName)];

                Grid dialogContent = new()
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                ReplicatesUserControl replicatesUserControl = new()
                {
                    Mode = ReplicatesUserControl.ReplicatesMode.Select,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                replicatesUserControl.SetFieldTrip(fieldTrip);
                replicatesUserControl.SetReplicateLayout(siteNameOrCode);
                replicatesUserControl.SetAllowedReplicates(allowedReplicateNames);
                replicatesUserControl.SetSelected(usedReplicateNames);

                dialogContent.Children.Add(replicatesUserControl);

                ContentDialog contentDialog = new()
                {
                    Title = "Set Transect Marker",
                    Content = dialogContent,
                    PrimaryButtonText = "OK",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    IsPrimaryButtonEnabled = false
                };

                if (eventsControl is FrameworkElement frameworkElement)
                {
                    contentDialog.XamlRoot = frameworkElement.XamlRoot;
                }

                void SelectionChangedHandler(object sender, RoutedEventArgs e)
                {
                    List<string> selectedReplicates = replicatesUserControl.GetSelected();
                    contentDialog.IsPrimaryButtonEnabled = selectedReplicates.Count > 0;
                }

                replicatesUserControl.SelectionChanged += SelectionChangedHandler;
                SelectionChangedHandler(replicatesUserControl, new RoutedEventArgs());

                ContentDialogResult result = await contentDialog.ShowAsync();

                replicatesUserControl.SelectionChanged -= SelectionChangedHandler;

                if (result == ContentDialogResult.Primary)
                {
                    List<string> selectedReplicates = replicatesUserControl.GetSelected();
                    replicateNameSelected = selectedReplicates.FirstOrDefault();
                }
            }
            else
            {
                // If no layout is provided, allow the user to enter any transect name
                TextBox inputTextBox = new()
                {
                    PlaceholderText = "Enter transect number",
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                FontIcon infoIcon = new()
                {
                    Glyph = "\uE783",
                    FontSize = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 12, 0, 0)
                };

                ToolTip toolTip = new()
                {
                    Content = "This is normally the number of transect that this portion of the video covers."
                };
                ToolTipService.SetToolTip(infoIcon, toolTip);

                StackPanel dialogStackPanel = new()
                {
                    Spacing = 0
                };
                dialogStackPanel.Children.Add(inputTextBox);
                dialogStackPanel.Children.Add(infoIcon);

                ContentDialog contentDialog = new()
                {
                    Title = "Set Transect Marker",
                    Content = dialogStackPanel,
                    PrimaryButtonText = "OK",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    IsPrimaryButtonEnabled = false
                };

                if (eventsControl is FrameworkElement frameworkElement)
                {
                    contentDialog.XamlRoot = frameworkElement.XamlRoot;
                }

                void TextChangedHandler(object sender, TextChangedEventArgs e)
                {
                    contentDialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(inputTextBox.Text);
                }

                inputTextBox.TextChanged += TextChangedHandler;
                TextChangedHandler(inputTextBox, null);

                ContentDialogResult result = await contentDialog.ShowAsync();

                inputTextBox.TextChanged -= TextChangedHandler;

                if (result == ContentDialogResult.Primary)
                {
                    replicateNameSelected = inputTextBox.Text.Trim();
                }
            }

            return replicateNameSelected;
        }


        /// <summary>
        /// Add a marker at the indicated position
        /// </summary>
        /// <param name="events"></param>
        /// <param name="positionTimelineController"></param>
        /// <param name="positionLeft"></param>
        /// <param name="positionRight"></param>
        public async Task AddMarkerAsync(EventsControl eventsControl, TimeSpan positionTimelineController, TimeSpan positionLeft, TimeSpan positionRight)
        {
            Event? newEvent = null;
            SurveyDataType markerType = SurveyDataType.SurveyStart;

            // Check if there are any markers already in the event list
            int eventCount = eventsControl.GetEvents().Count(e => (e.EventDataType == SurveyDataType.SurveyStart || e.EventDataType == SurveyDataType.SurveyEnd)
                                                             && e.TimeSpanTimelineController == positionTimelineController);
            if (eventCount == 0)
            {
                // First query the existing SurveyDataType.SurveyStart and SurveyDataType.SurveyStop events
                List<Event> startEndEvents = [.. eventsControl.GetEvents().Where(e => e.EventDataType == SurveyDataType.SurveyStart || e.EventDataType == SurveyDataType.SurveyEnd).OrderBy(e => e.TimeSpanTimelineController)];

                // Check if this is first marker
                if (startEndEvents.Count == 0)
                {
                    // There are no existing markers, so this new marker must be a start marker
                    newEvent = await AddSurveyStartEndMarkerAsync(eventsControl, 
                                                                  SurveyDataType.SurveyStart, 
                                                                  positionTimelineController, 
                                                                  positionLeft, positionRight);
                }
                else
                {
                    Event evtLast = startEndEvents[^1]; // Get the last event

                    // Is the new marker later than all the existing
                    if (evtLast.TimeSpanTimelineController < positionTimelineController)
                    {
                        if (evtLast.EventDataType == SurveyDataType.SurveyStart)
                            markerType = SurveyDataType.SurveyEnd;
                        else
                            markerType = SurveyDataType.SurveyStart;

                        // The new marker is later than all the existing markers, so we can calculate if the next marker is either a start or end marker 
                        newEvent = await AddSurveyStartEndMarkerAsync(eventsControl, 
                                                                      markerType, 
                                                                      positionTimelineController, 
                                                                      positionLeft, positionRight);
                    }
                    else
                    {
                        // The new marker is earlier and therefore in the middle of existing markers. I will insert a new start marker 
                        // for now and later on we will adjust all existing markers so they go start/end, start/end, etc.
                        newEvent = await AddSurveyStartEndMarkerAsync(eventsControl, 
                                                                      SurveyDataType.SurveyStart, 
                                                                      positionTimelineController, 
                                                                      positionLeft, positionRight);
                    }
                }
            }

            // Now we need to adjust all the existing markers so they go start/end, start/end, etc. 
            await ReCalcMarkerStartAndEndAsync(eventsControl);

            // If the added marker is an end marker, then report an overview of the survey start/end markers
            if (newEvent is not null && newEvent.EventDataType == SurveyDataType.SurveyEnd)
            {
                // Report the survey start/end markers
                await eventsControl.DisplaySurveyStartEndMarkersAsync(newEvent);
            }
        }


        /// <summary>
        /// Find and delete any SurveyStart/SurveyEnd markers for the indicated position
        /// </summary>
        /// <param name="eventsControl"></param>
        /// <param name="positionTimelineController"></param>
        /// <returns>true is anything was deleted</returns>
        public async Task<bool> DeleteMarkerAsync(EventsControl eventsControl, TimeSpan positionTimelineController)
        {
            bool ret = false;

            // Find the marker at the indicated position
            List<Event> startEndEvents = [.. eventsControl.GetEvents().Where(e => (e.EventDataType == SurveyDataType.SurveyStart || e.EventDataType == SurveyDataType.SurveyEnd)
                                                                                       && e.TimeSpanTimelineController == positionTimelineController)
                                                                          .OrderBy(e => e.TimeSpanTimelineController)];

            // Delete any found events
            foreach (Event evt in startEndEvents)
            {
                eventsControl.DeleteEvent(evt);
                ret = true;
            }

            // Recalculate start/end
            if (ret)
                await ReCalcMarkerStartAndEndAsync(eventsControl);

            return ret;
        }


        /// <summary>
        /// Run through the SurveyDataType.SurveyStart and SurveyDataType.SurveyEnd markers
        /// and ensure they are in the order start/end, start/end etc
        /// </summary>
        /// <param name="events"></param>
        /// <returns></returns>
        private async Task<bool> ReCalcMarkerStartAndEndAsync(EventsControl eventsControl)
        {
            bool ret = false;

            List<Event> startEndEvents = [.. eventsControl.GetEvents().Where(e => e.EventDataType == SurveyDataType.SurveyStart || e.EventDataType == SurveyDataType.SurveyEnd)
                                                                      .OrderBy(e => e.TimeSpanTimelineController)];

            bool expectingStart = true;
            int transectMarkerIndex = 1;

            // Renumber the transect marker
            foreach (Event evt in startEndEvents)
            {
                TransectMarker transectMarker = (TransectMarker)evt.EventData!;

                if (expectingStart)
                {
                    evt.EventDataType = SurveyDataType.SurveyStart;
                    transectMarker.MarkerName = $"{transectMarkerIndex}";
                    expectingStart = false;
                }
                else
                {
                    evt.EventDataType = SurveyDataType.SurveyEnd;
                    transectMarker.MarkerName = $"{transectMarkerIndex}";
                    expectingStart = true;
                    transectMarkerIndex++;
                }
            }

            // Remove all the existing transect marker
            foreach (Event evt in startEndEvents)
            {
                eventsControl.DeleteEvent(evt);
            }

            // Re-add them so the transect marker text is updated
            foreach (Event evt in startEndEvents)
            {
                await eventsControl.AddEventAsync(evt);
            }

            return ret;
        }


        /// <summary>
        /// Helper function to create and add Survey Start/End event
        /// </summary>
        /// <param name="eventsControl"></param>
        /// <param name="markerType"></param>
        /// <param name="positionTimelineController"></param>
        /// <param name="positionLeft"></param>
        /// <param name="positionRight"></param>
        private async Task<Event> AddSurveyStartEndMarkerAsync(EventsControl eventsControl, SurveyDataType markerType, TimeSpan positionTimelineController, TimeSpan positionLeft, TimeSpan positionRight)
        {
            // The new marker is earlier and therefore in the middle of existing markers. I will insert a new start marker 
            // for now and later on we will adjust all existing markers so they go start/end, start/end, etc.
            Event evt = new(markerType)
            {
                DateTimeCreate = DateTime.Now,
                TimeSpanTimelineController = positionTimelineController,
                TimeSpanLeftFrame = positionLeft,
                TimeSpanRightFrame = positionRight
            };
            evt.SetData(markerType);
            await eventsControl.AddEventAsync(evt);

            return evt;
        }
    }
}
