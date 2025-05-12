// InternetQueue  Handle the list of requested download and upload pages and files
//
// Version 1.0 11 Apr 2025
//
// Version 1.1 13 Apr 2025
// Renames from DownloadUploadManager to InternetQueue


using Surveyor.User_Controls;
using Surveyor.Helper;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using static Surveyor.User_Controls.SurveyorTesting;
using System.Net;
using System.Text.Json.Serialization;
using System.Text.Json;



namespace Surveyor
{
    public enum Direction { Upload, Download }
    public enum TransferType { Page, File }
    public enum Status { Required, Downloaded, Uploaded, Failed }

    // Item in the internet queue
    public class InternetQueueItem
    {
        // Backing field for Status
        private Status _status;

        // Constructor sets CreatedDate
        public InternetQueueItem()
        {
            CreatedDate = DateTime.Now;
        }

        // Download or Upload
        public Direction Direction { get; set; }

        // Page or File
        public TransferType Type { get; set; }

        // Url to download from/upload to
        public required string URL { get; set; }

        // Current status of the request, with timestamp update
        public Status Status
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

        // Time of the last status change (readonly outside)
        public DateTime? StatusDate { get; private set; } = null;

        // File name (only) in local storage
        public string RelativeLocalFileSpec { get; set; } = "";

        // Priority of this request
        public Priority Priority { get; set; }

        // Creation time, set once
        public DateTime CreatedDate { get; }
    }


    /// <summary>
    /// UI view of the download/upload list record
    /// </summary>
    public class InternetQueueViewItem
    {
        public Direction Direction { get; set; }
        public TransferType TransferType { get; set; }
        public Status Status { get; set; }
        public Priority Priority { get; set; }
        public string LocalFileSpec { get; set; } = "";
        public string StatusDate { get; set; } = "";
        public string URL { get; set; } = "";
        public string CreatedDate { get; set; } = "";
    }


    public class InternetQueue
    {
        private Reporter? Report { get; set; } = null;

        private const int maxSessions = 2;
        private int sessionCount = 0;
        private readonly List<InternetQueueItem> transferItems = [];
        private readonly SemaphoreSlim semaphore = new(maxSessions);
        private readonly HttpClient httpClient = new();
        private readonly string storageFile = "InternetQueue.json";
        private bool isReady = false;
        private bool isProcessing = false;
        private readonly SemaphoreSlim saveLock = new(1, 1);
        private readonly SemaphoreSlim transferItemsLock = new SemaphoreSlim(1, 1);


        // View call to bind to (updated via the RefreshView() method)
        public ObservableCollection<InternetQueueViewItem> InternetQueueView { get; } = [];


        public InternetQueue(Reporter? _report)
        {
            Report = _report;
        }


        /// <summary>
        /// Event that fires when internet activity starts (true) or stops (false).
        /// </summary>
        public event EventHandler<bool>? InternetActivityChanged;



        // Add a private static readonly field to cache the JsonSerializerOptions instance
        private static readonly JsonSerializerOptions CachedJsonSerializerOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        // Update the Load method to use the cached JsonSerializerOptions instance
        public async Task Load()
        {
            try
            {
                // Get StoreageItem
                IStorageItem item = await ApplicationData.Current.LocalFolder.TryGetItemAsync(storageFile);
                if (item is not StorageFile file)
                {
                    Debug.WriteLine($"InternetQueue.Load  No '{storageFile}' found.");
                    isReady = true;
                    return;
                }

                // Check if data file is zero bytes. If so delete.
                var fileProperties = await file.GetBasicPropertiesAsync();
                if (fileProperties.Size == 0)
                {
                    Debug.WriteLine($"InternetQueue.Load '{storageFile}' is zero-length, deleting...");
                    await file.DeleteAsync();

                    // Mark the class as ready
                    isReady = true;
                    return;
                }

                // Read the json data file from the local folder
                string json = await FileIO.ReadTextAsync(file);
                transferItems.Clear();

                // Parse the json
                List<InternetQueueItem>? transferItemList = System.Text.Json.JsonSerializer.Deserialize<List<InternetQueueItem>>(json, CachedJsonSerializerOptions);

                if (transferItemList is not null)
                {
                    transferItems.AddRange(transferItemList);
                    Report?.Info("", $"InternetQueue {transferItems.Count} item(s) loaded from disk");
                }

                // Mark the class as ready
                isReady = true;
            }
            catch (FileNotFoundException)
            {
                Report?.Warning("", $"InternetQueue.Load {storageFile} not found, not necessarily a problem, maybe first time the application has been ran.");
            }
            catch (Exception ex)
            {
                Report?.Error("", $"InternetQueue.Load Failed, {ex.Message}");
            }
        }


