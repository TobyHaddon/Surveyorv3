using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace Surveyor.Helper
{
    internal class LocalFolderHelper
    {
        /// <summary>
        /// Cache for folders to avoid creating them multiple times.
        /// relativePath can take a relative file spec and extract the
        /// relative path from it.
        /// </summary>
        /// <remarks>
        /// This is a simple cache that stores the folder path and the corresponding StorageFolder.
        /// </remarks>
        /// <returns></returns>

        private static readonly Dictionary<string, StorageFolder> _folderCache = [];

        public static async Task<StorageFolder> EnsureLocalSubfolderPathExists(string relativePath)
        {
            string folderPath = Path.GetDirectoryName(relativePath) ?? "";
            folderPath = folderPath.Replace('/', '\\'); // Normalize

            if (_folderCache.TryGetValue(folderPath, out var cachedFolder))
                return cachedFolder;

            StorageFolder current = ApplicationData.Current.LocalFolder;

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                _folderCache[folderPath] = current;
                return current;
            }

            string[] parts = folderPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            string currentPath = "";

            foreach (var part in parts)
            {
                currentPath = Path.Combine(currentPath, part);

                if (_folderCache.TryGetValue(currentPath, out var cached))
                {
                    current = cached;
                    continue;
                }

                current = await current.CreateFolderAsync(part, CreationCollisionOption.OpenIfExists);
                _folderCache[currentPath] = current;
            }

            _folderCache[folderPath] = current;
            return current;
        }
    }
}
