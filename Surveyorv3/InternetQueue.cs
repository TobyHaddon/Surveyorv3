// InternetQueue  Handle the list of requested download and upload pages and files
//
// Version 1.0 11 Apr 2025
//
// Version 1.1 13 Apr 2025
// Renames from DownloadUploadManager to InternetQueue


using Surveyor.User_Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;



namespace Surveyor
{
    public enum Direction { Upload, Download }
    public enum TransferType { Page, File }
    public enum Status { Required, Requested, Downloaded, Uploaded, Failed }

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
        public string LocalFileSpec { get; set; } = "";

        // Priority of this request
        public Priority Priority { get; set; }

        // Creation time, set once
        public DateTime CreatedDate { get; }
    }

    public class InternetQueue
    {
        private Reporter? Report { get; set; } = null;

        private const int maxSessions = 1;  // I think Fishbase only like 1 thread
        private readonly List<InternetQueueItem> transferItems = [];
        private readonly SemaphoreSlim semaphore = new(maxSessions);
        private readonly HttpClient httpClient = new();
        private readonly string storageFile = "InternetQueue.json";
        private bool isReady = false;
        private bool isProcessing = false;
        private readonly SemaphoreSlim saveLock = new(1, 1);

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


        // View call to bind to (updated via the RefreshView() method)
        public ObservableCollection<InternetQueueViewItem> InternetQueueView { get; } = [];


        public InternetQueue(Reporter? _report)
        {
            Report = _report;
        }


        /// <summary>
        /// Load the presistent download/upload list
        /// </summary>
        /// <returns></returns>
        public async Task Load()
        {
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(storageFile);
                string json = await FileIO.ReadTextAsync(file);
                transferItems.Clear();
                List<InternetQueueItem>? transferItemList = System.Text.Json.JsonSerializer.Deserialize<List<InternetQueueItem>>(json);

                if (transferItemList is not null)
                {
                    transferItems.AddRange(transferItemList);
                    Report?.Info("", $"InternetQueue {transferItems.Count} item(s) loaded from disk");
                }

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
        /// Save the presistent download/upload list
        /// </summary>
        /// <returns></returns>
        public async Task Save()
        {
            await saveLock.WaitAsync();
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(transferItems);
                string tempFileName = storageFile + ".tmp";

                StorageFile tempFile = await ApplicationData.Current.LocalFolder.CreateFileAsync(tempFileName, CreationCollisionOption.ReplaceExisting);
                using (var stream = await tempFile.OpenStreamForWriteAsync())
                using (var writer = new StreamWriter(stream))
                {
                    await writer.WriteAsync(json);
                }

                await tempFile.RenameAsync(storageFile, NameCollisionOption.ReplaceExisting);
            }
            finally
            {
                saveLock.Release();
            }
        }


        /// <summary>
        /// Add the URL of an item to be downloaded
        /// </summary>
        /// <param name="direction"></param>
        /// <param name="type"></param>
        /// <param name="url"></param>
        /// <param name="priority"></param>
        public async Task AddDownloadRequest(TransferType type, string url, Priority priority = Priority.Normal)
        {
            string localFileSpec;

            switch (type)
            {
                case TransferType.Page:
                    localFileSpec = GetLocalFileName(url);
                    break;

                case TransferType.File:
                    var uri = new Uri(url);
                    localFileSpec = GetLocalFileName(url, Path.GetExtension(uri.AbsolutePath));
                    break;
                default:
                    localFileSpec = "";
                    break;
            }

            if (!string.IsNullOrEmpty(localFileSpec))
            {
                transferItems.Add(new InternetQueueItem
                {
                    Direction = Direction.Download,
                    Type = type,
                    URL = url,
                    Status = Status.Required,
                    Priority = priority,
                    LocalFileSpec = localFileSpec
                });

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
            string localFile = GetLocalFileName(url);
            StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(localFile, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, payload);

            transferItems.Add(new InternetQueueItem
            {
                Direction = Direction.Upload,
                Type = type,
                URL = url,
                Status = Status.Required,
                Priority = priority,
                LocalFileSpec = localFile
            });

            await Save();
        }


        /// <summary>
        /// Remove item from the list and tidy up
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public async Task Remove(InternetQueueItem item)
        {
            transferItems.Remove(item);
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(Path.GetFileName(item.LocalFileSpec));
                await file.DeleteAsync();
            }
            catch (Exception ex)
            {
                // File might not exist — ignore or log if needed
                Report?.Warning("", $"InternetQueueItem.Remove Failed to delete file:{item.LocalFileSpec}, {ex.Message}");
            }

            await Save();
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
                    transferItems.Remove(item);
                    try
                    {
                        StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(Path.GetFileName(item.LocalFileSpec));
                        await file.DeleteAsync();
                    }
                    catch (Exception ex) 
                    {
                        // File might not exist — ignore or log if needed
                        Report?.Warning("", $"InternetQueueItem.RemoveAll Failed to delete file:{item.LocalFileSpec}, {ex.Message}");
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
            // Find an item in the transferItems list by searching on the url
            return transferItems.FirstOrDefault(item => item.URL == url);
        }


        /// <summary>
        /// Called to start the download/upload process
        /// </summary>
        /// <returns></returns>
        public async Task DownloadUpload()
        {
            if (!isReady || isProcessing)
                return;

            if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                return;

            // Flag we are in this function
            isProcessing = true;

            try
            {
                List<Task> tasks = [];

                foreach (var item in transferItems.Where(i => i.Status == Status.Required))
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        Report?.Info("", $"{DateTime.Now:HH:mm:ss.ff} Queuing {item.Direction} Url:{item.URL}");

                        await semaphore.WaitAsync();
                        try
                        {
                            if (item.Direction == Direction.Download)
                            {
                                Report?.Info("", $"{DateTime.Now:HH:mm:ss.ff} Start Downloaded file:{item.LocalFileSpec} Url:{item.URL}");

                                var response = await httpClient.GetAsync(item.URL);
                                response.EnsureSuccessStatusCode();

                                if (item.Type == TransferType.Page)
                                {
                                    var content = await response.Content.ReadAsStringAsync();

                                    StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(item.LocalFileSpec, CreationCollisionOption.ReplaceExisting);
                                    await FileIO.WriteTextAsync(file, content);

                                }
                                else if (item.Type == TransferType.File)
                                {
                                    byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();

                                    StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(item.LocalFileSpec, CreationCollisionOption.ReplaceExisting);
                                    using (var stream = await file.OpenStreamForWriteAsync())
                                    {
                                        await stream.WriteAsync(imageBytes, 0, imageBytes.Length);
                                    }

                                }

                                item.Status = Status.Downloaded;


                                Report?.Info("", $"{DateTime.Now:HH:mm:ss.ff} End Downloaded {item.Type}:{item.LocalFileSpec} Url:{item.URL}");
                            }
                            else if (item.Direction == Direction.Upload)
                            {
                                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(item.LocalFileSpec);
                                string payload = await FileIO.ReadTextAsync(file);

                                var response = await httpClient.PostAsync(item.URL, new StringContent(payload, Encoding.UTF8, "application/json"));
                                response.EnsureSuccessStatusCode();
                                item.Status = Status.Uploaded;

                                Report?.Info("", $"{DateTime.Now:HH:mm:ss.ff} Uploaded file: {file.Path}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Report?.Warning("", $"Failed to get {item.URL}, {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);
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
                        LocalFileSpec = item.LocalFileSpec,
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
    }
}
