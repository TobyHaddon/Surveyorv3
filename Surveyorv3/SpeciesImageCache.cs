// SpeciesImageCache  Mananges the cached species image file
//
// Version 1.0 11 Apr 2025

using Surveyor.User_Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using static Surveyor.SpeciesItem;

namespace Surveyor
{
    internal class SpeciesImageCache
    {
        private readonly SpeciesCodeList speciesCodeList;
        private readonly DownloadUploadManager downloadManager;
        private readonly Reporter? report;
        private readonly Dictionary<string, SpeciesCacheState> speciesStates = new();

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
            public required SpeciesItem SpeciesItem { get; set; }
            public State CurrentState { get; set; } = State.None;
            public int TotalImages { get; set; } = 0;
            public List<string> ImageUrlList { get; set; } = [];
            public List<string> ImageFileList { get; set; } = [];
            public DateOnly? DownloadDate { get; set; } = null;
        }


        /// <summary>
        /// UI view of the cache record
        /// </summary>
        public class SpeciesCacheViewItem
        {
            public string Genus => SpeciesItem.Genus;
            public string Species => SpeciesItem.Species;
            public string Code => SpeciesItem.Code;
            public string CurrentState { get; set; } = "";
            public int TotalImages { get; set; }
            public int ImageCount { get; set; }
            public string? DownloadDate { get; set; }
            public SpeciesItem SpeciesItem { get; set; } = default!;
        }

        // View call to bind to (updated via the RefreshView() method)
        public ObservableCollection<SpeciesCacheViewItem> SpeciesStateView { get; } = [];

