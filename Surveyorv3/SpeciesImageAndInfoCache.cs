// SpeciesImageAndInfoCache  Mananges the cached species image file and species information
//
// Version 1.0 11 Apr 2025
//
// Version 1.1 24 Apr 2025
// Rename s from SpeciesImageCache to SpeciesImageAndInfoCache
// Added Environment, Distribtuion and SpeciesSize information
// and calculate a hash and use later to check for updates (update checking not yet implimented)


using Emgu.CV.Flann;
using Surveyor.Helper;
using Surveyor.User_Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using static Surveyor.SpeciesItem;

namespace Surveyor
{

    /// <summary>
    /// UI view of the cache record
    /// </summary>
    public class SpeciesCacheViewItem
    {
        public string Family => SpeciesItem.Family;
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



    internal class SpeciesImageAndInfoCache
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
            RequestingSpeciesInformationPage,
            WaitingForSpeciesInformationPage,
            ParsingSpeciesInformationPage,
            Done,
            Error
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

            // Created date
            public DateTime CreatedDate { get; }

            // Unique hash of the data fields (Environment, Distribution, SpeciesSize and the contains of the image files)
            public string Hash { get; set; } = "";

            // List of image URL, that corresponding local file name donwload status and date/time)
            public List<SpeciesImageItem> SpeciesImageItemList { get; set; } = [];

            // Other information extracted and cached
            public string Environment { get; set; } = "";
            public string Distribution { get; set; } = "";
            public string SpeciesSize { get; set; } = "";
        }

        public class SpeciesImageItem
        {
            public string Source { get; set; } = "";
            public string ImageUrl { get; set; } = "";
            public string ImageFile { get; set; } = "";
            public string Author { get; set; } = "";
            public string GenusSpecies { get; set; } = "";
        }


        // View call to bind to (updated via the RefreshView() method)
        public ObservableCollection<SpeciesCacheViewItem> SpeciesStateView { get; } = [];

        public SpeciesImageAndInfoCache(SpeciesCodeList _speciesCodeList, InternetQueue _internetQueue, Reporter? _report)
        {
            speciesCodeList = _speciesCodeList;
            internetQueue = _internetQueue;
            report = _report;
        }

