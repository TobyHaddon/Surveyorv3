// DownloadUploadManager  Handle the list of requested download and upload pages and files
//
// Version 1.0 11 Apr 2025

using Surveyor.User_Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Windows.Storage;

namespace Surveyor
{
    public enum Direction { Upload, Download }
    public enum TransferType { Page, File }
    public enum Status { Required, Requested, Downloaded, Uploaded, Failed }


    public class TransferItem
    {
        public Direction Direction { get; set; }
        public TransferType Type { get; set; }
        public required string URL { get; set; }
        public Status Status { get; set; }
        public string LocalFileSpec { get; set; } = "";
        public Priority Priority { get; set; }
    }

    public class DownloadUploadManager
    {
        private Reporter? Report { get; set; } = null;

        private const int maxSessions = 4;
        private readonly List<TransferItem> transferItems = [];
        private readonly SemaphoreSlim semaphore = new(maxSessions);
        private readonly HttpClient httpClient = new();
        private readonly string storageFile = "DownloadUploadlist.json";
        private bool isReady = false;
        private bool isProcessing = false;


        public DownloadUploadManager(Reporter? _report)
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
                List<TransferItem>? transferItemList = System.Text.Json.JsonSerializer.Deserialize<List<TransferItem>>(json);

                if (transferItemList is not null)
                    transferItems.AddRange(transferItemList);

                isReady = true;
            }
            catch (FileNotFoundException)
            {
                // No saved state yet
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
                Report?.Warning("", "DownloadUploadManager.Unload timed out waiting for processing to complete.");
            }


            try
            {
                // Dispose of HttpClient to free up resources
                httpClient.Dispose();

                // Dispose the semaphore
                semaphore.Dispose();
            }
            catch (Exception ex)
            {
                Report?.Error("DownloadUploadManager", $"Error during resource cleanup: {ex.Message}");
            }

            isReady = false;
        }


        /// <summary>
        /// Save the presistent download/upload list
        /// </summary>
        /// <returns></returns>
        public async Task Save()
        {
            string json = System.Text.Json.JsonSerializer.Serialize(transferItems);
            StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(storageFile, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, json);
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
                transferItems.Add(new TransferItem
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

            transferItems.Add(new TransferItem
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
        public async Task Remove(TransferItem item)
        {
            transferItems.Remove(item);
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(Path.GetFileName(item.LocalFileSpec));
                await file.DeleteAsync();
            }
            catch { }

            await Save();
        }


        /// <summary>
        /// Removes all items from the list and tidies up
        /// </summary>
        /// <returns></returns>
        public async Task RemoveAll()
        {
            int itemCount = transferItems.Count;

            foreach (var item in transferItems.ToList())
            {
                try
                {
                    StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync(Path.GetFileName(item.LocalFileSpec));
                    await file.DeleteAsync();
                }
                catch
                {
                    // File might not exist — ignore or log if needed
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
        public TransferItem? Find(string url)
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
                                    //???item.LocalFileSpec = file.Path;
                                }
                                else if (item.Type == TransferType.File)
                                {
                                    byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();

                                    StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync(item.LocalFileSpec, CreationCollisionOption.ReplaceExisting);
                                    using (var stream = await file.OpenStreamForWriteAsync())
                                    {
                                        await stream.WriteAsync(imageBytes, 0, imageBytes.Length);
                                    }
                                    //???item.LocalFileSpec = file.Path;
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
                        catch
                        {
                            // Log or handle error
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



        ///
        /// PRIVATE
        /// 


        /// <summary>
        /// Make file name
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private static string GetLocalFileName(string url, string extension = ".json")
        {
            return url.GetHashCode().ToString("X") + extension;
        }
    }
}
