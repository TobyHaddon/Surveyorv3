using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace ActionCameraMP4MetadataExtraction
{
    public enum ActionCamFamily
    {
        Unknown = 0,
        GoPro_Hero9Plus,
        Insta360_AceProFamily,
        DJI_OsmoActionFamily
    }

    public sealed record CameraIdentificationResult(
        ActionCamFamily Family,
        double Confidence,
        IReadOnlyList<string> Evidence);

    /// <summary>
    /// Deterministically identifies action-cam family by MP4 structural signatures:
    /// - GoPro Hero9+: 'udta' contains 'GPMF'
    /// - Insta360 Ace Pro family: top-level 'inst' box
    /// - DJI Action family: presence of 'djmd' or 'dbgi' FourCC anywhere
    /// </summary>
    public static class IdentifyCameraFamily
    {
        private static readonly string[] DjiSignatureFourCC = { "djmd", "dbgi" };

        public static async Task<CameraIdentificationResult> IdentifyCameraFamilyAsync(StorageFile file)
        {
            ArgumentNullException.ThrowIfNull(file);

            using var fs = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var evidence = new List<string>();

            // 1) GoPro#; Serial Number
            GpmfItemList? gpmfItemList = await GetGoProMP4StaticMetadata.ExtractPropertiesAsync(file);
            if (gpmfItemList is not null)
            {
                GpmfItemList? gpmfItemListResult = gpmfItemList.GetItems("CASN");
                if (gpmfItemListResult is not null && gpmfItemListResult.Count > 0)
                {
                    GpmfItem gpmfItem = gpmfItemListResult[0];
                    if (gpmfItem is not null && gpmfItem.Payload is not null)
                    {
                        evidence.Add($"Found GoPro Serial Number:{(string)gpmfItem.Payload as string}.");
                        return new CameraIdentificationResult(ActionCamFamily.GoPro_Hero9Plus, 1.00, evidence);
                    }
                }
            }

            // 2) GoPro: GPMF inside udta
            if (await HasFourCCInsideTopLevelBoxAsync(fs, boxType: "udta", needleFourCC: "GPMF").ConfigureAwait(false))
            {
                evidence.Add("Found 'GPMF' inside 'udta' (GoPro telemetry).");
                return new CameraIdentificationResult(ActionCamFamily.GoPro_Hero9Plus, 0.95, evidence);
            }

            // 3) Insta360 Ace Pro family: top-level 'inst'
            if (await HasTopLevelBoxAsync(fs, "inst").ConfigureAwait(false))
            {
                evidence.Add("Found top-level box 'inst' (Insta360 Ace Pro-family signature).");
                // Optional supporting evidence
                if (await HasFourCCInsideTopLevelBoxAsync(fs, "udta", "nail").ConfigureAwait(false))
                    evidence.Add("Found 'nail' inside 'udta' (seen in Ace Pro 2 sample).");
                if (await HasFourCCInsideTopLevelBoxAsync(fs, "udta", "AMBA").ConfigureAwait(false))
                    evidence.Add("Found 'AMBA' inside 'udta' (seen in Ace Pro 2 sample).");

                return new CameraIdentificationResult(ActionCamFamily.Insta360_AceProFamily, 0.95, evidence);
            }

            // 4) DJI Action family: any appearance of djmd/dbgi FourCC atom type
            if (await HasAnyTopLevelFourCCAsync(fs, DjiSignatureFourCC).ConfigureAwait(false))
            {
                evidence.Add("Found 'djmd' or 'dbgi' FourCC (DJI metadata signature).");
                return new CameraIdentificationResult(ActionCamFamily.DJI_OsmoActionFamily, 0.92, evidence);
            }

            // 5) Weak fallback: ASCII maker scan in header region (helps with re-wrapped files)
            fs.Seek(0, SeekOrigin.Begin);
            byte[] head = await ReadUpToAsync(fs, 4 * 1024 * 1024).ConfigureAwait(false);
            string ascii = Encoding.ASCII.GetString(head);

            if (ascii.IndexOf("Insta360", StringComparison.OrdinalIgnoreCase) >= 0)
                return new CameraIdentificationResult(ActionCamFamily.Insta360_AceProFamily, 0.60,
                    new[] { "Found ASCII marker 'Insta360' in header scan." });

            if (ascii.IndexOf("DJI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                ascii.IndexOf("Osmo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                ascii.IndexOf("Action", StringComparison.OrdinalIgnoreCase) >= 0)
                return new CameraIdentificationResult(ActionCamFamily.DJI_OsmoActionFamily, 0.60,
                    new[] { "Found ASCII marker 'DJI/Osmo/Action' in header scan." });

            if (ascii.IndexOf("GoPro", StringComparison.OrdinalIgnoreCase) >= 0)
                return new CameraIdentificationResult(ActionCamFamily.GoPro_Hero9Plus, 0.50,
                    new[] { "Found ASCII marker 'GoPro' in header scan (no GPMF detected)." });

            return new CameraIdentificationResult(ActionCamFamily.Unknown, 0.30, evidence);
        }

        // ---------------------------
        // MP4 scanning helpers
        // ---------------------------

        private static async Task<bool> HasTopLevelBoxAsync(FileStream fs, string fourcc)
        {
            fs.Seek(0, SeekOrigin.Begin);
            long fileLen = fs.Length;
            long pos = 0;

            while (pos + 8 <= fileLen)
            {
                var (type, atomSize, _) = await ReadAtomHeaderAsync(fs, pos, fileLen).ConfigureAwait(false);
                if (atomSize <= 0) break;

                if (type == fourcc) return true;
                pos += atomSize;
            }

            return false;
        }

        private static async Task<bool> HasAnyTopLevelFourCCAsync(FileStream fs, IReadOnlyList<string> fourccList)
        {
            var set = new HashSet<string>(fourccList, StringComparer.Ordinal);

            fs.Seek(0, SeekOrigin.Begin);
            long fileLen = fs.Length;
            long pos = 0;

            while (pos + 8 <= fileLen)
            {
                var (type, atomSize, _) = await ReadAtomHeaderAsync(fs, pos, fileLen).ConfigureAwait(false);
                if (atomSize <= 0) break;

                if (set.Contains(type)) return true;
                pos += atomSize;
            }

            return false;
        }

        private static async Task<bool> HasFourCCInsideTopLevelBoxAsync(FileStream fs, string boxType, string needleFourCC)
        {
            fs.Seek(0, SeekOrigin.Begin);
            long fileLen = fs.Length;
            long pos = 0;

            byte[] needle = Encoding.ASCII.GetBytes(needleFourCC);

            while (pos + 8 <= fileLen)
            {
                var (type, atomSize, headerSize) = await ReadAtomHeaderAsync(fs, pos, fileLen).ConfigureAwait(false);
                if (atomSize <= 0) break;

                if (type == boxType)
                {
                    long payloadLen = atomSize - headerSize;
                    long windowLen = Math.Min(payloadLen, 512 * 1024); // scan first 512KB of that box

                    if (windowLen > 0)
                    {
                        byte[] buf = new byte[windowLen];
                        fs.Seek(pos + headerSize, SeekOrigin.Begin);
                        int r = await fs.ReadAsync(buf, 0, (int)windowLen).ConfigureAwait(false);

                        if (r > 0 && IndexOf(buf, needle) >= 0)
                            return true;
                    }
                }

                pos += atomSize;
            }

            return false;
        }

        private static async Task<(string Type, long AtomSize, int HeaderSize)> ReadAtomHeaderAsync(FileStream fs, long pos, long fileLen)
        {
            fs.Seek(pos, SeekOrigin.Begin);

            byte[] header = new byte[8];
            int n = await fs.ReadAsync(header, 0, 8).ConfigureAwait(false);
            if (n < 8) return ("", -1, 0);

            uint size32 = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
            string type = Encoding.ASCII.GetString(header, 4, 4);

            long atomSize;
            int headerSize = 8;

            if (size32 == 0)
            {
                atomSize = fileLen - pos; // extends to EOF (rare at top-level, but legal)
            }
            else if (size32 == 1)
            {
                byte[] ext = new byte[8];
                int n2 = await fs.ReadAsync(ext, 0, 8).ConfigureAwait(false);
                if (n2 < 8) return (type, -1, headerSize);

                atomSize = (long)BinaryPrimitives.ReadUInt64BigEndian(ext);
                headerSize = 16;
            }
            else
            {
                atomSize = size32;
            }

            if (atomSize < headerSize || pos + atomSize > fileLen)
                return (type, -1, headerSize);

            return (type, atomSize, headerSize);
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            if (needle.Length == 0) return 0;

            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { ok = false; break; }
                }
                if (ok) return i;
            }
            return -1;
        }

        private static async Task<byte[]> ReadUpToAsync(FileStream fs, int maxBytes)
        {
            fs.Seek(0, SeekOrigin.Begin);
            int toRead = (int)Math.Min(fs.Length, maxBytes);
            byte[] buf = new byte[toRead];
            int r = await fs.ReadAsync(buf, 0, toRead).ConfigureAwait(false);
            if (r < toRead) Array.Resize(ref buf, r);
            return buf;
        }
    }
}
