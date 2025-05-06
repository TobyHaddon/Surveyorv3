//
// Version 1.1  13 Jan 2025
// Fixed zero length arrays not returning null
// 

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Storage;

namespace GoProMP4MetadataExtraction
{
    public static class GetMP4UtdaFileProperities
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
}