        /// <summary>
        /// Save the presistent download/upload list
        /// </summary>
        /// <returns></returns>
        public async Task Save()
        {
            StorageFile? file = null;

            await saveLock.WaitAsync();
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(transferItems, CachedJsonSerializerOptions);
                file = await ApplicationData.Current.LocalFolder.CreateFileAsync(storageFile, CreationCollisionOption.ReplaceExisting);

                using (var stream = await file.OpenStreamForWriteAsync())
                using (var writer = new StreamWriter(stream))
                {
                    await writer.WriteAsync(json);
                }
            }
            catch (Exception ex)
            {
                if (file is not null)
                {
                    Report?.Error("", $"InternetQueue.Save Failed to save to disk ({file.Path}), {ex.Message}");
                }
                else
                {
                    Report?.Error("", $"InternetQueue.Save Failed to save to disk (file is null), {ex.Message}");
                }
            }
            finally
            {
                saveLock.Release();
            }
        }


        /// <summary>
        /// Shutdown the Download upload process
        /// </summary>
        /// <returns></returns>
        public async Task Unload()
        {
            // Wait for DownloadUpload to finish
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
                Report?.Warning("", "InternetQueue.Unload timed out waiting for processing to complete.");
            }


            try
            {
                // Dispose of HttpClient to free up resources
                if (httpClient is not null)
                {
                    httpClient.CancelPendingRequests();
                    httpClient.Dispose();
                }

                // Dispose the semaphore
                if (semaphore is not null)
                {
                    semaphore.Dispose();
                }

                transferItems.Clear();
            }
            catch (Exception ex)
            {
                Report?.Error("", $"InternetQueueItem.Unload Error during resource cleanup: {ex.Message}");
            }

