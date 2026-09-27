using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClickerTool
{
    /// <summary>
    /// 录制前的倒计时浮层: 无边框、置顶、不抢焦点, 居中显示在被录制的那块屏幕/窗口上。
    /// 倒计时结束后回调 onDone(由 UI 线程执行), 调用方在那里真正启动录制线程。
    /// 窗口被标记为 WDA_EXCLUDEFROMCAPTURE: 即使录制已经开始, 它也不会出现在视频里。
    /// </summary>
    internal sealed class CountdownForm : Form
    {
        private readonly Timer _timer;
        private readonly Label _lblNum;
        private readonly Label _lblHint;
        private readonly Action _onDone;
        private int _n;
        private bool _done;

        public CountdownForm(int seconds, Rectangle target, Action onDone)
        {
            _onDone = onDone;
            _n = seconds < 1 ? 1 : seconds;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Clay.WindowBg;
            Font = new Font("Microsoft YaHei UI", 9F);

            int w = Dpi.X(260);
            int h = Dpi.X(170);
            int x = target.Left + (target.Width - w) / 2;
            int y = target.Top + (target.Height - h) / 2;
            Bounds = new Rectangle(x, y, w, h);

            var card = new ClayPanel
            {
                Location = new Point(Dpi.X(8), Dpi.X(8)),
                Size = new Size(w - Dpi.X(16), h - Dpi.X(16)),
                BackColor = Clay.CardBg
            };
            _lblNum = new Label
            {
                Location = new Point(0, Dpi.X(14)),
                Size = new Size(card.Width, Dpi.X(84)),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 44F * Dpi.S, FontStyle.Bold),
                ForeColor = Clay.Run,
                BackColor = Clay.CardBg,
                Text = _n.ToString()
            };
            _lblHint = new Label
            {
                Location = new Point(0, Dpi.X(102)),
                Size = new Size(card.Width, Dpi.X(34)),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Clay.InkSoft,
                BackColor = Clay.CardBg,
                Text = Lang.T("Recording starts in...")
            };
            card.Controls.Add(_lblNum);
            card.Controls.Add(_lblHint);
            Controls.Add(card);

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += delegate { Tick(); };
        }

        /// <summary>显示时不激活(游戏/目标窗口保持焦点)。</summary>
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { Clay.ApplyFrameTheme(Handle); } catch (Exception) { }
            // 对捕获 API 隐藏本窗口(Win10 2004+); 老系统失败也无妨——倒计时结束后才开始抓帧
            try { NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE); } catch (Exception) { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _timer.Start();
        }

        private void Tick()
        {
            _n--;
            if (_n > 0)
            {
                _lblNum.Text = _n.ToString();
                _lblNum.Invalidate();
                return;
            }
            Finish();
        }

        /// <summary>取消倒计时: 关闭窗口但不触发 onDone(不开始录制)。</summary>
        public void CancelCountdown()
        {
            if (_done) return;
            _done = true;
            _timer.Stop();
            Close();
        }

        private void Finish()
        {
            if (_done) return;
            _done = true;
            _timer.Stop();
            Close();
            if (_onDone != null) _onDone();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosed(e);
        }
    }
}
