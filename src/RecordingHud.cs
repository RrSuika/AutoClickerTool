using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClickerTool
{
    /// <summary>
    /// 录制悬浮控制条(其它录屏软件那种右上角小条): 红点 + 已录时长 + 「立即开始/暂停/继续」+「停止并保存」。
    ///
    /// 三个关键行为:
    ///  - **不抢焦点**: WM_MOUSEACTIVATE 返回 MA_NOACTIVATE —— 点小条上的按钮不会把游戏/目标窗口切到后台;
    ///  - **整条可拖动**: 在卡片/文字上按住即可拖, 位置记进配置(RecHudX/RecHudY), 下次还在原地;
    ///  - **不会被录进视频**: SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE); 老系统失败时退回
    ///    WDA_MONITOR(录制里显示为黑块), 再失败也不影响录制, 用户可以把小条拖出录制区域。
    /// </summary>
    internal sealed class RecordingHud : Form
    {
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;

        private readonly ClayPanel _card;
        private readonly Label _lblDot;
        private readonly Label _lblTime;
        private readonly ClayButton _btnToggle; // 立即开始 / 暂停 / 继续
        private readonly ClayButton _btnStop;   // 取消 / 停止并保存
        private readonly Timer _timer;
        private readonly ScreenRecorder _rec;

        private bool _counting;        // 倒计时阶段(还没真正开始抓帧)
        private DateTime _countdownEnd;
        private bool _drag;
        private Point _dragMouse;
        private Point _dragWindow;
        private bool _moved;
        private string _hkStart = "";   // 按钮上的热键提示(MainForm 设置, 语言切换时同步)
        private string _hkPause = "";
        private string _hkStop = "";

        /// <summary>倒计时期间点「立即开始」(跳过剩余秒数)。</summary>
        public event Action StartNow;
        /// <summary>点「暂停」/「继续」。</summary>
        public event Action PauseToggle;
        /// <summary>点「停止并保存」。</summary>
        public event Action StopSave;
        /// <summary>倒计时期间点「取消」。</summary>
        public event Action CancelCountdown;

        public RecordingHud(ScreenRecorder rec)
        {
            _rec = rec;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Clay.WindowBg;
            Font = new Font("Microsoft YaHei UI", 9F);
            int w = Dpi.X(382);
            int h = Dpi.X(50);
            ClientSize = new Size(w, h);

            _card = new ClayPanel
            {
                Location = new Point(Dpi.X(6), Dpi.X(6)),
                Size = new Size(w - Dpi.X(12), h - Dpi.X(12)),
                BackColor = Clay.CardBg
            };
            _lblDot = new Label
            {
                Text = "●",
                Location = new Point(Dpi.X(14), Dpi.X(9)),
                Size = new Size(Dpi.X(14), Dpi.X(18)),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Current.DangerTop,
                BackColor = Clay.CardBg
            };
            _lblTime = new Label
            {
                Text = "00:00:00",
                Location = new Point(Dpi.X(34), Dpi.X(9)),
                Size = new Size(Dpi.X(64), Dpi.X(18)),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Clay.Ink,
                BackColor = Clay.CardBg,
                Font = new Font("Consolas", 9F, FontStyle.Bold)
            };
            _btnToggle = new ClayButton
            {
                Name = "", // 文案自己维护(会随状态变), 不能让 ApplyLangWalk 覆盖
                Text = Lang.T("Pause"),
                Location = new Point(Dpi.X(104), Dpi.X(7)),
                Size = new Size(Dpi.X(96), Dpi.X(24))
            };
            _btnStop = new ClayButton
            {
                Name = "",
                Text = Lang.T("Stop and save"),
                Location = new Point(Dpi.X(208), Dpi.X(7)),
                Size = new Size(Dpi.X(150), Dpi.X(24)),
                Danger = true
            };
            _card.Controls.AddRange(new Control[] { _lblDot, _lblTime, _btnToggle, _btnStop });
            Controls.Add(_card);

            _btnToggle.Click += delegate
            {
                if (_counting) { var h2 = StartNow; if (h2 != null) h2(); }
                else { var h2 = PauseToggle; if (h2 != null) h2(); }
            };
            _btnStop.Click += delegate
            {
                if (_counting) { var h2 = CancelCountdown; if (h2 != null) h2(); }
                else { var h2 = StopSave; if (h2 != null) h2(); }
            };

            AttachDrag(this);
            AttachDrag(_card);
            AttachDrag(_lblDot);
            AttachDrag(_lblTime);

            _timer = new Timer { Interval = 200 };
            _timer.Tick += delegate { Refresh2(); };
        }

        /// <summary>显示但不激活(游戏保持焦点)。</summary>
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { Clay.ApplyFrameTheme(Handle); } catch (Exception) { }
            // 关键: 让本窗口对捕获 API 不可见, 否则小条会被录进视频
            bool ok = false;
            try { ok = NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE); }
            catch (Exception) { ok = false; }
            if (!ok)
            {
                try { NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WDA_MONITOR); } catch (Exception) { }
                Log.Warn("当前系统不支持把窗口排除出捕获(WDA_EXCLUDEFROMCAPTURE), 录制小条可能出现在视频里; 可把它拖出录制区域");
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        // ---------------------------------------------------------------- 状态

        /// <summary>倒计时阶段: 两个按钮变成「立即开始」「取消」, 时间位置显示剩余秒数。</summary>
        public void ShowForCountdown(int seconds)
        {
            _counting = true;
            _countdownEnd = DateTime.Now.AddSeconds(seconds < 1 ? 1 : seconds);
            ApplyLang();
            Refresh2();
            _timer.Start();
        }

        /// <summary>真正开始抓帧: 时间位置改成已录时长。</summary>
        public void ShowForRecording()
        {
            _counting = false;
            ApplyLang();
            Refresh2();
            _timer.Start();
        }

        /// <summary>按钮上的热键提示(录制页改绑热键后由 MainForm 同步过来)。</summary>
        public void SetHotkeyTexts(string start, string pause, string stop)
        {
            _hkStart = start == null ? "" : start;
            _hkPause = pause == null ? "" : pause;
            _hkStop = stop == null ? "" : stop;
            ApplyLang();
        }

        private static string WithHk(string label, string hk)
        {
            return string.IsNullOrEmpty(hk) ? label : label + " (" + hk + ")";
        }

        /// <summary>语言切换/初始状态下的按钮文案(带热键提示)。</summary>
        public void ApplyLang()
        {
            bool paused = _rec != null && _rec.Paused;
            _btnToggle.Text = _counting
                ? WithHk(Lang.T("Start now"), _hkStart)
                : WithHk(Lang.T(paused ? "Resume" : "Pause"), _hkPause);
            _btnStop.Text = _counting
                ? Lang.T("Cancel")
                : WithHk(Lang.T("Stop and save"), _hkStop);
            _btnToggle.Invalidate();
            _btnStop.Invalidate();
        }

        private void Refresh2()
        {
            if (_counting)
            {
                double left = (_countdownEnd - DateTime.Now).TotalSeconds;
                if (left < 0) left = 0;
                _lblTime.Text = left > 0.5 ? Lang.F("{0}s", (int)Math.Ceiling(left)) : Lang.T("Preparing...");
            }
            else
            {
                double sec = _rec == null ? 0 : _rec.Seconds;
                int total = (int)sec;
                _lblTime.Text = string.Format("{0:00}:{1:00}:{2:00}", total / 3600, total % 3600 / 60, total % 60);
                // 暂停时红点变灰(时间也停止走动), 不用特殊字形以免字体缺字
                _lblDot.ForeColor = _rec != null && _rec.Paused ? Clay.InkSoft : Theme.Current.DangerTop;
            }
            ApplyLang();
        }

        // ---------------------------------------------------------------- 位置 / 拖动

        /// <summary>默认位置: 录制目标右上角内缩 16px。</summary>
        public void PlaceDefault(Rectangle target)
        {
            int x = target.Right - Width - Dpi.X(16);
            int y = target.Top + Dpi.X(16);
            if (target.Width < Width + Dpi.X(32)) x = target.Left + Dpi.X(16);
            Location = ClampToScreen(new Point(x, y));
        }

        /// <summary>恢复上次保存的位置(超出屏幕时收回可视范围)。</summary>
        public void PlaceAt(int x, int y)
        {
            Location = ClampToScreen(new Point(x, y));
        }

        private Point ClampToScreen(Point p)
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            if (p.X < vs.Left) p.X = vs.Left;
            if (p.Y < vs.Top) p.Y = vs.Top;
            if (p.X + Width > vs.Right) p.X = vs.Right - Width;
            if (p.Y + Height > vs.Bottom) p.Y = vs.Bottom - Height;
            return p;
        }

        /// <summary>用户是否拖动过(决定要不要把新位置写回配置)。</summary>
        public bool WasMoved { get { return _moved; } }

        private void AttachDrag(Control c)
        {
            c.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                _drag = true;
                _dragMouse = Cursor.Position;
                _dragWindow = Location;
                ((Control)s).Capture = true; // 拖出窗口范围也能继续收到 MouseMove
            };
            c.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!_drag) return;
                Point now = Cursor.Position;
                int nx = _dragWindow.X + (now.X - _dragMouse.X);
                int ny = _dragWindow.Y + (now.Y - _dragMouse.Y);
                Location = new Point(nx, ny);
            };
            c.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (!_drag) return;
                _drag = false;
                ((Control)s).Capture = false;
                Location = ClampToScreen(Location);
                _moved = true;
            };
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosed(e);
        }
    }
}
