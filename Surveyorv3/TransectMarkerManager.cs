// Used to manage the surevey transect start/stop markers in the events list
// Markers are just inserted at a points and this class looks through
// the list of events to figure out if a marker should be a start or a stop 
// marker. 
//
// Verison 1.0 13 Feb 2025
//
// Version 1.1 13 Apr 2025
// Rename from SurveryMarkerManager to TransectMarkerManager

using Surveyor.Events;
using Surveyor.User_Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


namespace Surveyor
{
    class TransectMarkerManager
    {
        public TransectMarkerManager() { }


        /// <summary>
        /// Add a marker at the indicated position
        /// </summary>
        /// <param name="events"></param>
        /// <param name="positionTimelineController"></param>
        /// <param name="positionLeft"></param>
        /// <param name="positionRight"></param>
        public async void AddMarker(EventsControl eventsControl, TimeSpan positionTimelineController, TimeSpan positionLeft, TimeSpan positionRight)
        {
            Event? newEvent = null;
            SurveyDataType markerType = SurveyDataType.SurveyStart;

            // Check if the marker is already in the list
            int eventCount = eventsControl.GetEvents().Count(e => (e.EventDataType == SurveyDataType.SurveyStart || e.EventDataType == SurveyDataType.SurveyEnd)
                                                             && e.TimeSpanTimelineController == positionTimelineController);
            if (eventCount == 0)
            {
                // First query the existing SurveyDataType.SurveyStart and SurveyDataType.SurveyStop events
                List<Event> startEndEvents = [.. eventsControl.GetEvents().Where(e => e.EventDataType == SurveyDataType.SurveyStart || e.EventDataType == SurveyDataType.SurveyEnd)                                                                                       
                                                                          .OrderBy(e => e.TimeSpanTimelineController)];

                // Check if this is first marker
                if (startEndEvents.Count == 0)
                {
                    // There are no existing markers, so this new marker must be a start marker
                    newEvent = await AddSurveyStartEnd(eventsControl, SurveyDataType.SurveyStart, positionTimelineController, positionLeft, positionRight);
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
                        newEvent = await AddSurveyStartEnd(eventsControl, markerType, positionTimelineController, positionLeft, positionRight);
                    }
                    else
                    {
                        // The new marker is earlier and therefore in the middle of existing markers. I will insert a new start marker 
                        // for now and later on we will adjust all existing markers so they go start/end, start/end, etc.
                        newEvent = await AddSurveyStartEnd(eventsControl, SurveyDataType.SurveyStart, positionTimelineController, positionLeft, positionRight);
                    }
                }
            }

            // Now we need to adjust all the existing markers so they go start/end, start/end, etc. 
            ReCalcMarkerStartAndEnd(eventsControl);

            // If the added marker is an end marker, then report an overview of the survey start/end markers
            if (newEvent is not null && newEvent.EventDataType == SurveyDataType.SurveyEnd)
            {
                // Report the survey start/end markers
                eventsControl.DisplaySurveyStartEndMarkers(newEvent);
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

            // Recalc start/end
            if (ret)
                ReCalcMarkerStartAndEnd(eventsControl);

            return ret;
        }


        /// <summary>
        /// Run through the SurveyDataType.SurveyStart and SurveyDataType.SurveyEnd markers
        /// and ensure they are in the order start/end, start/end etc
        /// </summary>
        /// <param name="events"></param>
        /// <returns></returns>
        private bool ReCalcMarkerStartAndEnd(EventsControl eventsControl)
        {
            bool ret = false;

            List<Event> startEndEvents = [.. eventsControl.GetEvents().Where(e => e.EventDataType == SurveyDataType.SurveyStart || e.EventDataType == SurveyDataType.SurveyEnd)
                                                                      .OrderBy(e => e.TimeSpanTimelineController)];

            bool expectingStart = true;
            int transectMarkerIndex = 1;

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
        private async Task<Event> AddSurveyStartEnd(EventsControl eventsControl, SurveyDataType markerType, TimeSpan positionTimelineController, TimeSpan positionLeft, TimeSpan positionRight)
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
            await eventsControl.AddEvent(evt);

            return evt;
        }
    }
}