            isReady = false;
        }


        /// <summary>
        /// Add the URL of an item to be downloaded if the item is not already 
        /// in the list of downloaded items. This is used to avoid duplicate entries.
        /// </summary>
        /// <param name="direction"></param>
        /// <param name="type"></param>
        /// <param name="url"></param>
        /// <param name="priority"></param>

        public async Task AddDownloadRequestIfNecessary(TransferType type, string url, string subFolder = "", Priority priority = Priority.Normal)
        {
            // Check we don't already have this downloaded
            var item = Find(url);

            // Check if item is in the list but it failed
            if (item is not null && item.Status == Status.Failed)
            {
                // Remove the failed item
                await Remove(item, false/*don't save at this stage*/);
                item = null;
            }

            if (item is null)
            {
                // Add a download request
                await AddDownloadRequest(type, url, subFolder, priority);
            }
        }

        /// <summary>
        /// Add the URL of an item to be downloaded
        /// </summary>
        /// <param name="direction"></param>
        /// <param name="type"></param>
        /// <param name="url"></param>
        /// <param name="priority"></param>
        public async Task AddDownloadRequest(TransferType type, string url, string subFolder = "", Priority priority = Priority.Normal)
        {
            string localFileSpec;

            switch (type)
            {
                case TransferType.Page:
                    if (string.IsNullOrEmpty(subFolder))
                        localFileSpec = GetLocalFileName(url);
                    else
                        localFileSpec = subFolder + "\\" + GetLocalFileName(url);
                    break;

                case TransferType.File:
                    var uri = new Uri(url);
                    if (string.IsNullOrEmpty(subFolder))
                        localFileSpec = GetLocalFileName(url, Path.GetExtension(uri.AbsolutePath));
                    else
                        localFileSpec = subFolder + "\\" + GetLocalFileName(url, Path.GetExtension(uri.AbsolutePath));
                    break;
                default:
                    localFileSpec = "";
                    break;
            }

            if (!string.IsNullOrEmpty(localFileSpec))
            {
                // Acquire the semaphore asynchronously
                await transferItemsLock.WaitAsync();
                try
                {
                    transferItems.Add(new InternetQueueItem
                    {
                        Direction = Direction.Download,
                        Type = type,
                        URL = url,
                        Status = Status.Required,
                        Priority = priority,
                        RelativeLocalFileSpec = localFileSpec
                    });
                }
                finally
                {
                    // Always release the semaphore in the finally block
                    transferItemsLock.Release();
                }

                await Save();
            }
        }


        /// <summary>
        /// Add a payload to be uploaded to a URL
        /// </summary>
        /// <param name="type"></param>
        /// <param name="url"></param>
        /// <param name="payload"></param>
        /// <param name="priority"></param>
        /// <returns></returns>
        public async Task AddUploadRequest(TransferType type, string url, string payload, Priority priority = Priority.Normal)
        {
            string localFile = @"uploads\" + GetLocalFileName(url);

            try
            {
                // Create any required sub directories
                await LocalFolderHelper.EnsureLocalSubfolderPathExists(localFile);
                // Create upload file in local folder
                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(localFile, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, payload);
            }
            catch (Exception ex)
            {
                Report?.Error("", $"InternetQueueItem.AddUploadRequest Failed to create upload file in local folder:{localFile} for URL:{url}, {ex.Message}");
                return;
            }

            // Acquire the semaphore asynchronously
            await transferItemsLock.WaitAsync();

            try
            {
                transferItems.Add(new InternetQueueItem
                {
                    Direction = Direction.Upload,
                    Type = type,
                    URL = url,
                    Status = Status.Required,
                    Priority = priority,
                    RelativeLocalFileSpec = localFile
                });
            }
            finally
            {
                // Always release the semaphore in the finally block
                transferItemsLock.Release();
            }

            await Save();
        }


        /// <summary>
        /// Remove item from the list and tidy up. Finally save the list to
        /// disk unless requested not to
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public async Task Remove(InternetQueueItem item, bool save = true)
        {
            // Acquire the semaphore asynchronously
            await transferItemsLock.WaitAsync();
            try
            {
                transferItems.Remove(item);
            }
            finally
            {
                // Always release the semaphore in the finally block
                transferItemsLock.Release();
            }

            try
            {
                // Delete if a file has been downloaded or if a file has been prepared for upload
                if (item.Status == Status.Downloaded || item.Direction == Direction.Upload)
                {
                    string folderPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                    string filePath = Path.Combine(folderPath, item.RelativeLocalFileSpec);

                    // Check file existing before deleting
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                    else
                    {
                        Report?.Warning("", $"InternetQueueItem.Remove Failed to delete file in local folder:{item.RelativeLocalFileSpec} for URL:{item.URL}, file not found");
                    }
                }
            }
            catch (Exception ex)
            {
                // File might not exist — ignore or log if needed
                Report?.Warning("", $"InternetQueueItem.Remove Failed to delete file:{item.RelativeLocalFileSpec}, {ex.Message}");
            }

            if (save)
            {
                await Save();
            }
        }


        /// <summary>
        /// Removes all items from the list and tidies up
        /// Called as RemoveAll() removes everything
        /// Called as RemoveAll(Status.Download) will remove only the items with a status of Download
        /// </summary>
        /// <returns></returns>
        public async Task RemoveAll(Status? queryStatus = null)
        {
            int itemCount = transferItems.Count;

            foreach (var item in transferItems.ToList())
            {
                if (queryStatus is null || (queryStatus is not null && queryStatus == item.Status))
                {
                    // Acquire the semaphore asynchronously
                    await transferItemsLock.WaitAsync();
                    try
                    {
                        transferItems.Remove(item);
                    }
                    finally
                    {
                        // Always release the semaphore in the finally block
                        transferItemsLock.Release();
                    }

                    try
                    {
                        // Delete if a file has been downloaded or if a file has been prepared for upload
                        if (item.Status == Status.Downloaded || item.Direction == Direction.Upload)
                        {
                            string folderPath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                            string filePath = Path.Combine(folderPath, item.RelativeLocalFileSpec);

                            // Check file existing before deleting
                            if (File.Exists(filePath))
                            {
                                File.Delete(filePath);
                            }
                            else
                            {
                                Report?.Warning("", $"InternetQueueItem.RemoveAll Failed to delete file in local folder:{item.RelativeLocalFileSpec} for URL:{item.URL}, file not found");
                            }
                        }
                    }
                    catch (Exception ex) 
                    {
                        // File might not exist — ignore or log if needed
                        Report?.Warning("", $"InternetQueueItem.RemoveAll Failed to delete file:{item.RelativeLocalFileSpec}, {ex.Message}");
                    }
                }
            }

            transferItems.Clear();
            await Save();

            Report?.Info("", $"{itemCount} items removed from the Download/Upload list");
        }


        /// <summary>
        /// Find an item from the list based on the url
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public InternetQueueItem? Find(string url)
        {
            InternetQueueItem? ret = null;

            // Acquire the semaphore asynchronously
            transferItemsLock.Wait();
            try
            {
                ret = transferItems.FirstOrDefault(item => item.URL == url);
            }
            finally
            {
                // Always release the semaphore in the finally block
                transferItemsLock.Release();
            }

            // Find an item in the transferItems list by searching on the url
            return ret;
        }



        /// <summary>
        /// Return the count of the number of items in the 'transferItems' list
        /// that match the indicated direction (upload or download), type (page or file),
        /// state. If GetCount(null,null, null) the total number of items is returned
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        public int? GetCount(Direction? direction, TransferType? transferType, Status? status)
        {
            if (status is null)
                return transferItems.Count;
            else
            {
                if (direction is not null && transferType is not null && status is not null)
                {
                    return transferItems.Count(i => i.Direction == direction && i.Type == transferType && i.Status == status);
                }
                else if (direction is not null && transferType is not null)
                {
                    return transferItems.Count(i => i.Direction == direction && i.Type == transferType);
                }
                else if (direction is not null && status is not null)
                {
                    return transferItems.Count(i => i.Direction == direction && i.Status == status);
                }
                else if (transferType is not null && status is not null)
                {
                    return transferItems.Count(i => i.Type == transferType && i.Status == status);
                }
                else if (direction is not null)
                {
                    return transferItems.Count(i => i.Direction == direction);
                }
                else if (transferType is not null)
                {
                    return transferItems.Count(i => i.Type == transferType);
                }
                else if (status is not null)
                {
                    return transferItems.Count(i => i.Status == status);
                }
            }

            return null;
        }


        /// <summary>
        /// Called to service any reqiured downloading or uploading
        /// Normally called on a timer
        /// </summary>
        /// <returns></returns>
        public async Task DownloadUpload(bool isMeteredConnection = false)
        {
            if (!isReady || isProcessing)
                return;

            if (!await NetworkHelper.IsInternetAvailableHttpAsync())
                return;

            // Flag we are in this function
            isProcessing = true;

            try
            {
                var queue = transferItems.Where(i => i.Status == Status.Required).ToList();
                List<Task> runningTasks = [];

                foreach (var item in queue)
                {
                    // Limit concurrent sessions to maxSessions
                    await semaphore.WaitAsync();

                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            // Atomic
                            int newSessionCount = Interlocked.Increment(ref sessionCount);
                            if (newSessionCount == 1)
                            {
                                try
                                {
                                    InternetActivityChanged?.Invoke(this, true); // Start
                                }
                                catch { }
                            }
                            Report?.Info("", $"{DateTime.Now:HH:mm:ss.ff} Session Count:{sessionCount} Queuing {item.Direction} Url:{item.URL}");

                            if (item.Direction == Direction.Download)
                            {
                                Report?.Info("", $"{DateTime.Now:HH:mm:ss.ff} Start Downloaded file:{item.RelativeLocalFileSpec} Url:{item.URL}");

                                //var response = await httpClient.GetAsync(item.URL);
                                var response = await GetWithBackoffAsync(item.URL);
                                response.EnsureSuccessStatusCode();

                                // Create any required sub directories
                                await LocalFolderHelper.EnsureLocalSubfolderPathExists(item.RelativeLocalFileSpec);

                                if (item.Type == TransferType.Page)
                                {
                                    var content = await response.Content.ReadAsStringAsync();

                                    // Create file and write content
                                    StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(item.RelativeLocalFileSpec, CreationCollisionOption.ReplaceExisting);
                                    await FileIO.WriteTextAsync(file, content);
                                }
                                else if (item.Type == TransferType.File)
                                {
                                    byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();

                                    // Create file and write content
                                    StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(item.RelativeLocalFileSpec, CreationCollisionOption.ReplaceExisting);
                                    using (var stream = await file.OpenStreamForWriteAsync())
                                    {
                                        await stream.WriteAsync(imageBytes, 0, imageBytes.Length);
                                    }
                                }

                                item.Status = Status.Downloaded;

                                Report?.Info("", $"{DateTime.Now:HH:mm:ss.ff} End Downloaded {item.Type}:{item.RelativeLocalFileSpec} Url:{item.URL}");
                            }
                            else if (item.Direction == Direction.Upload)
                            {
                                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(item.RelativeLocalFileSpec);
                                string payload = await FileIO.ReadTextAsync(file);

                                var response = await httpClient.PostAsync(item.URL, new StringContent(payload, Encoding.UTF8, "application/json"));
                                response.EnsureSuccessStatusCode();
                                item.Status = Status.Uploaded;

                                Report?.Info("", $"{DateTime.Now:HH:mm:ss.ff} Uploaded file: {file.Path}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Report?.Warning("", $"Failed to {item.Direction} {item.URL}, {ex.Message}");
                        }
                        finally
                        {
                            // Always release the semaphore even if there's an error
                            semaphore.Release();
                            int newSessionCount = Interlocked.Decrement(ref sessionCount);  // Atomic
                            if (newSessionCount == 0)
                            {
                                try
                                {
                                    InternetActivityChanged?.Invoke(this, false); // Stopped
                                }
                                catch { }
                            }
                        }
                    });

                    runningTasks.Add(task);

                    // Optional: throttle the number of queued tasks
                    if (runningTasks.Count >= maxSessions)
                    {
                        var completed = await Task.WhenAny(runningTasks);
                        runningTasks.Remove(completed);
                    }
                }

                // Wait for all remaining tasks to finish
                await Task.WhenAll(runningTasks);
                await Save();
            }
            finally
            {
                isProcessing = false;
            }
        }


        /// <summary>
        /// Called by the SettingWindows to refresh the UI view of the download upload list
        /// </summary>
        public void RefreshView(bool reset = false)        
        {
            try
            {
                if (!reset)
                {
                    InternetQueueView.Clear();

                    foreach (var item in transferItems)
                    {
                        InternetQueueView.Add(new InternetQueueViewItem
                        {
                            Direction = item.Direction,
                            TransferType = item.Type,
                            Status = item.Status,
                            Priority = item.Priority,
                            LocalFileSpec = item.RelativeLocalFileSpec,
                            StatusDate = item.StatusDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                            URL = item.URL,
                            CreatedDate = item.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss")
                        });
                    }
                }
                else
                {
                    // Request to free resources
                    InternetQueueView.Clear();
                }
            }
            catch { }
        }



        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Make file name
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private static string GetLocalFileName(string url, string extension = ".html")
        {
            return url.GetHashCode().ToString("X") + extension;
        }


        /// <summary>
        /// Used in place of httpClient.GetAsync() that automatically retries with 
        /// exponential backoff only if it gets 403 Forbidden
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private async Task<HttpResponseMessage> GetWithBackoffAsync(string url)
        {
            const int maxRetries = 5;
            TimeSpan delay = TimeSpan.FromSeconds(2);

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                var response = await httpClient.GetAsync(url);

                if (response.StatusCode != HttpStatusCode.Forbidden)
                {
                    return response; // Success or other error, return immediately
                }

                if (attempt == maxRetries)
                {
                    return response; // After max retries, return last forbidden response
                }

                // Exponential backoff: 2s, 4s, 8s, etc.
                await Task.Delay(delay);
                delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
            }

            throw new InvalidOperationException("Unreachable code in InternetQueueItem.GetWithBackoffAsync.");
        }
    }
}
