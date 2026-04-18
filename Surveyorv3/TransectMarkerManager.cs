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
        /// <returns>A SurveyDataType and a transect name, (null,null) if no name is available. </returns>
        public static async Task<(SurveyDataType?, string?)> SelectTransectNameAsync(EventsControl eventsControl, FieldTrip? fieldTrip,
                                                                  string siteNameOrCode, List<string> allowedReplicateNames,
                                                                  SurveyDataType? surveyDataTypeBefore, SurveyDataType? surveyDataTypeAfter, string beforeMarker, string afterMarker)
        {
            SurveyDataType? transectStartOrEnd = null;
            string? replicateNameSelected = null;

            // Figure out if the content dialog will have a start or and end transection option or 
            // no options at all (only Cancel)
            bool replicatesUserControlEnabled = false;
            bool startTransectOptionAvailable = false;
            bool endTransectOptionAvailable = false;

            // Build the content dialog
            if (surveyDataTypeBefore is null && surveyDataTypeAfter is null)
            {
                replicatesUserControlEnabled = true;
                startTransectOptionAvailable = true;
                endTransectOptionAvailable = false;
            }
            else if (surveyDataTypeBefore is null && surveyDataTypeAfter == SurveyDataType.SurveyStart)
            {
                replicatesUserControlEnabled = false;
                startTransectOptionAvailable = false;
                endTransectOptionAvailable = false;
            }
            else if (surveyDataTypeBefore is null && surveyDataTypeAfter == SurveyDataType.SurveyEnd)
            {
                replicatesUserControlEnabled = true;
                startTransectOptionAvailable = true;
                endTransectOptionAvailable = false;
            }
            else if (surveyDataTypeBefore == SurveyDataType.SurveyStart && surveyDataTypeAfter is null)
            {
                replicatesUserControlEnabled = true;
                startTransectOptionAvailable = false;
                endTransectOptionAvailable = true;
            }
            else if (surveyDataTypeBefore == SurveyDataType.SurveyStart && surveyDataTypeAfter == SurveyDataType.SurveyStart)
            {
                replicatesUserControlEnabled = true;
                startTransectOptionAvailable = false;
                endTransectOptionAvailable = true;
            }
            else if (surveyDataTypeBefore == SurveyDataType.SurveyStart && surveyDataTypeAfter == SurveyDataType.SurveyEnd)
            {
                replicatesUserControlEnabled = false;
                startTransectOptionAvailable = false;
                endTransectOptionAvailable = false;
            }
            else if (surveyDataTypeBefore == SurveyDataType.SurveyEnd && surveyDataTypeAfter is null)
            {
                replicatesUserControlEnabled = false;
                startTransectOptionAvailable = false;
                endTransectOptionAvailable = false;
            }
            else if (surveyDataTypeBefore == SurveyDataType.SurveyEnd && surveyDataTypeAfter == SurveyDataType.SurveyStart)
            {
                replicatesUserControlEnabled = false;
                startTransectOptionAvailable = false;
                endTransectOptionAvailable = false;
            }
            else if (surveyDataTypeBefore == SurveyDataType.SurveyEnd && surveyDataTypeAfter == SurveyDataType.SurveyEnd)
            {
                replicatesUserControlEnabled = true;
                startTransectOptionAvailable = true;
                endTransectOptionAvailable = false;
            }

            if (fieldTrip is not null)
            {
                ObservableCollection<string> usedReplicateNames = [.. eventsControl.GetEvents()
                                    .Where(e => e.EventDataType == SurveyDataType.SurveyStart)
                                    .OrderBy(e => e.TimeSpanTimelineController)
                                    .Select(e => (TransectMarker)e.EventData!)
                                    .Select(m => m.MarkerName)];

                // Setup the radio button initial state and if replciates are selectable
                Grid dialogContent = new()
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                StackPanel mainStackPanel = new()
                {
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                ReplicatesUserControl replicatesUserControl = new()
                {
                    Mode = ReplicatesUserControl.ReplicatesMode.Select,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsEnabled = replicatesUserControlEnabled
                };

                replicatesUserControl.SetFieldTrip(fieldTrip);
                replicatesUserControl.SetReplicateLayout(siteNameOrCode);
                replicatesUserControl.SetAllowedReplicates(allowedReplicateNames);
                replicatesUserControl.SetSelected(usedReplicateNames);

                TextBlock TransectMarkerInstructionsTextBlock = new()
                {
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Width = 350
                };

                // Update the instructions text based on the current selections
                string instructions = string.Empty;
                if (surveyDataTypeBefore is null && surveyDataTypeAfter is null)
                    instructions = $"Select the transect that this point in the media is the start of.";
                else if (surveyDataTypeBefore is null && surveyDataTypeAfter == SurveyDataType.SurveyStart)
                    instructions = $"You have an open transect '{afterMarker}' after this point in the media. Please find and mark the end of that transect before creating a new transect.";
                else if (surveyDataTypeBefore is null && surveyDataTypeAfter == SurveyDataType.SurveyEnd)
                {
                    instructions = $"Select transect marker '{afterMarker}' if this point in the media is the start of the transect '{afterMarker}'.";
                    replicatesUserControl.SetDefaultSelection(afterMarker);
                }
                else if (surveyDataTypeBefore == SurveyDataType.SurveyStart && surveyDataTypeAfter is null)
                {
                    instructions = $"Select transect marker '{beforeMarker}' if this point in the media is the end of transect '{beforeMarker}'";
                    replicatesUserControl.SetDefaultSelection(beforeMarker);
                }
                else if (surveyDataTypeBefore == SurveyDataType.SurveyStart && surveyDataTypeAfter == SurveyDataType.SurveyStart)
                {
                    instructions = $"Select transect marker '{afterMarker}' if this point in the media is the end of the transect '{afterMarker}'.";
                    replicatesUserControl.SetDefaultSelection(afterMarker);
                }
                else if (surveyDataTypeBefore == SurveyDataType.SurveyStart && surveyDataTypeAfter == SurveyDataType.SurveyEnd)
                    instructions = $"You are in the middle of an already defined transect '{beforeMarker}'. If you want to adjust this transect delete either the start or end markers first.";
                else if (surveyDataTypeBefore == SurveyDataType.SurveyEnd && surveyDataTypeAfter is null)
                    instructions = $"There is an end transect marker '{beforeMarker}' before this point. Please setup the start marker on that transect before creating a new transect.";
                else if (surveyDataTypeBefore == SurveyDataType.SurveyEnd && surveyDataTypeAfter == SurveyDataType.SurveyStart)
                    instructions = $"There is an end transect marker '{beforeMarker}' before this point and a start transect marker '{afterMarker}' after this point. Please setup the start marker on the earlier transect and setup an end marker on the later transect before creating a new transect.";
                else if (surveyDataTypeBefore == SurveyDataType.SurveyEnd && surveyDataTypeAfter == SurveyDataType.SurveyEnd)
                {
                    instructions = $"Select transect marker '{afterMarker}' if this point in the media is the start of the transect '{afterMarker}'";
                    replicatesUserControl.SetDefaultSelection(afterMarker);
                }

                TransectMarkerInstructionsTextBlock.Text = instructions;

                mainStackPanel.Children.Add(replicatesUserControl);
                mainStackPanel.Children.Add(TransectMarkerInstructionsTextBlock);
                dialogContent.Children.Add(mainStackPanel);


                ContentDialog contentDialog = new()
                {
                    Title = "Set Transect Marker",
                    Content = dialogContent,
                    CloseButtonText = "Cancel",
                };

                // Only show the 'OK' button if the user can select a replicate from the layout. 
                if (replicatesUserControlEnabled)
                {
                    contentDialog.PrimaryButtonText = "OK";
                    contentDialog.DefaultButton = ContentDialogButton.Primary;
                    contentDialog.IsPrimaryButtonEnabled = false;
                }
                else
                {
                    contentDialog.DefaultButton = ContentDialogButton.Close;
                }

                if (eventsControl is FrameworkElement frameworkElement)
                {
                    contentDialog.XamlRoot = frameworkElement.XamlRoot;
                }


                replicatesUserControl.IsEnabled = replicatesUserControlEnabled;

                // Change handler replicates user control selection and update the 'OK' button enabled state
                void SelectionChangedHandler(object sender, RoutedEventArgs e)
                {
                    List<string> selectedReplicates = replicatesUserControl.GetSelected();
                    contentDialog.IsPrimaryButtonEnabled = replicatesUserControlEnabled && selectedReplicates.Count > 0;
                }

                replicatesUserControl.SelectionChanged += SelectionChangedHandler;
                SelectionChangedHandler(replicatesUserControl, new RoutedEventArgs());

                ContentDialogResult result = await contentDialog.ShowAsync();

                replicatesUserControl.SelectionChanged -= SelectionChangedHandler;

                if (result == ContentDialogResult.Primary)
                {
                    List<string> selectedReplicates = replicatesUserControl.GetSelected();
                    replicateNameSelected = selectedReplicates.FirstOrDefault();

                    if (startTransectOptionAvailable)
                        transectStartOrEnd = SurveyDataType.SurveyStart;
                    else if (endTransectOptionAvailable)
                        transectStartOrEnd = SurveyDataType.SurveyEnd;
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


                StackPanel dialogStackPanel = new()
                {
                    Spacing = 0
                };

                TextBlock TransectMarkerInstructionsTextBlock = new()
                {
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Width = 350
                };

                // Update the instructions text based on the current selections
                string instructions = string.Empty;
                if (surveyDataTypeBefore is null && surveyDataTypeAfter is null)
                    instructions = $"Enter the transect number/name that this point in the media is the start of.";
                else if (surveyDataTypeBefore is null && surveyDataTypeAfter == SurveyDataType.SurveyStart)
                    instructions = $"You have an open transect '{afterMarker}' after this point in the media. Please find and mark the end of that transect before creating a new transect.";
                else if (surveyDataTypeBefore is null && surveyDataTypeAfter == SurveyDataType.SurveyEnd)
                {
                    instructions = $"Select transect marker '{afterMarker}' if this point in the media is the start of the transect '{afterMarker}'.";
                    inputTextBox.Text = afterMarker;
                }
                else if (surveyDataTypeBefore == SurveyDataType.SurveyStart && surveyDataTypeAfter is null)
                {
                    instructions = $"Select transect marker '{beforeMarker}' if this point in the media is the end of transect '{beforeMarker}'";
                    inputTextBox.Text = beforeMarker;
                }
                else if (surveyDataTypeBefore == SurveyDataType.SurveyStart && surveyDataTypeAfter == SurveyDataType.SurveyStart)
                {
                    instructions = $"Select transect marker '{afterMarker}' if this point in the media is the end of the transect '{afterMarker}'.";
                    inputTextBox.Text = afterMarker;
                }
                else if (surveyDataTypeBefore == SurveyDataType.SurveyStart && surveyDataTypeAfter == SurveyDataType.SurveyEnd)
                    instructions = $"You are in the middle of an already defined transect '{beforeMarker}'. If you want to adjust this transect delete either the start or end markers first.";
                else if (surveyDataTypeBefore == SurveyDataType.SurveyEnd && surveyDataTypeAfter is null)
                    instructions = $"There is an end transect marker '{beforeMarker}' before this point. Please setup the start marker on that transect before creating a new transect.";
                else if (surveyDataTypeBefore == SurveyDataType.SurveyEnd && surveyDataTypeAfter == SurveyDataType.SurveyStart)
                    instructions = $"There is an end transect marker '{beforeMarker}' before this point and a start transect marker '{afterMarker}' after this point. Please setup the start marker on the earlier transect and setup an end marker on the later transect before creating a new transect.";
                else if (surveyDataTypeBefore == SurveyDataType.SurveyEnd && surveyDataTypeAfter == SurveyDataType.SurveyEnd)
                {
                    instructions = $"Select transect marker '{afterMarker}' if this point in the media is the start of the transect '{afterMarker}'";
                    inputTextBox.Text = afterMarker;
                }

                TransectMarkerInstructionsTextBlock.Text = instructions;

                dialogStackPanel.Children.Add(inputTextBox);
                dialogStackPanel.Children.Add(TransectMarkerInstructionsTextBlock);

                ContentDialog contentDialog = new()
                {
                    Title = "Set Transect Marker",
                    Content = dialogStackPanel,
                    CloseButtonText = "Cancel",
                };

                // Only show the 'OK' button if the user can select a replicate from the layout. 
                if (replicatesUserControlEnabled)
                {
                    contentDialog.PrimaryButtonText = "OK";
                    contentDialog.DefaultButton = ContentDialogButton.Primary;
                    contentDialog.IsPrimaryButtonEnabled = false;
                }
                else
                {
                    contentDialog.DefaultButton = ContentDialogButton.Close;
                }

                if (eventsControl is FrameworkElement frameworkElement)
                {
                    contentDialog.XamlRoot = frameworkElement.XamlRoot;
                }

                void TextChangedHandler(object sender, TextChangedEventArgs e)
                {
                    contentDialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(inputTextBox.Text);
                }

                inputTextBox.TextChanged += TextChangedHandler;
                TextChangedHandler(inputTextBox, null!);

                ContentDialogResult result = await contentDialog.ShowAsync();

                inputTextBox.TextChanged -= TextChangedHandler;

                if (result == ContentDialogResult.Primary)
                {
                    replicateNameSelected = inputTextBox.Text.Trim();

                    if (startTransectOptionAvailable)
                        transectStartOrEnd = SurveyDataType.SurveyStart;
                    else if (endTransectOptionAvailable)
                        transectStartOrEnd = SurveyDataType.SurveyEnd;
                }
            }

            return (transectStartOrEnd, replicateNameSelected);
        }


        /// <summary>
        /// Add a marker at the indicated position
        /// </summary>
        /// <param name="events"></param>
        /// <param name="positionTimelineController"></param>
        /// <param name="positionLeft"></param>
        /// <param name="positionRight"></param>
        /// <param name="surveyDataType"></param>
        /// <param name="markerName"></param>
        public async Task AddMarkerAsync(EventsControl eventsControl, TimeSpan positionTimelineController, TimeSpan positionLeft, TimeSpan positionRight, SurveyDataType surveyDataType, string markerName)
        {
            Event? newEvent = null;
            SurveyDataType markerType = SurveyDataType.SurveyStart;

            // Check if there are any markers already in the event list
            int eventCount = eventsControl.GetEvents().Count(e => (e.EventDataType == SurveyDataType.SurveyStart || e.EventDataType == SurveyDataType.SurveyEnd)
                                                             && e.TimeSpanTimelineController == positionTimelineController);
            if (eventCount == 0)
            {

                // The new marker is earlier and therefore in the middle of existing markers. I will insert a new start marker 
                // for now and later on we will adjust all existing markers so they go start/end, start/end, etc.
                Event evt = new(surveyDataType)
                {
                    DateTimeCreate = DateTime.Now,
                    TimeSpanTimelineController = positionTimelineController,
                    TimeSpanLeftFrame = positionLeft,
                    TimeSpanRightFrame = positionRight
                };
                evt.SetData(markerType);
                TransectMarker transectMarker = (TransectMarker)evt.EventData!;
                transectMarker.MarkerName = markerName;

                await eventsControl.AddEventAsync(evt);
            }

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
        public bool DeleteMarker(EventsControl eventsControl, TimeSpan positionTimelineController)
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

            return ret;
        }


        /// <summary>
        /// Check there is a end transect marker for every start transect marker and that they are in the correct order (start/end, start/end, etc)
        /// </summary>
        /// <param name="eventsControl"></param>
        /// <returns></returns>
        public static bool CheckIfTransectMarkerSetupIsValid(EventsControl eventsControl, out Event? eventFirstProblem)
        {
            bool ret = true;

            // Reset
            eventFirstProblem = null;

            List<Event> startEndEvents = [.. eventsControl.GetEvents().Where(e => e.EventDataType == SurveyDataType.SurveyStart || e.EventDataType == SurveyDataType.SurveyEnd)
                                                                      .OrderBy(e => e.TimeSpanTimelineController)];
            bool expectingStart = true;
            // Check that the transect markers are in the order start/end, start/end etc
            foreach (Event evt in startEndEvents)
            {
                if (expectingStart && evt.EventDataType != SurveyDataType.SurveyStart)
                {
                    ret = false;
                    eventFirstProblem = evt;
                    break;
                }
                else if (!expectingStart && evt.EventDataType != SurveyDataType.SurveyEnd)
                {
                    ret = false;
                    eventFirstProblem = evt;
                    break;
                }
                if (evt.EventDataType == SurveyDataType.SurveyStart)
                    expectingStart = false;
                else
                    expectingStart = true;
            }
            return ret;
        }

#if OLDVERSION
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
#endif
    }
}
