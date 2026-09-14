using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AutoClickerTool
{
    /// <summary>
    /// 音效播放器: 用系统 MCI(wimmm.dll) 播放 wav/mp3, 零外部依赖。
    /// 覆盖式播放: 新音效开始前停止并关闭正在播放的旧音效。
    /// </summary>
    internal static class SfxPlayer
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciSendString(string command, StringBuilder result, int resultLen, IntPtr hwnd);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern bool mciGetErrorString(int errorCode, StringBuilder errorText, int errorTextLen);

        private static readonly object Lock = new object();
        private static string _alias;
        private static int _seq;
        private static int _volume = 1000; // 0~1000 (MCI 音量, 1000 = 100%)

        /// <summary>默认音量(0~1000, 1000=100%)。仅 Play(path) 重载使用。</summary>
        public static int Volume
        {
            get { return _volume; }
            set { _volume = Math.Max(0, Math.Min(1000, value)); }
        }

        /// <summary>
        /// 在**播放线程**上预热 MCI: 让 MCI 子系统(设备 + 隐藏通知窗口)在按键发生之前就完成初始化。
        /// 必须由将来真正调用 Play 的同一个线程调用, 否则第一次播放可能无声(需要先手动"试听"一次)。
        /// </summary>
        public static void WarmUp()
        {
            lock (Lock)
            {
                try
                {
                    var sb = new StringBuilder(64);
                    mciSendString("sysinfo all quantity", sb, sb.Capacity, IntPtr.Zero);
                }
                catch (Exception)
                {
                    // 老系统/设备缺失时忽略, 不影响后续播放尝试
                }
            }
        }

        /// <summary>播放一个音效文件; 会立即打断正在播放的音效(新按键覆盖旧音效)。使用默认音量。</summary>
        public static void Play(string path)
        {
            Play(path, _volume);
        }

        /// <summary>播放一个音效文件并指定音量(0~1000); 覆盖式播放。音量 &lt;= 0 视为静音, 不再打开设备。</summary>
        public static void Play(string path, int volume)
        {
            lock (Lock)
            {
                Stop();
                if (volume <= 0) return; // 音量 0 = 静音(含全局音量为 0)
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                int n = Interlocked.Increment(ref _seq);
                string alias = "sfx" + n;
                // 按扩展名选 MCI 设备类型; 不支持的格式 open 失败, 静默忽略
                string type = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? "waveaudio" : "mpegvideo";
                string p = path.Replace("\"", "");
                int rc = Open(p, type, alias);
                if (rc != 0)
                {
                    // 部分系统上第一次 open 会失败(设备尚未就绪), 重试一次;
                    // 重试仍失败才把 MCI 错误码与文字写进日志(第一次失败是已知的暂时现象, 不算错误)
                    rc = Open(p, type, alias);
                    if (rc != 0)
                    {
                        // 266 = MCI 设备驱动加载失败(多见于 mp3): 给出可操作的提示, 而不是让用户对着错误码猜
                        string hint = rc == 266
                            ? " — 本机 MCI 不支持播放该格式, 建议把音效转成 wav 后重新绑定"
                            : "";
                        Log.Warn("音效打开失败(" + rc + "): " + ErrorText(rc) + " - " + p + hint);
                        return;
                    }
                }
                // 应用音量(0~1000; waveaudio 支持, 不支持音量的设备静默忽略)
                mciSendString(string.Format("setaudio {0} volume to {1}", alias, Math.Max(0, Math.Min(1000, volume))), null, 0, IntPtr.Zero);
                mciSendString(string.Format("play {0} notify", alias), null, 0, IntPtr.Zero);
                _alias = alias;
            }
        }

        private static int Open(string path, string type, string alias)
        {
            return mciSendString(string.Format("open \"{0}\" type {1} alias {2}", path, type, alias), null, 0, IntPtr.Zero);
        }

        /// <summary>MCI 错误码 → 可读文字(取不到时返回空串)。</summary>
        private static string ErrorText(int code)
        {
            try
            {
                var sb = new StringBuilder(256);
                if (mciGetErrorString(code, sb, sb.Capacity)) return sb.ToString();
            }
            catch (Exception)
            {
            }
            return "";
        }

        /// <summary>停止并释放当前音效。</summary>
        public static void Stop()
        {
            if (_alias == null) return;
            mciSendString("stop " + _alias, null, 0, IntPtr.Zero);
            mciSendString("close " + _alias, null, 0, IntPtr.Zero);
            _alias = null;
        }
    }

    /// <summary>组合键音效绑定(如 Ctrl+C)。</summary>
    internal class SfxCombo
    {
        public Hotkey Combo;   // 组合键
        public string Path;    // 文件绝对路径
        public int Volume;     // 生效音量 0~1000
    }

    /// <summary>
    /// 按键音效管理器: 全局键盘钩子监听按键, 按下已绑定的键或组合键时播放对应音效。
    /// 只监听不拦截, 与热键/录制钩子互不干扰; 忽略注入按键(宏回放不会触发音效)。
    /// 钩子回调只做入队(MCI 打开/播放是磁盘 I/O, 移出回调防止 UI 卡顿与钩子超时被系统摘除)。
    /// </summary>
    internal class SfxManager : IDisposable
    {
        private const int LLKHF_INJECTED = 0x10;

        private sealed class SfxJob
        {
            public string Path;
            public int Volume;
        }

        private readonly NativeMethods.LowLevelProc _kbProc;
        private IntPtr _kbHook;
        private readonly HashSet<uint> _down = new HashSet<uint>(); // 当前按住的键(归一化)
        private readonly Queue<SfxJob> _jobs = new Queue<SfxJob>();
        private readonly object _qLock = new object();
        private Thread _worker;                    // 常驻播放线程: 所有 MCI 调用都在它上面执行
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private volatile bool _workerStop;
        private bool _disposed;

        /// <summary>总开关。</summary>
        public volatile bool Enabled;

        /// <summary>回放期间抑制: 驱动级注入没有 INJECTED 标记, 防止回放触发自己的音效。</summary>
        public volatile bool Suppress;

        /// <summary>键码(原始, 可区分左右修饰键) → 音效文件绝对路径。</summary>
        public Dictionary<int, string> Bindings = new Dictionary<int, string>();

        /// <summary>键码 → 生效音量(0~1000)。缺省键未命中时按 1000(100%) 播放。</summary>
        public Dictionary<int, int> Volumes = new Dictionary<int, int>();

        /// <summary>组合键绑定列表。</summary>
        public List<SfxCombo> Combos = new List<SfxCombo>();

        public SfxManager()
        {
            _kbProc = KeyboardProc;
        }

        public void Start()
        {
            if (_disposed) return;
            if (_kbHook == IntPtr.Zero)
            {
                _kbHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _kbProc,
                    NativeMethods.GetModuleHandle(null), 0);
                if (_kbHook == IntPtr.Zero) Log.Warn("音效钩子安装失败");
            }
            StartWorker(); // 启动常驻播放线程并在其上预热 MCI, 保证开机后第一次按键就能出声
        }

        /// <summary>启动常驻播放线程(MCI 的隐藏通知窗口/设备上下文绑定在这个线程上, 线程存活才能稳定播放)。</summary>
        private void StartWorker()
        {
            if (_worker != null || _disposed) return;
            _workerStop = false;
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "SfxPlayer" };
            _worker.Start();
        }

        private void WorkerLoop()
        {
            try { SfxPlayer.WarmUp(); } catch (Exception) { }
            while (!_workerStop)
            {
                PlayLatest();
                try { _wake.WaitOne(200); } catch (Exception) { }
            }
            PlayLatest();
        }

        /// <summary>取出并播放最新一条(队列只保留最新, 实现"新键覆盖旧音效")。</summary>
        private void PlayLatest()
        {
            SfxJob job = null;
            lock (_qLock)
            {
                if (_jobs.Count > 0)
                {
                    job = _jobs.Dequeue();
                    _jobs.Clear();
                }
            }
            if (job == null) return;
            try
            {
                SfxPlayer.Play(job.Path, job.Volume);
            }
            catch (Exception ex)
            {
                try { Log.Warn("音效播放异常: " + ex.Message); } catch (Exception) { }
            }
        }

        private bool Satisfied(Hotkey hk)
        {
            if (hk == null) return false;
            foreach (var k in hk.Keys) if (!_down.Contains(k)) return false;
            foreach (var m in hk.Modifiers) if (!_down.Contains(m)) return false;
            return true;
        }

        /// <summary>入队一条播放请求(只保留最新一条, 保持"新键覆盖旧音效")。</summary>
        private void Enqueue(string path, int volume)
        {
            if (_disposed) return;
            lock (_qLock)
            {
                _jobs.Clear();
                _jobs.Enqueue(new SfxJob { Path = path, Volume = volume });
            }
            try { _wake.Set(); } catch (Exception) { }
        }

        /// <summary>「试听」: 与按键播放走同一条常驻线程, 保证 MCI 状态一致(不必先试听才能出声)。</summary>
        public void TestPlay(string path, int volume)
        {
            Enqueue(path, volume);
        }

        private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && Enabled && !Suppress)
                {
                    var info = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                    bool up = (int)wParam == NativeMethods.WM_KEYUP || (int)wParam == NativeMethods.WM_SYSKEYUP;
                    uint vk = info.vkCode;
                    uint n = Hotkey.Normalize(vk);
                    if (up)
                    {
                        _down.Remove(n);
                    }
                    else if ((info.flags & LLKHF_INJECTED) == 0) // 忽略注入按键, 防止宏回放触发音效
                    {
                        if (_down.Add(n)) // 按住自动重复的消息不重复触发
                        {
                            string file;
                            if (Bindings.TryGetValue((int)vk, out file))
                            {
                                int vol;
                                if (!Volumes.TryGetValue((int)vk, out vol)) vol = 1000;
                                Enqueue(file, vol);
                            }
                            // 组合键: 刚按下的键是组合的触发键之一, 且所有修饰键/键都已按住
                            foreach (var cb in Combos)
                            {
                                if (cb.Combo != null && cb.Combo.Keys.Contains(n) && Satisfied(cb.Combo))
                                {
                                    Enqueue(cb.Path, cb.Volume);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 回调异常不能穿越原生边界(进程会终止), 兜底吞掉
                try { Log.Warn("音效钩子回调异常: " + ex.Message); } catch (Exception) { }
            }
            return NativeMethods.CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_kbHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_kbHook);
            _kbHook = IntPtr.Zero;
            _workerStop = true;
            try { _wake.Set(); } catch (Exception) { }
            Thread w = _worker;
            if (w != null && w != Thread.CurrentThread)
            {
                try { w.Join(500); } catch (Exception) { }
            }
            SfxPlayer.Stop();
        }
    }
}
