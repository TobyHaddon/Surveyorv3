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

        private static readonly string _rootPath = Path.Combine(
            Environment.GetEnvironmentVariable("TEMP")
            ?? Environment.GetEnvironmentVariable("TMP")
            ?? Path.GetTempPath(),
            "Surveyor");

        public static string GetFullPath(string relativePath)
        {
            return Path.Combine(_rootPath, relativePath.Replace('/', '\\'));
        }

        public static async Task<StorageFolder> EnsureLocalSubfolderPathExistsAsync(string relativePath)
        {
            string folderPath = Path.GetDirectoryName(relativePath) ?? "";
            folderPath = folderPath.Replace('/', '\\'); // Normalize

            if (_folderCache.TryGetValue(folderPath, out var cachedFolder))
                return cachedFolder;

            string fullFolderPath = string.IsNullOrWhiteSpace(folderPath)
                ? _rootPath
                : Path.Combine(_rootPath, folderPath);

            Directory.CreateDirectory(fullFolderPath);

            StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(fullFolderPath);
            _folderCache[folderPath] = folder;

            return folder;
        }
    }
}
