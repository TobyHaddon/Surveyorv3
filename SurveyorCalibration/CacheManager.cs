using Microsoft.UI.Xaml.Data;
using System;
using System.IO;
using System.Linq;
using Windows.Storage;

namespace Surveyor
{
    /// <summary>
    /// Manages cached calibration frame information stored as JSON files
    /// in the application's local storage folder.
    /// </summary>
    public class CacheManager
    {
        /// <summary>
        /// Returns the full path to the cache root (LocalFolder path).
        /// </summary>
        public string GetCachePath()
        {
            return ApplicationData.Current.LocalFolder.Path;
        }

        /// <summary>
        /// Recursively totals the size (in bytes) of all *.json files beneath the cache path.
        /// Returns 0 if the folder does not exist or on error.
        /// </summary>
        public long GetCacheTotalDiskSpaceUsed()
        {
            try
            {
                string root = GetCachePath();
                if (!Directory.Exists(root)) return 0;
                return Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                                 .Select(f => {
                                     try { return new FileInfo(f).Length; } catch { return 0L; }
                                 })
                                 .Sum();
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Deletes all *.json files beneath the cache path (recursively).
        /// Returns true if operation completed (even if there were no files).
        /// Returns false only if an unexpected exception prevented completion.
        /// </summary>
        public bool ClearCache()
        {
            try
            {
                string root = GetCachePath();
                if (!Directory.Exists(root)) return true; // Nothing to clear

                foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); } catch { /* swallow individual file errors */ }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Deletes *.json files older than 3 months. If ALL files are older than 3 months,
        /// the newest file is preserved (not deleted) to avoid emptying the cache entirely.
        /// Returns true if operation completed (even if no deletions occurred),
        /// false only if a blocking exception occurred.
        /// </summary>
        public bool ClearCacheOlderItems()
        {
            try
            {
                string root = GetCachePath();
                if (!Directory.Exists(root)) return true;

                var allJsonFiles = Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                                            .Select(p => new FileInfo(p))
                                            .Where(fi => fi.Exists)
                                            .ToList();

                if (allJsonFiles.Count == 0) return true; // Nothing to do

                DateTime cutoff = DateTime.UtcNow.AddMonths(-3);

                // Files older than cutoff
                var olderFiles = allJsonFiles.Where(fi => fi.LastWriteTimeUtc < cutoff).ToList();
                var newerOrEqualFiles = allJsonFiles.Except(olderFiles).ToList();

                // If there are NO newer/equal files then preserve the newest among the older ones
                if (newerOrEqualFiles.Count == 0 && olderFiles.Count > 0)
                {
                    var newestOlder = olderFiles.OrderByDescending(fi => fi.LastWriteTimeUtc).First();
                    olderFiles = olderFiles.Where(fi => fi.FullName != newestOlder.FullName).ToList();
                }

                foreach (var fi in olderFiles)
                {
                    try { fi.Delete(); } catch { /* swallow individual file errors */ }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

}
