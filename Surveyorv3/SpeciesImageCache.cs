// SpeciesImageCache  Mananges the cached species image file
//
// Version 1.0 11 Apr 2025

using Surveyor.User_Controls;
using Surveyor.Helper;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using static Surveyor.SpeciesItem;
using static Surveyor.User_Controls.SurveyorTesting;

namespace Surveyor
{
    internal class SpeciesImageCache
    {
        private readonly SpeciesCodeList speciesCodeList;
        private readonly InternetQueue internetQueue;
        private readonly Reporter? report;
        private readonly Dictionary<string, SpeciesCacheState> speciesStates = [];
        private bool isReady = false;

        private Timer? timer;
        private bool timerRunning = false;
        private bool isProcessing = false;
        private readonly TimeSpan timerInterval = TimeSpan.FromMinutes(5);


        private enum State
        {
            None,
            RequestingFirsrPhotoPage,
            WaitingForFirstPhotoPage,
            ParsingFirstPhotoPage,
            RequestingAllPhotoPages,
            WaitingForAllPhotoPages,
            ParsingAllPhotoPagesAndRequestImages,
            WaitingForAllImages,
            Done
        }

        /// <summary>
        /// Underlaying cache record structure
        /// </summary>
        private class SpeciesCacheState
        {
            // Backing field for State
            private State _status = State.None;

            public SpeciesCacheState()
            {
                CreatedDate = DateTime.Now;
            }

            // This ache item pertains to this species record
            public required SpeciesItem SpeciesItem { get; set; }

            // Current status of the record
            public State Status
            {
                get => _status;
                set
                {
                    if (_status != value)
                    {
                        _status = value;
                        StatusDate = DateTime.Now;
                    }
                }
            }

            // Date the status changed
            public DateTime? StatusDate { get; private set; } = null;

            // Total images available for this fish species
            public int TotalImages { get; set; } = 0;

            // Parallel list of url and their corresponding files
            public List<SpeciesImageItem> SpeciesImageItemList { get; set; } = [];

            // Created date
            public DateTime CreatedDate { get; }
        }

        public class SpeciesImageItem
        {
            public string Source { get; set; } = "";
            public string ImageUrl { get; set; } = "";
            public string ImageFile { get; set; } = "";
            public string Author { get; set; } = "";
            public string GenusSpecies { get; set; } = "";
        }


        /// <summary>
        /// UI view of the cache record
        /// </summary>
        public class SpeciesCacheViewItem
        {
            public string Genus => SpeciesItem.Genus;
            public string Species => SpeciesItem.Species;
            public string Code => SpeciesItem.Code;
            public string Status { get; set; } = "";
            public int TotalImages { get; set; }
            public int ImageCount { get; set; }
            public string? StatusDate { get; set; }
            public SpeciesItem SpeciesItem { get; set; } = default!;
            public string CreatedDate { get; set; } = "";
        }

        // View call to bind to (updated via the RefreshView() method)
        public ObservableCollection<SpeciesCacheViewItem> SpeciesStateView { get; } = [];

        public SpeciesImageCache(SpeciesCodeList _speciesCodeList, InternetQueue _internetQueue, Reporter? _report)
        {
            speciesCodeList = _speciesCodeList;
            internetQueue = _internetQueue;
            report = _report;
        }


        /// <summary>
        /// Load the on disk persistent cache
        /// This function must be called before using other methods
        /// </summary>
        /// <returns></returns>
        public async Task<bool> Load(bool enableTimer)
        {
            bool ret = false;

            try
            {
                await LoadSpeciesStates();

                if (enableTimer)
                    Enable(true);

                isReady = true;
                ret = true;

            }
            catch (Exception ex)
            {
                report?.Error("", $"SpeciesImageCache.Load Failed {ex.Message}");
            }

            return ret;
        }


