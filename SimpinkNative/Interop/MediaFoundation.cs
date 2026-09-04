using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace SimpinkNative.Interop
{
    [ComImport, Guid("00000000-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IUnknown
    {
        void QueryInterface(ref Guid riid, out IntPtr ppvObject);
        void AddRef();
        void Release();
    }

    [ComImport, Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFAttributes
    {
        void GetItem(ref Guid guidKey, out PropVariant pValue);
        void GetItemType(ref Guid guidKey, out MF_ATTRIBUTE_TYPE pType);
        void CompareItem(ref Guid guidKey, ref PropVariant Value, out bool pbResult);
        void Compare(ref IMFAttributes pTheirs, MF_ATTRIBUTES_MATCH_TYPE MatchType, out bool pbResult);
        void GetUINT32(ref Guid guidKey, out uint punValue);
        void GetUINT64(ref Guid guidKey, out ulong punValue);
        void GetDouble(ref Guid guidKey, out double pfValue);
        void GetGUID(ref Guid guidKey, out Guid pguidValue);
        void GetStringLength(ref Guid guidKey, out uint pcchLength);
        void GetString(ref Guid guidKey, IntPtr pwszValue, uint cchBufSize, out uint pcchLength);
        void GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out uint pcchLength);
        void GetBlobSize(ref Guid guidKey, out uint pcbBlobSize);
        void GetBlob(ref Guid guidKey, IntPtr pBuf, uint cbBufSize, out uint pcbBlobSize);
        void GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out uint pcbSize);
        void GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
        void SetItem(ref Guid guidKey, ref PropVariant Value);
        void DeleteItem(ref Guid guidKey);
        void DeleteAllItems();
        void SetUINT32(ref Guid guidKey, uint unValue);
        void SetUINT64(ref Guid guidKey, ulong unValue);
        void SetDouble(ref Guid guidKey, double fValue);
        void SetGUID(ref Guid guidKey, ref Guid guidValue);
        void SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        void SetBlob(ref Guid guidKey, IntPtr pBuf, uint cbBufSize);
        void SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object punk);
        void LockStore();
        void UnlockStore();
        void GetCount(out uint pcItems);
        void GetItemByIndex(uint unIndex, out Guid pguidKey, out PropVariant pValue);
        void CopyAllItems(IMFAttributes pDest);
    }

    [ComImport, Guid("045FA593-8799-42B8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaBuffer : IUnknown
    {
        void Lock(out IntPtr ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength);
        void Unlock();
        void GetCurrentLength(out uint pcbCurrentLength);
        void SetCurrentLength(uint cbCurrentLength);
        void GetMaxLength(out uint pcbMaxLength);
    }

    [ComImport, Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSample : IUnknown
    {
        void GetSampleTime(out long phnsSampleTime);
        void SetSampleTime(long hnsSampleTime);
        void GetSampleDuration(out long phnsSampleDuration);
        void SetSampleDuration(long hnsSampleDuration);
        void GetSampleFlags(out uint pdwSampleFlags);
        void SetSampleFlags(uint dwSampleFlags);
        void GetTotalLength(out uint pcbTotalLength);
        void AddBuffer(IMFMediaBuffer pBuffer);
        void RemoveBufferByIndex(uint dwIndex, out IMFMediaBuffer ppBuffer);
        void RemoveAllBuffers();
        void GetBufferCount(out uint pdwBufferCount);
        void GetBufferByIndex(uint dwIndex, out IMFMediaBuffer ppBuffer);
        void ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
    }

    [ComImport, Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSinkWriter : IUnknown
    {
        void AddStream(IMFAttributes pMediaTypeOut, out uint pdwStreamIndex);
        void SetInputMediaType(uint dwStreamIndex, IMFAttributes pMediaType, IMFAttributes? pEncodingParameters);
        void SendStreamTick(uint dwStreamIndex, long llTimestamp);
        void BeginWriting();
        void WriteSample(uint dwStreamIndex, IMFSample pSample);
        void Flush(uint dwStreamIndex);
        void Finalize_();
        void GetServiceForStream(uint dwStreamIndex, ref Guid guidService, ref Guid riid, out IntPtr ppv);
        void GetInputMediaType(uint dwStreamIndex, uint dwMediaTypeIndex, out IMFAttributes ppType);
        void GetOutputMediaType(uint dwStreamIndex, uint dwMediaTypeIndex, out IMFAttributes ppType);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropVariant
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr data1;
        public IntPtr data2;
    }

    public enum MF_ATTRIBUTE_TYPE
    {
        Invalid = 0,
        UINT32 = 1,
        UINT64 = 2,
        DOUBLE = 3,
        GUID = 4,
        STRING = 5,
        BLOB = 6,
        IUNKNOWN = 7,
    }

    public enum MF_ATTRIBUTES_MATCH_TYPE
    {
        OurItems = 0,
        TheirItems = 1,
        Intersection = 2,
        Union = 3,
        SmallerSet = 4,
    }

    public static class MFGuids
    {
        public static readonly Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFVideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFVideoFormat_NV12 = new Guid("3231564E-0000-0010-8000-00AA00389B71");

        public static readonly Guid MF_MT_MAJOR_TYPE = new Guid("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
        public static readonly Guid MF_MT_SUBTYPE = new Guid("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
        public static readonly Guid MF_MT_FRAME_SIZE = new Guid("1652C33D-D6B2-4012-B834-72030849A37D");
        public static readonly Guid MF_MT_FRAME_RATE = new Guid("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
        public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new Guid("C6376A1E-8D0A-4027-BE45-6D9A0AD39BB6");
        public static readonly Guid MF_MT_INTERLACE_MODE = new Guid("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");
        public static readonly Guid MF_MT_AVG_BITRATE = new Guid("20332624-FB0D-4D9E-BD0D-CBF6786C102E");
        public static readonly Guid MF_MT_DEFAULT_STRIDE = new Guid("644B4E48-1E02-4516-B0EB-C01CA9D49AC6");
        public static readonly Guid MF_MT_ALL_SAMPLES_INDEPENDENT = new Guid("C9173739-5E56-461C-B713-46FB995CB95F");
        public static readonly Guid MF_MT_MPEG2_PROFILE = new Guid("F6651B60-8E3C-4F3E-9D8D-3F2A5E1B7C9D");
        public static readonly Guid MF_TRANSCODE_CONTAINERTYPE = new Guid("150FF23F-4ABC-478B-AC4F-E1916FBA1CCA");
        public static readonly Guid MFTranscodeContainerType_MPEG4 = new Guid("DC6CD05D-B9D0-40EF-BD35-FA622C1AB28A");

        public static readonly Guid MFSampleExtension_CleanPoint = new Guid("9CDF01D9-A0F0-43BA-B077-EAA06CBD728A");
    }

    public static class MF
    {
        private static void Check(int hr)
        {
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }

        public static void MFStartup(uint Version, uint dwFlags = 0) => Check(MFStartupNative(Version, dwFlags));

        public static void MFShutdown() => Check(MFShutdownNative());

        public static void MFCreateMediaType(out IMFAttributes ppMFType) { ppMFType = null!; Check(MFCreateMediaTypeNative(out ppMFType)); }

        public static void MFCreateAttributes(out IMFAttributes ppMFAttributes, uint cInitialSize) { ppMFAttributes = null!; Check(MFCreateAttributesNative(out ppMFAttributes, cInitialSize)); }

        public static void MFCreateMemoryBuffer(uint cbMaxLength, out IMFMediaBuffer ppBuffer) { ppBuffer = null!; Check(MFCreateMemoryBufferNative(cbMaxLength, out ppBuffer)); }

        public static void MFCreateSinkWriterFromURL(string pwszOutputURL, IntPtr pByteStream, IMFAttributes? pAttributes, out IMFSinkWriter ppSinkWriter)
        {
            ppSinkWriter = null!;
            IntPtr attrsPtr = IntPtr.Zero;
            try
            {
                if (pAttributes != null)
                    attrsPtr = Marshal.GetIUnknownForObject(pAttributes);
                Check(MFCreateSinkWriterFromURLNative(pwszOutputURL, pByteStream, attrsPtr, out ppSinkWriter));
            }
            finally
            {
                if (attrsPtr != IntPtr.Zero)
                    Marshal.Release(attrsPtr);
            }
        }

        public static void MFCreateSample(out IMFSample ppIMFSample) { ppIMFSample = null!; Check(MFCreateSampleNative(out ppIMFSample)); }

        [DllImport("mfplat.dll", ExactSpelling = true, EntryPoint = "MFStartup")]
        private static extern int MFStartupNative(uint Version, uint dwFlags);

        [DllImport("mfplat.dll", ExactSpelling = true, EntryPoint = "MFShutdown")]
        private static extern int MFShutdownNative();

        [DllImport("mfplat.dll", ExactSpelling = true, EntryPoint = "MFCreateMediaType")]
        private static extern int MFCreateMediaTypeNative(out IMFAttributes ppMFType);

        [DllImport("mfplat.dll", ExactSpelling = true, EntryPoint = "MFCreateAttributes")]
        private static extern int MFCreateAttributesNative(out IMFAttributes ppMFAttributes, uint cInitialSize);

        [DllImport("mfplat.dll", ExactSpelling = true, EntryPoint = "MFCreateMemoryBuffer")]
        private static extern int MFCreateMemoryBufferNative(uint cbMaxLength, out IMFMediaBuffer ppBuffer);

        [DllImport("mfreadwrite.dll", ExactSpelling = true, EntryPoint = "MFCreateSinkWriterFromURL")]
        private static extern int MFCreateSinkWriterFromURLNative(
            [MarshalAs(UnmanagedType.LPWStr)] string pwszOutputURL,
            IntPtr pByteStream,
            IntPtr pAttributes,
            out IMFSinkWriter ppSinkWriter);

        [DllImport("mfplat.dll", ExactSpelling = true, EntryPoint = "MFCreateSample")]
        private static extern int MFCreateSampleNative(out IMFSample ppIMFSample);

        public const uint MF_VERSION = 0x00020007;
        public const uint MFSTARTUP_FULL = 0;
    }
}