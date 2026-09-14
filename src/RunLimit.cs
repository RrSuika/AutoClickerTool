using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace AutoClickerTool
{
    /// <summary>运行限制模式(鼠标连点 / 键盘连按 / 录制回放 三个功能共用)。</summary>
    internal enum RunLimitMode
    {
        Count = 0,     // 指定次数 / 指定轮数
        Infinite = 1,  // 无限循环(直到按热键停止)
        Duration = 2   // 运行指定时长, 或运行到指定时刻(两者双向同步)
    }

    /// <summary>
    /// 三个功能共用的「运行限制」控件, 三个选项:
    ///   ① 指定次数   ② 无限循环   ③ 运行时长(h/m/s) / 运行到指定时刻
    /// 选项 ③ 里的「时长」与「截止时刻」双向同步:
    ///   改时长 → 截止时刻 = 当前本地时间 + 时长(例: 10 分钟 → 显示 "现在+10 分钟");
    ///   改截止时刻 → 时长 = 截止时刻 - 当前本地时间。
    /// 该控件在三个页面里的坐标与大小完全一致, 方便用户形成一致的肌肉记忆。
    /// </summary>
    internal class RunLimitBox : Panel
    {
        private readonly ClayRadio _rbCount = new ClayRadio();
        private readonly ClayRadio _rbInfinite = new ClayRadio();
        private readonly ClayRadio _rbDuration = new ClayRadio();
        private readonly ClayNumericUpDown _numCount = new ClayNumericUpDown();
        private readonly ClayNumericUpDown _numHours = new ClayNumericUpDown();
        private readonly ClayNumericUpDown _numMins = new ClayNumericUpDown();
        private readonly ClayNumericUpDown _numSecs = new ClayNumericUpDown();
        private readonly TextBox _txtUntil = new TextBox();

        private bool _sync;         // 程序化回填期间抑制事件与联动
        private bool _untilPrimary; // 用户最近编辑的是"截止时刻" → 以绝对时刻为准; 否则以"时长"为准

        private const int MaxSeconds = 999 * 3600 + 59 * 60 + 59;      // 时长上限(界面 h 上限 999)
        private const int DefaultDurationSeconds = 10 * 60;            // 时长模式的默认值(10 分钟)
        private const string UntilFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>任一运行限制设置发生变化(用于即时保存配置)。</summary>
        public event EventHandler Changed;

        public RunLimitBox()
        {
            BackColor = Clay.CardBg;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BuildUi();
            Wire();
            SetModeInternal(RunLimitMode.Infinite);
        }

        // ---------- 布局 ----------

        private void BuildUi()
        {
            // 第 1 行: 指定次数 / 无限循环
            _rbCount.Name = "Repeat count:";
            _rbCount.Text = Lang.T("Repeat count:");
            _rbCount.Location = new Point(Dpi.X(2), Dpi.X(0));
            Controls.Add(_rbCount);

            _numCount.Minimum = 0;
            _numCount.Maximum = 100000000;
            _numCount.Value = 0;
            Controls.Add(ClayKit.InputShell(_numCount, 112, 0, 58));

            _rbInfinite.Name = "Infinite loop";
            _rbInfinite.Text = Lang.T("Infinite loop");
            _rbInfinite.Location = new Point(Dpi.X(190), Dpi.X(0));
            Controls.Add(_rbInfinite);

            // 第 2 行: 运行时长 时/分/秒
            _rbDuration.Name = "Run for:";
            _rbDuration.Text = Lang.T("Run for:");
            _rbDuration.Location = new Point(Dpi.X(2), Dpi.X(24));
            Controls.Add(_rbDuration);

            SetupNum(_numHours, 0, 999, 0);
            Controls.Add(ClayKit.InputShell(_numHours, 100, 24, 54));
            Controls.Add(MkLabel("h", 158, 28));
            SetupNum(_numMins, 0, 59, 0);
            Controls.Add(ClayKit.InputShell(_numMins, 172, 24, 54));
            Controls.Add(MkLabel("m", 230, 28));
            SetupNum(_numSecs, 0, 59, 0);
            Controls.Add(ClayKit.InputShell(_numSecs, 244, 24, 54));
            Controls.Add(MkLabel("s", 302, 28));

            // 第 3 行: 截止时刻(与时长双向同步)
            Controls.Add(MkLabel("Until:", 2, 54));
            _txtUntil.BorderStyle = BorderStyle.None;
            _txtUntil.MaxLength = 19;
            _txtUntil.Text = DateTime.Now.AddSeconds(DefaultDurationSeconds).ToString(UntilFormat, CultureInfo.InvariantCulture);
            Controls.Add(ClayKit.InputShell(_txtUntil, 50, 50, 210));
        }

        private static Label MkLabel(string text, int x, int y)
        {
            return new Label
            {
                Name = text, // Name 存英文原文 → ApplyLangWalk 切语言时自动刷新
                Text = Lang.T(text),
                Location = new Point(Dpi.X(x), Dpi.X(y)),
                AutoSize = true,
                ForeColor = Clay.InkSoft,
                BackColor = Clay.CardBg
            };
        }

        private static void SetupNum(ClayNumericUpDown n, decimal min, decimal max, decimal val)
        {
            n.Minimum = min;
            n.Maximum = max;
            n.Value = val;
            n.BorderStyle = BorderStyle.None;
        }

        private void Wire()
        {
            _rbCount.CheckedChanged += delegate { if (_rbCount.Checked) OnModeChanged(RunLimitMode.Count); };
            _rbInfinite.CheckedChanged += delegate { if (_rbInfinite.Checked) OnModeChanged(RunLimitMode.Infinite); };
            _rbDuration.CheckedChanged += delegate { if (_rbDuration.Checked) OnModeChanged(RunLimitMode.Duration); };

            _numCount.ValueChanged += delegate { RaiseChanged(); };

            _numHours.ValueChanged += delegate { OnDurationEdited(); };
            _numMins.ValueChanged += delegate { OnDurationEdited(); };
            _numSecs.ValueChanged += delegate { OnDurationEdited(); };

            _txtUntil.TextChanged += delegate { OnUntilEdited(); };
        }

        // ---------- 属性 ----------

        /// <summary>当前运行限制模式。</summary>
        public RunLimitMode Mode
        {
            get
            {
                if (_rbCount.Checked) return RunLimitMode.Count;
                if (_rbDuration.Checked) return RunLimitMode.Duration;
                return RunLimitMode.Infinite;
            }
        }

        /// <summary>指定次数模式下的次数。</summary>
        public int Count { get { return (int)_numCount.Value; } }

        /// <summary>时长模式下的时长(秒)。</summary>
        public int Seconds { get { return TotalSeconds(); } }

        /// <summary>时长模式下界面上显示的绝对截止时刻文本。</summary>
        public string UntilText { get { return _txtUntil.Text == null ? "" : _txtUntil.Text.Trim(); } }

        /// <summary>用户最近编辑的是"截止时刻"(以绝对时刻为准)。保存进配置, 重启后截止点不漂移。</summary>
        public bool UntilPrimary
        {
            get { return _untilPrimary; }
            set { _untilPrimary = value; }
        }

        /// <summary>
        /// 换算给引擎的停止条件: 模式 ① 返回次数(0=无限), 模式 ② 返回 0(无限),
        /// 模式 ③ 返回 0(无限轮询) + 截止时刻。
        /// </summary>
        public int EngineRepeatCount
        {
            get { return Mode == RunLimitMode.Count ? Count : 0; }
        }

        /// <summary>模式 ③ 的绝对截止时刻; 其它模式或无法解析时返回 null。</summary>
        public DateTime? GetUntilTarget()
        {
            if (Mode != RunLimitMode.Duration) return null;
            if (_untilPrimary)
            {
                DateTime parsed;
                if (ParseAbsolute(UntilText, out parsed))
                {
                    // 绝对时刻已过期 → 顺延到"下一个该时刻"(过夜挂机/每天固定时刻, 与旧 HH:mm 语义一致)
                    while (parsed <= DateTime.Now) parsed = parsed.AddDays(1);
                    return parsed;
                }
            }
            // 以"时长"为准: 0 秒 = 立即停止(与界面显示 0h0m0s 一致, 不默默跑默认时长)
            return DateTime.Now.AddSeconds(TotalSeconds());
        }

        // ---------- 回填 / 联动 ----------

        /// <summary>从配置回填(不触发 Changed, 不即时保存)。untilPrimary: 上次保存时用户是否以"截止时刻"为准。</summary>
        public void SetFrom(int mode, int count, int seconds, string untilAtText, bool untilPrimary)
        {
            _sync = true;
            try
            {
                SetModeInternal((RunLimitMode)Clamp(mode, 0, 2));
                // 「指定次数」模式下 0 无意义(0=无限由「无限循环」表达), 收敛到 1 次
                if (Mode == RunLimitMode.Count && count <= 0) count = 1;
                _numCount.Value = Clamp(count, 0, 100000000);

                string untilText = untilAtText == null ? "" : untilAtText.Trim();
                // 注意: 必须初始化。若写成 "DateTime parsed;" 再放进 && 短路里, 空串时 ParseAbsolute 不会被调用,
                // 编译器无法推出 hasUntil==true 时 parsed 已赋值 → CS0165 未赋值局部变量。
                DateTime parsed = DateTime.MinValue;
                bool hasUntil = untilText.Length > 0 && ParseAbsolute(untilText, out parsed);
                if (Mode == RunLimitMode.Duration && hasUntil && untilPrimary)
                {
                    // 以"截止时刻"为准(用户在到点框里设的绝对时刻, 或旧 "HH:mm" 配置迁移而来):
                    // 已过期则顺延到下一个该时刻, 重启后截止点不漂移、过夜挂机语义不变
                    while (parsed <= DateTime.Now) parsed = parsed.AddDays(1);
                    untilText = parsed.ToString(UntilFormat, CultureInfo.InvariantCulture);
                    int secs = (int)Math.Max(0, Math.Min(MaxSeconds, (parsed - DateTime.Now).TotalSeconds));
                    SetDuration(secs);
                    _txtUntil.Text = untilText;
                    _untilPrimary = true;
                }
                else
                {
                    // 以"时长"为准: 从时长重新锚定截止时刻显示; 0 秒保持 0(切到时长模式时 UI 会给默认值)
                    SetDuration(seconds);
                    RefreshUntilFromDuration();
                    _untilPrimary = false;
                }
            }
            finally
            {
                _sync = false;
            }
        }

        private static int Clamp(int v, int min, int max)
        {
            return Math.Max(min, Math.Min(max, v));
        }

        private int TotalCount()
        {
            return (int)_numCount.Value;
        }

        private int TotalSeconds()
        {
            int h = (int)_numHours.Value;
            int m = (int)_numMins.Value;
            int s = (int)_numSecs.Value;
            int total = h * 3600 + m * 60 + s;
            if (total < 0) total = 0;
            if (total > MaxSeconds) total = MaxSeconds;
            return total;
        }

        private void SetDuration(int seconds)
        {
            if (seconds < 0) seconds = 0;
            if (seconds > MaxSeconds) seconds = MaxSeconds;
            int h = seconds / 3600;
            int m = seconds % 3600 / 60;
            int s = seconds % 60;
            bool old = _sync;
            _sync = true;
            try
            {
                if (_numHours.Value != h) _numHours.Value = h;
                if (_numMins.Value != m) _numMins.Value = m;
                if (_numSecs.Value != s) _numSecs.Value = s;
            }
            finally
            {
                _sync = old;
            }
        }

        /// <summary>把"时长"换算成"截止时刻"显示(例: 10 分钟 → 本地时间 + 10 分钟)。</summary>
        private void RefreshUntilFromDuration()
        {
            int secs = TotalSeconds();
            DateTime until = DateTime.Now.AddSeconds(secs);
            bool old = _sync;
            _sync = true;
            try
            {
                _txtUntil.Text = until.ToString(UntilFormat, CultureInfo.InvariantCulture);
            }
            finally
            {
                _sync = old;
            }
        }

        private void SetModeInternal(RunLimitMode mode)
        {
            bool old = _sync;
            _sync = true;
            try
            {
                _rbCount.Checked = mode == RunLimitMode.Count;
                _rbInfinite.Checked = mode == RunLimitMode.Infinite;
                _rbDuration.Checked = mode == RunLimitMode.Duration;
            }
            finally
            {
                _sync = old;
            }
            UpdateEnabledState();
        }

        private void OnModeChanged(RunLimitMode mode)
        {
            if (_sync) return;
            UpdateEnabledState();
            if (mode == RunLimitMode.Count && TotalCount() <= 0)
            {
                _numCount.Value = 1; // 切到「指定次数」时 0 没有意义(0=无限由上一个选项表达), 默认给 1 次
            }
            if (mode == RunLimitMode.Duration)
            {
                // 刚切到"时长/到点"且两边都是空/0 时, 给一个合理默认(10 分钟), 并同步出截止时刻
                if (TotalSeconds() <= 0 && UntilText.Length == 0)
                {
                    SetDuration(DefaultDurationSeconds);
                    _untilPrimary = false;
                    RefreshUntilFromDuration();
                }
                else if (TotalSeconds() > 0 && !_untilPrimary)
                {
                    RefreshUntilFromDuration();
                }
            }
            RaiseChanged();
        }

        private void UpdateEnabledState()
        {
            bool dur = _rbDuration.Checked;
            _numCount.Enabled = _rbCount.Checked;
            _numHours.Enabled = dur;
            _numMins.Enabled = dur;
            _numSecs.Enabled = dur;
            _txtUntil.Enabled = dur;
            _rbCount.Invalidate();
            _rbDuration.Invalidate();
        }

        private void OnDurationEdited()
        {
            if (_sync) return;
            _untilPrimary = false;
            RefreshUntilFromDuration();
            RaiseChanged();
        }

        private void OnUntilEdited()
        {
            if (_sync) return;
            DateTime parsed;
            if (!ParseAbsolute(UntilText, out parsed)) return; // 输入过程中可能只是半截文本, 不联动
            _untilPrimary = true;
            int secs = (int)Math.Max(0, Math.Min(MaxSeconds, (parsed - DateTime.Now).TotalSeconds));
            SetDuration(secs);
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            if (_sync) return;
            var h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        /// <summary>解析绝对时刻 "yyyy-MM-dd HH:mm:ss"(也接受常见宽松写法)。</summary>
        private static bool ParseAbsolute(string text, out DateTime dt)
        {
            dt = DateTime.MinValue;
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            // 只认完整格式(不做宽松解析): 用户输入到一半(如 "2026-08")时不能被误判成合法时刻
            string[] formats =
            {
                "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy/M/d HH:mm:ss", "yyyy/M/d HH:mm",
                "yyyy-M-d H:mm:ss", "yyyy-M-d H:mm", "M/d/yyyy H:mm:ss", "M/d/yyyy H:mm"
            };
            return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
        }
    }
}
