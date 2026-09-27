using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading;

namespace AutoClickerTool
{
    /// <summary>
    /// 录屏引擎: GDI 抓屏(BitBlt/StretchBlt/PrintWindow) + Media Foundation H.264 编码出 mp4。
    ///
    /// 设计要点:
    ///  - 录制跑在后台线程; 每帧直接 Blit 进自建的 DIB section(自上而下 32bpp), 再整块拷进
    ///    复用的 MF 缓冲 → 全流程只有一次内存拷贝, 不产生每帧的 GC 垃圾。
    ///  - 时间戳用真实时钟(掉帧时视频时长仍然正确, 不会把 10 秒录成 6 秒)。
    ///  - 暂停时不计时: 恢复后时间戳接着走, 暂停的片段不会进视频。
    ///  - Stop() 只置标志, finally 里一定 Finalize(写 moov 索引), 否则 mp4 不可播。
    /// </summary>
    internal sealed class ScreenRecorder
    {
        /// <summary>一块显示器(用于"指定屏幕"下拉框)。</summary>
        internal sealed class MonitorEntry
        {
            public IntPtr Handle;
            public Rectangle Bounds;
            public bool Primary;
            public string Device;
        }

        /// <summary>一个可录制的顶层窗口。</summary>
        internal sealed class WindowEntry
        {
            public IntPtr Handle;
            public string Title;
        }

        private volatile bool _running;
        private volatile bool _stop;
        private volatile bool _paused;
        private volatile int _frames;
        private volatile int _elapsedMs;   // 已录制时长(不含暂停), UI 线程读取
        private volatile bool _hardware;   // 本次录制是否走硬件编码
        private Thread _thread;
        private string _outputPath = "";
        private string _lastError;
        private Rectangle _lastSource;

        // 录制设置(Start 前由 UI 线程写入)
        private int _targetMode;     // 0 = 指定屏幕, 1 = 指定窗口
        private int _monitorIndex;
        private IntPtr _window;
        private int _outHeight;      // 0 = 与源一致
        private int _fps = 30;
        private bool _drawCursor = true;

        public bool Running { get { return _running; } }
        public bool Paused { get { return _paused; } }
        public int FrameCount { get { return _frames; } }

        /// <summary>已录制时长(秒, 不含暂停时间)。</summary>
        public double Seconds { get { return _elapsedMs / 1000.0; } }
        public string OutputPath { get { return _outputPath; } }
        public string LastError { get { return _lastError; } }

        /// <summary>是否用上了硬件编码器(硬件不可用时自动退回软件编码)。</summary>
        public bool HardwareEncoder { get { return _hardware; } }
        public Rectangle LastSource { get { return _lastSource; } }

        /// <summary>录制结束(含保存完成), 后台线程回调。</summary>
        public event Action Finished;

        // ---------------------------------------------------------------- 静态枚举

