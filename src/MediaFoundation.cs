using System;
using System.Runtime.InteropServices;

namespace AutoClickerTool
{
    /// <summary>
    /// Media Foundation 的最小 COM 互操作: 用 Windows 自带的 H.264 编码器 + MP4 复用器
    /// (mfplat.dll / mfreadwrite.dll) 写出 mp4 文件, 不引入任何第三方 DLL 或 NuGet 包。
    ///
    /// 注意: COM 接口的方法必须**严格按 vtable 顺序**声明, 用不到的方法用占位符跳过
    /// (占位符的参数列表无关紧要, 只要不调用它即可) —— 顺序错一位就会调用到别的函数。
    /// </summary>
    internal static class Mf
    {
        public const int MF_VERSION = 0x00020070; // MF_SDK_VERSION(2) << 16 | MF_API_VERSION(0x70)
        public const int MFSTARTUP_FULL = 0;
        public const int MFSTARTUP_NOSOCKET = 1;

        public const int MFVideoInterlace_Progressive = 2;

        // 注意: 这些 GUID 要用 ref 传给 COM 方法, 因此不能是 readonly(静态只读字段不能按 ref 传递)
        public static Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static Guid MF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static Guid MF_MT_AVG_BITRATE = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        public static Guid MF_MT_INTERLACE_MODE = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static Guid MF_MT_FRAME_SIZE = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        public static Guid MF_MT_FRAME_RATE = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static Guid MF_MT_PIXEL_ASPECT_RATIO = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        public static Guid MF_MT_ALL_SAMPLES_INDEPENDENT = new Guid("c9173739-5e56-461c-b713-46fb995cb95f");
        public static Guid MF_MT_DEFAULT_STRIDE = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new Guid("a634a91c-822b-41b9-a494-4de4643612b0");

        public static Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        public static Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        public static Guid MFVideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00AA00389B71");

        // ---- 平台 ----

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFStartup(int Version, int dwFlags);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFShutdown();

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateMediaType(out IMFMediaType ppMFType);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateMemoryBuffer(int cbMaxLength, out IMFMediaBuffer ppBuffer);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateSample(out IMFSample ppIMFSample);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFCreateAttributes(out IMFAttributes ppMFAttributes, int cInitialSize);

        /// <summary>把一个 UINT32 对(宽高/帧率/像素比)打包成 MF 需要的 UINT64: 高 32 位在前。</summary>
        public static long Pack2(int a, int b)
        {
            return ((long)a << 32) | (uint)b;
        }

        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int MFCreateSinkWriterFromURL(string pwszOutputURL, IntPtr pByteStream,
            IMFAttributes pAttributes, out IMFSinkWriter ppSinkWriter);

        // ---- COM 线程 ----

        [DllImport("ole32.dll")]
        public static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

        [DllImport("ole32.dll")]
        public static extern void CoUninitialize();

        public const int COINIT_MULTITHREADED = 0x0;

        public static string Hr(int hr)
        {
            return "0x" + hr.ToString("X8");
        }
    }

    // ---------------------------------------------------------------- 接口

    // 注意(踩过的坑): COM 互操作**不会**把基接口的方法自动排进子接口的 vtable ——
    // 子接口里只声明自己的新方法时, 这些方法会被派发到 slot 0,1,2...(即基接口的方法上),
    // 结果是 AddBuffer 调到 GetItem、SetSampleTime 调到 Compare 直接访问违例崩进程。
    // 所以每个派生接口都必须**完整平铺**基接口的全部方法。