        public SpeciesImageCache(SpeciesCodeList _speciesCodeList, DownloadUploadManager _downloadManager, Reporter? _report)
        {
            speciesCodeList = _speciesCodeList;
            downloadManager = _downloadManager;
            report = _report;
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
                    timer = new Timer(async _ => await TimerCallback(), null, TimeSpan.Zero, timerInterval);
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
        /// Call to shutdown
        /// </summary>
        /// <returns></returns>
        public async Task Unload()
        {
            Enable(false);

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

            timer?.Dispose();
            timer = null;
        }


        /// <summary>
        /// Returns a list of image file name for a given species that are stored in local storage
        /// </summary>
        /// <param name="speciesCode"></param>
        /// <returns></returns>
        public List<string> GetImagesForSpecies(string speciesCode)
        {
            if (speciesStates.TryGetValue(speciesCode, out var state))
            {
                return state.ImageFileList;
            }
            return new List<string>();
        }


        /// <summary>
        /// Called by the SettingWindows to refresh the UI view of the cache
        /// </summary>
        public void RefreshView()
        {
            SpeciesStateView.Clear();
            foreach (var state in speciesStates.Values)
            {
                SpeciesStateView.Add(new SpeciesCacheViewItem
                {
                    SpeciesItem = state.SpeciesItem,
                    CurrentState = state.CurrentState.ToString(),
                    TotalImages = state.TotalImages,
                    ImageCount = state.ImageFileList.Count,
                    DownloadDate = state.DownloadDate?.ToString("yyyy-MM-dd")
                });
            }
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
            foreach (var state in speciesStates.Values)
            {
                switch (state.CurrentState)
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
                (CodeType codeType, string code) = item.GetCodeTypeAndCode();
                if (codeType == CodeType.Fishbase && !string.IsNullOrEmpty(code))
                {
                    // Now check in the species cache for this species
                    SpeciesCacheState search = speciesStates[code];

                    if (search is null)
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
            string url = MakePhotoPageUrl(state.SpeciesItem.Code, 1/*page*/);

            await downloadManager.AddDownloadRequest(TransferType.Page, url);
            state.CurrentState = State.WaitingForFirstPhotoPage;
        }


        /// <summary>
        /// Check if the requested first page has been downloaded and if so move the state on
        /// </summary>
        /// <param name="state"></param>
        private void CheckFirstPhotoPageDownloaded(SpeciesCacheState state)
        {
            // Record key
            string url = MakePhotoPageUrl(state.SpeciesItem.Code, 1/*page*/);

            var item = downloadManager.Find(url);
            if (item != null && item.Status == Status.Downloaded)
            {
                state.CurrentState = State.ParsingFirstPhotoPage;
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
            string url = MakePhotoPageUrl(state.SpeciesItem.Code, 1/*page*/);

            // Get the record of the download from the Download manager
            var item = downloadManager.Find(url);
            if (item != null)
            {
                // Get the StorageFile of the downloaded page
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(Path.GetFileName(item.LocalFileSpec));

                // Parse the first photo page for this species and extract the image url, genus+species, auther and the
                // total number of photo pages available
                var metadata = await HtmlParser.ParseHtmlFishbasePhotoPage(file);
                if (metadata.TotalImages.HasValue)
                {
                    // Remember the total number of photo pages available for this fish species
                    state.TotalImages = metadata.TotalImages.Value;

                    state.CurrentState = State.RequestingAllPhotoPages;
                }

                // Remove the page from the Download Manafer
                await downloadManager.Remove(item);
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
                string url = MakePhotoPageUrl(state.SpeciesItem.Code, page);
                await downloadManager.AddDownloadRequest(TransferType.Page, url);
            }
            state.CurrentState = State.WaitingForAllPhotoPages;
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
                string url = MakePhotoPageUrl(state.SpeciesItem.Code, page);

                var item = downloadManager.Find(url);
                if (item != null && item.Status != Status.Downloaded)
                {
                    allDownloaded = false;
                    break;
                }
            }

            if (allDownloaded)
                state.CurrentState = State.ParsingAllPhotoPagesAndRequestImages;
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
                string url = MakePhotoPageUrl(state.SpeciesItem.Code, page);

                // Get the record of the download from the Download manager
                var item = downloadManager.Find(url);
                if (item != null)
                {
                    // Get the StorageFile of the downloaded page
                    StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(Path.GetFileName(item.LocalFileSpec));

                    // Parse the first photo page for this species and extract the image url, genus+species, auther and the
                    // total number of photo pages available
                    var metadata = await HtmlParser.ParseHtmlFishbasePhotoPage(file);
                    if (!string.IsNullOrEmpty(metadata.ImageSrc))
                    {
                        var baseUri = new Uri(url);
                        var fullUri = new Uri(baseUri, metadata.ImageSrc);
                        string fullUrl = fullUri.ToString();

                        // Remember the image url, this is the key record used in the following states
                        state.ImageUrlList.Add(fullUrl);
                        state.ImageFileList.Add("");

                        // Request the image file to be downloaded
                        await downloadManager.AddDownloadRequest(TransferType.File, fullUrl);
                    }

                    // Remove the page from the Download Manafer
                    await downloadManager.Remove(item);
                }
            }
            state.CurrentState = State.WaitingForAllImages;
        }


        /// <summary>
        /// Check that all the image files for this species have been downloaded
        /// </summary>
        /// <param name="state"></param>
        private void CheckAllImagesDownloaded(SpeciesCacheState state)
        {
            bool allDownloaded = true;

            for (int page = 1; page <= state.TotalImages; page++)
            {
                // Record key
                string url = state.ImageUrlList[page];

                var item = downloadManager.Find(url);
                if (item != null && item.Status != Status.Downloaded)
                {
                    allDownloaded = false;
                    break;
                }
            }

            if (allDownloaded)
            {
                // Setup file name in cache
                for (int page = 1; page <= state.TotalImages; page++)
                {
                    // Record key
                    string url = state.ImageUrlList[page];

                    var item = downloadManager.Find(url);
                    if (item != null)
                    {
                        state.ImageUrlList[page] = item.LocalFileSpec;

                    }
                }
                // Remember the date this was downloaded
                state.DownloadDate = DateOnly.FromDateTime(DateTime.Now);

                // Mark as done and ready to use
                state.CurrentState = State.Done;
            }
        }





        /// <summary>
        /// Make the Fieshbase.se Url for a species photo page
        /// </summary>
        /// <param name="speciesID"></param>
        /// <param name="page"></param>
        /// <returns></returns>
        private string MakePhotoPageUrl(string speciesID, int page)
        {
            return $"https://www.fishbase.se/photos/PicturesSummary.php?resultPage={page}&ID={speciesID}&what=species";
        }
    }



    public class HtmlImageMetadata
    {
        public string? ImageSrc { get; set; }
        public string? GenusSpecies { get; set; }
        public string? Author { get; set; }
        public int? TotalImages { get; set; }
    }

    public class HtmlParser
    {
        /// <summary>
        /// Extract the iamge url, the genus and species, the auther and the total number of images available for this species
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public static async Task<HtmlImageMetadata> ParseHtmlFishbasePhotoPage(StorageFile file)
        {
            string content = await FileIO.ReadTextAsync(file);

            var result = new HtmlImageMetadata();

            // Extract image src from <!--image section-->
            var imageMatch = Regex.Match(content, "<!--image section-->.*?<img[^>]*src=\"(.*?)\"", RegexOptions.Singleline);
            if (imageMatch.Success)
            {
                result.ImageSrc = imageMatch.Groups[1].Value;
            }

            // Extract genusSpecies in <i> inside <!--image caption section-->
            var genusMatch = Regex.Match(content, @"<!--image caption section-->.*?<i>(.*?)</i>", RegexOptions.Singleline);
            if (genusMatch.Success)
            {
                result.GenusSpecies = genusMatch.Groups[1].Value;
            }

            // Extract author in <a> after 'by' inside <!--image caption section-->
            var authorMatch = Regex.Match(content, @"<!--image caption section-->.*?by\s*<a[^>]*>(.*?)</a>", RegexOptions.Singleline);
            if (authorMatch.Success)
            {
                result.Author = authorMatch.Groups[1].Value;
            }

            // Extract totalImages from <!--page navigation--> x of n
            var totalMatch = Regex.Match(content, @"<!--page navigation-->.*?(\d+)\s+of\s+(\d+)", RegexOptions.Singleline);
            if (totalMatch.Success)
            {
                result.TotalImages = int.Parse(totalMatch.Groups[2].Value);
            }

            return result;
        }


        /// <summary>
        /// Extract the SpeciesID from a species summary page the SpeciesID is found by
        /// finding the speccode= value in the page
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public static async Task<int?> ParseHtmlFishbaseSummaryAndExtractSpeciesId(StorageFile file)
        {
            string content = await FileIO.ReadTextAsync(file);

            // Match common speccode= pattern, found in many links and parameters
            var speccodeMatch = Regex.Match(content, @"speccode=(\d+)", RegexOptions.IgnoreCase);
            if (speccodeMatch.Success && int.TryParse(speccodeMatch.Groups[1].Value, out int speciesId))
            {
                return speciesId;
            }

            return null;
        }
    }
}
