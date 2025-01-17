//
// Version 1.0  03 Jan 2025
//

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;

namespace GoProMP4MetadataExtraction
{
    public static class GetMP4DataStream
    {
        // Resolve the byte stream to a Media Source
        public enum MFResolution { MediaSource = 0x1 }

        public static async Task ExtractDataStreamAsync(StorageFile? videoFile)
        {
            if (videoFile == null)
                throw new ArgumentNullException(nameof(videoFile));

            try
            {
                // Open a stream from the video file
                using (IRandomAccessStream randomAccessStream = await videoFile.OpenAsync(FileAccessMode.Read))
                {
                    // Create a Media Foundation byte stream
                    IMFByteStream byteStream = CreateMFByteStream(randomAccessStream);

                    if (byteStream == null)
                    {
                        throw new InvalidOperationException("The byteStream instance is null.");
                    }

                    // Create a Media Foundation Source Resolver
                    MFCreateSourceResolver(out IMFSourceResolver sourceResolver);
                    if (sourceResolver == null)
                    {
                        throw new InvalidOperationException("Failed to create IMFSourceResolver.");
                    }

                    sourceResolver.CreateObjectFromByteStream(byteStream, null, (int)MFResolution.MediaSource, out _, out object sourceObject);

                    IMFMediaSource mediaSource = (IMFMediaSource)sourceObject;

                    // Create a Source Reader from the Media Source
                    IMFSourceReader sourceReader;
                    MFCreateSourceReaderFromMediaSource(mediaSource, IntPtr.Zero, out sourceReader);

                    // Enumerate the streams to find the data stream
                    int streamIndex = -1;
                    for (int i = 0; ; i++)
                    {
                        IMFMediaType? mediaType;
                        var hr = sourceReader.GetNativeMediaType(i, 0, out mediaType);

                        if (hr != 0 || mediaType is null) // Break on error (end of streams)
                            break;

                        Guid majorType;
                        CheckHR(mediaType.GetMajorType(out majorType));

                        if (majorType == new Guid("73647561-0000-0010-8000-00AA00389B71")) // new Guid("73647561-0000-0010-8000-00AA00389B71") // MFMediaType_Data GUID GUID
                        {
                            streamIndex = i;
                            break;
                        }
                    }

                    if (streamIndex == -1)
                    {
                        Console.WriteLine("No data stream found in the MP4 file.");
                        return;
                    }

                    // Read packets from the data stream
                    while (true)
                    {
                        var hr = sourceReader.ReadSample(streamIndex, 0, out _, out _, out _, out IMFSample? sample);

                        if (hr != 0 || sample is null) // End of stream or error
                            break;

                        // Access the sample buffer
                        IMFMediaBuffer? buffer;
                        buffer = GetBuffer(sample);

                        if (buffer == null)
                            continue;

                        IntPtr rawData;
                        int maxLength, currentLength;
                        buffer.Lock(out rawData, out maxLength, out currentLength);

                        // Process raw data (replace with your custom parsing logic)
                        byte[] data = new byte[currentLength];
                        Marshal.Copy(rawData, data, 0, currentLength);
                        ProcessRawData(data); // Custom function for handling data

                        buffer.Unlock();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting data stream: {ex.Message}");
            }
        }

        private static IMFByteStream CreateMFByteStream(IRandomAccessStream randomAccessStream)
        {
            // Get the IStream interface from IRandomAccessStream
            var comStream = WindowsRuntimeStreamExtensions.AsStreamForRead(randomAccessStream).AsInputStream();
            return new MFByteStreamWrapper(comStream);
        }

        private static void ProcessRawData(byte[] data)
        {
            // Implement your custom logic for processing raw data
            Console.WriteLine($"Processed {data.Length} bytes from the data stream.");
        }

        private static void CheckHR(int hr)
        {
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }
        }

        [DllImport("mfplat.dll", ExactSpelling = true, PreserveSig = false)]
        private static extern void MFCreateSourceResolver(out IMFSourceResolver sourceResolver);

        [DllImport("mf.dll", ExactSpelling = true, PreserveSig = false)]
        private static extern void MFCreateSourceReaderFromMediaSource(IMFMediaSource mediaSource, IntPtr attributes, out IMFSourceReader sourceReader);

        // Required interfaces (use Windows SDK or MF objects definitions)
        [ComImport, Guid("e7f2e42c-6d9c-4d1b-a8ad-fbfe73cabc3a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFSourceReader
        {
            [PreserveSig]
            int ReadSample(
                int streamIndex,
                int controlFlags,
                out int actualStreamIndex,
                out int streamFlags,
                out long timestamp,
                out IMFSample? sample
            );

            [PreserveSig]
            int GetNativeMediaType(int streamIndex, int mediaTypeIndex, out IMFMediaType? mediaType);
        }

        [ComImport, Guid("c7a4a1a1-9f2d-4a8f-aed1-8e1a3b3be7c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFMediaBuffer
        {
            void Lock(out IntPtr ppBuffer, out int pcbMaxLength, out int pcbCurrentLength);
            void Unlock();
        }

        [ComImport, Guid("bfc99d31-80b0-4e97-9b5d-16e597424e3b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFMediaSource { }

        [ComImport, Guid("b022e5ac-7e0f-4d5d-9c25-62e1d0b2d3e7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFMediaType
        {
            int GetMajorType(out Guid guid);
        }

        [ComImport, Guid("ad4c1b00-4bf7-422f-9175-756693d9130d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFByteStream
        {
            void Read(IntPtr pb, int cb, out int pcbRead);
            void Write(IntPtr pb, int cb, out int pcbWritten);
            void Seek(long SeekOrigin, long qwPosition, uint dwSeekFlags, out long pqwCurrent);
            void GetCurrentPosition(out long pqwPosition);
            void SetCurrentPosition(long qwPosition);
            void GetLength(out long pqwLength);
            void SetLength(long qwLength);
            void Close();
        }

        [ComImport, Guid("fbe5a32d-a497-4b61-bb85-97b1a848a6e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFSourceResolver
        {
            void CreateObjectFromByteStream(IMFByteStream pByteStream, [MarshalAs(UnmanagedType.LPWStr)] string? pwszURL, int dwFlags, out int pObjectType, out object ppObject);
            void CreateObjectFromURL([MarshalAs(UnmanagedType.LPWStr)] string pwszURL, int dwFlags, ref Guid pwszMimeType, out int pObjectType, out object ppObject);
        }

        public class MFByteStreamWrapper : IMFByteStream
        {
            private readonly IInputStream _inputStream;
            private long _position;
            private readonly long _length;

            public MFByteStreamWrapper(IInputStream inputStream)
            {
                _inputStream = inputStream ?? throw new ArgumentNullException(nameof(inputStream));
                _position = 0;
                _length = GetStreamLength(inputStream); // Implement a method to get stream length
            }

            public void Read(IntPtr pb, int cb, out int pcbRead)
            {
                if (pb == IntPtr.Zero)
                    throw new ArgumentNullException(nameof(pb));

                var buffer = new byte[cb];
                var result = _inputStream.ReadAsync(buffer.AsBuffer(), (uint)cb, InputStreamOptions.None).GetResults();
                Marshal.Copy(buffer, 0, pb, (int)result.Length);
                pcbRead = (int)result.Length;

                _position += pcbRead;
            }

            public void Write(IntPtr pb, int cb, out int pcbWritten)
            {
                throw new NotSupportedException("Write operation is not supported.");
            }

            public void Seek(long SeekOrigin, long qwPosition, uint dwSeekFlags, out long pqwCurrent)
            {
                if (SeekOrigin != 0) // Only supports SEEK_SET
                    throw new NotSupportedException("Seek only supports beginning of the stream.");

                if (qwPosition < 0 || qwPosition > _length)
                    throw new ArgumentOutOfRangeException(nameof(qwPosition));

                _position = qwPosition;
                pqwCurrent = _position;
            }

            public void GetCurrentPosition(out long pqwPosition)
            {
                pqwPosition = _position;
            }

            public void SetCurrentPosition(long qwPosition)
            {
                if (qwPosition < 0 || qwPosition > _length)
                    throw new ArgumentOutOfRangeException(nameof(qwPosition));

                _position = qwPosition;
            }

            public void GetLength(out long pqwLength)
            {
                pqwLength = _length;
            }

            public void SetLength(long qwLength)
            {
                throw new NotSupportedException("Setting the length of the stream is not supported.");
            }

            public void Close()
            {
                // Dispose of the input stream if necessary
                (_inputStream as IDisposable)?.Dispose();
            }

            private long GetStreamLength(IInputStream inputStream)
            {
                // If available, calculate or estimate the length of the stream
                // This is placeholder logic; adapt as needed.
                return long.MaxValue; // For streams of unknown length
            }
        }



        [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMFSample
        {
            void GetBufferCount(out int bufferCount);
            void GetBufferByIndex(int index, out IMFMediaBuffer buffer);
            void ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
            void AddBuffer(IMFMediaBuffer buffer);
            void RemoveBufferByIndex(int index);
            void GetSampleTime(out long sampleTime);
            void SetSampleTime(long sampleTime);
            void GetSampleDuration(out long sampleDuration);
            void SetSampleDuration(long sampleDuration);
        }

        private static IMFMediaBuffer? GetBuffer(IMFSample sample)
        {
            sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
            return buffer;
        }

    }
   
}