        // Add a private static readonly field to cache the JsonSerializerOptions instance
        private static readonly JsonSerializerOptions CachedJsonSerializerOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

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
        /// Return the SpeciesCacheState for the indicated code or null if not found
        /// </summary>
        /// <param name="speciesCode"></param>
        /// <returns></returns>
        public (string, string, string) GetInfo(string speciesCode)
        {
            if (speciesStates.TryGetValue(speciesCode, out var speciesCacheState))
            {
                return (speciesCacheState.Environment, speciesCacheState.Distribution, speciesCacheState.SpeciesSize);
            }
            return ("","","");
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
                if (state.Status == State.Done) // Image all downloaded
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
                        if (!string.IsNullOrEmpty(speciesImageItem.ImageFile))
                        {
                            StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(speciesImageItem.ImageFile);
                            await file.DeleteAsync();
                        }
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

            try
            {
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
            catch { }

        }

        /// <summary>
        /// Total the number of images for each cache record that have been downloaded
        /// </summary>
        /// <returns></returns>
        public int TotalImagesAvailable()
        {
            // Sum the number of species image items where imageFile is not empty
            int totalSpeciesImageItems = speciesStates.Values
                .SelectMany(state => state.SpeciesImageItemList)
                .Count(item => !string.IsNullOrEmpty(item.ImageFile));

            return totalSpeciesImageItems;
        }


        /// <summary>
        /// Total the number of images that are required by every cache record
        /// </summary>
        /// <returns></returns>
        public int TotalImagesRequired()
        {
            int totalImages = speciesStates.Values.Sum(state => state.TotalImages);

            return totalImages;
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
                //var beforeTotals = CountStates();

                // Run the state machine
                foreach (var state in speciesStates.Values)
                {
                    // Check for exit request
                    if (!timerRunning)
                        return;

                    try
                    {

                        switch (state.Status)
                        {
                            case State.None:
                                await RequestFirstPage(state);
                                break;

                            case State.WaitingForFirstPhotoPage:
                                WaitingForFirstPhotoPage(state);
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
                                await WaitingForAllImages(state);
                                break;

                            case State.RequestingSpeciesInformationPage:
                                RequestingSpeciesInformationPage(state);
                                break;

                            case State.WaitingForSpeciesInformationPage:
                                WaitingForSpeciesInformationPage(state);
                                break;

                            case State.ParsingSpeciesInformationPage:
                                ParsingSpeciesInformationPage(state);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {       
                        string reportMessage = $"SpeciesImageCache.ProcessAsync() Failed, Species:{state.SpeciesItem.Species}, Status:{state.Status}({state.Status.ToString()}) {ex.Message}";
                        report?.Error("", reportMessage);
                        Debug.WriteLine(reportMessage);
                    }
                }

                // Get the after State totals
                //var afterTotals = CountStates();

                //if (SettingsManagerLocal.DiagnosticInformation)
                //{
                //    // Report if changes to an key states
                //    int afterTotalsWaitingForFirstPhotoPage = afterTotals[State.WaitingForFirstPhotoPage];
                //    int beforeTotalsWaitingForFirstPhotoPage = beforeTotals[State.WaitingForFirstPhotoPage];
                //    int afterTotalsParsingFirstPhotoPage = afterTotals[State.ParsingFirstPhotoPage];
                //    int beforeTotalsParsingFirstPhotoPage = beforeTotals[State.ParsingFirstPhotoPage];
                //    int afterTotalsRequestingAllPhotoPages = afterTotals[State.RequestingAllPhotoPages];
                //    int beforeTotalsRequestingAllPhotoPages = beforeTotals[State.RequestingAllPhotoPages];
                //    int afterTotalsWaitingForAllPhotoPages = afterTotals[State.WaitingForAllPhotoPages];
                //    int beforeTotalsWaitingForAllPhotoPages = beforeTotals[State.WaitingForAllPhotoPages];
                //    int afterTotalsParsingAllPhotoPagesAndRequestImages = afterTotals[State.ParsingAllPhotoPagesAndRequestImages];
                //    int beforeTotalsParsingAllPhotoPagesAndRequestImages = afterTotals[State.ParsingAllPhotoPagesAndRequestImages];
                //    int afterTotalsWaitingForAllImages = afterTotals[State.WaitingForAllImages];
                //    int beforeTotalsWaitingForAllImages = beforeTotals[State.WaitingForAllImages];
                //    int afterTotalsDone = afterTotals[State.Done];
                //    int beforeTotalsDone = beforeTotals[State.Done];

                //    if (afterTotalsWaitingForFirstPhotoPage != beforeTotalsWaitingForFirstPhotoPage ||
                //        afterTotalsParsingFirstPhotoPage != beforeTotalsParsingFirstPhotoPage ||
                //        afterTotalsRequestingAllPhotoPages != beforeTotalsRequestingAllPhotoPages ||
                //        afterTotalsWaitingForAllPhotoPages != beforeTotalsWaitingForAllPhotoPages ||
                //        afterTotalsParsingAllPhotoPagesAndRequestImages != beforeTotalsParsingAllPhotoPagesAndRequestImages ||
                //        afterTotalsWaitingForAllImages != beforeTotalsWaitingForAllImages ||
                //        afterTotalsDone != beforeTotalsDone)
                //    {
                //        report?.Info("", $"Fish Image Cache staqte changes:");
                //        report?.Info("", $"    WaitingForFirstPhotoPage:             {beforeTotalsWaitingForFirstPhotoPage} > {afterTotalsWaitingForFirstPhotoPage}");
                //        report?.Info("", $"    ParsingFirstPhotoPage:                {beforeTotalsParsingFirstPhotoPage} > {afterTotalsParsingFirstPhotoPage}");
                //        report?.Info("", $"    RequestingAllPhotoPages:              {beforeTotalsRequestingAllPhotoPages} > {afterTotalsRequestingAllPhotoPages}");
                //        report?.Info("", $"    WaitingForAllPhotoPages:              {beforeTotalsWaitingForAllPhotoPages} > {afterTotalsWaitingForAllPhotoPages}");
                //        report?.Info("", $"    ParsingAllPhotoPagesAndRequestImages: {beforeTotalsParsingAllPhotoPagesAndRequestImages} > {afterTotalsParsingAllPhotoPagesAndRequestImages}");
                //        report?.Info("", $"    WaitingForAllImages:                  {beforeTotalsWaitingForAllImages} > {afterTotalsWaitingForAllImages}");
                //        report?.Info("", $"    Done:                                 {beforeTotalsDone} > {afterTotalsDone}");
                //    }
                //}

                // Tigger the next timer in 3 secs time
                if (timerRunning)
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
            // Make the URL we need to download
            string url = MakePhotoPageUrl(state.SpeciesItem.ExtractID(), 1/*page*/);

            // Queue this page for download if not already in the queue or downloaded
            await internetQueue.AddDownloadRequestIfNecessary(TransferType.Page, url, "temp");  // Intermediate file so put in localFolder/temp
            state.Status = State.WaitingForFirstPhotoPage;
        }


        /// <summary>
        /// Check if the requested first page has been downloaded and if so move the state on
        /// </summary>
        /// <param name="state"></param>
        private void WaitingForFirstPhotoPage(SpeciesCacheState state)
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
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(item.RelativeLocalFileSpec);

                // Parse the first photo page for this species and extract the image url, genus+species, auther and the
                // total number of photo pages available
                var metadata = HtmlFishBaseParser.ParseHtmlFishbasePhotoPage(file.Path);
                if (metadata.TotalImages.HasValue)
                {
                    // Remember the total number of photo pages available for this fish species
                    state.TotalImages = metadata.TotalImages.Value;

                    state.Status = State.RequestingAllPhotoPages;
                }
                else
                {
                    // There are no images for this fish species on FishBase to jump to 
                    // getting species information
                    state.TotalImages = 0;
                    state.Status = State.RequestingSpeciesInformationPage;
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
                await internetQueue.AddDownloadRequestIfNecessary(TransferType.Page, url, "temp");  // Intermediate file so put in localFolder/temp
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
            bool setErrorState = false;

            for (int page = 1; page <= state.TotalImages; page++)
            {
                // Record key
                string url = MakePhotoPageUrl(state.SpeciesItem.ExtractID(), page);

                var item = internetQueue.Find(url);
                if (item is not null && item.Status != Status.Downloaded)
                {
                    allDownloaded = false;
                    break;
                }
                else if (item is null)
                {
                    string reportMessage = $"SpeciesImageAndInfoCache.CheckAllPhotoPageDownloaded Download request missing from internet queue. Suggest removing the FishID:{state.SpeciesItem.Code} from the Species Image Cache";
                    //???Debug.WriteLine(reportMessage);
                    report?.Warning("", reportMessage);
                    allDownloaded = false;
                    setErrorState = true;
                    break;
                }
            }

            if (allDownloaded)
                state.Status = State.ParsingAllPhotoPagesAndRequestImages;

            // Error the species
            if (setErrorState)
            {
                state.Status = State.Error;
            }

        }


        /// <summary>
        /// Now all the photo pages are known to be downloaded and available
        /// parse each one and extact the image url, genus and species and the auther
        /// </summary>
        /// <param name="state"></param>
        private async Task ParseAllPhotoPagesAndRequestAllImages(SpeciesCacheState state)
        {
            // Clear the image list (should be empty but just in case)
            state.SpeciesImageItemList.Clear();

            // Loop through the total image count and request each image
            for (int page = 1; page <= state.TotalImages; page++)
            {
                // Record key
                string url = MakePhotoPageUrl(state.SpeciesItem.ExtractID(), page);

                // Get the record of the download from the Download manager
                var item = internetQueue.Find(url);
                if (item != null)
                {
                    // Get the StorageFile of the downloaded page
                    StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(item.RelativeLocalFileSpec);  // Get the location

                    // Parse the first photo page for this species and extract the image url, genus+species, auther and the
                    // total number of photo pages available
                    var metadata =  HtmlFishBaseParser.ParseHtmlFishbasePhotoPage(file.Path);
                    
                    if (!string.IsNullOrEmpty(metadata.ImageSrc))
                    {
                        var baseUri = new Uri(url);
                        var fullUri = new Uri(baseUri, metadata.ImageSrc);
                        string fullImageUrl = fullUri.ToString();

                        // Remember the image url, this is the key record used in the following states
                        SpeciesImageItem speciesImageItem = new()
                        {
                            ImageUrl = fullImageUrl,
                            ImageFile = "",
                            Author = metadata.Author ?? "",
                        };
                        state.SpeciesImageItemList.Add(speciesImageItem);

                        // Request the image file to be downloaded
                        await internetQueue.AddDownloadRequestIfNecessary(TransferType.File, fullImageUrl, "ImageCache");  // Image file to place in the localFolder/ImageCache
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
        private async Task WaitingForAllImages(SpeciesCacheState state)
        {
            bool setErrorState = false;

            // Check the SpeciesImageItemList size matches the indicated TotalImages
            if (state.SpeciesImageItemList.Count == state.TotalImages)
            {
                // Assume all ok until we find a problem
                bool allDownloaded = true;

                // Check if all pages have downloaded
                for (int index = 0; index < state.TotalImages; index++)
                {
                    // Record key
                    string url = state.SpeciesImageItemList[index].ImageUrl;

                    var item = internetQueue.Find(url);
                    if (item != null && item.Status != Status.Downloaded)
                    {
                        allDownloaded = false;
                        break;
                    }
                    else if (item is null)
                    {
                        string reportMessage = $"SpeciesImageAndInfoCache.CheckAllImagesDownloaded Download request missing from internet queue. Suggest removing the FishID:{state.SpeciesItem.Code} from the Species Image Cache";
                        //???Debug.WriteLine(reportMessage);
                        report?.Warning("", reportMessage);
                        allDownloaded = false;
                        setErrorState = true;
                        break;
                    }

                }

                // If all downloaded ok proceed
                if (allDownloaded)
                {
                    // Write the file name the page was downloaded as in cache record
                    for (int index = 0; index < state.TotalImages; index++)
                    {
                        // Record key
                        string url = state.SpeciesImageItemList[index].ImageUrl;

                        var item = internetQueue.Find(url);
                        if (item != null)
                        {
                            state.SpeciesImageItemList[index].ImageFile = item.RelativeLocalFileSpec;
                        }
                    }

                    // Calculate hash (this is so we can check for updates to the page later)
                    state.Hash = await ComputeHashAsync(state);

                    // Move to the state of requesting a species info page
                    state.Status = State.RequestingSpeciesInformationPage;
                }

                // Error the species
                if (setErrorState)
                {
                    state.Status = State.Error;
                }
            }
            else
            {                
                string reportText = $"Expected {state.TotalImages} images, but found {state.SpeciesImageItemList.Count}";
                report?.Error("", $"SpeciesImageCache.CheckAllImagesDownloaded {reportText}");
                Debug.WriteLine($"SpeciesImageCache.CheckAllImagesDownloaded {reportText}");
                state.Status = State.Error;
            }
        }


        /// <summary>
        /// Request the species summary page to get Environment, Distribution and SpeciesSize
        /// </summary>
        /// <param name="state"></param>
        private async void RequestingSpeciesInformationPage(SpeciesCacheState state)
        {
            string url = MakeSummaryPageUrl(state.SpeciesItem.ExtractID());

            // Add a download request
            await internetQueue.AddDownloadRequestIfNecessary(TransferType.Page, url, "temp");   // Intermediate file so put in localFolder/temp
            state.Status = State.WaitingForSpeciesInformationPage;
        }


        /// <summary>
        /// Wait for the species summary page to be downloaded
        /// </summary>
        /// <param name="state"></param>
        private void WaitingForSpeciesInformationPage(SpeciesCacheState state)
        {
            // Record key
            string url = MakeSummaryPageUrl(state.SpeciesItem.ExtractID());

            var item = internetQueue.Find(url);
            if (item != null && item.Status == Status.Downloaded)
            {
                state.Status = State.ParsingSpeciesInformationPage;
            }

        }

        private async void ParsingSpeciesInformationPage(SpeciesCacheState state)
        {
            // Record key
            string url = MakeSummaryPageUrl(state.SpeciesItem.ExtractID());

            // Get the record of the download from the Download manager
            var item = internetQueue.Find(url);
            if (item != null)
            {
                // Get the StorageFile of the downloaded page
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(item.RelativeLocalFileSpec);

                // Parse the species summary and extract species information for storage int the cache
                var metadata = await HtmlFishBaseParser.ParseHtmlFishbaseSummaryAndExtractSpeciesMetadata(file.Path);

                // Environment
                state.Environment = metadata.Environment ?? "";
                // Distribution
                state.Distribution = metadata.Distribution ?? "";
                // SpeciesSize
                state.SpeciesSize = metadata.SpeciesSize ?? "";

                // Calculate hash
                state.Hash = await ComputeHashAsync(state);

                // Remove the page from the Download Manafer
                await internetQueue.Remove(item);
            }


            // Mark as done and ready to use
            state.Status = State.Done;

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
        /// Make the Fieshbase.se Url for a species summary page
        /// </summary>
        /// <param name="speciesID"></param>
        /// <returns></returns>
        private static string MakeSummaryPageUrl(string speciesID)
        {
            return $"https://www.fishbase.se/summary/{speciesID}";
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
            try
            {
                string json = JsonSerializer.Serialize(speciesStates, CachedJsonSerializerOptions);

                // Write to a temporary file first
                string tempFileName = "speciesStates_tmp.json";
                StorageFile tempFile = await ApplicationData.Current.LocalFolder.CreateFileAsync(tempFileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(tempFile, json);

                // Flush is implicit in FileIO.WriteTextAsync, but you could optionally verify file size if needed

                // Rename the temp file to the actual file (replace if it exists)
                await tempFile.RenameAsync("speciesStates.json", NameCollisionOption.ReplaceExisting);
            }
            catch (Exception ex)
            {
                report?.Error("", $"SpeciesImageCache.SaveSpeciesStates (temp-write strategy) failed: {ex.Message}");
            }
        }

        //private async Task SaveSpeciesStates()
        //{
        //    try
        //    {
        //        string json = System.Text.Json.JsonSerializer.Serialize(speciesStates, CachedJsonSerializerOptions);
        //        StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync("speciesStates.json", CreationCollisionOption.ReplaceExisting);
        //        await FileIO.WriteTextAsync(file, json);
        //    }
        //    catch (Exception ex)
        //    {
        //        report?.Error("", $"SpeciesImageCache.SaveSpeciesStates Failed {ex.Message}");
        //    }
        //}



        /// <summary>
        /// Save speciesStates to disk
        /// </summary>
        /// <returns></returns>
        private async Task LoadSpeciesStates()
        {
            try
            {
                IStorageItem item = await ApplicationData.Current.LocalFolder.TryGetItemAsync("speciesStates.json");
                if (item is not StorageFile file)
                {
                    Debug.WriteLine("SpeciesImageCache.LoadSpeciesStates  No 'speciesStates.json' found.");
                    return;
                }

                var properties = await file.GetBasicPropertiesAsync();
                if (properties.Size == 0)
                {
                    // File exists but is empty — delete it and skip
                    await file.DeleteAsync();
                    report?.Warning("", $"SpeciesImageCache.LoadSpeciesStates  Zero byte 'speciesStates.json' found. Deleting file.");
                    return;
                }

                string json = await FileIO.ReadTextAsync(file);

                var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, SpeciesCacheState>>(json, CachedJsonSerializerOptions);

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
                report?.Warning("", $"SpeciesImageCache.LoadSpeciesStates  No 'speciesStates.json' found.");
                // No cache found — skip
            }
            catch (Exception ex)
            {
                report?.Error("", $"SpeciesImageCache.LoadSpeciesStates Failed {ex.Message}");
            }
        }


        /// <summary>
        /// Compute a unique hash for the species cache state.  This is stores and 
        /// used to check for updates
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        private static async Task<string> ComputeHashAsync(SpeciesCacheState state)
        {
            var sb = new StringBuilder();

            // Append core properties
            sb.Append(state.Environment);
            sb.Append(state.Distribution);
            sb.Append(state.SpeciesSize);

            // Append file contents (or empty string if file doesn't exist)
            foreach (var item in state.SpeciesImageItemList)
            {
                try
                {
                    StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(item.ImageFile);
                    IBuffer buffer = await FileIO.ReadBufferAsync(file);

                    byte[] fileBytes;
                    using (var dataReader = DataReader.FromBuffer(buffer))
                    {
                        fileBytes = new byte[buffer.Length];
                        dataReader.ReadBytes(fileBytes);
                    }

                    string fileHash = Convert.ToBase64String(SHA256.HashData(fileBytes));
                    sb.Append(fileHash);
                }
                catch (FileNotFoundException)
                {
                    sb.Append("MISSING_FILE");
                }
            }

            // Convert final string to hash
            byte[] combinedBytes = Encoding.UTF8.GetBytes(sb.ToString());
            byte[] hashBytes = SHA256.HashData(combinedBytes);

            return Convert.ToBase64String(hashBytes);
        }

        // *** End of SpeciesImageCache class
    }

}
