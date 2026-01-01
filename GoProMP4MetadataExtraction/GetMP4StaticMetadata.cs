//
// Version 1.1  13 Jan 2025
// Fixed zero length arrays not returning null
// 

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;

namespace ActionCameraMP4MetadataExtraction
{
    /// <summary>
    /// GoPro Section
    /// </summary>
    public static class GetGoProMP4StaticMetadata
    {
        private const int MAX_BUFFER_SIZE = 1024 * 1024 * 24; // Adjust if necessary

        public static async Task<GpmfItemList?> ExtractPropertiesAsync(StorageFile videoFile)
        {
            ArgumentNullException.ThrowIfNull(videoFile);

            long mdatOffset = 0;
            long udtaOffset = -1;
            uint gpmfSize = 0;

            try
            {
                using FileStream fileStream = new(videoFile.Path, FileMode.Open, FileAccess.Read);
                using BinaryReader reader = new(fileStream);
                byte[] fileBuffer = new byte[MAX_BUFFER_SIZE];

                // Read the first 60 bytes of the file
                int bytesRead = await fileStream.ReadAsync(fileBuffer.AsMemory(0, 60));
                if (bytesRead > 0)
                {
                    for (int c = 0; c < bytesRead - 4; c++)
                    {
                        if (CHECKID(fileBuffer, c, 'm', 'd', 'a', 't') == true)
                        {
                            if (c >= 4 && fileBuffer[c - 4] == 0 && fileBuffer[c - 3] == 0 && fileBuffer[c - 2] == 0 && fileBuffer[c - 1] == 1) // 64-bit offset
                            {
                                 mdatOffset = ((long)fileBuffer[c + 7] << 32) |
                                              ((long)fileBuffer[c + 8] << 24) | 
                                              ((long)fileBuffer[c + 9] << 16) | 
                                              ((long)fileBuffer[c + 10] << 8) | 
                                              (long)fileBuffer[c + 11] + (c - 4); 

                            }
                            else
                            {
                                mdatOffset = (long)BYTESWAP32(BitConverter.ToUInt32(fileBuffer, (int)c - 4)) + c - 4;
                            }
                            break;
                        }
                    }

                    if (mdatOffset > 0)
                    {
                        fileStream.Seek(mdatOffset, SeekOrigin.Begin);
                        long udtaSeek = mdatOffset;

                        do
                        {
                            bytesRead = await fileStream.ReadAsync(fileBuffer.AsMemory(0, MAX_BUFFER_SIZE));
                            if (bytesRead > 0)
                                if (bytesRead > 0)
                            {
                                for (int c = 0; c < bytesRead - 4; c++)
                                {
                                    if (CHECKID(fileBuffer, c, 'u', 'd', 't', 'a') == true)
                                    {
                                        udtaOffset = udtaSeek + c;
                                        break;
                                    }
                                }
                                udtaSeek += bytesRead;
                            }
                        } while (udtaOffset == -1 && bytesRead == MAX_BUFFER_SIZE);

                        if (udtaOffset != -1)
                        {
                            // Read the first 4096 bytes of the udta atom
                            fileStream.Seek(udtaOffset, SeekOrigin.Begin);
                            fileStream.Read(fileBuffer, 0, 4096);

                            for (long c = 0; c < 4096 - 4; c++)
                            {                               
                                if (CHECKID(fileBuffer, c, 'G', 'P', 'M', 'F') == true)
                                {
                                    gpmfSize = (uint)((fileBuffer[c - 3] << 16) | (fileBuffer[c - 2] << 8) | fileBuffer[c - 1]) - 8;

                                    // Read in the GPMF section
                                    fileStream.Seek(udtaOffset + c + 4, SeekOrigin.Begin);
                                    fileStream.Read(fileBuffer, 0, (int)gpmfSize);

                                    IntPtr ptr = Marshal.UnsafeAddrOfPinnedArrayElement(fileBuffer, 0);
                                    GpmfItemList items = GpmfParser.GetItems(ref ptr, (int)gpmfSize);
                                    return items;
                                }
                            }
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting Utda data stream: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Compare the four characters of a FourCC (a,b,c,d) with the offset in the fileBuffer to see if they compare
        /// </summary>
        /// <param name="fileBuffer"></param>
        /// <param name="offset"></param>
        /// <param name="a">FourCC first character</param>
        /// <param name="b">FourCC second character</param>
        /// <param name="c">FourCC third character</param>
        /// <param name="d">FourCC fourth character</param>
        /// <returns></returns>
        private static bool CHECKID(byte[] fileBuffer, long offset, char a, char b, char c, char d)
        {
            bool ret = false;

            if ((char)fileBuffer[offset] == a)
            {
                if ((char)fileBuffer[offset + 1] == b)
                {
                    if ((char)fileBuffer[offset + 2] == c)
                    {
                        if ((char)fileBuffer[offset + 3] == d)
                        {
                            ret = true;
                        }
                    }
                }
            }
            return ret;
        }


        /// <summary>
        /// This method performs a byte swap operation on a 32-bit unsigned integer (uint) value. 
        /// Specifically, it reverses the order of the bytes in the input value, effectively 
        /// converting it between big-endian and little-endian representations.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private static uint BYTESWAP32(uint value)
        {
            return (value >> 24) | ((value & 0x00FF0000) >> 8) | ((value & 0x0000FF00) << 8) | (value << 24);
        }
    }



    /// <summary>
    /// Insta360 Section
    /// </summary>

    public sealed record Insta360MP4StaticMetadata(
        bool HasInstTopLevelBox,
        long? InstOffset,
        long? InstSize,
        bool HasUdtaBox,
        bool HasNailUdtaChild,
        bool HasAmbaUdtaChild,
        IReadOnlyList<string> Evidence,
        IReadOnlyList<string> ExtractedStrings // from inst/nail/udta samples
    );

    public static class GetInsta360MP4StaticMetadata
    {
        // You can bump these during reverse engineering, but keep bounded for big files
        private const int MaxBoxSampleBytes = 256 * 1024;
        private const int MaxStringsTotal = 200;
        private const int MinStringLen = 4;

        public static async Task<Insta360MP4StaticMetadata> ReadAsync(StorageFile mp4File)
        {
            ArgumentNullException.ThrowIfNull(mp4File);

            using var fs = new FileStream(mp4File.Path, FileMode.Open, FileAccess.Read, FileShare.Read);

            var evidence = new List<string>();
            var strings = new List<string>();

            // Find key boxes anywhere in the tree
            var inst = await Mp4AtomWalker.FindFirstAsync(fs, type: "inst").ConfigureAwait(false); // top-level in your Ace Pro 2 sample
            var udta = await Mp4AtomWalker.FindFirstAsync(fs, type: "udta").ConfigureAwait(false);

            bool hasInst = inst != null;
            if (hasInst)
                evidence.Add($"Found 'inst' box at 0x{inst!.Offset:X} size={inst.Size}.");

            bool hasUdta = udta != null;
            if (hasUdta)
                evidence.Add($"Found 'udta' box at 0x{udta!.Offset:X} size={udta.Size}.");

            bool hasNail = false;
            bool hasAmba = false;

            // If we have udta, scan its children for nail/AMBA (as in your MP4Box output)
            if (udta != null)
            {
                var udtaChildren = await Mp4AtomWalker.ListChildrenAsync(fs, udta).ConfigureAwait(false);
                hasNail = udtaChildren.Any(c => c.Type == "nail");
                hasAmba = udtaChildren.Any(c => c.Type == "AMBA");

                if (hasNail) evidence.Add("Found 'nail' child inside 'udta'.");
                if (hasAmba) evidence.Add("Found 'AMBA' child inside 'udta'.");

                // Pull a bounded string sample from udta and from nail/AMBA if present
                await AddStringsFromBoxSampleAsync(fs, udta, strings).ConfigureAwait(false);

                foreach (var child in udtaChildren.Where(c => c.Type is "nail" or "AMBA"))
                    await AddStringsFromBoxSampleAsync(fs, child, strings).ConfigureAwait(false);
            }

            // Also sample inst (big box, but we only take a bounded prefix)
            if (inst != null)
                await AddStringsFromBoxSampleAsync(fs, inst, strings).ConfigureAwait(false);

            strings = strings
                .Where(s => s.Length >= MinStringLen && s.Length <= 300)
                .Distinct()
                .Take(MaxStringsTotal)
                .ToList();

            // Heuristic: inst should be top-level for Ace Pro 2; still report even if nested
            bool hasInstTopLevel = inst != null && inst.Parent == null;

            return new Insta360MP4StaticMetadata(
                HasInstTopLevelBox: hasInstTopLevel,
                InstOffset: inst?.Offset,
                InstSize: inst?.Size,
                HasUdtaBox: hasUdta,
                HasNailUdtaChild: hasNail,
                HasAmbaUdtaChild: hasAmba,
                Evidence: evidence,
                ExtractedStrings: strings
            );
        }

        private static async Task AddStringsFromBoxSampleAsync(FileStream fs, Mp4AtomWalker.Atom atom, List<string> strings)
        {
            // Payload start depends on header size; for 'meta' there is also version/flags,
            // but inst/udta/nail/AMBA are normal.
            long payloadOffset = atom.PayloadOffset;
            long payloadLen = atom.PayloadSize;

            if (payloadLen <= 0) return;

            int toRead = (int)Math.Min(payloadLen, MaxBoxSampleBytes);
            byte[] buf = new byte[toRead];
            fs.Seek(payloadOffset, SeekOrigin.Begin);
            int r = await fs.ReadAsync(buf, 0, toRead).ConfigureAwait(false);
            if (r <= 0) return;
            if (r < toRead) Array.Resize(ref buf, r);

            strings.AddRange(Mp4AtomWalker.ExtractPrintableStrings(buf, minLen: 4));
        }
    }

    /// <summary>
    /// DJI Section
    /// </summary>

    public sealed record DJIMP4StaticMetadata(
        bool HasDjmdBox,
        bool HasDbgiBox,
        bool HasUdtaBox,
        string? ToolString,                 // e.g., "DJI OsmoAction5 Pro" if found
        IReadOnlyList<string> Evidence,
        IReadOnlyList<string> ExtractedStrings // from meta/udta + small samples
    );

    public static class GetDJIMP4StaticMetadata
    {
        private const int MaxBoxSampleBytes = 256 * 1024;
        private const int MaxStringsTotal = 250;
        private const int MinStringLen = 4;

        public static async Task<DJIMP4StaticMetadata> ReadAsync(StorageFile mp4File)
        {
            ArgumentNullException.ThrowIfNull(mp4File);

            using var fs = new FileStream(mp4File.Path, FileMode.Open, FileAccess.Read, FileShare.Read);

            var evidence = new List<string>();
            var strings = new List<string>();

            // DJI signatures observed in your MP4Box dump:
            // - meta streams / sample entries: djmd + dbgi
            // These often appear under trak->mdia->minf->stbl->stsd (but MP4Box called them "Unknown box type djmd in parent stsd")
            var djmd = await Mp4AtomWalker.FindFirstAsync(fs, "djmd").ConfigureAwait(false);
            var dbgi = await Mp4AtomWalker.FindFirstAsync(fs, "dbgi").ConfigureAwait(false);
            var udta = await Mp4AtomWalker.FindFirstAsync(fs, "udta").ConfigureAwait(false);
            var meta = await Mp4AtomWalker.FindFirstAsync(fs, "meta").ConfigureAwait(false);

            bool hasDjmd = djmd != null;
            bool hasDbgi = dbgi != null;
            bool hasUdta = udta != null;

            if (hasDjmd) evidence.Add($"Found 'djmd' box at 0x{djmd!.Offset:X} size={djmd.Size}.");
            if (hasDbgi) evidence.Add($"Found 'dbgi' box at 0x{dbgi!.Offset:X} size={dbgi.Size}.");
            if (hasUdta) evidence.Add($"Found 'udta' box at 0x{udta!.Offset:X} size={udta.Size}.");
            if (meta != null) evidence.Add($"Found 'meta' box at 0x{meta.Offset:X} size={meta.Size}.");

            // Pull bounded string samples from meta and udta (where MP4Box reported tags like tool/fsid/btec)
            if (meta != null)
                await AddStringsFromBoxSampleAsync(fs, meta, strings, isMetaBox: true).ConfigureAwait(false);
            if (udta != null)
                await AddStringsFromBoxSampleAsync(fs, udta, strings, isMetaBox: false).ConfigureAwait(false);

            // Also sample djmd/dbgi headers (sometimes contain readable identifiers)
            if (djmd != null)
                await AddStringsFromBoxSampleAsync(fs, djmd, strings, isMetaBox: false).ConfigureAwait(false);
            if (dbgi != null)
                await AddStringsFromBoxSampleAsync(fs, dbgi, strings, isMetaBox: false).ConfigureAwait(false);

            // One more: do a small ASCII scan of the file header region to find the "tool" string reliably
            fs.Seek(0, SeekOrigin.Begin);
            byte[] head = await Mp4AtomWalker.ReadUpToAsync(fs, 4 * 1024 * 1024).ConfigureAwait(false);
            strings.AddRange(Mp4AtomWalker.ExtractPrintableStrings(head, minLen: MinStringLen));

            var distinctStrings = strings
                .Where(s => s.Length >= MinStringLen && s.Length <= 400)
                .Distinct()
                .Take(MaxStringsTotal)
                .ToList();

            // Try to find the tool string you saw in MP4Box output
            // This is heuristic; the deterministic signature is djmd/dbgi presence.
            string? tool = distinctStrings.FirstOrDefault(s =>
                s.Contains("DJI", StringComparison.OrdinalIgnoreCase) &&
                (s.Contains("Osmo", StringComparison.OrdinalIgnoreCase) || s.Contains("Action", StringComparison.OrdinalIgnoreCase)));

            if (tool != null)
                evidence.Add($"Found likely tool/camera marker string: '{tool}'.");

            return new DJIMP4StaticMetadata(
                HasDjmdBox: hasDjmd,
                HasDbgiBox: hasDbgi,
                HasUdtaBox: hasUdta,
                ToolString: tool,
                Evidence: evidence,
                ExtractedStrings: distinctStrings
            );
        }

        private static async Task AddStringsFromBoxSampleAsync(FileStream fs, Mp4AtomWalker.Atom atom, List<string> strings, bool isMetaBox)
        {
            long payloadOffset = atom.PayloadOffset;
            long payloadLen = atom.PayloadSize;

            // For QuickTime 'meta' full box, there is typically 4 bytes version/flags after header
            // Mp4AtomWalker already accounts for this by setting PayloadOffset accordingly for 'meta'.
            if (payloadLen <= 0) return;

            int toRead = (int)Math.Min(payloadLen, MaxBoxSampleBytes);
            byte[] buf = new byte[toRead];
            fs.Seek(payloadOffset, SeekOrigin.Begin);
            int r = await fs.ReadAsync(buf, 0, toRead).ConfigureAwait(false);
            if (r <= 0) return;
            if (r < toRead) Array.Resize(ref buf, r);

            strings.AddRange(Mp4AtomWalker.ExtractPrintableStrings(buf, minLen: 4));
        }
    }


    public static class GetInsta360SerialAfterMoov
    {
        private const int MaxReadAfterMoovBytes = 32 * 1024 * 1024;

        // Ace Pro 2 serial looks like uppercase alnum, length ~14 (yours: IBGLA2509KGEBC)
        private static readonly Regex SerialRegex =
            new(@"[A-Z0-9]{14,18}", RegexOptions.Compiled);

        public static async Task<string?> TryExtractSerialAsync(string mp4Path)
        {
            using var fs = new FileStream(mp4Path, FileMode.Open, FileAccess.Read, FileShare.Read);

            // Use Mp4AtomWalker to find the LAST top-level moov
            var moov = await Mp4AtomWalker.FindLastTopLevelAsync(fs, "moov").ConfigureAwait(false);
            if (moov == null)
                return null;

            long moovEnd = moov.Offset + moov.Size;
            if (moovEnd <= 0 || moovEnd >= fs.Length)
                return null;

            long remaining = fs.Length - moovEnd;
            if (remaining <= 0)
                return null;

            int toRead = (int)Math.Min(remaining, MaxReadAfterMoovBytes);

            fs.Seek(moovEnd, SeekOrigin.Begin);
            byte[] buf = new byte[toRead];
            int r = await fs.ReadAsync(buf, 0, toRead).ConfigureAwait(false);
            if (r <= 0) return null;
            if (r < toRead) Array.Resize(ref buf, r);

            // Quick anchor: your tail block contains "Insta360 Ace Pro"
            string ascii = Encoding.ASCII.GetString(buf);

            int instaIndex = ascii.IndexOf("Insta360", StringComparison.OrdinalIgnoreCase);
            if (instaIndex < 0)
                return null;

            // Find candidate serials in that post-moov region (keep positions!)
            var candidates = SerialRegex.Matches(ascii)
                .Cast<Match>()
                .Select(m => new { Value = m.Value, Index = m.Index })
                .Where(x => IsLikelySerial(x.Value))
                // If the same candidate appears multiple times, keep the occurrence closest to the anchor
                .GroupBy(x => x.Value)
                .Select(g => g.OrderBy(x => Math.Abs(x.Index - instaIndex)).First())
                .ToList();

            if (candidates.Count == 0)
                return null;

            // Pick the candidate closest to "Insta360"; tie-break by length closeness to 14
            var best = candidates
                .OrderBy(x => Math.Abs(x.Index - instaIndex))
                .ThenBy(x => Math.Abs(x.Value.Length - 14))
                .First();

            return best.Value;

        }

        /// <summary>
        /// Check the string resembles a Insta360 serial number
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private static bool IsLikelySerial(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;

            // Must start with capital 'I'
            if (s[0] != 'I')
                return false;

            // Length bounds (Ace Pro 2 observed length ≈ 14)
            if (s.Length < 14 || s.Length > 18)
                return false;

            // Must contain at least one letter and one digit
            bool hasLetter = false;
            bool hasDigit = false;

            foreach (char c in s)
            {
                if (char.IsLetter(c)) hasLetter = true;
                if (char.IsDigit(c)) hasDigit = true;
                if (hasLetter && hasDigit) break;
            }

            if (!hasLetter || !hasDigit)
                return false;

            // Avoid obvious non-serials / noise
            if (s.StartsWith("VID", StringComparison.Ordinal))
                return false;

            if (s.StartsWith("DCIM", StringComparison.Ordinal))
                return false;

            if (s.Contains("000000", StringComparison.Ordinal))
                return false;

            return true;
        }
    }

    /// <summary>
    /// Atom walker for MP4 files - used by Insta360 and DFI
    /// </summary>
    internal static class Mp4AtomWalker
    {
        internal sealed record Atom(
            string Type,
            long Offset,
            long Size,
            int HeaderSize,
            long PayloadOffset,
            long PayloadSize,
            Atom? Parent);

        // Common container atoms (not exhaustive, but good enough)
        private static readonly HashSet<string> ContainerTypes = new(StringComparer.Ordinal)
        {
            "moov","trak","mdia","minf","stbl","edts","dinf","udta","meta","ilst","mdta","tref","mvex","moof","traf","mfra"
        };

        internal static async Task<Atom?> FindFirstAsync(FileStream fs, string type)
        {
            fs.Seek(0, SeekOrigin.Begin);
            return await FindFirstRecursiveAsync(fs, parent: null, start: 0, end: fs.Length, type).ConfigureAwait(false);
        }

        internal static async Task<List<Atom>> ListChildrenAsync(FileStream fs, Atom parent)
        {
            var result = new List<Atom>();
            long start = parent.PayloadOffset;
            long end = parent.Offset + parent.Size;

            // meta is a FullBox and has 4 bytes version/flags; we already baked that into payload offset
            long pos = start;

            while (pos + 8 <= end)
            {
                var atom = await ReadAtomAsync(fs, parent, pos, end).ConfigureAwait(false);
                if (atom == null) break;

                result.Add(atom);
                pos += atom.Size;
            }

            return result;
        }

        internal static async Task<Atom?> FindLastTopLevelAsync(FileStream fs, string type)
        {
            fs.Seek(0, SeekOrigin.Begin);

            Atom? last = null;
            long pos = 0;
            long end = fs.Length;

            while (pos + 8 <= end)
            {
                var atom = await ReadAtomAsync(fs, parent: null, pos: pos, end: end).ConfigureAwait(false);
                if (atom == null)
                    break;

                if (atom.Type == type)
                    last = atom;

                pos += atom.Size;
            }

            return last;
        }

        private static async Task<Atom?> FindFirstRecursiveAsync(FileStream fs, Atom? parent, long start, long end, string type)
        {
            long pos = start;

            while (pos + 8 <= end)
            {
                var atom = await ReadAtomAsync(fs, parent, pos, end).ConfigureAwait(false);
                if (atom == null) break;

                if (atom.Type == type)
                    return atom;

                if (IsContainer(atom.Type))
                {
                    var found = await FindFirstRecursiveAsync(fs, atom, atom.PayloadOffset, atom.Offset + atom.Size, type).ConfigureAwait(false);
                    if (found != null) return found;
                }

                pos += atom.Size;
            }

            return null;
        }

        private static bool IsContainer(string fourcc) => ContainerTypes.Contains(fourcc);

        private static async Task<Atom?> ReadAtomAsync(FileStream fs, Atom? parent, long pos, long end)
        {
            if (pos + 8 > end) return null;

            fs.Seek(pos, SeekOrigin.Begin);
            byte[] hdr = new byte[8];
            int n = await fs.ReadAsync(hdr, 0, 8).ConfigureAwait(false);
            if (n < 8) return null;

            uint size32 = BinaryPrimitives.ReadUInt32BigEndian(hdr.AsSpan(0, 4));
            string type = Encoding.ASCII.GetString(hdr, 4, 4);

            long size;
            int headerSize = 8;

            if (size32 == 0)
            {
                size = end - pos; // to end of parent box
            }
            else if (size32 == 1)
            {
                byte[] ext = new byte[8];
                int n2 = await fs.ReadAsync(ext, 0, 8).ConfigureAwait(false);
                if (n2 < 8) return null;
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(ext);
                headerSize = 16;
            }
            else
            {
                size = size32;
            }

            if (size < headerSize || pos + size > end) return null;

            long payloadOffset = pos + headerSize;
            long payloadSize = size - headerSize;

            // QuickTime 'meta' is usually a FullBox: 4 bytes version/flags after header
            if (type == "meta" && payloadSize >= 4)
            {
                payloadOffset += 4;
                payloadSize -= 4;
            }

            return new Atom(type, pos, size, headerSize, payloadOffset, payloadSize, parent);
        }

        internal static IEnumerable<string> ExtractPrintableStrings(byte[] data, int minLen)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                bool printable = (b >= 32 && b <= 126);
                if (printable) sb.Append((char)b);
                else
                {
                    if (sb.Length >= minLen) yield return sb.ToString();
                    sb.Clear();
                }
            }
            if (sb.Length >= minLen) yield return sb.ToString();
        }

        internal static async Task<byte[]> ReadUpToAsync(FileStream fs, int maxBytes)
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