        /// <summary>枚举所有显示器(左到右排序, 与显示设置里的编号一致)。</summary>
        public static List<MonitorEntry> GetMonitors()
        {
            var list = new List<MonitorEntry>();
            try
            {
                NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr hMon, IntPtr hdc, ref NativeMethods.RECT r, IntPtr data)
                {
                    var mi = new NativeMethods.MONITORINFOEX();
                    mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));
                    bool primary = false;
                    string dev = "";
                    if (NativeMethods.GetMonitorInfo(hMon, ref mi))
                    {
                        primary = (mi.dwFlags & 1) != 0; // MONITORINFOF_PRIMARY
                        dev = mi.szDevice == null ? "" : mi.szDevice;
                    }
                    list.Add(new MonitorEntry
                    {
                        Handle = hMon,
                        Bounds = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top),
                        Primary = primary,
                        Device = dev
                    });
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception)
            {
            }
            list.Sort(delegate(MonitorEntry a, MonitorEntry b) { return a.Bounds.Left.CompareTo(b.Bounds.Left); });
            return list;
        }

        /// <summary>枚举可作为录制目标的顶层窗口(可见、有标题、非本程序自身)。</summary>
        public static List<WindowEntry> GetWindows()
        {
            var list = new List<WindowEntry>();
            uint self = (uint)Process.GetCurrentProcess().Id;
            try
            {
                NativeMethods.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
                {
                    try
                    {
                        if (!NativeMethods.IsWindowVisible(hWnd)) return true;
                        if (NativeMethods.GetParent(hWnd) != IntPtr.Zero) return true; // 只要顶层窗口
                        int len = NativeMethods.GetWindowTextLength(hWnd);
                        if (len <= 0) return true;
                        uint pid;
                        NativeMethods.GetWindowThreadProcessId(hWnd, out pid);
                        if (pid == self) return true;
                        var sb = new StringBuilder(len + 2);
                        NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
                        string title = sb.ToString();
                        if (title.Length == 0) return true;
                        list.Add(new WindowEntry { Handle = hWnd, Title = title });
                    }
                    catch (Exception)
                    {
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception)
            {
            }
            list.Sort(delegate(WindowEntry a, WindowEntry b) { return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase); });
            return list;
        }

        /// <summary>窗口矩形(不含最小化窗口); 无效返回 false。</summary>
        public static bool GetWindowRect(IntPtr hwnd, out Rectangle rect)
        {
            rect = Rectangle.Empty;
            if (hwnd == IntPtr.Zero) return false;
            if (NativeMethods.IsIconic(hwnd)) return false;
            NativeMethods.RECT r;
            if (!NativeMethods.GetWindowRect(hwnd, out r)) return false;
            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;
            if (w < 16 || h < 16) return false;
            rect = new Rectangle(r.Left, r.Top, w, h);
            return true;
        }

        // ---------------------------------------------------------------- 控制

        public bool Start(int targetMode, int monitorIndex, IntPtr window, int outHeight, int fps,
            string outputPath, bool drawCursor)
        {
            if (_running) return false;
            _targetMode = targetMode;
            _monitorIndex = monitorIndex;
            _window = window;
            _outHeight = outHeight;
            _fps = fps > 0 && fps <= 120 ? fps : 30;
            _outputPath = outputPath;
            _drawCursor = drawCursor;
            _lastError = null;
            _stop = false;
            _paused = false;
            _frames = 0;
            _elapsedMs = 0;
            _lastError = null;
            _running = true;
            _thread = new Thread(Run) { IsBackground = true, Name = "ScreenRecorder" };
            _thread.Start();
            return true;
        }

        public void Pause() { if (_running) _paused = true; }
        public void Resume() { _paused = false; }
        public void Stop() { _stop = true; _paused = false; }

        /// <summary>退出前等待线程收尾(超时后中止, 保证 mp4 已 Finalize)。</summary>
        public void WaitExit(int ms)
        {
            Thread t = _thread;
            if (t == null || t == Thread.CurrentThread) return;
            try
            {
                if (!t.Join(ms))
                {
                    try { t.Abort(); } catch (Exception) { }
                    try { t.Join(300); } catch (Exception) { }
                }
            }
            catch (Exception)
            {
            }
        }

        // ---------------------------------------------------------------- 主循环

        private void Run()
        {
            int coInit = Mf.CoInitializeEx(IntPtr.Zero, Mf.COINIT_MULTITHREADED);
            int mfStart = Mf.MFStartup(Mf.MF_VERSION, Mf.MFSTARTUP_FULL);
            // 把系统计时器精度提到 1ms: 否则 Thread.Sleep 最小 ~15.6ms, 30fps 的节拍会被拖到 ~24fps
            bool timerRaised = NativeMethods.TimeBeginPeriod(1) == 0;

            IntPtr memDc = IntPtr.Zero, dib = IntPtr.Zero, oldDib = IntPtr.Zero, bits = IntPtr.Zero;
            IntPtr srcDc = IntPtr.Zero, srcDib = IntPtr.Zero, srcOld = IntPtr.Zero, srcBits = IntPtr.Zero;
            Mp4Writer writer = null;
            var clock = Stopwatch.StartNew();
            long pausedMs = 0;
            long pauseStart = 0;
            int outW = 0, outH = 0;
            try
            {
                if (mfStart < 0)
                {
                    Fail("Media Foundation 初始化失败 " + Mf.Hr(mfStart));
                    return;
                }

                Rectangle src;
                if (!ResolveSource(out src))
                {
                    Fail(_targetMode == 1 ? "录制目标窗口已关闭或最小化" : "找不到录制屏幕");
                    return;
                }
                ComputeOutput(src.Width, src.Height, out outW, out outH);

                IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
                memDc = NativeMethods.CreateCompatibleDC(screenDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                if (memDc == IntPtr.Zero)
                {
                    Fail("创建内存 DC 失败");
                    return;
                }
                if (!CreateDib(memDc, outW, outH, out dib, out oldDib, out bits))
                {
                    Fail("创建位图缓冲失败(分辨率过大?)");
                    return;
                }

                int bitrate = ComputeBitrate(outW, outH, _fps);
                string err = Mp4Writer.Open(_outputPath, outW, outH, _fps, bitrate, true, out writer);
                if (err != null)
                {
                    Fail(err);
                    return;
                }
                _hardware = writer.HardwareEncoder;
                Log.Info(string.Format("录屏开始: {0}x{1} {2}fps 码率={3}kbps 编码器={4}",
                    outW, outH, _fps, bitrate / 1000, _hardware ? "硬件" : "软件"));

                // 初始化(建编码器/协商格式)可能要几百毫秒: 这里重启计时, 让第一帧的时间戳从 0 开始,
                // 否则视频开头会多出一段"第一帧静止"的空白
                clock.Restart();
                pausedMs = 0;
                long frameDur = 10000000L / _fps;   // 100ns 单位
                int frameInterval = Math.Max(1, 1000 / _fps);
                long nextMs = frameInterval;

                while (!_stop)
                {
                    if (_paused)
                    {
                        if (pauseStart == 0) pauseStart = clock.ElapsedMilliseconds;
                        Thread.Sleep(30);
                        continue;
                    }
                    if (pauseStart != 0)
                    {
                        pausedMs += clock.ElapsedMilliseconds - pauseStart;
                        pauseStart = 0;
                    }

                    if (_targetMode == 1)
                    {
                        Rectangle cur;
                        if (!GetWindowRect(_window, out cur))
                        {
                            // 窗口没了/最小化了: 保留上一帧的时间轴, 稍后重试
                            Thread.Sleep(50);
                            continue;
                        }
                        src = cur;
                    }
                    _lastSource = src;

                    GrabFrame(memDc, ref srcDc, ref srcDib, ref srcOld, ref srcBits, src, outW, outH);
                    if (_drawCursor) DrawCursor(memDc, src, outW, outH);

                    long nowMs = clock.ElapsedMilliseconds - pausedMs;
                    _elapsedMs = (int)nowMs;
                    err = writer.WriteFrame(bits, nowMs * 10000L, frameDur);
                    if (err != null)
                    {
                        Fail(err);
                        return;
                    }
                    _frames++;

                    // 节拍: 先粗睡到接近目标, 最后 2ms 忙等(SpinWait)。
                    // 注意不能用 Thread.Sleep(0) 收尾: 它会让出时间片, 可能多等一整个调度时隙(~15ms),
                    // 会把 30fps 拖成 24fps、60fps 拖成 50fps(实测)。配合 timeBeginPeriod(1) 才跑得满。
                    long wait = nextMs - (clock.ElapsedMilliseconds - pausedMs);
                    if (wait > 3) Thread.Sleep((int)wait - 2);
                    while (!_stop && clock.ElapsedMilliseconds - pausedMs < nextMs) Thread.SpinWait(400);
                    if (wait < -250) nextMs = clock.ElapsedMilliseconds - pausedMs; // 落后太多: 重新锚定节拍
                    nextMs += frameInterval;
                }
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
            finally
            {
                if (writer != null)
                {
                    string err = writer.Finish();
                    if (err != null) _lastError = err;
                    writer = null;
                }
                if (srcOld != IntPtr.Zero && srcDc != IntPtr.Zero) NativeMethods.SelectObject(srcDc, srcOld);
                if (srcDib != IntPtr.Zero) NativeMethods.DeleteObject(srcDib);
                if (srcDc != IntPtr.Zero) NativeMethods.DeleteDC(srcDc);
                if (oldDib != IntPtr.Zero && memDc != IntPtr.Zero) NativeMethods.SelectObject(memDc, oldDib);
                if (dib != IntPtr.Zero) NativeMethods.DeleteObject(dib);
                if (memDc != IntPtr.Zero) NativeMethods.DeleteDC(memDc);
                if (timerRaised) { try { NativeMethods.TimeEndPeriod(1); } catch (Exception) { } }
                if (mfStart >= 0) { try { Mf.MFShutdown(); } catch (Exception) { } }
                if (coInit >= 0) { try { Mf.CoUninitialize(); } catch (Exception) { } }
                _running = false;
                _paused = false;
                var h = Finished;
                if (h != null) h();
            }
        }

        private void Fail(string message)
        {
            _lastError = message;
            try { Log.Warn("录屏失败: " + message); } catch (Exception) { }
        }

        private bool ResolveSource(out Rectangle src)
        {
            src = Rectangle.Empty;
            if (_targetMode == 1) return GetWindowRect(_window, out src);
            var monitors = GetMonitors();
            if (monitors.Count == 0) return false;
            int idx = _monitorIndex;
            if (idx < 0 || idx >= monitors.Count) idx = 0;
            src = monitors[idx].Bounds;
            return src.Width >= 16 && src.Height >= 16;
        }

        /// <summary>按目标高度等比缩放(0 = 与源一致); 宽高取偶数(H.264 要求), 并限制在 4096 内。</summary>
        private void ComputeOutput(int srcW, int srcH, out int w, out int h)
        {
            h = _outHeight > 0 ? _outHeight : srcH;
            w = (int)Math.Round((double)srcW * h / srcH);
            if (w > 4096) { w = 4096; h = (int)Math.Round((double)srcH * w / srcW); }
            if (h > 4096) { h = 4096; w = (int)Math.Round((double)srcW * h / srcH); }
            w &= ~1;
            h &= ~1;
            if (w < 16) w = 16;
            if (h < 16) h = 16;
        }

        /// <summary>码率按"每像素每帧 0.1 bit"估算(1080p30 ≈ 6 Mbps, 1440p30 ≈ 11 Mbps), 限制在 2~40 Mbps。</summary>
        private static int ComputeBitrate(int w, int h, int fps)
        {
            double bps = (double)w * h * fps * 0.10;
            if (bps < 2000000) bps = 2000000;
            if (bps > 40000000) bps = 40000000;
            return (int)bps;
        }

        private static bool CreateDib(IntPtr dc, int w, int h, out IntPtr dib, out IntPtr oldObj, out IntPtr bits)
        {
            var info = new NativeMethods.BITMAPINFO();
            info.bmiHeader.biSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.BITMAPINFOHEADER));
            info.bmiHeader.biWidth = w;
            info.bmiHeader.biHeight = -h;   // 负值 = 自上而下(与屏幕行序一致)
            info.bmiHeader.biPlanes = 1;
            info.bmiHeader.biBitCount = 32;
            info.bmiHeader.biCompression = 0; // BI_RGB
            dib = NativeMethods.CreateDIBSection(dc, ref info, 0, out bits, IntPtr.Zero, 0);
            oldObj = IntPtr.Zero;
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return false;
            oldObj = NativeMethods.SelectObject(dc, dib);
            return true;
        }

        /// <summary>把源矩形抓进输出位图(必要时缩放)。窗口模式优先 PrintWindow(被遮挡也能录), 失败退回抓屏。</summary>
        private void GrabFrame(IntPtr memDc, ref IntPtr srcDc, ref IntPtr srcDib, ref IntPtr srcOld, ref IntPtr srcBits,
            Rectangle src, int outW, int outH)
        {
            const uint rop = NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT;

            if (_targetMode == 1)
            {
                // 窗口: 先渲染到与窗口等大的中间位图, 再缩放进输出位图
                if (srcDib == IntPtr.Zero)
                {
                    IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
                    srcDc = NativeMethods.CreateCompatibleDC(screenDc);
                    NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                    if (srcDc == IntPtr.Zero) return;
                    if (!CreateDib(srcDc, src.Width, src.Height, out srcDib, out srcOld, out srcBits))
                    {
                        NativeMethods.DeleteDC(srcDc);
                        srcDc = IntPtr.Zero;
                        srcDib = IntPtr.Zero;
                        return;
                    }
                    // 中间位图尺寸固定为"第一次看到的窗口大小"; 窗口改变大小时重建
                    _srcW = src.Width;
                    _srcH = src.Height;
                }
                else if (_srcW != src.Width || _srcH != src.Height)
                {
                    NativeMethods.SelectObject(srcDc, srcOld);
                    NativeMethods.DeleteObject(srcDib);
                    srcDib = IntPtr.Zero;
                    if (!CreateDib(srcDc, src.Width, src.Height, out srcDib, out srcOld, out srcBits))
                    {
                        srcDib = IntPtr.Zero;
                        return;
                    }
                    _srcW = src.Width;
                    _srcH = src.Height;
                }

                bool ok = NativeMethods.PrintWindow(_window, srcDc, 2); // PW_RENDERFULLCONTENT
                if (!ok) ok = NativeMethods.PrintWindow(_window, srcDc, 0);
                if (!ok)
                {
                    // PrintWindow 不支持(部分游戏/加速渲染): 退回"抓屏幕上该区域"(可能录到遮挡窗口)
                    IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
                    NativeMethods.BitBlt(srcDc, 0, 0, _srcW, _srcH, screenDc, src.Left, src.Top, rop);
                    NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                }

                NativeMethods.SetStretchBltMode(memDc, NativeMethods.HALFTONE);
                NativeMethods.SetBrushOrgEx(memDc, 0, 0, IntPtr.Zero);
                if (_srcW == outW && _srcH == outH)
                    NativeMethods.BitBlt(memDc, 0, 0, outW, outH, srcDc, 0, 0, NativeMethods.SRCCOPY);
                else
                    NativeMethods.StretchBlt(memDc, 0, 0, outW, outH, srcDc, 0, 0, _srcW, _srcH, NativeMethods.SRCCOPY);
                return;
            }

            IntPtr dc = NativeMethods.GetDC(IntPtr.Zero);
            try
            {
                if (src.Width == outW && src.Height == outH)
                {
                    NativeMethods.BitBlt(memDc, 0, 0, outW, outH, dc, src.Left, src.Top, rop);
                }
                else
                {
                    NativeMethods.SetStretchBltMode(memDc, NativeMethods.HALFTONE);
                    NativeMethods.SetBrushOrgEx(memDc, 0, 0, IntPtr.Zero);
                    NativeMethods.StretchBlt(memDc, 0, 0, outW, outH, dc, src.Left, src.Top, src.Width, src.Height, rop);
                }
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, dc);
            }
        }

        private int _srcW;
        private int _srcH;

        /// <summary>把鼠标指针画进输出位图(BitBlt/PrintWindow 都不含光标)。</summary>
        private void DrawCursor(IntPtr memDc, Rectangle src, int outW, int outH)
        {
            try
            {
                var ci = new NativeMethods.CURSORINFO();
                ci.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.CURSORINFO));
                if (!NativeMethods.GetCursorInfo(ref ci)) return;
                if ((ci.flags & NativeMethods.CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero) return;

                NativeMethods.ICONINFO ii;
                if (!NativeMethods.GetIconInfo(ci.hCursor, out ii)) return;
                try
                {
                    int hotX = ii.xHotspot;
                    int hotY = ii.yHotspot;
                    double scaleX = (double)outW / src.Width;
                    double scaleY = (double)outH / src.Height;
                    int x = (int)Math.Round((ci.ptScreenPos.x - src.Left - hotX) * scaleX);
                    int y = (int)Math.Round((ci.ptScreenPos.y - src.Top - hotY) * scaleY);
                    NativeMethods.DrawIconEx(memDc, x, y, ci.hCursor, 0, 0, 0, IntPtr.Zero, NativeMethods.DI_NORMAL);
                }
                finally
                {
                    if (ii.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmMask);
                    if (ii.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmColor);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