        /// <summary>
        /// Call to shutdown
        /// </summary>
        /// <returns></returns>
        public async Task Unload()
        {
            isReady = false;

            if (timerRunning)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    while (isProcessing)
                    {
                        await Task.Delay(100, cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    report?.Warning("", "SpeciesImageCache.Unload timed out waiting for processing to complete.");
                }

                Enable(false);

            }

            if (timer != null)
            {
                timer?.Dispose();
                timer = null;
            }

            speciesStates.Clear();
        }


        /// <summary>
        /// Starts or stops the background timer
        /// </summary>
        /// <param name="enable"></param>
        public void Enable(bool enable)
        {
            if (enable)
            {
                if (timer == null)
                {
#if DEBUG
                    TimeSpan dueTime = TimeSpan.FromSeconds(5);        // Better to fire the initial timer quickly if debugging 
#else
                    TimeSpan dueTime = TimeSpan.FromMinutes(1);
#endif
                    timer = new Timer(async _ => await TimerCallback(), null, dueTime, timerInterval);
                    timerRunning = true;
                }
            }
            else
            {
                timer?.Change(Timeout.Infinite, Timeout.Infinite);
                timerRunning = false;
            }
        }


        /// <summary>
        /// Returns a list of image file name for a given species that are stored in local storage
        /// </summary>
        /// <param name="speciesCode"></param>
        /// <returns></returns>
        public List<SpeciesImageItem> GetImagesForSpecies(string speciesCode)
        {
            if (speciesStates.TryGetValue(speciesCode, out var state))
            {
                return state.SpeciesImageItemList;
            }
            return [];
        }


        /// <summary>
        /// Return the SpeciesItem for a given species code
        /// </summary>
        /// <param name="speciesCode"></param>
        /// <returns></returns>
        public SpeciesItem? GetSpeciesItem(string speciesCode)
        {
            if (speciesStates.TryGetValue(speciesCode, out var state))
            {
                return state.SpeciesItem;
            }
            return null;
        }


        /// <summary>
        /// Remove item from the list and tidy up
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public async Task Remove(string code)
        {
            if (speciesStates.TryGetValue(code, out var item))            
            {
                List<SpeciesImageItem> speciesImageItemList = [.. item.SpeciesImageItemList];
                speciesStates.Remove(code);
                foreach (SpeciesImageItem speciesImageItem in speciesImageItemList)
                {
                    try
                    {
                        StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(Path.GetFileName(speciesImageItem.ImageFile));
                        await file.DeleteAsync();
                    }
                    catch (Exception ex)
                    {
                        // File might not exist — ignore or log if needed
                        report?.Warning("", $"SpeciesImageCache.Remove Failed to delete file:{speciesImageItem.ImageFile}, {ex.Message}");
                    }
                }

                await SaveSpeciesStates();
            }
        }


        /// <summary>
        /// Removes all items from the list and tidies up
        /// Called as RemoveAll() removes everything
        /// </summary>
        /// <returns></returns>
        public async Task RemoveAll()
        {
            int itemCount = speciesStates.Count;

            foreach (var item in speciesStates.ToList())
            {
                await Remove(item.Value.SpeciesItem.Code);
            }

            await SaveSpeciesStates();

            report?.Info("", $"{itemCount} items removed from the Download/Upload list");
        }


        /// <summary>
        /// Called by the SettingWindows to refresh the UI view of the cache
        /// </summary>
        public void RefreshView(bool reset = false)
        {
            if (!isReady) return;

            if (!reset)
            {
                SpeciesStateView.Clear();

                foreach (var state in speciesStates.Values)
                {
                    SpeciesStateView.Add(new SpeciesCacheViewItem
                    {
                        SpeciesItem = state.SpeciesItem,
                        Status = state.Status.ToString(),
                        TotalImages = state.TotalImages,
                        ImageCount = state.SpeciesImageItemList.Count,
                        StatusDate = state.StatusDate?.ToString("yyyy-MM-dd hh:mm:ss"),
                        CreatedDate = state.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss")
                    });
                }
            }
            else
            {
                // Request to free resources
                SpeciesStateView.Clear();
            }
        }