    [ComImport, Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFAttributes
    {
        [PreserveSig] int _A00(); // 0 GetItem (未使用, 仅占位)
        [PreserveSig] int _A01(); // 1 GetItemType (未使用, 仅占位)
        [PreserveSig] int _A02(); // 2 CompareItem (未使用, 仅占位)
        [PreserveSig] int _A03(); // 3 Compare (未使用, 仅占位)
        [PreserveSig] int _A04(); // 4 GetUINT32 (未使用, 仅占位)
        [PreserveSig] int _A05(); // 5 GetUINT64 (未使用, 仅占位)
        [PreserveSig] int _A06(); // 6 GetDouble (未使用, 仅占位)
        [PreserveSig] int _A07(); // 7 GetGUID (未使用, 仅占位)
        [PreserveSig] int _A08(); // 8 GetStringLength (未使用, 仅占位)
        [PreserveSig] int _A09(); // 9 GetString (未使用, 仅占位)
        [PreserveSig] int _A10(); // 10 GetAllocatedString (未使用, 仅占位)
        [PreserveSig] int _A11(); // 11 GetBlobSize (未使用, 仅占位)
        [PreserveSig] int _A12(); // 12 GetBlob (未使用, 仅占位)
        [PreserveSig] int _A13(); // 13 GetAllocatedBlob (未使用, 仅占位)
        [PreserveSig] int _A14(); // 14 GetUnknown (未使用, 仅占位)
        [PreserveSig] int _A15(); // 15 SetItem (未使用, 仅占位)
        [PreserveSig] int _A16(); // 16 DeleteItem (未使用, 仅占位)
        [PreserveSig] int _A17(); // 17 DeleteAllItems (未使用, 仅占位)
        [PreserveSig] int SetUINT32(ref Guid guidKey, int unValue);   // 18
        [PreserveSig] int SetUINT64(ref Guid guidKey, long unValue);  // 19
        [PreserveSig] int _A20(); // 20 SetDouble (未使用, 仅占位)
        [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue); // 21
        [PreserveSig] int _A22(); // 22 SetString (未使用, 仅占位)
        [PreserveSig] int _A23(); // 23 SetBlob (未使用, 仅占位)
        [PreserveSig] int _A24(); // 24 SetUnknown (未使用, 仅占位)
        [PreserveSig] int _A25(); // 25 LockStore (未使用, 仅占位)
        [PreserveSig] int _A26(); // 26 UnlockStore (未使用, 仅占位)
        [PreserveSig] int _A27(); // 27 GetCount (未使用, 仅占位)
        [PreserveSig] int _A28(); // 28 GetItemByIndex (未使用, 仅占位)
        [PreserveSig] int _A29(); // 29 CopyAllItems (未使用, 仅占位)
    }

    [ComImport, Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaType
    {
        [PreserveSig] int _A00(); // 0 GetItem (未使用, 仅占位)
        [PreserveSig] int _A01(); // 1 GetItemType (未使用, 仅占位)
        [PreserveSig] int _A02(); // 2 CompareItem (未使用, 仅占位)
        [PreserveSig] int _A03(); // 3 Compare (未使用, 仅占位)
        [PreserveSig] int _A04(); // 4 GetUINT32 (未使用, 仅占位)
        [PreserveSig] int _A05(); // 5 GetUINT64 (未使用, 仅占位)
        [PreserveSig] int _A06(); // 6 GetDouble (未使用, 仅占位)
        [PreserveSig] int _A07(); // 7 GetGUID (未使用, 仅占位)
        [PreserveSig] int _A08(); // 8 GetStringLength (未使用, 仅占位)
        [PreserveSig] int _A09(); // 9 GetString (未使用, 仅占位)
        [PreserveSig] int _A10(); // 10 GetAllocatedString (未使用, 仅占位)
        [PreserveSig] int _A11(); // 11 GetBlobSize (未使用, 仅占位)
        [PreserveSig] int _A12(); // 12 GetBlob (未使用, 仅占位)
        [PreserveSig] int _A13(); // 13 GetAllocatedBlob (未使用, 仅占位)
        [PreserveSig] int _A14(); // 14 GetUnknown (未使用, 仅占位)
        [PreserveSig] int _A15(); // 15 SetItem (未使用, 仅占位)
        [PreserveSig] int _A16(); // 16 DeleteItem (未使用, 仅占位)
        [PreserveSig] int _A17(); // 17 DeleteAllItems (未使用, 仅占位)
        [PreserveSig] int SetUINT32(ref Guid guidKey, int unValue);   // 18
        [PreserveSig] int SetUINT64(ref Guid guidKey, long unValue);  // 19
        [PreserveSig] int _A20(); // 20 SetDouble (未使用, 仅占位)
        [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue); // 21
        [PreserveSig] int _A22(); // 22 SetString (未使用, 仅占位)
        [PreserveSig] int _A23(); // 23 SetBlob (未使用, 仅占位)
        [PreserveSig] int _A24(); // 24 SetUnknown (未使用, 仅占位)
        [PreserveSig] int _A25(); // 25 LockStore (未使用, 仅占位)
        [PreserveSig] int _A26(); // 26 UnlockStore (未使用, 仅占位)
        [PreserveSig] int _A27(); // 27 GetCount (未使用, 仅占位)
        [PreserveSig] int _A28(); // 28 GetItemByIndex (未使用, 仅占位)
        [PreserveSig] int _A29(); // 29 CopyAllItems (未使用, 仅占位)
        [PreserveSig] int GetMajorType(out Guid pguidMajorType);                     // 30
        [PreserveSig] int IsCompressedFormat(out int pfCompressed);                  // 31
        [PreserveSig] int IsEqual(IMFMediaType pIMediaType, out int pdwFlags);       // 32
        [PreserveSig] int GetRepresentation(ref Guid guidRepresentation, out IntPtr ppvRepresentation); // 33
        [PreserveSig] int FreeRepresentation(ref Guid guidRepresentation, IntPtr pvRepresentation);     // 34
    }

    [ComImport, Guid("045FA593-8799-42B8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr ppbBuffer, out int pcbMaxLength, out int pcbCurrentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out int pcbCurrentLength);
        [PreserveSig] int SetCurrentLength(int cbCurrentLength);
        [PreserveSig] int GetMaxLength(out int pcbMaxLength);
    }

    [ComImport, Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSample
    {
        [PreserveSig] int _A00(); // 0 GetItem (未使用, 仅占位)
        [PreserveSig] int _A01(); // 1 GetItemType (未使用, 仅占位)
        [PreserveSig] int _A02(); // 2 CompareItem (未使用, 仅占位)
        [PreserveSig] int _A03(); // 3 Compare (未使用, 仅占位)
        [PreserveSig] int _A04(); // 4 GetUINT32 (未使用, 仅占位)
        [PreserveSig] int _A05(); // 5 GetUINT64 (未使用, 仅占位)
        [PreserveSig] int _A06(); // 6 GetDouble (未使用, 仅占位)
        [PreserveSig] int _A07(); // 7 GetGUID (未使用, 仅占位)
        [PreserveSig] int _A08(); // 8 GetStringLength (未使用, 仅占位)
        [PreserveSig] int _A09(); // 9 GetString (未使用, 仅占位)
        [PreserveSig] int _A10(); // 10 GetAllocatedString (未使用, 仅占位)
        [PreserveSig] int _A11(); // 11 GetBlobSize (未使用, 仅占位)
        [PreserveSig] int _A12(); // 12 GetBlob (未使用, 仅占位)
        [PreserveSig] int _A13(); // 13 GetAllocatedBlob (未使用, 仅占位)
        [PreserveSig] int _A14(); // 14 GetUnknown (未使用, 仅占位)
        [PreserveSig] int _A15(); // 15 SetItem (未使用, 仅占位)
        [PreserveSig] int _A16(); // 16 DeleteItem (未使用, 仅占位)
        [PreserveSig] int _A17(); // 17 DeleteAllItems (未使用, 仅占位)
        [PreserveSig] int SetUINT32(ref Guid guidKey, int unValue);   // 18
        [PreserveSig] int SetUINT64(ref Guid guidKey, long unValue);  // 19
        [PreserveSig] int _A20(); // 20 SetDouble (未使用, 仅占位)
        [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue); // 21
        [PreserveSig] int _A22(); // 22 SetString (未使用, 仅占位)
        [PreserveSig] int _A23(); // 23 SetBlob (未使用, 仅占位)
        [PreserveSig] int _A24(); // 24 SetUnknown (未使用, 仅占位)
        [PreserveSig] int _A25(); // 25 LockStore (未使用, 仅占位)
        [PreserveSig] int _A26(); // 26 UnlockStore (未使用, 仅占位)
        [PreserveSig] int _A27(); // 27 GetCount (未使用, 仅占位)
        [PreserveSig] int _A28(); // 28 GetItemByIndex (未使用, 仅占位)
        [PreserveSig] int _A29(); // 29 CopyAllItems (未使用, 仅占位)
        [PreserveSig] int GetSampleFlags(out int pdwSampleFlags);              // 30
        [PreserveSig] int SetSampleFlags(int dwSampleFlags);                   // 31
        [PreserveSig] int GetSampleTime(out long phnsSampleTime);              // 32
        [PreserveSig] int SetSampleTime(long hnsSampleTime);                   // 33
        [PreserveSig] int GetSampleDuration(out long phnsSampleDuration);      // 34
        [PreserveSig] int SetSampleDuration(long hnsSampleDuration);           // 35
        [PreserveSig] int GetBufferCount(out int pdwBufferCount);              // 36
        [PreserveSig] int GetBufferByIndex(int dwIndex, out IMFMediaBuffer ppBuffer); // 37
        [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);     // 38
        [PreserveSig] int AddBuffer(IMFMediaBuffer pBuffer);                   // 39
        [PreserveSig] int RemoveBufferByIndex(int dwIndex);                    // 40
        [PreserveSig] int RemoveAllBuffers();                                  // 41
        [PreserveSig] int GetTotalLength(out int pcbTotalLength);              // 42
        [PreserveSig] int CopyToBuffer(IMFMediaBuffer pBuffer);                // 43
    }

    [ComImport, Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSinkWriter
    {
        [PreserveSig] int AddStream(IMFMediaType pTargetMediaType, out int pdwStreamIndex);
        [PreserveSig] int SetInputMediaType(int dwStreamIndex, IMFMediaType pInputMediaType, IMFAttributes pEncodingParameters);
        [PreserveSig] int BeginWriting();
        [PreserveSig] int WriteSample(int dwStreamIndex, IMFSample pSample);
        [PreserveSig] int SendStreamTick(int dwStreamIndex, long llTimestamp);
        [PreserveSig] int PlaceMarker(int dwStreamIndex, IntPtr pvContext);
        [PreserveSig] int NotifyEndOfSegment(int dwStreamIndex);
        [PreserveSig] int Flush(int dwStreamIndex);
        [PreserveSig] int FinalizeWriter();   // Finalize(): 名字随意, 顺序才对
        [PreserveSig] int GetServiceForStream(int dwStreamIndex, ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] int GetStatistics(int dwStreamIndex, IntPtr pStats);
    }

    // ---------------------------------------------------------------- MP4 写出器

    /// <summary>
    /// 把一帧帧 BGRA 位图写进 mp4(H.264)。每帧直接拷进复用的 MF 缓冲区, 不额外分配。
    /// 时间戳由调用方按真实时钟给出(掉帧时视频时长仍然正确)。
    /// </summary>
    internal sealed class Mp4Writer : IDisposable
    {
        private IMFSinkWriter _writer;
        private IMFSample _sample;
        private IMFMediaBuffer _buffer;
        private int _streamIndex;
        private int _frameBytes;
        private bool _finished;

        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>是否用上了硬件编码器(硬件不可用时自动退回软件编码, 帧率会明显下降)。</summary>
        public bool HardwareEncoder { get; private set; }

        /// <summary>打开 mp4 输出; 失败时返回错误描述(非 null), 成功返回 null。</summary>
        public static string Open(string path, int width, int height, int fps, int bitrate,
            bool topDown, out Mp4Writer writer)
        {
            writer = null;
            if (width <= 0 || height <= 0) return "分辨率无效";
            if (fps <= 0) fps = 30;

            // 先尝试启用硬件编码器(Intel/AMD/NVIDIA 的 H.264 MFT), 失败再退回纯软件编码
            string err = TryOpen(path, width, height, fps, bitrate, topDown, true, out writer);
            if (err == null) return null;
            err = TryOpen(path, width, height, fps, bitrate, topDown, false, out writer);
            return err;
        }

        private static string TryOpen(string path, int width, int height, int fps, int bitrate,
            bool topDown, bool hardware, out Mp4Writer writer)
        {
            writer = null;
            IMFMediaType outType = null;
            IMFMediaType inType = null;
            IMFAttributes attrs = null;
            IMFSinkWriter sink = null;
            try
            {
                int hr = Mf.MFCreateMediaType(out outType);
                if (hr < 0) return "MFCreateMediaType 失败 " + Mf.Hr(hr);
                hr = outType.SetGUID(ref Mf.MF_MT_MAJOR_TYPE, ref Mf.MFMediaType_Video);
                if (hr < 0) return "设置主类型失败 " + Mf.Hr(hr);
                hr = outType.SetGUID(ref Mf.MF_MT_SUBTYPE, ref Mf.MFVideoFormat_H264);
                if (hr < 0) return "设置 H.264 子类型失败(系统缺少 H.264 编码器?) " + Mf.Hr(hr);
                outType.SetUINT32(ref Mf.MF_MT_AVG_BITRATE, bitrate);
                outType.SetUINT32(ref Mf.MF_MT_INTERLACE_MODE, Mf.MFVideoInterlace_Progressive);
                outType.SetUINT64(ref Mf.MF_MT_FRAME_SIZE, Mf.Pack2(width, height));
                outType.SetUINT64(ref Mf.MF_MT_FRAME_RATE, Mf.Pack2(fps, 1));
                outType.SetUINT64(ref Mf.MF_MT_PIXEL_ASPECT_RATIO, Mf.Pack2(1, 1));

                hr = Mf.MFCreateMediaType(out inType);
                if (hr < 0) return "MFCreateMediaType(输入) 失败 " + Mf.Hr(hr);
                inType.SetGUID(ref Mf.MF_MT_MAJOR_TYPE, ref Mf.MFMediaType_Video);
                inType.SetGUID(ref Mf.MF_MT_SUBTYPE, ref Mf.MFVideoFormat_RGB32);
                inType.SetUINT32(ref Mf.MF_MT_INTERLACE_MODE, Mf.MFVideoInterlace_Progressive);
                inType.SetUINT32(ref Mf.MF_MT_ALL_SAMPLES_INDEPENDENT, 1);
                // MF_MT_DEFAULT_STRIDE 的正负与画面上下方向有关。本机实测(Windows 11 + 系统内置 H.264 MFT):
                // 自上而下的位图必须配**正** stride, 给负值会让录出来的视频上下颠倒
                // (用 tests 目录外的 mftest 自检脚本录"上半红/下半蓝"验证过)。以实测为准。
                int stride = width * 4;
                if (!topDown) stride = -stride;
                inType.SetUINT32(ref Mf.MF_MT_DEFAULT_STRIDE, stride);
                inType.SetUINT64(ref Mf.MF_MT_FRAME_SIZE, Mf.Pack2(width, height));
                inType.SetUINT64(ref Mf.MF_MT_FRAME_RATE, Mf.Pack2(fps, 1));
                inType.SetUINT64(ref Mf.MF_MT_PIXEL_ASPECT_RATIO, Mf.Pack2(1, 1));

                if (hardware)
                {
                    if (Mf.MFCreateAttributes(out attrs, 1) >= 0)
                    {
                        attrs.SetUINT32(ref Mf.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
                    }
                    else attrs = null;
                }

                hr = Mf.MFCreateSinkWriterFromURL(path, IntPtr.Zero, attrs, out sink);
                if (hr < 0) return "创建 MP4 写出器失败(路径不可写?) " + Mf.Hr(hr);

                int stream;
                hr = sink.AddStream(outType, out stream);
                if (hr < 0) return "添加视频流失败 " + Mf.Hr(hr);
                hr = sink.SetInputMediaType(stream, inType, null);
                if (hr < 0) return "协商输入格式失败 " + Mf.Hr(hr);
                hr = sink.BeginWriting();
                if (hr < 0) return "开始写入失败 " + Mf.Hr(hr);

                int frameBytes = width * 4 * height;
                IMFMediaBuffer buf;
                hr = Mf.MFCreateMemoryBuffer(frameBytes, out buf);
                if (hr < 0) return "分配帧缓冲失败 " + Mf.Hr(hr);
                IMFSample sample;
                hr = Mf.MFCreateSample(out sample);
                if (hr < 0)
                {
                    Marshal.ReleaseComObject(buf);
                    return "创建帧样本失败 " + Mf.Hr(hr);
                }
                sample.AddBuffer(buf);

                var w = new Mp4Writer();
                w._writer = sink;
                w._sample = sample;
                w._buffer = buf;
                w._streamIndex = stream;
                w._frameBytes = frameBytes;
                w.Width = width;
                w.Height = height;
                w.HardwareEncoder = hardware;
                writer = w;
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            finally
            {
                if (outType != null) Marshal.ReleaseComObject(outType);
                if (inType != null) Marshal.ReleaseComObject(inType);
                if (attrs != null) Marshal.ReleaseComObject(attrs);
                if (writer == null && sink != null)
                {
                    try { sink.FinalizeWriter(); } catch (Exception) { }
                    Marshal.ReleaseComObject(sink);
                }
            }
        }

        /// <summary>写入一帧(自上而下排列的 BGRA 像素); 返回错误描述或 null。</summary>
        public string WriteFrame(IntPtr pixels, long timeHns, long durationHns)
        {
            if (_writer == null || _finished) return "写出器已关闭";
            IntPtr dst;
            int max, cur;
            int hr = _buffer.Lock(out dst, out max, out cur);
            if (hr < 0) return "锁定帧缓冲失败 " + Mf.Hr(hr);
            try
            {
                int n = _frameBytes < max ? _frameBytes : max;
                CopyMemory(dst, pixels, new IntPtr(n));
                _buffer.SetCurrentLength(n);
            }
            finally
            {
                _buffer.Unlock();
            }
            _sample.SetSampleTime(timeHns);
            _sample.SetSampleDuration(durationHns);
            hr = _writer.WriteSample(_streamIndex, _sample);
            if (hr < 0) return "写入帧失败 " + Mf.Hr(hr);
            return null;
        }

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyMemory(IntPtr dest, IntPtr src, IntPtr count);

        /// <summary>收尾: 写完 moov 索引, 生成的 mp4 才算完整可播。</summary>
        public string Finish()
        {
            if (_finished) return null;
            _finished = true;
            string err = null;
            try
            {
                if (_writer != null)
                {
                    int hr = _writer.FinalizeWriter();
                    if (hr < 0) err = "关闭 MP4 失败 " + Mf.Hr(hr);
                }
            }
            catch (Exception ex)
            {
                err = ex.Message;
            }
            finally
            {
                if (_sample != null) { try { Marshal.ReleaseComObject(_sample); } catch (Exception) { } _sample = null; }
                if (_buffer != null) { try { Marshal.ReleaseComObject(_buffer); } catch (Exception) { } _buffer = null; }
                if (_writer != null) { try { Marshal.ReleaseComObject(_writer); } catch (Exception) { } _writer = null; }
            }
            return err;
        }

        public void Dispose()
        {
            Finish();
        }
    }
}