        /// <summary>
        /// Triggers the next timer with a specified delay.
        /// </summary>
        /// <param name="timeSpanNextTimer">The delay before the next timer starts.</param>
        public void TriggerNextTimer(TimeSpan timeSpanNextTimer)
        {
            timer?.Change(timeSpanNextTimer, timerInterval);
        }


        /// <summary>
        /// Overload to trigger the next timer with a default delay of zero.
        /// </summary>
        public void TriggerNextTimer()
        {
            TriggerNextTimer(TimeSpan.Zero);
        }



        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Background timer
        /// </summary>
        /// <returns></returns>
        private async Task TimerCallback()
        {
            if (!isReady) return;
            if (isProcessing) return;

            isProcessing = true;
            try
            {
                CheckForOutstandingSpeciesRequiringImages();
                await ProcessAsync();
            }
            finally
            {
                isProcessing = false;
            }
        }


        /// <summary>
        /// State machine
        /// </summary>
        /// <returns></returns>
        private async Task ProcessAsync()
        {
            try
            {
                // Get the before State totals
                var beforeTotals = CountStates();

                // Run the state machine
                foreach (var state in speciesStates.Values)
                {
                    switch (state.Status)
                    {
                        case State.None:
                            await RequestFirstPage(state);
                            break;

                        case State.WaitingForFirstPhotoPage:
                            CheckFirstPhotoPageDownloaded(state);
                            break;

                        case State.ParsingFirstPhotoPage:
                            await ParseFirstPhotoPage(state);
                            break;

                        case State.RequestingAllPhotoPages:
                            await RequestAllPhotoPages(state);
                            break;

                        case State.WaitingForAllPhotoPages:
                            CheckAllPhotoPageDownloaded(state);
                            break;

                        case State.ParsingAllPhotoPagesAndRequestImages:
                            await ParseAllPhotoPagesAndRequestAllImages(state);
                            break;

                        case State.WaitingForAllImages:
                            CheckAllImagesDownloaded(state);
                            break;
                    }
                }

                // Get the after State totals
                var afterTotals = CountStates();

                if (SettingsManagerLocal.DiagnosticInformation)
                {
                    // Report if changes to an key states
                    int afterTotalsWaitingForFirstPhotoPage = afterTotals[State.WaitingForFirstPhotoPage];
                    int beforeTotalsWaitingForFirstPhotoPage = beforeTotals[State.WaitingForFirstPhotoPage];
                    int afterTotalsParsingFirstPhotoPage = afterTotals[State.ParsingFirstPhotoPage];
                    int beforeTotalsParsingFirstPhotoPage = beforeTotals[State.ParsingFirstPhotoPage];
                    int afterTotalsRequestingAllPhotoPages = afterTotals[State.RequestingAllPhotoPages];
                    int beforeTotalsRequestingAllPhotoPages = beforeTotals[State.RequestingAllPhotoPages];
                    int afterTotalsWaitingForAllPhotoPages = afterTotals[State.WaitingForAllPhotoPages];
                    int beforeTotalsWaitingForAllPhotoPages = beforeTotals[State.WaitingForAllPhotoPages];
                    int afterTotalsParsingAllPhotoPagesAndRequestImages = afterTotals[State.ParsingAllPhotoPagesAndRequestImages];
                    int beforeTotalsParsingAllPhotoPagesAndRequestImages = afterTotals[State.ParsingAllPhotoPagesAndRequestImages];
                    int afterTotalsWaitingForAllImages = afterTotals[State.WaitingForAllImages];
                    int beforeTotalsWaitingForAllImages = beforeTotals[State.WaitingForAllImages];
                    int afterTotalsDone = afterTotals[State.Done];
                    int beforeTotalsDone = beforeTotals[State.Done];

                    if (afterTotalsWaitingForFirstPhotoPage != beforeTotalsWaitingForFirstPhotoPage ||
                        afterTotalsParsingFirstPhotoPage != beforeTotalsParsingFirstPhotoPage ||
                        afterTotalsRequestingAllPhotoPages != beforeTotalsRequestingAllPhotoPages ||
                        afterTotalsWaitingForAllPhotoPages != beforeTotalsWaitingForAllPhotoPages ||
                        afterTotalsParsingAllPhotoPagesAndRequestImages != beforeTotalsParsingAllPhotoPagesAndRequestImages ||
                        afterTotalsWaitingForAllImages != beforeTotalsWaitingForAllImages ||
                        afterTotalsDone != beforeTotalsDone)
                    {
                        report?.Info("", $"Fish Image Cache staqte changes:");
                        report?.Info("", $"    WaitingForFirstPhotoPage:             {beforeTotalsWaitingForFirstPhotoPage} > {afterTotalsWaitingForFirstPhotoPage}");
                        report?.Info("", $"    ParsingFirstPhotoPage:                {beforeTotalsParsingFirstPhotoPage} > {afterTotalsParsingFirstPhotoPage}");
                        report?.Info("", $"    RequestingAllPhotoPages:              {beforeTotalsRequestingAllPhotoPages} > {afterTotalsRequestingAllPhotoPages}");
                        report?.Info("", $"    WaitingForAllPhotoPages:              {beforeTotalsWaitingForAllPhotoPages} > {afterTotalsWaitingForAllPhotoPages}");
                        report?.Info("", $"    ParsingAllPhotoPagesAndRequestImages: {beforeTotalsParsingAllPhotoPagesAndRequestImages} > {afterTotalsParsingAllPhotoPagesAndRequestImages}");
                        report?.Info("", $"    WaitingForAllImages:                  {beforeTotalsWaitingForAllImages} > {afterTotalsWaitingForAllImages}");
                        report?.Info("", $"    Done:                                 {beforeTotalsDone} > {afterTotalsDone}");
                    }
                }

                // Tigger the next timer in 3 secs time
                TriggerNextTimer(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                report?.Error("", $"SpeciesImageCache.ProcessAsync() Failed, {ex.Message}");
            }
            finally
            {
                // Save the persistent list
                await SaveSpeciesStates();
            }
        }


        /// <summary>
        /// Check the speciesCodeList for any species that require images
        /// </summary>
        /// <returns></returns>
        private bool CheckForOutstandingSpeciesRequiringImages()
        {
            bool ret = false;

            foreach (var item in speciesCodeList.SpeciesItems)
            {
                // If we have a code and it is a fishbase code that add to the speciesstates list
                (CodeType codeType, string SpeciesID) = item.ExtractCodeTypeAndID();
                // Only supporting Fishbase.org ID currently
                if (codeType == CodeType.FishBase && !string.IsNullOrEmpty(SpeciesID))
                {                    
                    if (!speciesStates.TryGetValue(item.Code, out SpeciesCacheState? search))
                    { 
                        speciesStates[item.Code] = new SpeciesCacheState { SpeciesItem = item };
                        ret = true; // At least one found
                    }
                }
            }
            

            return ret;
        }


        /// <summary>
        /// Request the first photo first is downloaded. The first page is important because it has the total 
        /// number of photos available which will drive the next set of requests
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        private async Task RequestFirstPage(SpeciesCacheState state)
        {
            string url = MakePhotoPageUrl(state.SpeciesItem.ExtractID(), 1/*page*/);

            var item = internetQueue.Find(url);
            if (item is null)
            {
                // Add a download request
                await internetQueue.AddDownloadRequest(TransferType.Page, url);
                state.Status = State.WaitingForFirstPhotoPage;
            }
            else if (item is not null && item.Status == Status.Downloaded)
            {
                // We already have this page cached, should normally happen but maybe there was a crash
                // so just move the status on
                state.Status = State.WaitingForFirstPhotoPage;
            }
            
        }


        /// <summary>
        /// Check if the requested first page has been downloaded and if so move the state on
        /// </summary>
        /// <param name="state"></param>
        private void CheckFirstPhotoPageDownloaded(SpeciesCacheState state)
        {
            // Record key
            string url = MakePhotoPageUrl(state.SpeciesItem.ExtractID(), 1/*page*/);

            var item = internetQueue.Find(url);
            if (item != null && item.Status == Status.Downloaded)
            {
                state.Status = State.ParsingFirstPhotoPage;
            }
        }


        /// <summary>
        /// Get from the first photo page the total number of photo pages that are available
        /// for this species of fish
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        private async Task ParseFirstPhotoPage(SpeciesCacheState state)
        {
            // Record key
            string url = MakePhotoPageUrl(state.SpeciesItem.ExtractID(), 1/*page*/);

            // Get the record of the download from the Download manager
            var item = internetQueue.Find(url);
            if (item != null)
            {
                // Get the StorageFile of the downloaded page
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(Path.GetFileName(item.LocalFileSpec));

                // Parse the first photo page for this species and extract the image url, genus+species, auther and the
                // total number of photo pages available
                var metadata = await HtmlFishBaseParser.ParseHtmlFishbasePhotoPage(file);
                if (metadata.TotalImages.HasValue)
                {
                    // Remember the total number of photo pages available for this fish species
                    state.TotalImages = metadata.TotalImages.Value;

                    state.Status = State.RequestingAllPhotoPages;
                }

                // Remove the page from the Download Manafer
                await internetQueue.Remove(item);
            }
        }

        /// <summary>
        /// Now we know how many photo pages there are for this species let's request
        /// each page
        /// </summary>
        /// <param name="state"></param>
        private async Task RequestAllPhotoPages(SpeciesCacheState state)
        {
            for (int page = 1; page <= state.TotalImages; page++)
            {
                string url = MakePhotoPageUrl(state.SpeciesItem.ExtractID(), page);
                await internetQueue.AddDownloadRequest(TransferType.Page, url);
            }
            state.Status = State.WaitingForAllPhotoPages;
        }


        /// <summary>
        /// Check to see if all the requested photo pages have been downloaded
        /// </summary>
        /// <param name="state"></param>
        private void CheckAllPhotoPageDownloaded(SpeciesCacheState state)
        {
            bool allDownloaded = true;

            for (int page = 1; page <= state.TotalImages; page++)
            {
                // Record key
                string url = MakePhotoPageUrl(state.SpeciesItem.ExtractID(), page);

                var item = internetQueue.Find(url);
                if (item != null && item.Status != Status.Downloaded)
                {
                    allDownloaded = false;
                    break;
                }
            }

            if (allDownloaded)
                state.Status = State.ParsingAllPhotoPagesAndRequestImages;
        }


        /// <summary>
        /// Now all the photo pages are known to be downloaded and available
        /// parse each one and extact the image url, genus and species and the auther
        /// </summary>
        /// <param name="state"></param>
        private async Task ParseAllPhotoPagesAndRequestAllImages(SpeciesCacheState state)
        {
            for (int page = 1; page <= state.TotalImages; page++)
            {
                // Record key
                string url = MakePhotoPageUrl(state.SpeciesItem.ExtractID(), page);

                // Get the record of the download from the Download manager
                var item = internetQueue.Find(url);
                if (item != null)
                {
                    // Get the StorageFile of the downloaded page
                    StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(item.LocalFileSpec);

                    // Parse the first photo page for this species and extract the image url, genus+species, auther and the
                    // total number of photo pages available
                    var metadata = await HtmlFishBaseParser.ParseHtmlFishbasePhotoPage(file);
                    if (!string.IsNullOrEmpty(metadata.ImageSrc))
                    {
                        var baseUri = new Uri(url);
                        var fullUri = new Uri(baseUri, metadata.ImageSrc);
                        string fullUrl = fullUri.ToString();

                        // Remember the image url, this is the key record used in the following states
                        SpeciesImageItem speciesImageItem = new()
                        {
                            ImageUrl = fullUrl,
                            ImageFile = "",
                            Author = metadata.Author ?? "",
                        };
                        state.SpeciesImageItemList.Add(speciesImageItem);

                        // Request the image file to be downloaded
                        await internetQueue.AddDownloadRequest(TransferType.File, fullUrl);
                    }

                    // Remove the page from the Download Manafer
                    await internetQueue.Remove(item);
                }
            }
            state.Status = State.WaitingForAllImages;
        }


        /// <summary>
        /// Check that all the image files for this species have been downloaded
        /// </summary>
        /// <param name="state"></param>
        private void CheckAllImagesDownloaded(SpeciesCacheState state)
        {
            bool allDownloaded = true;

            for (int index =01; index < state.TotalImages; index++)
            {
                // Record key
                string url = state.SpeciesImageItemList[index].ImageUrl;

                var item = internetQueue.Find(url);
                if (item != null && item.Status != Status.Downloaded)
                {
                    allDownloaded = false;
                    break;
                }
            }

            if (allDownloaded)
            {
                // Setup file name in cache
                for (int index = 0; index < state.TotalImages; index++)
                {
                    // Record key
                    string url = state.SpeciesImageItemList[index].ImageUrl;

                    var item = internetQueue.Find(url);
                    if (item != null)
                    {
                        state.SpeciesImageItemList[index].ImageFile = item.LocalFileSpec;
                    }
                }

                // Mark as done and ready to use
                state.Status = State.Done;
            }
        }


        /// <summary>
        /// Make the Fieshbase.se Url for a species photo page
        /// </summary>
        /// <param name="speciesID"></param>
        /// <param name="page"></param>
        /// <returns></returns>
        private static string MakePhotoPageUrl(string speciesID, int page)
        {
            return $"https://www.fishbase.se/photos/PicturesSummary.php?resultPage={page}&ID={speciesID}&what=species";
        }


        /// <summary>
        /// Totals the number of each State in the speciesStates directory
        /// Used for reporting only
        /// </summary>
        /// <returns></returns>
        private Dictionary<State, int> CountStates()
        {
            var counts = speciesStates
                .Values
                .GroupBy(s => s.Status)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (State state in Enum.GetValues<State>())
                counts.TryAdd(state, 0);

            return counts;
        }


        /// <summary>
        /// Load speciesStates from disk
        /// </summary>
        /// <returns></returns>
        private async Task SaveSpeciesStates()
        {
            string json = System.Text.Json.JsonSerializer.Serialize(speciesStates);
            StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync("speciesStates.json", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, json);
        }


        /// <summary>
        /// Save speciesStates to disk
        /// </summary>
        /// <returns></returns>
        private async Task LoadSpeciesStates()
        {
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync("speciesStates.json");

                var properties = await file.GetBasicPropertiesAsync();
                if (properties.Size == 0)
                {
                    // File exists but is empty — delete it and skip
                    await file.DeleteAsync();
                    Debug.WriteLine($"SpeciesImageCache.LoadSpeciesStates  Zero byte 'speciesStates.json' found. Deleting file.");
                    return;
                }

                string json = await FileIO.ReadTextAsync(file);

                var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, SpeciesCacheState>>(json);

                if (loaded != null)
                {
                    foreach (var (key, value) in loaded)
                    {
                        speciesStates[key] = value;
                    }
                }
            }
            catch (FileNotFoundException)
            {
                Debug.WriteLine($"SpeciesImageCache.LoadSpeciesStates  No 'speciesStates.json' found.");
                // No cache found — skip
            }
        }



        // *** End of SpeciesImageCache class
    }

}
