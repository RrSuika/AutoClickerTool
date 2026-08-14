using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AutoClickerTool
{
    internal class MainForm : Form
    {
        private readonly AutoClicker _clicker = new AutoClicker();
        private readonly KeyboardSpammer _spammer = new KeyboardSpammer();
        private readonly MacroRecorder _recorder = new MacroRecorder();
        private readonly MacroPlayer _player = new MacroPlayer();
        private readonly HotkeyManager _hotkeys = new HotkeyManager();
        private readonly SfxManager _sfx = new SfxManager();
        private readonly List<KeyValuePair<string, int>> _keyOptions = new List<KeyValuePair<string, int>>();
        private readonly System.Windows.Forms.Timer _statusTimer;
        private readonly ToolTip _hintTip = new ToolTip();
        private readonly List<string> _loadNotes = new List<string>();
        private AppConfig _cfg;
        private bool _applying; // 配置回填期间抑制控件事件

        // 胶囊标签页
        private Button[] _tabBtns;
        private Panel[] _pages;
        private Panel _contentPanel;
        private Panel _topPanel;
        private Panel _tabStrip;
        private Panel _statusBar;
        private Label _lblVersion;
        private int _currentTab;

        // 鼠标连点
        private NumericUpDown numInterval;
        private ComboBox cboButton;
        private RadioButton rbFollow;
        private RadioButton rbFixed;
        private NumericUpDown numX;
        private NumericUpDown numY;
        private NumericUpDown numRepeat;
        private Button btnClickerToggle;
        private Button btnTestClick;
        private Button btnGetPos;
        private Label lblClickerState;

        // 键盘连按
        private ComboBox cboKey;
        private TextBox txtKey;
        private RadioButton rbTap;
        private RadioButton rbHold;
        private NumericUpDown numKeyInterval;
        private Button btnKeyboardToggle;
        private Label lblKeyboardState;

        // 录制回放
        private Button btnRecord;
        private Button btnPlay;
        private Button btnSave;
        private Button btnLoad;
        private Button btnClear;
        private NumericUpDown numSpeed;
        private CheckBox chkLoop;
        private Label lblEventCount;
        private ListView lstEvents;
        private Button btnEditDelay;
        private Button btnAddEvent;
        private Button btnDeleteEvent;
        private ContextMenuStrip _eventMenu; // 事件列表右键菜单

        // 回放运行限制 + 循环热键提示
        private NumericUpDown numPlayLoops;
        private NumericUpDown numPlayMinutes;
        private CheckBox chkUntilTime;
        private TextBox txtUntilTime;
        private Label lblLoopHint;

        // 宏库页
        private ListView lstMacros;
        private Button btnMacroRename;
        private Button btnMacroCopy;
        private Button btnMacroDelete;
        private ContextMenuStrip _macroMenu; // 宏列表右键菜单
        private ListBox lstPrograms;
        private Button btnProgramAdd;
        private Button btnProgramRemove;
        private CheckBox chkLaunchStart;
        private CheckBox chkLaunchEnd;
        private readonly List<string> _macroFiles = new List<string>(); // 宏库列表对应的完整路径

        // 热键页
        private Button[] _hkButtons;
        private Button btnResetHotkeys;

        // 高级设置页
        private ComboBox cboMethod;
        private CheckBox chkScanCode;
        private RadioButton rbTargetForeground;
        private RadioButton rbTargetNamed;
        private TextBox txtTargetWindow;
        private Button btnGrabWindow;
        private CheckBox chkHumanizeEnabled;
        private CheckBox chkHumanizeTiming;
        private NumericUpDown numTimingPct;
        private CheckBox chkHumanizePos;
        private NumericUpDown numPosPx;
        private CheckBox chkHumanizePress;
        private CheckBox chkHumanizeTraj;
        private ComboBox cboLanguage;
        private ComboBox cboTheme;
        private CheckBox chkAnimations;

        // 按键音效页
        private CheckBox chkSfx;
        private ClaySlider sldGlobalVolume;
        private Label lblGlobalVol;
        private ClaySlider sldKeyVolume;
        private Label lblKeyVol;
        private ListView lstSfx;
        private Label lblSfxCount;
        private Button btnSfxAdd;
        private Button btnSfxDelete;
        private Button btnSfxTest;
        private Button btnSfxFolder;
        private bool _sfxSliderSync; // 程序化更新音量滑块时抑制事件
        private readonly List<SfxKey> _sfxKeys = new List<SfxKey>(); // 统一列表(单键 + 组合键)

        /// <summary>音效绑定条目(单键或组合键统一表示)。</summary>
        private sealed class SfxKey
        {
            public bool IsCombo;
            public int Vk;         // 单键 vk(IsCombo=false)
            public string Combo;   // 组合键字符串(IsCombo=true)
            public string Display; // 显示名
            public string File;    // 文件名
            public int Volume;     // 生效音量 0~100
        }

        private CheckBox chkTopmost;
        private Label lblHotkeyHint;
        private Label lblStatus;
        private Button btnAbout;

        // 启动与系统托盘
        private CheckBox chkAutoStart;
        private CheckBox chkSilentStart;
        private NotifyIcon _trayIcon;
        private ToolStripMenuItem _trayOpen;
        private ToolStripMenuItem _trayExit;
        private Icon _trayAppIcon;         // 从 exe 提取的图标(需手动释放)
        private bool _firstShow = true;   // 静默启动: 拦截首次显示(之后恢复正常)
        private bool _shutdownDone;       // 收尾只执行一次

        // 标签切换内容过渡
        private Panel _fadeOverlay;
        private Bitmap _fadeTo;
        private float _fadeT = 1f;
        private readonly Action<float> _animFade;

        private DateTime _lastTestClick = DateTime.MinValue;

        public MainForm()
        {
            // 先加载配置, 用配置的语言/主题构建整个界面
            string cfgError;
            _cfg = AppConfig.Load(out cfgError);
            Lang.Code = _cfg.Language == "en" ? "en" : "zh";
            Theme.Current = Theme.ById(_cfg.ThemeName);
            if (!string.IsNullOrEmpty(cfgError)) { _loadNotes.Add(Lang.T("Config load failed, using defaults")); Log.Warn("配置加载失败, 使用默认值: " + cfgError); }
            else Log.Info("配置加载成功");

            _animFade = v =>
            {
                _fadeT = v;
                if (_fadeOverlay != null) _fadeOverlay.Invalidate();
                if (v >= 0.999f) EndFade();
            };

            // 未捕获异常兜底: 停引擎释放按住键, 防止 Hold 模式崩溃后卡键残留
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                try { Log.Error("未捕获后台线程异常: " + (e.ExceptionObject == null ? "(无对象)" : e.ExceptionObject.ToString())); } catch (Exception) { }
                StopEnginesSilent();
            };
            // 注意: Application.ThreadException / SetUnhandledExceptionMode 必须在 Program.Main 里
            // (创建任何窗口之前)调用, 放在这里会抛 InvalidOperationException。

            AppConfig.EnsureDataDirs();

            BuildKeyOptions();
            BuildUi();
            WireEvents();

            _clicker.Stopped += () => Ui(() => { UpdateClickerUi(); SetStatus(Lang.T("Clicker stopped")); });
            _spammer.Stopped += () => Ui(() => { UpdateKeyboardUi(); SetStatus(Lang.T("Keyboard spam stopped")); });
            _player.Finished += () => Ui(() =>
            {
                UpdatePlayUi();
                UpdateRecordUi();
                SetStatus(Lang.T("Playback finished"));
                if (_cfg != null && _cfg.LaunchOnEnd) LaunchPrograms();
            });
            _recorder.AutoStopRequested += () => Ui(() =>
            {
                _recorder.Stop();
                UpdateRecordUi();
                RefreshEventList(false);
                SetStatus(Lang.T("Event limit reached (200k), recording auto-stopped"));
            });

            // 热键 → 功能映射(任意组合键)
            _recorder.Hotkeys = _hotkeys;
            _hotkeys.SetCallback(HotkeyAction.Clicker, () => Ui(() => ToggleClicker()));
            _hotkeys.SetCallback(HotkeyAction.Record, () => Ui(() => ToggleRecord()));
            _hotkeys.SetCallback(HotkeyAction.Play, () => Ui(() => TogglePlay()));
            _hotkeys.SetCallback(HotkeyAction.Keyboard, () => Ui(() => ToggleKeyboard()));
            _hotkeys.SetCallback(HotkeyAction.StopAll, () => Ui(() => StopAll()));

            ApplyConfigToUi(_cfg);
            BuildTray();
            AutoStart.SetEnabled(_cfg.AutoStart); // 注册表与配置保持一致(幂等)
            _hotkeys.Start();
            _sfx.Start();
            if (_loadNotes.Count > 0) SetStatus(string.Join("; ", _loadNotes.ToArray()));

            _statusTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _statusTimer.Tick += delegate
            {
                if (_recorder.Recording) RefreshEventList(true);
                SyncDpi(); // 窗口所在显示器 DPI 与布局不一致时重建(跨屏移动的安全网)
            };
            _statusTimer.Start();

            // 首次启动(config.json 不存在): 弹出欢迎窗口选择默认语言 + 功能介绍
            if (!AppConfig.ConfigExists)
            {
                Shown += delegate { ShowWelcomeDialog(); };
            }
        }

        /// <summary>首次启动欢迎窗口: 选择默认语言并保存。</summary>
        private void ShowWelcomeDialog()
        {
            using (var f = new WelcomeForm())
            {
                f.SelectedLanguage = Lang.Code;
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    if (f.SelectedLanguage != Lang.Code)
                    {
                        Lang.Code = f.SelectedLanguage;
                        BuildKeyOptions();
                        ApplyLanguage();
                    }
                    SaveSettings();
                }
            }
        }

        // ---------- 界面构建 ----------

        private void BuildKeyOptions()
        {
            _keyOptions.Clear();
            for (char c = 'A'; c <= 'Z'; c++) _keyOptions.Add(new KeyValuePair<string, int>(c.ToString(), (int)c));
            for (char c = '0'; c <= '9'; c++) _keyOptions.Add(new KeyValuePair<string, int>(c.ToString(), (int)c));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("Space"), 0x20));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("Enter"), 0x0D));
            _keyOptions.Add(new KeyValuePair<string, int>("Tab", 0x09));
            _keyOptions.Add(new KeyValuePair<string, int>("Esc", 0x1B));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("Backspace"), 0x08));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("Shift"), 0x10));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("Ctrl"), 0x11));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("Alt"), 0x12));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("CapsLock"), 0x14));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("Up"), 0x26));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("Down"), 0x28));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("Left2"), 0x25));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("Right2"), 0x27));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("Insert"), 0x2D));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("Delete"), 0x2E));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("Home"), 0x24));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("End"), 0x23));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("PageUp"), 0x21));
            _keyOptions.Add(new KeyValuePair<string, int>(Lang.T("PageDown"), 0x22));
            for (int i = 1; i <= 12; i++) _keyOptions.Add(new KeyValuePair<string, int>("F" + i, 0x6F + i));
            // 小键盘与多媒体键(名称统一走 Hotkey.GetName, 支持中英切换)
            int[] extras = { 0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x6B, 0x6D, 0x6E, 0x6F, 0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3 };
            foreach (int vk in extras)
            {
                if (_keyOptions.FindIndex(kv => kv.Value == vk) < 0)
                    _keyOptions.Add(new KeyValuePair<string, int>(Hotkey.GetName((uint)vk), vk));
            }
        }

        private void BuildUi()
        {
            Text = Lang.F("Auto Clicker {0}", VersionInfo.Version);
            ClientSize = new Size(Dpi.X(560), Dpi.X(530));
            // 可拖拽缩放, 最小尺寸保证各控件之间的间距与可读性
            MinimumSize = new Size(Dpi.X(562), Dpi.X(520));
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            // 手工布局 + 代码设置字体, 禁用自动缩放; 布局坐标由 Dpi.X() 按实际 DPI 等比放大
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Clay.WindowBg;

            // 1. 先创建并停靠骨架(空的): 让各面板先按窗体尺寸确定大小。
            //    这是修复"卡片溢出窗口"的关键——带 Left|Right 锚点的控件必须在
            //    父容器已具备最终尺寸之后再加入, 否则锚点偏移会按默认 200px 宽计算,
            //    导致卡片被撑到窗口之外。
            _topPanel = new Panel { Dock = DockStyle.Top, Height = Dpi.X(34), BackColor = Clay.WindowBg };
            _tabStrip = new Panel { Dock = DockStyle.Top, Height = Dpi.X(34), BackColor = Clay.WindowBg };
            _statusBar = new Panel { Dock = DockStyle.Bottom, Height = Dpi.X(26), BackColor = Theme.Current.StatusBg };
            _contentPanel = new Panel { Dock = DockStyle.Fill, BackColor = Clay.WindowBg };
            // 注意: Fill 控件要先 Add, 停靠布局才正确
            Controls.Add(_contentPanel);
            Controls.Add(_statusBar);
            Controls.Add(_tabStrip);
            Controls.Add(_topPanel);

            // 2. 填充顶栏 / 标签条 / 状态栏(父容器此刻已是最终尺寸)
            BuildTopBar();
            BuildTabStrip();
            BuildStatusBar();

            // 3. 页面先停靠到内容区(立即获得最终尺寸), 再填充内容。
            Action<Panel>[] builders =
            {
                BuildClickerPage, BuildKeyboardPage, BuildMacroPage, BuildHotkeyPage, BuildAdvancedPage, BuildSfxPage, BuildLibraryPage
            };
            _pages = new Panel[builders.Length];
            for (int i = 0; i < builders.Length; i++)
            {
                var page = new Panel { BackColor = Clay.WindowBg, Dock = DockStyle.Fill };
                _pages[i] = page;
                _contentPanel.Controls.Add(page); // 先停靠(保持可见) → page 立即获得最终宽度
                builders[i](page);                // 再填充 → 锚点按正确父尺寸计算
                page.Visible = (i == 0);          // 填充完成后才隐藏非活动页(隐藏页也必须先完成停靠)
            }

            // 4. 内容切换过渡覆盖层: 最后添加(最上层), 默认隐藏。
            _fadeOverlay = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Clay.WindowBg };
            typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(_fadeOverlay, true, null);
            _fadeOverlay.Paint += delegate(object s, PaintEventArgs pe)
            {
                // 背景由 Panel.BackColor(WindowBg)填充; 这里只把"新页快照"以"上滑 + 淡入"方式画出
                if (_fadeTo != null)
                {
                    float t = Anim.EaseOutCubic(_fadeT);
                    int dy = (int)Math.Round(Dpi.X(10) * (1f - t)); // 从下 10px 处滑入到位
                    var cm = new ColorMatrix { Matrix33 = t };       // alpha 淡入
                    using (var ia = new ImageAttributes())
                    {
                        ia.SetColorMatrix(cm);
                        pe.Graphics.DrawImage(_fadeTo,
                            new Rectangle(0, dy, _fadeTo.Width, _fadeTo.Height),
                            0, 0, _fadeTo.Width, _fadeTo.Height, GraphicsUnit.Pixel, ia);
                    }
                }
            };
            _contentPanel.Controls.Add(_fadeOverlay);
            _fadeOverlay.BringToFront();
        }

        /// <summary>顶栏: 热键提示 + 置顶开关。</summary>
        private void BuildTopBar()
        {
            lblHotkeyHint = new Label
            {
                Location = new Point(Dpi.X(10), Dpi.X(9)),
                Size = new Size(Dpi.X(405), Dpi.X(18)),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                AutoEllipsis = true,
                ForeColor = Clay.InkSoft,
                BackColor = Clay.WindowBg
            };
            _topPanel.Controls.Add(lblHotkeyHint);
            chkTopmost = new ClayCheck
            {
                Name = "Topmost",
                Text = Lang.T("Topmost"),
                Location = new Point(Dpi.X(432), Dpi.X(8)),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Clay.WindowBg
            };
            _topPanel.Controls.Add(chkTopmost);
        }

        /// <summary>胶囊标签页条。</summary>
        private void BuildTabStrip()
        {
            string[] tabNames = { Lang.T("Mouse Clicking"), Lang.T("Keyboard Spam"), Lang.T("Record & Play"), Lang.T("Hotkeys"), Lang.T("Advanced"), Lang.T("Sound FX"), Lang.T("Macro Library") };
            _tabBtns = new Button[tabNames.Length];
            for (int i = 0; i < tabNames.Length; i++)
            {
                var b = new ClayButton
                {
                    Text = tabNames[i],
                    Location = new Point(Dpi.X(10), Dpi.X(3)),
                    Size = new Size(Dpi.X(78), Dpi.X(28)),
                    Tab = true,
                    Selected = i == 0,
                    ForeColor = Clay.Ink
                };
                int idx = i;
                b.Click += delegate { SelectTab(idx); };
                _tabStrip.Controls.Add(b);
                _tabBtns[i] = b;
            }
            LayoutTabStrip();
        }

        /// <summary>按当前语言文本测量各标签所需宽度并等比分配条宽: 中文与原来接近, 英文文案较长时整体压缩而非截断。</summary>
        private void LayoutTabStrip()
        {
            if (_tabBtns == null || _tabBtns.Length == 0) return;
            string[] names = { Lang.T("Mouse Clicking"), Lang.T("Keyboard Spam"), Lang.T("Record & Play"), Lang.T("Hotkeys"), Lang.T("Advanced"), Lang.T("Sound FX"), Lang.T("Macro Library") };
            int n = names.Length;
            int min = Dpi.X(48);
            int avail = _tabStrip.Width - Dpi.X(20);
            if (avail < Dpi.X(546)) avail = Dpi.X(546); // 构建期条宽未布局时为 0, 用默认窗口宽度兜底
            if (avail < min * n) avail = min * n;
            int[] nat = new int[n];
            int total = 0;
            for (int i = 0; i < n; i++)
            {
                nat[i] = TextRenderer.MeasureText(names[i], _tabBtns[i].Font).Width + Dpi.X(16);
                total += nat[i];
            }
            int x = Dpi.X(10);
            int used = 0;
            for (int i = 0; i < n; i++)
            {
                _tabBtns[i].Text = names[i];
                int w = nat[i];
                if (total > avail)
                {
                    w = nat[i] * avail / total;
                    if (w < min) w = min;
                }
                if (i == n - 1) w = avail - used; // 最后一块吃掉余量, 总宽恰好铺满
                _tabBtns[i].SetBounds(x, Dpi.X(3), w, Dpi.X(28));
                x += w;
                used += w;
            }
        }

        /// <summary>底部状态栏。</summary>
        private void BuildStatusBar()
        {
            lblStatus = new Label
            {
                Text = Lang.T("Ready"),
                Location = new Point(Dpi.X(12), Dpi.X(5)),
                Size = new Size(Dpi.X(300), Dpi.X(16)),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                AutoEllipsis = true,
                ForeColor = Clay.InkSoft,
                BackColor = Theme.Current.StatusBg
            };
            _statusBar.Controls.Add(lblStatus);
            btnAbout = new ClayButton
            {
                Name = "About",
                Text = Lang.T("About"),
                Location = new Point(Dpi.X(320), Dpi.X(1)),
                Size = new Size(Dpi.X(55), Dpi.X(24)),
                BackColor = Theme.Current.StatusBg
            };
            _statusBar.Controls.Add(btnAbout);
            _lblVersion = new Label
            {
                Name = "{0} · Settings auto-save",
                Text = Lang.F("{0} · Settings auto-save", VersionInfo.Version),
                Location = new Point(Dpi.X(430), Dpi.X(5)),
                Size = new Size(Dpi.X(118), Dpi.X(16)),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Clay.InkSoft,
                BackColor = Theme.Current.StatusBg
            };
            _statusBar.Controls.Add(_lblVersion);
        }

        private void SelectTab(int index)
        {
            bool same = index == _currentTab;

            _currentTab = index;
            for (int i = 0; i < _tabBtns.Length; i++)
            {
                var b = (ClayButton)_tabBtns[i];
                b.Selected = i == index;
                b.Font = new Font(Font, i == index ? FontStyle.Bold : FontStyle.Regular);
                b.SetSelectionT(i == index ? 1f : 0f); // 选中态交叉淡入
                b.Invalidate();
                _pages[i].Visible = i == index;
            }
            _pages[index].CreateControl();  // 确保子控件句柄已创建, 快照才能渲染完整
            _pages[index].PerformLayout();
            if (index == 6) RefreshMacroList(); // 每次进入宏库页刷新已存宏列表

            if (!same) StartReveal(SnapshotPage(_pages[index]));
        }

        /// <summary>把页面渲染成位图快照(用于切换时的上滑淡入)。</summary>
        private static Bitmap SnapshotPage(Control page)
        {
            try
            {
                int w = Math.Max(1, page.Width);
                int h = Math.Max(1, page.Height);
                var bmp = new Bitmap(w, h);
                page.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                return bmp;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>用单张"新页快照"做上滑淡入过渡(比双图交叉淡化更省、更明显、更顺滑)。</summary>
        private void StartReveal(Bitmap to)
        {
            if (_fadeOverlay == null)
            {
                if (to != null) to.Dispose();
                return;
            }
            DisposeFadeBitmaps();
            if (to == null)
            {
                _fadeOverlay.Visible = false;
                return;
            }
            _fadeTo = to;
            _fadeT = 0f;
            _fadeOverlay.Visible = true;
            _fadeOverlay.BringToFront();
            Anim.To(_animFade, 0f, 1f, 95f);
        }

        private void EndFade()
        {
            if (_fadeOverlay != null) _fadeOverlay.Visible = false;
            DisposeFadeBitmaps();
        }

        private void DisposeFadeBitmaps()
        {
            if (_fadeTo != null) { _fadeTo.Dispose(); _fadeTo = null; }
        }

        // 小组件快捷构造: 卡片内标签 / 页面提示文字。
        // Name 存英文原文, 语言切换时按 Name 更新文本。
        private static Label Lbl(string en, int x, int y)
        {
            return new Label
            {
                Name = en,
                Text = Lang.T(en),
                Location = new Point(Dpi.X(x), Dpi.X(y)),
                AutoSize = true,
                ForeColor = Clay.InkSoft,
                BackColor = Clay.CardBg
            };
        }

        private static Label Tip(string en, int x, int y)
        {
            return new Label
            {
                Name = en,
                Text = Lang.T(en),
                Location = new Point(Dpi.X(x), Dpi.X(y)),
                MaximumSize = new Size(Dpi.X(504), 0), // 超出卡片宽度自动换行
                AutoSize = true,
                ForeColor = Clay.InkSoft,
                BackColor = Clay.WindowBg
            };
        }

        private static ClayGroup Grp(string en, int x, int y, int w, int h)
        {
            return new ClayGroup
            {
                Name = en,
                Text = Lang.T(en),
                Location = new Point(Dpi.X(x), Dpi.X(y)),
                Size = new Size(Dpi.X(w), Dpi.X(h)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
        }

        private void BuildClickerPage(Panel page)
        {
            var gb = Grp("Click Settings", 10, 10, 524, 128);
            gb.Controls.Add(Lbl("Interval (ms):", 15, 33));
            numInterval = new ClayNumericUpDown { Minimum = 1, Maximum = 3600000, Value = 100, BorderStyle = BorderStyle.None };
            gb.Controls.Add(ClayKit.InputShell(numInterval, 95, 29, 80));
            gb.Controls.Add(Lbl("Mouse button:", 200, 33));
            cboButton = new ClayComboBox();
            cboButton.Items.AddRange(new object[] { Lang.T("Left"), Lang.T("Right"), Lang.T("Middle") });
            cboButton.SelectedIndex = 0;
            gb.Controls.Add(ClayKit.InputShell(cboButton, 270, 29, 80));
            rbFollow = new ClayRadio { Name = "Follow cursor", Text = Lang.T("Follow cursor"), Location = new Point(Dpi.X(15), Dpi.X(66)), Checked = true };
            rbFixed = new ClayRadio { Name = "Fixed position:", Text = Lang.T("Fixed position:"), Location = new Point(Dpi.X(15), Dpi.X(93)) };
            numX = new ClayNumericUpDown { Minimum = 0, Maximum = 20000, Value = 0, BorderStyle = BorderStyle.None };
            numY = new ClayNumericUpDown { Minimum = 0, Maximum = 20000, Value = 0, BorderStyle = BorderStyle.None };
            gb.Controls.Add(ClayKit.InputShell(numX, 105, 90, 70));
            gb.Controls.Add(ClayKit.InputShell(numY, 195, 90, 70));
            btnGetPos = new ClayButton { Name = "Get mouse position", Text = Lang.T("Get mouse position"), Location = new Point(Dpi.X(280), Dpi.X(89)), Size = new Size(Dpi.X(140), Dpi.X(26)) };
            var lblRepeat = Lbl("Count (0=infinite):", 395, 33);
            lblRepeat.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            gb.Controls.Add(lblRepeat);
            numRepeat = new ClayNumericUpDown { Minimum = 0, Maximum = 100000000, Value = 0, BorderStyle = BorderStyle.None };
            var repeatShell = ClayKit.InputShell(numRepeat, 445, 29, 66);
            repeatShell.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            gb.Controls.Add(repeatShell);
            gb.Controls.AddRange(new Control[] { rbFollow, rbFixed, btnGetPos });
            page.Controls.Add(gb);

            btnClickerToggle = new ClayButton { Text = Lang.F("Start clicking ({0})", "F6"), Location = new Point(Dpi.X(15), Dpi.X(150)), Size = new Size(Dpi.X(175), Dpi.X(42)), Accent = true, BackColor = Clay.WindowBg };
            btnTestClick = new ClayButton { Name = "Test", Text = Lang.T("Test"), Location = new Point(Dpi.X(200), Dpi.X(150)), Size = new Size(Dpi.X(100), Dpi.X(42)), Mint = true, BackColor = Clay.WindowBg };
            lblClickerState = new Label { Text = Lang.T("Status: Idle"), Location = new Point(Dpi.X(310), Dpi.X(163)), AutoSize = true, ForeColor = Clay.InkSoft, BackColor = Clay.WindowBg };
            page.Controls.AddRange(new Control[] { btnClickerToggle, btnTestClick, lblClickerState });

            page.Controls.Add(Tip("Tip: move the mouse to the target after starting. Do not click Start while the cursor is on the button,\r\nor it will click the button itself. If the game blocks standard injection, switch method in Advanced.", 12, 205));
        }

        private void BuildKeyboardPage(Panel page)
        {
            var gb = Grp("Key Settings", 10, 10, 524, 150);
            gb.Controls.Add(Lbl("Key:", 15, 33));
            cboKey = new ClayComboBox();
            foreach (var kv in _keyOptions) cboKey.Items.Add(kv.Key);
            cboKey.SelectedIndex = 0;
            gb.Controls.Add(ClayKit.InputShell(cboKey, 60, 29, 115));
            rbTap = new ClayRadio { Name = "Tap (click once per interval)", Text = Lang.T("Tap (click once per interval)"), Location = new Point(Dpi.X(200), Dpi.X(32)), Checked = true };
            rbHold = new ClayRadio { Name = "Hold (until stopped)", Text = Lang.T("Hold (until stopped)"), Location = new Point(Dpi.X(360), Dpi.X(32)) };

            // 直接输入按键: 免去在下拉列表里逐个翻找
            gb.Controls.Add(Lbl("or type a key:", 15, 67));
            txtKey = new TextBox { BorderStyle = BorderStyle.None, MaxLength = 16 };
            gb.Controls.Add(ClayKit.InputShell(txtKey, 135, 63, 105));

            gb.Controls.Add(Lbl("Interval (ms):", 15, 101));
            numKeyInterval = new ClayNumericUpDown { Minimum = 1, Maximum = 3600000, Value = 100, BorderStyle = BorderStyle.None };
            gb.Controls.Add(ClayKit.InputShell(numKeyInterval, 95, 97, 80));
            gb.Controls.AddRange(new Control[] { rbTap, rbHold });
            page.Controls.Add(gb);

            btnKeyboardToggle = new ClayButton { Text = Lang.F("Start spam ({0})", "F9"), Location = new Point(Dpi.X(15), Dpi.X(175)), Size = new Size(Dpi.X(170), Dpi.X(42)), Accent = true, BackColor = Clay.WindowBg };
            lblKeyboardState = new Label { Text = Lang.T("Status: Idle"), Location = new Point(Dpi.X(195), Dpi.X(187)), AutoSize = true, ForeColor = Clay.InkSoft, BackColor = Clay.WindowBg };
            page.Controls.AddRange(new Control[] { btnKeyboardToggle, lblKeyboardState });

            page.Controls.Add(Tip("Tip: keys go to the foreground window. Switch to the target window first, then toggle with the hotkey.\r\nOr type a key directly: a letter/number, or a name like F1 / Space / Enter.", 12, 232));
        }

        private void BuildMacroPage(Panel page)
        {
            var gbRec = Grp("Record / Play", 10, 10, 524, 80);
            btnRecord = new ClayButton { Name = "Start recording", Text = Lang.T("Start recording"), Location = new Point(Dpi.X(15), Dpi.X(27)), Size = new Size(Dpi.X(115), Dpi.X(36)), Accent = true };
            btnPlay = new ClayButton { Name = "Start playback", Text = Lang.T("Start playback"), Location = new Point(Dpi.X(140), Dpi.X(27)), Size = new Size(Dpi.X(115), Dpi.X(36)), Accent = true };
            btnSave = new ClayButton { Name = "Save Macro", Text = Lang.T("Save Macro"), Location = new Point(Dpi.X(265), Dpi.X(27)), Size = new Size(Dpi.X(85), Dpi.X(36)) };
            btnLoad = new ClayButton { Name = "Load Macro", Text = Lang.T("Load Macro"), Location = new Point(Dpi.X(360), Dpi.X(27)), Size = new Size(Dpi.X(87), Dpi.X(36)) };
            btnClear = new ClayButton { Name = "Clear", Text = Lang.T("Clear"), Location = new Point(Dpi.X(457), Dpi.X(27)), Size = new Size(Dpi.X(53), Dpi.X(36)) };
            gbRec.Controls.AddRange(new Control[] { btnRecord, btnPlay, btnSave, btnLoad, btnClear });
            page.Controls.Add(gbRec);

            var gbOpt = Grp("Playback Options", 10, 100, 524, 124);
            gbOpt.Controls.Add(Lbl("Speed:", 15, 32));
            numSpeed = new ClayNumericUpDown { Minimum = 0.1m, Maximum = 10m, Increment = 0.1m, DecimalPlaces = 1, Value = 1m, BorderStyle = BorderStyle.None };
            gbOpt.Controls.Add(ClayKit.InputShell(numSpeed, 85, 28, 70));
            gbOpt.Controls.Add(Lbl("x (1 = original)", 165, 32));
            chkLoop = new ClayCheck { Name = "Loop playback (until hotkey stops)", Text = Lang.T("Loop playback (until hotkey stops)"), Location = new Point(Dpi.X(260), Dpi.X(30)) };
            gbOpt.Controls.Add(chkLoop);

            // 运行限制: 循环次数 / 运行分钟数 / 运行到系统时刻
            gbOpt.Controls.Add(Lbl("Loop count:", 15, 64));
            numPlayLoops = new ClayNumericUpDown { Minimum = 0, Maximum = 999999, Value = 0, BorderStyle = BorderStyle.None };
            gbOpt.Controls.Add(ClayKit.InputShell(numPlayLoops, 95, 60, 50));
            gbOpt.Controls.Add(Lbl("Run minutes:", 155, 64));
            numPlayMinutes = new ClayNumericUpDown { Minimum = 0, Maximum = 99999, Value = 0, BorderStyle = BorderStyle.None };
            gbOpt.Controls.Add(ClayKit.InputShell(numPlayMinutes, 240, 60, 45));
            gbOpt.Controls.Add(Lbl("Until:", 300, 64));
            chkUntilTime = new ClayCheck { Location = new Point(Dpi.X(330), Dpi.X(62)) };
            txtUntilTime = new TextBox { BorderStyle = BorderStyle.None, Text = "23:59", MaxLength = 5, Enabled = false };
            gbOpt.Controls.Add(ClayKit.InputShell(txtUntilTime, 365, 60, 55));

            // 循环回放开/关快捷键提示(跟随热键设置)
            lblLoopHint = new Label { Location = new Point(Dpi.X(15), Dpi.X(96)), AutoSize = true, ForeColor = Clay.InkSoft, BackColor = Clay.CardBg };
            gbOpt.Controls.Add(lblLoopHint);
            page.Controls.Add(gbOpt);

            // 事件列表(虚拟模式: 录制时实时刷新, 20 万条也流畅)
            var lstShell = new ClayPanel
            {
                Location = new Point(Dpi.X(10), Dpi.X(234)),
                Size = new Size(Dpi.X(420), Dpi.X(112)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Inset = true,
                BackColor = Theme.Current.InputBg
            };
            lstEvents = new ListView
            {
                Location = new Point(Dpi.X(4), Dpi.X(4)),
                Size = new Size(Dpi.X(412), Dpi.X(104)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                View = View.Details,
                VirtualMode = true,
                FullRowSelect = true,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Current.InputBg,
                ForeColor = Clay.Ink
            };
            lstEvents.Columns.Add(Lang.T("Delay(ms)"), Dpi.X(66));
            lstEvents.Columns.Add(Lang.T("Event"), Dpi.X(330));
            lstEvents.RetrieveVirtualItem += delegate(object sender, RetrieveVirtualItemEventArgs e)
            {
                var items = _recorder.Events;
                int n = items.Count;
                if (e.ItemIndex < 0 || e.ItemIndex >= n)
                {
                    e.Item = new ListViewItem("");
                    return;
                }
                var ev = items[e.ItemIndex];
                var lvi = new ListViewItem(new[] { ev.DelayMs.ToString(), DescribeEvent(ev) });
                lvi.ForeColor = Clay.Ink;
                e.Item = lvi;
            };
            lstEvents.DoubleClick += delegate { EditDelay(); };
            typeof(ListView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(lstEvents, true, null);
            lstShell.Controls.Add(lstEvents);
            page.Controls.Add(lstShell);

            // 事件列表右键菜单(按键精灵风格: 编辑延迟/插入/删除/上下移)
            _eventMenu = ClayMenu.Build();
            ToolStripMenuItem mi;
            mi = ClayMenu.Item(_eventMenu, "Edit delay...");
            mi.Click += delegate { EditDelay(); };
            mi = ClayMenu.Item(_eventMenu, "Insert event here");
            mi.Click += delegate { AddEvent(); };
            mi = ClayMenu.Item(_eventMenu, "Delete this event");
            mi.Click += delegate { DeleteEvent(false); };
            _eventMenu.Items.Add(new ToolStripSeparator());
            mi = ClayMenu.Item(_eventMenu, "Move up");
            mi.Click += delegate { MoveEvent(-1); };
            mi = ClayMenu.Item(_eventMenu, "Move down");
            mi.Click += delegate { MoveEvent(1); };
            lstEvents.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Right) return;
                var item = lstEvents.GetItemAt(e.X, e.Y);
                if (item != null)
                {
                    lstEvents.SelectedIndices.Clear();
                    lstEvents.SelectedIndices.Add(item.Index);
                }
                bool busy = _recorder.Recording || _player.Playing;
                bool has = SelectedEventIndex() >= 0;
                foreach (ToolStripItem it in _eventMenu.Items)
                {
                    if (it is ToolStripSeparator) continue;
                    if (it.Name == "Insert event here") it.Enabled = !busy;
                    else it.Enabled = !busy && has;
                }
                _eventMenu.Show(lstEvents, e.Location);
            };

            // 事件编辑按钮
            btnEditDelay = new ClayButton { Name = "Edit delay", Text = Lang.T("Edit delay"), Location = new Point(Dpi.X(440), Dpi.X(234)), Size = new Size(Dpi.X(94), Dpi.X(30)), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnAddEvent = new ClayButton { Name = "Add event", Text = Lang.T("Add event"), Location = new Point(Dpi.X(440), Dpi.X(266)), Size = new Size(Dpi.X(94), Dpi.X(30)), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnDeleteEvent = new ClayButton { Name = "Delete selected", Text = Lang.T("Delete selected"), Location = new Point(Dpi.X(424), Dpi.X(298)), Size = new Size(Dpi.X(110), Dpi.X(30)), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            page.Controls.AddRange(new Control[] { btnEditDelay, btnAddEvent, btnDeleteEvent });

            lblEventCount = new Label { Text = Lang.F("Events: {0}", 0), Location = new Point(Dpi.X(15), Dpi.X(354)), AutoSize = true, ForeColor = Clay.InkSoft, BackColor = Clay.WindowBg };
            page.Controls.Add(lblEventCount);

            page.Controls.Add(Tip("Tip: recording captures mouse moves, clicks, wheel and keys; bound hotkeys and injected clicks are excluded.\r\nLoop count 0 = infinite; run minutes 0 = no limit; Until + time stops at that clock time. After stopping you can edit events: double-click a row or use the right-side buttons.", 12, 372));
        }

        private void BuildLibraryPage(Panel page)
        {
            // ---- 已存宏列表: 最左侧 ▶ 快速触发, 点击选中后右侧可重命名/副本/删除 ----
            var gbMacros = Grp("Saved macros", 10, 10, 524, 214);
            var shell = new ClayPanel
            {
                Location = new Point(Dpi.X(10), Dpi.X(28)),
                Size = new Size(Dpi.X(360), Dpi.X(172)),
                Inset = true,
                BackColor = Theme.Current.InputBg
            };
            lstMacros = new ListView
            {
                Location = new Point(Dpi.X(4), Dpi.X(4)),
                Size = new Size(Dpi.X(352), Dpi.X(164)),
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Current.InputBg,
                ForeColor = Clay.Ink
            };
            lstMacros.Columns.Add("▶", Dpi.X(36));
            lstMacros.Columns.Add(Lang.T("Macro name"), Dpi.X(150));
            lstMacros.Columns.Add(Lang.T("Events"), Dpi.X(56));
            lstMacros.Columns.Add(Lang.T("Modified"), Dpi.X(110));
            typeof(ListView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(lstMacros, true, null);
            shell.Controls.Add(lstMacros);
            gbMacros.Controls.Add(shell);

            _macroMenu = ClayMenu.Build();
            ToolStripMenuItem mi;
            mi = ClayMenu.Item(_macroMenu, "Play this macro");
            mi.Click += delegate { PlaySelectedMacro(); };
            mi = ClayMenu.Item(_macroMenu, "Rename...");
            mi.Click += delegate { RenameMacro(); };
            mi = ClayMenu.Item(_macroMenu, "Create a copy");
            mi.Click += delegate { CopyMacro(); };
            _macroMenu.Items.Add(new ToolStripSeparator());
            mi = ClayMenu.Item(_macroMenu, "Delete macro");
            mi.Click += delegate { DeleteMacro(); };
            lstMacros.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right)
                {
                    var item = lstMacros.GetItemAt(e.X, e.Y);
                    if (item != null)
                    {
                        lstMacros.SelectedIndices.Clear();
                        lstMacros.SelectedIndices.Add(item.Index);
                    }
                    UpdateMacroButtons();
                    _macroMenu.Show(lstMacros, e.Location);
                    return;
                }
                // 最左侧 ▶ 列 = 快速触发按钮
                if (e.Button == MouseButtons.Left && lstMacros.Columns.Count > 0 && e.X < lstMacros.Columns[0].Width)
                {
                    var item = lstMacros.GetItemAt(e.X, e.Y);
                    if (item != null) PlayMacroFile(_macroFiles[item.Index]);
                }
            };
            lstMacros.DoubleClick += delegate { PlaySelectedMacro(); };

            btnMacroRename = new ClayButton { Name = "Rename", Text = Lang.T("Rename"), Location = new Point(Dpi.X(380), Dpi.X(28)), Size = new Size(Dpi.X(140), Dpi.X(30)) };
            btnMacroCopy = new ClayButton { Name = "Create a copy", Text = Lang.T("Create a copy"), Location = new Point(Dpi.X(380), Dpi.X(65)), Size = new Size(Dpi.X(140), Dpi.X(30)) };
            btnMacroDelete = new ClayButton { Name = "Delete macro", Text = Lang.T("Delete macro"), Location = new Point(Dpi.X(380), Dpi.X(102)), Size = new Size(Dpi.X(140), Dpi.X(30)) };
            var btnMacroRefresh = new ClayButton { Name = "Refresh", Text = Lang.T("Refresh"), Location = new Point(Dpi.X(380), Dpi.X(139)), Size = new Size(Dpi.X(140), Dpi.X(30)) };
            var btnMacroFolder = new ClayButton { Name = "Open macros folder", Text = Lang.T("Open macros folder"), Location = new Point(Dpi.X(380), Dpi.X(176)), Size = new Size(Dpi.X(140), Dpi.X(30)) };
            btnMacroRefresh.Click += delegate { RefreshMacroList(); };
            btnMacroFolder.Click += delegate { OpenMacrosFolder(); };
            gbMacros.Controls.AddRange(new Control[] { btnMacroRename, btnMacroCopy, btnMacroDelete, btnMacroRefresh, btnMacroFolder });
            page.Controls.Add(gbMacros);

            // ---- 软件控制: 自动启动程序 ----
            var gbSoft = Grp("Software control", 10, 234, 524, 150);
            var softShell = new ClayPanel
            {
                Location = new Point(Dpi.X(10), Dpi.X(28)),
                Size = new Size(Dpi.X(300), Dpi.X(80)),
                Inset = true,
                BackColor = Theme.Current.InputBg
            };
            lstPrograms = new ListBox
            {
                Location = new Point(Dpi.X(4), Dpi.X(4)),
                Size = new Size(Dpi.X(292), Dpi.X(72)),
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Current.InputBg,
                ForeColor = Clay.Ink,
                IntegralHeight = false
            };
            softShell.Controls.Add(lstPrograms);
            gbSoft.Controls.Add(softShell);
            btnProgramAdd = new ClayButton { Name = "Add program...", Text = Lang.T("Add program..."), Location = new Point(Dpi.X(325), Dpi.X(28)), Size = new Size(Dpi.X(130), Dpi.X(30)) };
            btnProgramRemove = new ClayButton { Name = "Remove program", Text = Lang.T("Remove program"), Location = new Point(Dpi.X(325), Dpi.X(65)), Size = new Size(Dpi.X(130), Dpi.X(30)) };
            gbSoft.Controls.AddRange(new Control[] { btnProgramAdd, btnProgramRemove });
            chkLaunchStart = new ClayCheck { Name = "Launch programs when playback starts", Text = Lang.T("Launch programs when playback starts"), Location = new Point(Dpi.X(15), Dpi.X(118)) };
            chkLaunchEnd = new ClayCheck { Name = "Launch programs when playback ends", Text = Lang.T("Launch programs when playback ends"), Location = new Point(Dpi.X(230), Dpi.X(118)) };
            gbSoft.Controls.AddRange(new Control[] { chkLaunchStart, chkLaunchEnd });
            page.Controls.Add(gbSoft);

            page.Controls.Add(Tip("Tip: click the ▶ button on the left of a saved macro to run it immediately; click the name to select it, then rename / copy / delete on the right.\r\nPrograms in the Software control list auto-launch when playback starts or ends.", 12, 392));
        }

        private void BuildHotkeyPage(Panel page)
        {
            var gb = Grp("Function hotkeys (click a button, then press the new combo)", 10, 10, 524, 200);
            string[] names = { Lang.T("Clicker toggle"), Lang.T("Recording toggle"), Lang.T("Playback toggle"), Lang.T("Keyboard toggle"), Lang.T("Stop all") };
            _hkButtons = new Button[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int y = 32 + i * 32;
                gb.Controls.Add(Lbl(names[i], 20, y + 5));
                // 右锚定: 按钮右缘始终保持在卡片内, 窗口缩放/DPI 变化不会溢出画面
                var b = new ClayButton
                {
                    Location = new Point(Dpi.X(200), Dpi.X(y)),
                    Size = new Size(Dpi.X(170), Dpi.X(26)),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Text = "..."
                };
                int idx = i;
                b.Click += delegate { CaptureHotkey(idx); };
                _hkButtons[i] = b;
                gb.Controls.Add(b);
            }
            page.Controls.Add(gb);

            btnResetHotkeys = new ClayButton { Name = "Reset default hotkeys", Text = Lang.T("Reset default hotkeys"), Location = new Point(Dpi.X(15), Dpi.X(220)), Size = new Size(Dpi.X(145), Dpi.X(30)), BackColor = Clay.WindowBg };
            page.Controls.Add(btnResetHotkeys);

            page.Controls.Add(Tip("Tip: any key, Ctrl/Alt/Shift/Win combos, multi-key combos (e.g. Ctrl+Q+W), mouse side buttons X1/X2.\r\nChanges are saved to config.json immediately and restored on next launch.\r\nCombos with modifiers or F-keys are recommended; bare letters/numbers conflict with typing.", 12, 262));
        }

        private void BuildAdvancedPage(Panel page)
        {
            // ---- 注入方式 ----
            var gbInject = Grp("Injection method (switch when a game blocks clicking)", 10, 10, 524, 128);
            gbInject.Controls.Add(Lbl("Injection method:", 15, 30));
            cboMethod = new ClayComboBox();
            cboMethod.Items.AddRange(new object[]
            {
                Lang.T("SendInput - standard system injection (default)"),
                Lang.T("SendMessage - direct window messages (bypasses injected-flag detection)"),
                Lang.T("Interception - driver-level injection (most thorough, needs driver)")
            });
            cboMethod.SelectedIndex = 0;
            var methodShell = ClayKit.InputShell(cboMethod, 90, 26, 260);
            methodShell.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            gbInject.Controls.Add(methodShell);
            chkScanCode = new ClayCheck { Name = "Keyboard uses scan code injection (experimental, some games only accept scan codes)", Text = Lang.T("Keyboard uses scan code injection (experimental, some games only accept scan codes)"), Location = new Point(Dpi.X(15), Dpi.X(60)) };
            rbTargetForeground = new ClayRadio { Name = "Target window: foreground", Text = Lang.T("Target window: foreground"), Location = new Point(Dpi.X(15), Dpi.X(88)), Checked = true };
            rbTargetNamed = new ClayRadio { Name = "Specified title:", Text = Lang.T("Specified title:"), Location = new Point(Dpi.X(130), Dpi.X(88)) };
            txtTargetWindow = new TextBox { BorderStyle = BorderStyle.None };
            var targetShell = ClayKit.InputShell(txtTargetWindow, 205, 84, 140);
            targetShell.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            gbInject.Controls.Add(targetShell);
            btnGrabWindow = new ClayButton { Name = "Grab window title", Text = Lang.T("Grab window title"), Location = new Point(Dpi.X(355), Dpi.X(84)), Size = new Size(Dpi.X(120), Dpi.X(26)), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            gbInject.Controls.AddRange(new Control[] { chkScanCode, rbTargetForeground, rbTargetNamed, btnGrabWindow });
            page.Controls.Add(gbInject);

            // ---- 拟人化(总开关 + 各子项) ----
            var gbHuman = Grp("Humanization", 10, 148, 524, 132);
            chkHumanizeEnabled = new ClayCheck { Name = "Enable humanization", Text = Lang.T("Enable humanization"), Location = new Point(Dpi.X(140), Dpi.X(4)), Checked = true, BackColor = Clay.CardBg };
            chkHumanizeTiming = new ClayCheck { Name = "Randomize interval ±", Text = Lang.T("Randomize interval ±"), Location = new Point(Dpi.X(15), Dpi.X(28)), Checked = true };
            numTimingPct = new ClayNumericUpDown { Minimum = 0, Maximum = 90, Value = 15, BorderStyle = BorderStyle.None };
            gbHuman.Controls.Add(ClayKit.InputShell(numTimingPct, 155, 24, 55));
            gbHuman.Controls.Add(Lbl("%", 215, 28));
            chkHumanizePos = new ClayCheck { Name = "Fixed position jitter ±", Text = Lang.T("Fixed position jitter ±"), Location = new Point(Dpi.X(15), Dpi.X(54)), Checked = true };
            numPosPx = new ClayNumericUpDown { Minimum = 0, Maximum = 20, Value = 2, BorderStyle = BorderStyle.None };
            gbHuman.Controls.Add(ClayKit.InputShell(numPosPx, 155, 50, 55));
            gbHuman.Controls.Add(Lbl("pixels", 215, 54));
            chkHumanizePress = new ClayCheck { Name = "Random press duration (40~180ms, human-like)", Text = Lang.T("Random press duration (40~180ms, human-like)"), Location = new Point(Dpi.X(15), Dpi.X(80)), Checked = true };
            chkHumanizeTraj = new ClayCheck { Name = "Mouse trajectory (smooth curve instead of teleport)", Text = Lang.T("Mouse trajectory (smooth curve instead of teleport)"), Location = new Point(Dpi.X(15), Dpi.X(106)), Checked = true };
            gbHuman.Controls.AddRange(new Control[] { chkHumanizeEnabled, chkHumanizeTiming, chkHumanizePos, chkHumanizePress, chkHumanizeTraj });
            page.Controls.Add(gbHuman);

            // ---- 界面: 语言与主题 / 动效 ----
            var gbUi = Grp("Interface", 10, 290, 524, 84);
            gbUi.Controls.Add(Lbl("Language:", 15, 28));
            cboLanguage = new ClayComboBox();
            cboLanguage.Items.AddRange(new object[] { Lang.T("中文"), Lang.T("English") });
            cboLanguage.SelectedIndex = 0;
            gbUi.Controls.Add(ClayKit.InputShell(cboLanguage, 100, 24, 110));
            gbUi.Controls.Add(Lbl("Theme:", 240, 28));
            cboTheme = new ClayComboBox();
            foreach (var t in Theme.All) cboTheme.Items.Add(Lang.Code == "zh" ? t.NameZh : t.NameEn);
            cboTheme.SelectedIndex = 0;
            gbUi.Controls.Add(ClayKit.InputShell(cboTheme, 305, 24, 195));
            chkAnimations = new ClayCheck { Name = "Enable UI animations (hover / press / tab transitions)", Text = Lang.T("Enable UI animations (hover / press / tab transitions)"), Location = new Point(Dpi.X(15), Dpi.X(58)), Checked = true };
            gbUi.Controls.Add(chkAnimations);
            page.Controls.Add(gbUi);

            // ---- 启动与托盘 ----
            var gbStartup = Grp("Startup", 10, 378, 524, 56);
            chkAutoStart = new ClayCheck { Name = "Start with Windows", Text = Lang.T("Start with Windows"), Location = new Point(Dpi.X(15), Dpi.X(30)) };
            chkSilentStart = new ClayCheck { Name = "Start silently (to tray)", Text = Lang.T("Start silently (to tray)"), Location = new Point(Dpi.X(280), Dpi.X(30)) };
            gbStartup.Controls.AddRange(new Control[] { chkAutoStart, chkSilentStart });
            page.Controls.Add(gbStartup);

            // 驱动模式说明改为悬停提示(页面已排满): 选中驱动模式无 dll 时状态栏也会额外警告
            _hintTip.SetToolTip(cboMethod, Lang.T("Driver mode: install the driver (github.com/oblitum/Interception, admin) and put interception.dll next to this exe.\r\nHVCI (memory integrity) may block unsigned drivers; rename this exe to dodge process-name checks."));
        }

        private void BuildSfxPage(Panel page)
        {
            var gb = Grp("Key Sound Effects", 10, 10, 524, 128);
            chkSfx = new ClayCheck { Name = "Enable key sound effects (new key overrides the playing sound)", Text = Lang.T("Enable key sound effects (new key overrides the playing sound)"), Location = new Point(Dpi.X(15), Dpi.X(28)), Checked = true };
            // 全局音量滑块
            gb.Controls.Add(Lbl("Global volume:", 15, 60));
            sldGlobalVolume = new ClaySlider { Minimum = 0, Maximum = 100, Value = 100, Location = new Point(Dpi.X(110), Dpi.X(56)), Size = new Size(Dpi.X(150), Dpi.X(22)) };
            gb.Controls.Add(sldGlobalVolume);
            lblGlobalVol = new Label { Text = "100%", Location = new Point(Dpi.X(268), Dpi.X(60)), AutoSize = true, ForeColor = Clay.Ink, BackColor = Clay.CardBg };
            gb.Controls.Add(lblGlobalVol);
            btnSfxAdd = new ClayButton { Name = "Add binding", Text = Lang.T("Add binding"), Location = new Point(Dpi.X(15), Dpi.X(88)), Size = new Size(Dpi.X(100), Dpi.X(28)) };
            btnSfxDelete = new ClayButton { Name = "Delete binding", Text = Lang.T("Delete binding"), Location = new Point(Dpi.X(125), Dpi.X(88)), Size = new Size(Dpi.X(105), Dpi.X(28)) };
            btnSfxTest = new ClayButton { Name = "Play sound", Text = Lang.T("Play sound"), Location = new Point(Dpi.X(240), Dpi.X(88)), Size = new Size(Dpi.X(80), Dpi.X(28)) };
            btnSfxFolder = new ClayButton { Name = "Open sounds folder", Text = Lang.T("Open sounds folder"), Location = new Point(Dpi.X(330), Dpi.X(88)), Size = new Size(Dpi.X(140), Dpi.X(28)) };
            gb.Controls.AddRange(new Control[] { chkSfx, btnSfxAdd, btnSfxDelete, btnSfxTest, btnSfxFolder });
            page.Controls.Add(gb);

            // 绑定列表
            var lstShell = new ClayPanel
            {
                Location = new Point(Dpi.X(10), Dpi.X(148)),
                Size = new Size(Dpi.X(420), Dpi.X(150)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Inset = true,
                BackColor = Theme.Current.InputBg
            };
            lstSfx = new ListView
            {
                Location = new Point(Dpi.X(4), Dpi.X(4)),
                Size = new Size(Dpi.X(412), Dpi.X(142)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Current.InputBg,
                ForeColor = Clay.Ink
            };
            lstSfx.Columns.Add(Lang.T("Key:"), Dpi.X(110));
            lstSfx.Columns.Add(Lang.T("Sound file"), Dpi.X(290));
            lstSfx.DoubleClick += delegate { TestSfx(); };
            typeof(ListView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(lstSfx, true, null);
            lstShell.Controls.Add(lstSfx);
            page.Controls.Add(lstShell);

            lblSfxCount = new Label { Text = Lang.F("Bindings: {0}", 0), Location = new Point(Dpi.X(15), Dpi.X(306)), AutoSize = true, ForeColor = Clay.InkSoft, BackColor = Clay.WindowBg };
            page.Controls.Add(lblSfxCount);

            // 选中按键音量(单键覆盖全局)
            var lblKeyVolTitle = new Label { Name = "Selected key volume:", Text = Lang.T("Selected key volume:"), Location = new Point(Dpi.X(15), Dpi.X(330)), AutoSize = true, ForeColor = Clay.InkSoft, BackColor = Clay.WindowBg };
            sldKeyVolume = new ClaySlider { Minimum = 0, Maximum = 100, Value = 100, Location = new Point(Dpi.X(150), Dpi.X(326)), Size = new Size(Dpi.X(150), Dpi.X(22)), Enabled = false };
            lblKeyVol = new Label { Text = "100%", Location = new Point(Dpi.X(308), Dpi.X(330)), AutoSize = true, ForeColor = Clay.Ink, BackColor = Clay.WindowBg };
            page.Controls.AddRange(new Control[] { lblKeyVolTitle, sldKeyVolume, lblKeyVol });

            page.Controls.Add(Tip("Tip: assign a sound to any key; pressing the key plays the sound, and a newly pressed bound key overrides the currently playing one.\r\nPut sound files (wav/mp3) into the Sounds folder next to this program.", 12, 356));
        }

        private void WireEvents()
        {
            btnClickerToggle.Click += delegate { ToggleClicker(); };
            btnKeyboardToggle.Click += delegate { ToggleKeyboard(); };
            btnRecord.Click += delegate { ToggleRecord(); };
            btnPlay.Click += delegate { TogglePlay(); };
            btnTestClick.Click += delegate { TestClick(); };
            btnGetPos.Click += delegate { GetPos(); };
            _hintTip.SetToolTip(btnTestClick, Lang.T("Test click tooltip"));
            btnSave.Click += delegate { SaveMacro(); };
            btnLoad.Click += delegate { LoadMacro(); };
            btnClear.Click += delegate { ClearMacro(); };
            btnEditDelay.Click += delegate { EditDelay(); };
            btnAddEvent.Click += delegate { AddEvent(); };
            btnDeleteEvent.Click += delegate { DeleteEvent(); };
            lstEvents.SelectedIndexChanged += delegate { if (!_applying) UpdateEventEditButtons(); };
            chkUntilTime.CheckedChanged += delegate { txtUntilTime.Enabled = chkUntilTime.Checked; if (!_applying) SaveSettings(); };
            txtUntilTime.TextChanged += delegate { if (!_applying) SaveSettings(); };

            // 宏库页
            lstMacros.SelectedIndexChanged += delegate { if (!_applying) UpdateMacroButtons(); };
            btnMacroRename.Click += delegate { RenameMacro(); };
            btnMacroCopy.Click += delegate { CopyMacro(); };
            btnMacroDelete.Click += delegate { DeleteMacro(); };
            btnProgramAdd.Click += delegate { AddProgram(); };
            btnProgramRemove.Click += delegate { RemoveProgram(); };
            chkLaunchStart.CheckedChanged += delegate { if (!_applying) SaveSettings(); };
            chkLaunchEnd.CheckedChanged += delegate { if (!_applying) SaveSettings(); };
            chkTopmost.CheckedChanged += delegate { TopMost = chkTopmost.Checked; };
            btnAbout.Click += delegate { using (var f = new AboutForm()) f.ShowDialog(this); };
            rbTap.CheckedChanged += delegate { numKeyInterval.Enabled = rbTap.Checked; };

            // 高级设置: 改动即时生效
            cboMethod.SelectedIndexChanged += delegate
            {
                PushToEngines();
                if (cboMethod.SelectedIndex == 2 && !InterceptionDriver.IsDllPresent())
                    SetStatus(Lang.T("Warning: interception.dll not found, driver mode unavailable (see note below)"));
            };
            chkScanCode.CheckedChanged += delegate { PushToEngines(); };
            rbTargetForeground.CheckedChanged += delegate { PushToEngines(); };
            rbTargetNamed.CheckedChanged += delegate { PushToEngines(); };
            txtTargetWindow.TextChanged += delegate { PushToEngines(); };
            chkHumanizeEnabled.CheckedChanged += delegate
            {
                UpdateHumanizeChildState();
                PushToEngines();
            };
            chkHumanizeTiming.CheckedChanged += delegate { PushToEngines(); };
            numTimingPct.ValueChanged += delegate { PushToEngines(); };
            chkHumanizePos.CheckedChanged += delegate { PushToEngines(); };
            numPosPx.ValueChanged += delegate { PushToEngines(); };
            chkHumanizePress.CheckedChanged += delegate { PushToEngines(); };
            chkHumanizeTraj.CheckedChanged += delegate { PushToEngines(); };
            btnGrabWindow.Click += delegate { GrabWindow(); };
            btnResetHotkeys.Click += delegate { ResetHotkeys(); };

            // 按键音效
            chkSfx.CheckedChanged += delegate
            {
                if (_applying) return;
                _sfx.Enabled = chkSfx.Checked;
                SaveSettings();
                SetStatus(chkSfx.Checked ? Lang.T("Key sound effects enabled") : Lang.T("Key sound effects disabled"));
            };
            sldGlobalVolume.ValueChanged += delegate
            {
                if (_applying || _sfxSliderSync) return;
                _cfg.SfxVolume = (int)sldGlobalVolume.Value;
                SfxPlayer.Volume = _cfg.SfxVolume * 10; // 0~100 → MCI 0~1000
                lblGlobalVol.Text = _cfg.SfxVolume + "%";
                ApplySfxToEngine(); // 重新计算各键生效音量
                SaveSettings();
                UpdateKeyVolumeSlider(); // 未单独设音量的选中键跟随全局
            };
            sldKeyVolume.ValueChanged += delegate
            {
                if (_applying || _sfxSliderSync) return;
                var sel = SelectedSfx();
                if (sel == null) return;
                sel.Volume = (int)sldKeyVolume.Value;
                if (sel.IsCombo) _cfg.SfxComboVolumes[sel.Combo] = sel.Volume;
                else _cfg.SfxBindingVolumes[sel.Vk.ToString()] = sel.Volume;
                lblKeyVol.Text = sel.Volume + "%";
                ApplySfxToEngine();
                SaveSettings();
            };
            lstSfx.SelectedIndexChanged += delegate
            {
                if (_applying || _sfxSliderSync) return;
                UpdateKeyVolumeSlider();
            };
            btnSfxAdd.Click += delegate { AddSfxBinding(); };
            btnSfxDelete.Click += delegate { DeleteSfxBinding(); };
            btnSfxTest.Click += delegate { TestSfx(); };
            btnSfxFolder.Click += delegate { OpenSfxFolder(); };

            // 语言 / 主题切换
            cboLanguage.SelectedIndexChanged += delegate
            {
                if (_applying) return;
                Lang.Code = cboLanguage.SelectedIndex == 0 ? "zh" : "en";
                BuildKeyOptions();
                ApplyLanguage();
                SaveSettings();
            };
            cboTheme.SelectedIndexChanged += delegate
            {
                if (_applying) return;
                if (cboTheme.SelectedIndex < 0 || cboTheme.SelectedIndex >= Theme.All.Length) return;
                Theme.Current = Theme.All[cboTheme.SelectedIndex];
                ApplyTheme();
                SaveSettings();
            };
            chkAnimations.CheckedChanged += delegate
            {
                if (_applying) return;
                Anim.Enabled = chkAnimations.Checked;
                SaveSettings();
                Invalidate(true);
            };

            // 启动与托盘
            chkAutoStart.CheckedChanged += delegate
            {
                if (_applying) return;
                AutoStart.SetEnabled(chkAutoStart.Checked);
                SaveSettings();
                SetStatus(chkAutoStart.Checked ? Lang.T("Auto-start enabled") : Lang.T("Auto-start disabled"));
            };
            chkSilentStart.CheckedChanged += delegate
            {
                if (_applying) return;
                SaveSettings(); // 下次启动生效
            };
        }

        // ---------- 语言 / 主题应用 ----------

        /// <summary>按 Name(=英文原文)递归更新所有静态控件文本。</summary>
        private void ApplyLangWalk(Control c)
        {
            if (!string.IsNullOrEmpty(c.Name))
            {
                string t = Lang.T(c.Name);
                if (c is Label) ((Label)c).Text = t;
                else if (c is CheckBox) ((CheckBox)c).Text = t;
                else if (c is RadioButton) ((RadioButton)c).Text = t;
                else if (c is Button) ((Button)c).Text = t;
                else if (c is GroupBox) ((GroupBox)c).Text = t;
            }
            foreach (Control child in c.Controls) ApplyLangWalk(child);
        }

        private void ApplyLanguage()
        {
            Text = Lang.F("Auto Clicker {0}", VersionInfo.Version);
            ApplyLangWalk(this);

            LayoutTabStrip();

            if (lstEvents.Columns.Count == 2)
            {
                lstEvents.Columns[0].Text = Lang.T("Delay(ms)");
                lstEvents.Columns[1].Text = Lang.T("Event");
            }

            // 右键菜单与音效列表按语言刷新
            if (_eventMenu != null)
            {
                foreach (ToolStripItem it in _eventMenu.Items)
                {
                    if (it is ToolStripSeparator) continue;
                    it.Text = Lang.T(it.Name);
                }
            }
            if (_macroMenu != null)
            {
                foreach (ToolStripItem it in _macroMenu.Items)
                {
                    if (it is ToolStripSeparator) continue;
                    it.Text = Lang.T(it.Name);
                }
            }
            if (lstMacros != null && lstMacros.Columns.Count == 4)
            {
                lstMacros.Columns[1].Text = Lang.T("Macro name");
                lstMacros.Columns[2].Text = Lang.T("Events");
                lstMacros.Columns[3].Text = Lang.T("Modified");
                RefreshMacroList();
            }
            if (lstSfx.Columns.Count == 2)
            {
                lstSfx.Columns[0].Text = Lang.T("Key:");
                lstSfx.Columns[1].Text = Lang.T("Sound file");
            }
            RefreshSfxList();

            // 下拉框 items 重建(保持选择)
            int sel = cboButton.SelectedIndex;
            cboButton.Items.Clear();
            cboButton.Items.AddRange(new object[] { Lang.T("Left"), Lang.T("Right"), Lang.T("Middle") });
            cboButton.SelectedIndex = Clamp(sel, 0, 2);

            int curVk = _keyOptions[cboKey.SelectedIndex].Value;
            cboKey.Items.Clear();
            foreach (var kv in _keyOptions) cboKey.Items.Add(kv.Key);
            int idx = _keyOptions.FindIndex(kv => kv.Value == curVk);
            cboKey.SelectedIndex = idx >= 0 ? idx : 0;

            sel = cboMethod.SelectedIndex;
            cboMethod.Items.Clear();
            cboMethod.Items.Add(Lang.T("SendInput - standard system injection (default)"));
            cboMethod.Items.Add(Lang.T("SendMessage - direct window messages (bypasses injected-flag detection)"));
            cboMethod.Items.Add(Lang.T("Interception - driver-level injection (most thorough, needs driver)"));
            cboMethod.SelectedIndex = Clamp(sel, 0, 2);

            sel = cboTheme.SelectedIndex;
            cboTheme.Items.Clear();
            foreach (var t in Theme.All) cboTheme.Items.Add(Lang.Code == "zh" ? t.NameZh : t.NameEn);
            cboTheme.SelectedIndex = Clamp(sel, 0, Theme.All.Length - 1);

            if (_trayOpen != null) _trayOpen.Text = Lang.T("Show main window");
            if (_trayExit != null) _trayExit.Text = Lang.T("Exit");
            if (_trayIcon != null) _trayIcon.Text = Lang.F("Auto Clicker {0}", VersionInfo.Version);

            UpdateAllUi();
            RefreshEventList(false);
            Invalidate(true);
        }

        private void WalkTheme(Control c, Color parentBg)
        {
            Color bg = parentBg;
            if (c is ClayGroup)
            {
                bg = Clay.CardBg;
                c.BackColor = bg;
            }
            else if (c is ClayPanel)
            {
                bg = ((ClayPanel)c).Inset ? Theme.Current.InputBg : Clay.CardBg;
                c.BackColor = bg;
            }
            else if (c is Label)
            {
                c.BackColor = bg;
                c.ForeColor = Clay.InkSoft;
            }
            else if (c is ClayButton)
            {
                c.BackColor = bg;
                c.ForeColor = Clay.Ink;
            }
            else if (c is ClayCheck || c is ClayRadio)
            {
                c.BackColor = bg;
                c.ForeColor = Clay.Ink;
            }
            else if (c is ComboBox || c is NumericUpDown || c is TextBox)
            {
                c.BackColor = Theme.Current.InputBg;
                c.ForeColor = Theme.Current.Ink;
            }
            else if (c is ListView)
            {
                c.BackColor = Theme.Current.InputBg;
                c.ForeColor = Clay.Ink;
            }
            else if (c is Panel)
            {
                c.BackColor = bg;
            }
            foreach (Control child in c.Controls) WalkTheme(child, bg);
        }

        private void ApplyTheme()
        {
            BackColor = Clay.WindowBg;
            WalkTheme(_topPanel, Clay.WindowBg);
            WalkTheme(_tabStrip, Clay.WindowBg);
            WalkTheme(_contentPanel, Clay.WindowBg);
            WalkTheme(_statusBar, Theme.Current.StatusBg);
            UpdateAllUi();
            RefreshEventList(false);
            RefreshSfxList();
            ApplyFrameTheme();
            Invalidate(true);
        }

        // ---------- 事件列表与编辑 ----------

        private string DescribeEvent(MacroEvent e)
        {
            switch (e.Kind)
            {
                case MacroEventKind.Move: return Lang.F("Move to ({0}, {1})", e.X, e.Y);
                case MacroEventKind.LeftDown: return Lang.T("Left down");
                case MacroEventKind.LeftUp: return Lang.T("Left up");
                case MacroEventKind.RightDown: return Lang.T("Right down");
                case MacroEventKind.RightUp: return Lang.T("Right up");
                case MacroEventKind.MiddleDown: return Lang.T("Middle down");
                case MacroEventKind.MiddleUp: return Lang.T("Middle up");
                case MacroEventKind.Wheel: return Lang.F("Wheel {0}", e.Data);
                case MacroEventKind.KeyDown: return KeyName(e.Data) + " " + Lang.T("Key down");
                case MacroEventKind.KeyUp: return KeyName(e.Data) + " " + Lang.T("Key up");
                case MacroEventKind.LeftClick: return Lang.T("Left click");
                case MacroEventKind.RightClick: return Lang.T("Right click");
                case MacroEventKind.MiddleClick: return Lang.T("Middle click");
                case MacroEventKind.KeyTap: return Lang.F("Key tap {0}", KeyName(e.Data));
                case MacroEventKind.Delay: return Lang.T("Delay");
                case MacroEventKind.KeyComboTap: return Lang.F("Key combo {0}", e.Combo ?? "");
                case MacroEventKind.KeyComboDown: return Lang.F("Key combo down {0}", e.Combo ?? "");
                case MacroEventKind.KeyComboUp: return Lang.F("Key combo up {0}", e.Combo ?? "");
            }
            return "";
        }

        private string KeyName(int vk)
        {
            foreach (var kv in _keyOptions)
            {
                if (kv.Value == vk) return kv.Key;
            }
            return Hotkey.GetName((uint)vk); // 原始键码: 左右修饰键/媒体键给出精确可读名
        }

        private int SelectedEventIndex()
        {
            if (lstEvents.SelectedIndices.Count > 0) return lstEvents.SelectedIndices[0];
            return -1;
        }

        private void RefreshEventList(bool follow)
        {
            lstEvents.VirtualListSize = _recorder.Count;
            lblEventCount.Text = Lang.F("Events: {0}", _recorder.Count);
            if (follow && _recorder.Count > 0) lstEvents.EnsureVisible(_recorder.Count - 1); // 录制中实时跟随最新
            lstEvents.Invalidate();
        }

        private void EditDelay()
        {
            int i = SelectedEventIndex();
            if (i < 0)
            {
                SetStatus(Lang.T("Select an event first"));
                return;
            }
            var ev = _recorder.Events[i];
            using (var f = new DelayEditForm(ev.DelayMs))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    ev.DelayMs = f.NewDelayMs;
                    RefreshEventList(false);
                    SetStatus(Lang.F("Delay of event {0} changed to {1} ms", i + 1, ev.DelayMs));
                }
            }
        }

        private void AddEvent()
        {
            using (var f = new EventAddForm(_keyOptions))
            {
                if (f.ShowDialog(this) == DialogResult.OK && f.Result != null)
                {
                    int i = SelectedEventIndex();
                    if (i >= 0 && i + 1 <= _recorder.Events.Count)
                    {
                        _recorder.Events.Insert(i + 1, f.Result);
                        SetStatus(Lang.F("Event added at position {0}", i + 2));
                    }
                    else
                    {
                        _recorder.Events.Add(f.Result);
                        SetStatus(Lang.F("Event added at position {0}", _recorder.Events.Count));
                    }
                    RefreshEventList(false);
                    // VirtualListSize 变化可能清空选择: 重新选中新增事件
                    int sel = Math.Min(i + 1, _recorder.Events.Count - 1);
                    if (sel < 0) sel = _recorder.Events.Count - 1;
                    lstEvents.SelectedIndices.Clear();
                    lstEvents.SelectedIndices.Add(sel);
                    UpdateRecordUi();
                }
            }
        }

        private void DeleteEvent(bool confirm = true)
        {
            int i = SelectedEventIndex();
            if (i < 0)
            {
                SetStatus(Lang.T("Select an event first"));
                return;
            }
            if (confirm && MessageBox.Show(this, Lang.T("Delete this event?"), Lang.T("Delete event"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _recorder.Events.RemoveAt(i);
            RefreshEventList(false);
            UpdateRecordUi();
            SetStatus(Lang.T("Event deleted"));
        }

        /// <summary>上下移动事件: dir = -1 上移, 1 下移。事件的等待时长跟随事件本身。</summary>
        private void MoveEvent(int dir)
        {
            int i = SelectedEventIndex();
            if (i < 0) return;
            int j = i + dir;
            if (j < 0 || j >= _recorder.Events.Count) return;
            var e = _recorder.Events[i];
            _recorder.Events.RemoveAt(i);
            _recorder.Events.Insert(j, e);
            lstEvents.SelectedIndices.Clear();
            lstEvents.SelectedIndices.Add(j);
            RefreshEventList(false);
            UpdateRecordUi();
        }

        // ---------- 任务开关 ----------

        private void ToggleClicker()
        {
            if (_clicker.Running)
            {
                _clicker.Stop();
                SetStatus(Lang.T("Stopping clicker..."));
                return;
            }
            if (InputSimulator.Method == InjectionMethod.InterceptionDriver && !InterceptionDriver.IsDllPresent())
            {
                SetStatus(Lang.T("Driver mode unavailable: interception.dll missing or driver not installed"));
                return;
            }
            _clicker.IntervalMs = (int)numInterval.Value;
            _clicker.Button = (MouseButton)cboButton.SelectedIndex;
            _clicker.FixedPosition = rbFixed.Checked;
            _clicker.FixedX = (int)numX.Value;
            _clicker.FixedY = (int)numY.Value;
            _clicker.RepeatCount = (int)numRepeat.Value;
            _clicker.Start();
            Log.Info(string.Format("开始连点: 间隔={0}ms 按键={1} 模式={2} 次数={3}",
                _clicker.IntervalMs, _clicker.Button, _clicker.FixedPosition ? "固定坐标" : "跟随光标", _clicker.RepeatCount));
            UpdateClickerUi();
            SetStatus(Lang.F("Clicker running, press {0} to stop", _hotkeys.Describe(HotkeyAction.Clicker)));
        }

        private void ToggleKeyboard()
        {
            if (_spammer.Running)
            {
                _spammer.Stop();
                SetStatus(Lang.T("Stopping keyboard spam..."));
                return;
            }
            int vk = ResolveKeyVk();
            if (vk <= 0)
            {
                SetStatus(Lang.T("Key not recognized, please select from the list or type a letter/number"));
                return;
            }
            _spammer.Vk = vk;
            _spammer.IntervalMs = (int)numKeyInterval.Value;
            _spammer.Mode = rbHold.Checked ? KeySpamMode.Hold : KeySpamMode.Tap;
            _spammer.ExtendedKey = InputSimulator.IsExtendedKey(vk);
            _spammer.Start();
            Log.Info(string.Format("开始连按: VK=0x{0:X2} 间隔={1}ms 模式={2}", vk, _spammer.IntervalMs, _spammer.Mode));
            UpdateKeyboardUi();
            SetStatus(Lang.F("Keyboard spam running, press {0} to stop", _hotkeys.Describe(HotkeyAction.Keyboard)));
        }

        /// <summary>
        /// 解析键盘连按的按键: 输入框有内容时优先按输入解析(按键名或单个字符), 空则用下拉框选择。
        /// 返回虚拟键码; 无法识别返回 0。
        /// </summary>
        private int ResolveKeyVk()
        {
            string t = txtKey != null && txtKey.Text != null ? txtKey.Text.Trim() : "";
            if (t.Length > 0)
            {
                // 1. 名称匹配(与下拉框显示名一致, 忽略大小写): "F1" / "Space" / "Enter" / "CapsLock" ...
                for (int i = 0; i < _keyOptions.Count; i++)
                {
                    if (string.Equals(_keyOptions[i].Key, t, StringComparison.OrdinalIgnoreCase))
                        return _keyOptions[i].Value;
                }
                // 2. 单个字符(字母/数字/符号): 经 VkKeyScan 转虚拟键码(忽略 shift 状态)
                if (t.Length == 1)
                {
                    short s = NativeMethods.VkKeyScan(t[0]);
                    int vk = s & 0xFF;
                    if (vk != 0xFF) return vk;
                }
                return 0;
            }
            return _keyOptions[cboKey.SelectedIndex].Value;
        }

        private void ToggleRecord()
        {
            if (_recorder.Recording)
            {
                _recorder.Stop();
                UpdateRecordUi();
                RefreshEventList(false);
                SetStatus(Lang.F("Recording stopped, {0} events", _recorder.Count));
                return;
            }
            if (_player.Playing) _player.Stop();
            _recorder.Start();
            if (!_recorder.Recording)
            {
                Log.Warn("录制启动失败");
                SetStatus(Lang.T("Recording failed to start"));
                return;
            }
            Log.Info("开始录制");
            UpdateRecordUi();
            RefreshEventList(false);
            SetStatus(Lang.F("Recording... (old macro cleared) press {0} to stop", _hotkeys.Describe(HotkeyAction.Record)));
        }

        private void TogglePlay()
        {
            if (_player.Playing)
            {
                _player.Stop();
                SetStatus(Lang.T("Stopping playback..."));
                return;
            }
            if (_recorder.Recording) _recorder.Stop();
            if (_recorder.Count == 0)
            {
                SetStatus(Lang.T("No macro to play, record first"));
                return;
            }
            string until = chkUntilTime.Checked && txtUntilTime != null ? txtUntilTime.Text.Trim() : "";
            if (chkUntilTime.Checked && !IsValidTime(until))
            {
                SetStatus(Lang.T("Invalid time, use HH:mm format"));
                return;
            }
            _player.Events = _recorder.Snapshot();
            _player.Speed = (double)numSpeed.Value;
            _player.Loop = chkLoop.Checked;
            _player.LoopCount = (int)numPlayLoops.Value;
            _player.RunMinutes = (int)numPlayMinutes.Value;
            _player.UntilTime = until;
            _player.Start();
            Log.Info(string.Format("开始回放: 事件={0} 倍速={1} 循环={2} 次数={3} 分钟={4} 直到={5}",
                _player.Events.Count, _player.Speed, _player.Loop, _player.LoopCount, _player.RunMinutes,
                string.IsNullOrEmpty(until) ? "不限" : until));
            if (_cfg.LaunchOnStart) LaunchPrograms();
            UpdateRecordUi();
            UpdatePlayUi();
            SetStatus(Lang.F("Playing... press {0} to stop", _hotkeys.Describe(HotkeyAction.Play)));
        }

        private static bool IsValidTime(string t)
        {
            DateTime dt;
            return DateTime.TryParseExact(t, "HH:mm",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dt);
        }

        private void StopAll()
        {
            _clicker.Stop();
            _spammer.Stop();
            _player.Stop();
            if (_recorder.Recording) _recorder.Stop();
            Log.Info("停止全部引擎");
            UpdateAllUi();
            SetStatus(Lang.F("Stopped all ({0})", _hotkeys.Describe(HotkeyAction.StopAll)));
        }

        // ---------- 其他界面操作 ----------

        private void TestClick()
        {
            if (_clicker.Running || _player.Playing)
            {
                SetStatus(Lang.T("Stop current task before testing"));
                return;
            }
            // 防抖: 避免模拟点击恰好落在“测试一下”按钮上造成连发
            if ((DateTime.Now - _lastTestClick).TotalMilliseconds < 800)
            {
                SetStatus(Lang.T("Test interval too short, please wait"));
                return;
            }
            _lastTestClick = DateTime.Now;
            var button = (MouseButton)cboButton.SelectedIndex;
            if (rbFixed.Checked)
                InputSimulator.ClickAt((int)numX.Value, (int)numY.Value, button);
            else
                InputSimulator.Click(button);
            SetStatus(Lang.T("Sent one click"));
        }

        private void GetPos()
        {
            // 给 3 秒时间把鼠标移到目标位置再抓取
            btnGetPos.Enabled = false;
            btnGetPos.Text = Lang.T("Get position in 3s...");
            var timer = new System.Windows.Forms.Timer { Interval = 3000 };
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                if (IsDisposed || Disposing) return;
                NativeMethods.POINT p;
                if (NativeMethods.GetCursorPos(out p))
                {
                    numX.Value = Math.Max(0, Math.Min(20000, p.x));
                    numY.Value = Math.Max(0, Math.Min(20000, p.y));
                    SetStatus(Lang.F("Got position: {0}, {1}", p.x, p.y));
                }
                btnGetPos.Text = Lang.T("Get mouse position");
                btnGetPos.Enabled = true;
            };
            timer.Start();
        }

        private void SaveMacro()
        {
            if (_recorder.Count == 0)
            {
                SetStatus(Lang.T("No macro to save"));
                return;
            }
            using (var dlg = new SaveFileDialog { Filter = "宏文件 (*.json)|*.json", FileName = "macro.json", InitialDirectory = AppConfig.MacrosDir })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        var ser = new JavaScriptSerializer();
                        File.WriteAllText(dlg.FileName, ser.Serialize(_recorder.Events), Encoding.UTF8);
                        Log.Info(string.Format("保存宏: {0} ({1} 个事件)", dlg.FileName, _recorder.Events.Count));
                        SetStatus(Lang.F("Macro saved: {0}", dlg.FileName));
                        RefreshMacroList();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, Lang.F("Save failed: {0}", ex.Message), Lang.T("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void LoadMacro()
        {
            using (var dlg = new OpenFileDialog { Filter = "宏文件 (*.json)|*.json", InitialDirectory = AppConfig.MacrosDir })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        if (_recorder.Recording) _recorder.Stop();
                        if (_player.Playing) _player.Stop();
                        var ser = new JavaScriptSerializer();
                        var list = ser.Deserialize<List<MacroEvent>>(File.ReadAllText(dlg.FileName, Encoding.UTF8));
                        _recorder.Load(list);
                        UpdateAllUi();
                        RefreshEventList(false);
                        RefreshMacroList();
                        Log.Info(string.Format("加载宏: {0} ({1} 个事件)", dlg.FileName, list.Count));
                        SetStatus(Lang.F("Macro loaded: {0} events", list.Count));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, Lang.F("Load failed: {0}", ex.Message), Lang.T("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ClearMacro()
        {
            if (_recorder.Recording) _recorder.Stop();
            if (_player.Playing) _player.Stop();
            _recorder.Clear();
            UpdateAllUi();
            RefreshEventList(false);
            SetStatus(Lang.T("Macro cleared"));
        }

        // ---------- 宏库: 已存宏列表 + 软件控制 ----------

        private void RefreshMacroList()
        {
            if (lstMacros == null) return;
            string selName = SelectedMacroIndex() >= 0 ? lstMacros.Items[SelectedMacroIndex()].SubItems[1].Text : "";
            _macroFiles.Clear();
            lstMacros.Items.Clear();
            try
            {
                if (!Directory.Exists(AppConfig.MacrosDir)) return;
                string[] files = Directory.GetFiles(AppConfig.MacrosDir, "*.json");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string f in files)
                {
                    _macroFiles.Add(f);
                    var name = Path.GetFileNameWithoutExtension(f);
                    int events = -1;
                    try
                    {
                        var ser = new JavaScriptSerializer();
                        var list = ser.Deserialize<List<MacroEvent>>(File.ReadAllText(f, Encoding.UTF8));
                        events = list != null ? list.Count : -1;
                    }
                    catch (Exception)
                    {
                        events = -1; // 损坏文件
                    }
                    var lvi = new ListViewItem(new[] { "▶", name, events >= 0 ? events.ToString() : "?", File.GetLastWriteTime(f).ToString("yyyy-MM-dd HH:mm") });
                    lvi.ForeColor = Clay.Ink;
                    lstMacros.Items.Add(lvi);
                }
                if (selName.Length > 0)
                {
                    for (int i = 0; i < lstMacros.Items.Count; i++)
                    {
                        if (lstMacros.Items[i].SubItems[1].Text == selName)
                        {
                            lstMacros.SelectedIndices.Clear();
                            lstMacros.SelectedIndices.Add(i);
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            UpdateMacroButtons();
        }

        private int SelectedMacroIndex()
        {
            if (lstMacros != null && lstMacros.SelectedIndices.Count > 0) return lstMacros.SelectedIndices[0];
            return -1;
        }

        private void UpdateMacroButtons()
        {
            bool has = SelectedMacroIndex() >= 0;
            btnMacroRename.Enabled = has;
            btnMacroCopy.Enabled = has;
            btnMacroDelete.Enabled = has;
        }

        /// <summary>快速触发: 加载宏文件并立即开始回放(使用当前回放设置)。</summary>
        private void PlayMacroFile(string file)
        {
            try
            {
                if (_recorder.Recording) _recorder.Stop();
                if (_player.Playing) _player.Stop();
                var ser = new JavaScriptSerializer();
                var list = ser.Deserialize<List<MacroEvent>>(File.ReadAllText(file, Encoding.UTF8));
                if (list == null || list.Count == 0)
                {
                    SetStatus(Lang.T("Macro is empty or corrupted"));
                    return;
                }
                _recorder.Load(list);
                RefreshEventList(false);
                UpdateRecordUi();
                TogglePlay();
                SetStatus(Lang.F("Playing macro: {0}", Path.GetFileNameWithoutExtension(file)));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Lang.F("Load failed: {0}", ex.Message), Lang.T("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PlaySelectedMacro()
        {
            int i = SelectedMacroIndex();
            if (i < 0 || i >= _macroFiles.Count)
            {
                SetStatus(Lang.T("Select a macro first"));
                return;
            }
            PlayMacroFile(_macroFiles[i]);
        }

        private void RenameMacro()
        {
            int i = SelectedMacroIndex();
            if (i < 0 || i >= _macroFiles.Count)
            {
                SetStatus(Lang.T("Select a macro first"));
                return;
            }
            string oldFile = _macroFiles[i];
            string oldName = Path.GetFileNameWithoutExtension(oldFile);
            using (var f = new NamePromptForm(Lang.T("Rename"), Lang.T("Macro name:"), oldName))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                string newName = SanitizeFileName(f.ValueText.Trim());
                if (newName.Length == 0 || newName == oldName) return;
                string newFile = Path.Combine(Path.GetDirectoryName(oldFile), newName + ".json");
                if (File.Exists(newFile))
                {
                    SetStatus(Lang.F("Macro \"{0}\" already exists", newName));
                    return;
                }
                try
                {
                    File.Move(oldFile, newFile);
                    RefreshMacroList();
                    SetStatus(Lang.F("Macro renamed to {0}", newName));
                }
                catch (Exception ex)
                {
                    SetStatus(Lang.F("Rename failed: {0}", ex.Message));
                }
            }
        }

        private void CopyMacro()
        {
            int i = SelectedMacroIndex();
            if (i < 0 || i >= _macroFiles.Count)
            {
                SetStatus(Lang.T("Select a macro first"));
                return;
            }
            string oldFile = _macroFiles[i];
            string dir = Path.GetDirectoryName(oldFile);
            string name = Path.GetFileNameWithoutExtension(oldFile);
            string newFile = Path.Combine(dir, name + " - copy.json");
            int n = 2;
            while (File.Exists(newFile)) newFile = Path.Combine(dir, name + " - copy " + n++ + ".json");
            try
            {
                File.Copy(oldFile, newFile);
                RefreshMacroList();
                SetStatus(Lang.F("Macro copy created: {0}", Path.GetFileNameWithoutExtension(newFile)));
            }
            catch (Exception ex)
            {
                SetStatus(Lang.F("Copy failed: {0}", ex.Message));
            }
        }

        private void DeleteMacro()
        {
            int i = SelectedMacroIndex();
            if (i < 0 || i >= _macroFiles.Count)
            {
                SetStatus(Lang.T("Select a macro first"));
                return;
            }
            string name = Path.GetFileNameWithoutExtension(_macroFiles[i]);
            if (MessageBox.Show(this, Lang.F("Delete macro \"{0}\"?", name), Lang.T("Delete macro"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                File.Delete(_macroFiles[i]);
                RefreshMacroList();
                SetStatus(Lang.F("Macro deleted: {0}", name));
            }
            catch (Exception ex)
            {
                SetStatus(Lang.F("Delete failed: {0}", ex.Message));
            }
        }

        private static string SanitizeFileName(string name)
        {
            char[] bad = Path.GetInvalidFileNameChars();
            foreach (char c in bad) name = name.Replace(c.ToString(), "_");
            name = name.TrimEnd(' ', '.'); // 尾部空格/点会让文件系统报错或产生隐患
            // Windows 保留设备名(CON/PRN/AUX/NUL/COM1-9/LPT1-9)不能作文件名
            string upper = name.ToUpperInvariant();
            string[] reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            foreach (string r in reserved) { if (upper == r) name = "_" + name; }
            return name.Trim();
        }

        private void OpenMacrosFolder()
        {
            try
            {
                AppConfig.EnsureDataDirs();
                Process.Start("explorer.exe", AppConfig.MacrosDir);
            }
            catch (Exception)
            {
            }
        }

        private void RefreshProgramList()
        {
            if (lstPrograms == null) return;
            lstPrograms.Items.Clear();
            if (_cfg == null || _cfg.LaunchPrograms == null) return;
            foreach (string p in _cfg.LaunchPrograms) lstPrograms.Items.Add(p);
        }

        private void AddProgram()
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = "程序 (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|所有文件 (*.*)|*.*",
                Title = Lang.T("Select a program to launch")
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (!_cfg.LaunchPrograms.Contains(dlg.FileName))
                {
                    _cfg.LaunchPrograms.Add(dlg.FileName);
                    RefreshProgramList();
                    SaveSettings();
                    SetStatus(Lang.F("Program added: {0}", dlg.FileName));
                }
            }
        }

        private void RemoveProgram()
        {
            if (lstPrograms.SelectedIndex < 0 || lstPrograms.SelectedIndex >= _cfg.LaunchPrograms.Count)
            {
                SetStatus(Lang.T("Select a program first"));
                return;
            }
            _cfg.LaunchPrograms.RemoveAt(lstPrograms.SelectedIndex);
            RefreshProgramList();
            SaveSettings();
        }

        /// <summary>按配置启动程序列表(播放开始/结束时)。</summary>
        private void LaunchPrograms()
        {
            if (_cfg == null || _cfg.LaunchPrograms == null) return;
            foreach (string p in _cfg.LaunchPrograms)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                try
                {
                    Process.Start(p);
                    Log.Info("启动程序: " + p);
                }
                catch (Exception ex)
                {
                    Log.Error("启动程序失败: " + p + " - " + ex.Message);
                    SetStatus(Lang.F("Launch failed: {0}", ex.Message));
                }
            }
        }

        // ---------- 热键捕获 ----------

        private static string ActionName(HotkeyAction action)
        {
            switch (action)
            {
                case HotkeyAction.Clicker: return Lang.T("Clicker toggle");
                case HotkeyAction.Record: return Lang.T("Recording toggle");
                case HotkeyAction.Play: return Lang.T("Playback toggle");
                case HotkeyAction.Keyboard: return Lang.T("Keyboard toggle");
                default: return Lang.T("Stop all");
            }
        }

        private void CaptureHotkey(int actionIndex)
        {
            var action = (HotkeyAction)actionIndex;
            _hotkeys.Suppress = true; // 捕获期间暂停触发, 避免按旧热键误触发功能
            try
            {
                using (var f = new HotkeyCaptureForm())
                {
                    if (f.ShowDialog(this) != DialogResult.OK || f.Captured == null) return;
                    if (MessageBox.Show(this, Lang.F("Bind \"{0}\" to: {1} ?", ActionName(action), f.Captured),
                            Lang.T("Confirm hotkey"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return;
                    HotkeyAction conflict;
                    if (_hotkeys.FindConflict(f.Captured, action, out conflict))
                    {
                        SetStatus(Lang.F("Hotkey conflicts with \"{0}\", not changed", ActionName(conflict)));
                        return;
                    }
                    _hotkeys.SetBinding(action, f.Captured);
                    UpdateHotkeyUi();
                    UpdateAllUi();
                    SaveSettings();
                    SetStatus(Lang.F("Hotkey for {0} changed to {1} (saved)", ActionName(action), f.Captured));
                }
            }
            finally
            {
                _hotkeys.Suppress = false;
            }
        }

        private void ResetHotkeys()
        {
            if (MessageBox.Show(this, Lang.T("Restore default hotkeys (F6~F9/F12) ?"), Lang.T("Confirm"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            for (int i = 0; i < 5; i++)
                _hotkeys.SetBinding((HotkeyAction)i, HotkeyManager.Default((HotkeyAction)i));
            UpdateHotkeyUi();
            UpdateAllUi();
            SaveSettings();
            SetStatus(Lang.T("Restored default hotkeys and saved"));
        }

        private void GrabWindow()
        {
            btnGrabWindow.Enabled = false;
            btnGrabWindow.Text = Lang.T("Grabbing in 3s...");
            SetStatus(Lang.T("Switch to the target window within 3 seconds"));
            var timer = new System.Windows.Forms.Timer { Interval = 3000 };
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                if (IsDisposed || Disposing) return;
                IntPtr h = NativeMethods.GetForegroundWindow();
                if (h != IntPtr.Zero)
                {
                    var sb = new StringBuilder(512);
                    NativeMethods.GetWindowText(h, sb, sb.Capacity);
                    txtTargetWindow.Text = sb.ToString();
                    rbTargetNamed.Checked = true;
                    SetStatus(Lang.F("Grabbed window title: {0}", sb));
                }
                btnGrabWindow.Text = Lang.T("Grab window title");
                btnGrabWindow.Enabled = true;
            };
            timer.Start();
        }

        // ---------- 按键音效 ----------

        /// <summary>把配置中的音效绑定同步到监听引擎(路径 = Sounds 文件夹 + 文件名 + 各键/组合生效音量)。</summary>
        private void ApplySfxToEngine()
        {
            _sfx.Enabled = chkSfx.Checked;
            SfxPlayer.Volume = _cfg.SfxVolume * 10; // 0~100 → MCI 0~1000
            _sfx.Bindings.Clear();
            _sfx.Volumes.Clear();
            _sfx.Combos.Clear();
            foreach (var kv in _cfg.SfxBindings)
            {
                int vk;
                if (!int.TryParse(kv.Key, out vk)) continue;
                _sfx.Bindings[vk] = Path.Combine(AppConfig.SoundsDir, kv.Value);
                int perKey = _cfg.SfxBindingVolumes.ContainsKey(kv.Key) ? _cfg.SfxBindingVolumes[kv.Key] : _cfg.SfxVolume;
                _sfx.Volumes[vk] = perKey * 10; // 0~100 → 0~1000
            }
            foreach (var kv in _cfg.SfxComboBindings)
            {
                var hk = Hotkey.Parse(kv.Key);
                if (hk == null) continue;
                int vol = _cfg.SfxComboVolumes.ContainsKey(kv.Key) ? _cfg.SfxComboVolumes[kv.Key] : _cfg.SfxVolume;
                _sfx.Combos.Add(new SfxCombo { Combo = hk, Path = Path.Combine(AppConfig.SoundsDir, kv.Value), Volume = vol * 10 });
            }
        }

        /// <summary>返回选中的绑定条目(单键或组合键); 未选中返回 null。</summary>
        private SfxKey SelectedSfx()
        {
            if (lstSfx.SelectedIndices.Count == 0) return null;
            int i = lstSfx.SelectedIndices[0];
            if (i < 0 || i >= _sfxKeys.Count) return null;
            return _sfxKeys[i];
        }

        /// <summary>按当前选中项刷新单键/组合音量滑块(未选中则禁用; 未单独设音量时显示全局音量)。</summary>
        private void UpdateKeyVolumeSlider()
        {
            var sel = SelectedSfx();
            _sfxSliderSync = true;
            try
            {
                if (sel == null)
                {
                    sldKeyVolume.Enabled = false;
                    lblKeyVol.Text = "";
                    // 未选中任何按键绑定时, 不联动"选中按键音量"滑块(只让它保持不动)
                }
                else
                {
                    sldKeyVolume.Enabled = true;
                    sldKeyVolume.Value = sel.Volume;
                    lblKeyVol.Text = sel.Volume + "%";
                }
            }
            finally
            {
                _sfxSliderSync = false;
            }
        }

        private void RefreshSfxList()
        {
            lstSfx.Items.Clear();
            _sfxKeys.Clear();
            foreach (var kv in _cfg.SfxBindings)
            {
                string file = kv.Value;
                int vk;
                if (!int.TryParse(kv.Key, out vk)) continue;
                int vol = _cfg.SfxBindingVolumes.ContainsKey(kv.Key) ? _cfg.SfxBindingVolumes[kv.Key] : _cfg.SfxVolume;
                var sk = new SfxKey { IsCombo = false, Vk = vk, Display = Hotkey.GetName((uint)vk), File = file, Volume = vol };
                _sfxKeys.Add(sk);
                var lvi = new ListViewItem(new[] { sk.Display, file });
                lvi.ForeColor = Clay.Ink;
                lstSfx.Items.Add(lvi);
            }
            foreach (var kv in _cfg.SfxComboBindings)
            {
                string file = kv.Value;
                int vol = _cfg.SfxComboVolumes.ContainsKey(kv.Key) ? _cfg.SfxComboVolumes[kv.Key] : _cfg.SfxVolume;
                var sk = new SfxKey { IsCombo = true, Combo = kv.Key, Display = kv.Key, File = file, Volume = vol };
                _sfxKeys.Add(sk);
                var lvi = new ListViewItem(new[] { sk.Display, file });
                lvi.ForeColor = Clay.Ink;
                lstSfx.Items.Add(lvi);
            }
            lblSfxCount.Text = Lang.F("Bindings: {0}", lstSfx.Items.Count);
        }

        private void AddSfxBinding()
        {
            using (var f = new HotkeyCaptureForm(Lang.T("Press a key or combo to bind a sound...\r\nSingle key, or Ctrl/Alt/Shift/Win combos (e.g. Ctrl+C)\r\nRelease all keys to finish, Esc to cancel")))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Captured == null) return;
                var hk = f.Captured;
                bool isCombo = hk.Modifiers.Count > 0 || hk.Keys.Count > 1;
                string combo = null;
                int vk = 0;
                if (isCombo)
                {
                    combo = hk.ToString();
                }
                else
                {
                    vk = (int)hk.Keys[0];
                    if (vk >= 0x01 && vk <= 0x06) // 鼠标键由鼠标钩子捕获, 音效仅监听键盘
                    {
                        SetStatus(Lang.T("Only a single non-modifier key is supported for sound binding"));
                        return;
                    }
                }
                using (var dlg = new OpenFileDialog
                {
                    Title = Lang.T("Select sound file"),
                    InitialDirectory = AppConfig.SoundsDir,
                    Filter = "音效文件 (*.wav;*.mp3)|*.wav;*.mp3|All files (*.*)|*.*"
                })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    string fileName = Path.GetFileName(dlg.FileName);
                    string dest = Path.Combine(AppConfig.SoundsDir, fileName);
                    try
                    {
                        // 不在 Sounds 文件夹内的文件复制进来, 统一音效存储
                        if (!string.Equals(dlg.FileName, dest, StringComparison.OrdinalIgnoreCase) && !File.Exists(dest))
                            File.Copy(dlg.FileName, dest);
                        if (isCombo) _cfg.SfxComboBindings[combo] = fileName; // 同组合旧绑定被覆盖
                        else _cfg.SfxBindings[vk.ToString()] = fileName;      // 同键旧绑定被覆盖
                        ApplySfxToEngine();
                        SaveSettings();
                        RefreshSfxList();
                        SetStatus(Lang.F("Sound bound: {0} → {1}", isCombo ? combo : Hotkey.GetName((uint)vk), fileName));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, Lang.F("Save failed: {0}", ex.Message), Lang.T("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void DeleteSfxBinding()
        {
            var sel = SelectedSfx();
            if (sel == null)
            {
                SetStatus(Lang.T("Select a sound binding first"));
                return;
            }
            if (sel.IsCombo)
            {
                _cfg.SfxComboBindings.Remove(sel.Combo);
                if (_cfg.SfxComboVolumes.ContainsKey(sel.Combo)) _cfg.SfxComboVolumes.Remove(sel.Combo);
            }
            else
            {
                _cfg.SfxBindings.Remove(sel.Vk.ToString());
                if (_cfg.SfxBindingVolumes.ContainsKey(sel.Vk.ToString())) _cfg.SfxBindingVolumes.Remove(sel.Vk.ToString());
            }
            ApplySfxToEngine();
            SaveSettings();
            RefreshSfxList();
            SetStatus(Lang.T("Sound binding deleted"));
        }

        private void TestSfx()
        {
            var sel = SelectedSfx();
            if (sel == null)
            {
                SetStatus(Lang.T("Select a sound binding first"));
                return;
            }
            SfxPlayer.Play(Path.Combine(AppConfig.SoundsDir, sel.File), sel.Volume * 10);
        }

        private void OpenSfxFolder()
        {
            try
            {
                AppConfig.EnsureDataDirs();
                Process.Start("explorer.exe", "\"" + AppConfig.SoundsDir + "\"");
            }
            catch (Exception ex)
            {
                SetStatus(Lang.F("Save failed: {0}", ex.Message));
            }
        }

        // ---------- 窗口边框主题 ----------

        /// <summary>让窗口外边框/标题栏跟随主题: 深色主题启用沉浸式深色模式, Win11 同时设置边框与标题栏颜色。</summary>
        private void ApplyFrameTheme()
        {
            if (!IsHandleCreated) return;
            try
            {
                int dark = Theme.Current.Dark ? 1 : 0;
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));
                int border = ColorTranslator.ToWin32(Theme.Current.WindowBg);
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));
                int caption = ColorTranslator.ToWin32(Theme.Current.CardBg);
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            }
            catch (Exception)
            {
                // 旧系统不支持这些属性, 静默忽略
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyFrameTheme();
        }

        /// <summary>
        /// WM_DPICHANGED: 窗口跨 DPI 显示器移动或系统缩放变化时,
        /// 按新 DPI 重建全部界面, 保证布局尺寸始终与窗口物理尺寸一致(不会出现卡片溢出窗外)。
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x02E0) // WM_DPICHANGED: 按新 DPI 重建界面并应用系统建议的窗口矩形
            {
                try
                {
                    int newDpi = (int)((long)m.WParam >> 16) & 0xFFFF; // 高 16 位 = 新 Y DPI
                    if (newDpi < 96 || newDpi > 480) newDpi = 96;
                    Dpi.SetS(newDpi);
                    RebuildUiForDpi();
                    var rc = (NativeMethods.RECT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.RECT));
                    Bounds = new Rectangle(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
                    m.Result = IntPtr.Zero;
                    return;
                }
                catch (Exception)
                {
                    // 重建失败时走默认处理
                }
            }
            base.WndProc(ref m);
        }

        /// <summary>
        /// 轮询窗口所在显示器的真实 DPI(不是窗口 DPI 属性——窗口被跨屏拖动后属性可能滞后)。
        /// 与当前布局不一致时按新 DPI 重建界面, 并重建窗口句柄:
        /// 新句柄按当前显示器 DPI 建立, 消除 DWM 位图缩放导致的卡片溢出/模糊。
        /// </summary>
        private void SyncDpi()
        {
            try
            {
                uint monDpi = WindowMonitorDpi();
                if (monDpi < 96 || monDpi > 480) return;
                float s = monDpi / 96f;
                bool sMismatch = Math.Abs(s - Dpi.S) > 0.01f;
                uint winDpi = NativeMethods.GetDpiForWindow(Handle); // 窗口 DPI 属性(跨屏拖动后可能滞后于显示器)
                bool attrMismatch = winDpi != 0 && Math.Abs((int)winDpi - (int)monDpi) > 24;
                if (sMismatch)
                {
                    Dpi.SetS(monDpi);
                    RebuildUiForDpi();
                }
                if (sMismatch || attrMismatch)
                {
                    RecreateHandle();
                    ApplyFrameTheme();
                }
            }
            catch (Exception)
            {
                // 旧系统无 GetDpiForMonitor, 跳过
            }
        }

        /// <summary>窗口所在显示器的有效 DPI; 失败返回 0。</summary>
        private uint WindowMonitorDpi()
        {
            IntPtr hmon = NativeMethods.MonitorFromWindow(Handle, 2); // MONITOR_DEFAULTTONEAREST
            uint dx, dy;
            if (NativeMethods.GetDpiForMonitor(hmon, 0, out dx, out dy) == 0) return dx;
            return 0;
        }

        /// <summary>按当前 Dpi.S 重建整个界面(构造与启动时完全一致), 随后回填配置。</summary>
        private void RebuildUiForDpi()
        {
            int tab = _currentTab;
            // 注意: 不能用 SuspendLayout 包裹重建——挂起期间 Dock 链不会传播父容器尺寸,
            // 卡片会在父容器仍是默认 200px 宽时计算 Left|Right 锚点偏移, 重新撑爆窗口。
            Anim.Clear();          // 清掉仍在跑动画的旧控件委托(Controls.Clear 会销毁它们)
            DisposeFadeBitmaps();  // 释放过渡快照
            Controls.Clear();
            BuildUi();
            WireEvents();
            ApplyConfigToUi(_cfg);
            if (tab > 0 && tab < _tabBtns.Length) SelectTab(tab);
            Invalidate(true);
            SetStatus(Lang.T("Display DPI changed, interface relaid out"));
        }

        // ---------- 配置加载 / 保存 ----------

        private void ApplyHotkey(HotkeyAction action, string text)
        {
            var hk = Hotkey.Parse(text);
            if (hk == null)
            {
                hk = HotkeyManager.Default(action);
                _loadNotes.Add(Lang.F("{0} hotkey config invalid, using default", ActionName(action)));
            }
            _hotkeys.SetBinding(action, hk);
        }

        private void ApplyConfigToUi(AppConfig cfg)
        {
            _applying = true;
            try
            {
                numInterval.Value = Clamp(cfg.ClickIntervalMs, (int)numInterval.Minimum, (int)numInterval.Maximum);
                cboButton.SelectedIndex = Clamp(cfg.ClickButton, 0, cboButton.Items.Count - 1);
                rbFixed.Checked = cfg.ClickFixedPosition;
                rbFollow.Checked = !cfg.ClickFixedPosition;
                numX.Value = Clamp(cfg.ClickFixedX, 0, 20000);
                numY.Value = Clamp(cfg.ClickFixedY, 0, 20000);
                numRepeat.Value = Clamp(cfg.ClickRepeatCount, 0, 100000000);

                txtKey.Text = cfg.SpamKeyText ?? "";
                int keyIdx = _keyOptions.FindIndex(kv => kv.Value == cfg.SpamVk);
                cboKey.SelectedIndex = keyIdx >= 0 ? keyIdx : 0;
                rbHold.Checked = cfg.SpamHold;
                rbTap.Checked = !cfg.SpamHold;
                numKeyInterval.Value = Clamp(cfg.SpamIntervalMs, 1, 3600000);

                numSpeed.Value = (decimal)Math.Max((double)numSpeed.Minimum,
                    Math.Min((double)numSpeed.Maximum, cfg.PlaySpeed));
                chkLoop.Checked = cfg.PlayLoop;
                numPlayLoops.Value = Clamp(cfg.PlayLoops, 0, 999999);
                numPlayMinutes.Value = Clamp(cfg.PlayMinutes, 0, 99999);
                chkUntilTime.Checked = !string.IsNullOrEmpty(cfg.PlayUntilTime);
                txtUntilTime.Text = string.IsNullOrEmpty(cfg.PlayUntilTime) ? "23:59" : cfg.PlayUntilTime;
                txtUntilTime.Enabled = chkUntilTime.Checked;
                chkTopmost.Checked = cfg.Topmost;
                chkAutoStart.Checked = cfg.AutoStart;
                chkSilentStart.Checked = cfg.StartMinimized;

                ApplyHotkey(HotkeyAction.Clicker, cfg.ClickerHotkey);
                ApplyHotkey(HotkeyAction.Record, cfg.RecordHotkey);
                ApplyHotkey(HotkeyAction.Play, cfg.PlayHotkey);
                ApplyHotkey(HotkeyAction.Keyboard, cfg.KeyboardHotkey);
                ApplyHotkey(HotkeyAction.StopAll, cfg.StopAllHotkey);

                int method = 0;
                if (cfg.InjectionMethod == "SendMessage") method = 1;
                else if (cfg.InjectionMethod == "InterceptionDriver") method = 2;
                if (method == 2 && !InterceptionDriver.IsDllPresent())
                {
                    method = 0;
                    _loadNotes.Add(Lang.T("Driver mode unavailable (interception.dll not found), fell back to SendInput"));
                }
                cboMethod.SelectedIndex = method;
                rbTargetNamed.Checked = cfg.TargetNamed;
                rbTargetForeground.Checked = !cfg.TargetNamed;
                txtTargetWindow.Text = cfg.TargetWindowTitle ?? "";
                chkScanCode.Checked = cfg.KeyboardScanCode;

                chkHumanizeEnabled.Checked = cfg.HumanizeEnabled;
                chkHumanizeTiming.Checked = cfg.HumanizeTiming;
                numTimingPct.Value = Clamp(cfg.HumanizeTimingPct, 0, 90);
                chkHumanizePos.Checked = cfg.HumanizePosition;
                numPosPx.Value = Clamp(cfg.HumanizePositionPx, 0, 20);
                chkHumanizePress.Checked = cfg.HumanizePressDuration;
                chkHumanizeTraj.Checked = cfg.HumanizeTrajectory;
                UpdateHumanizeChildState();

                // 按键音效
                chkSfx.Checked = cfg.SfxEnabled;
                sldGlobalVolume.Value = Clamp(cfg.SfxVolume, 0, 100);
                lblGlobalVol.Text = _cfg.SfxVolume + "%";
                ApplySfxToEngine();
                RefreshSfxList();
                UpdateKeyVolumeSlider();

                // 语言/主题在 ctor 中已应用; 这里同步下拉框显示
                cboLanguage.SelectedIndex = Lang.Code == "zh" ? 0 : 1;
                for (int i = 0; i < Theme.All.Length; i++)
                {
                    if (Theme.All[i] == Theme.Current) cboTheme.SelectedIndex = i;
                }
                Anim.Enabled = cfg.AnimationsEnabled;
                chkAnimations.Checked = cfg.AnimationsEnabled;

                // 宏库页: 软件控制
                RefreshProgramList();
                chkLaunchStart.Checked = cfg.LaunchOnStart;
                chkLaunchEnd.Checked = cfg.LaunchOnEnd;

                PushToEngines();
                UpdateHotkeyUi();
                UpdateAllUi();
                RefreshEventList(false);
                RefreshMacroList();
            }
            finally
            {
                _applying = false;
            }
        }

        /// <summary>把界面上的注入/拟人化设置同步到引擎。</summary>
        private void PushToEngines()
        {
            InputSimulator.Method = (InjectionMethod)cboMethod.SelectedIndex;
            InputSimulator.TargetWindowTitle = rbTargetNamed.Checked ? txtTargetWindow.Text.Trim() : "";
            InputSimulator.KeyboardScanCode = chkScanCode.Checked;
            Humanizer.Enabled = chkHumanizeEnabled.Checked;
            Humanizer.TimingEnabled = chkHumanizeTiming.Checked;
            Humanizer.TimingJitterPct = (int)numTimingPct.Value;
            Humanizer.PositionEnabled = chkHumanizePos.Checked;
            Humanizer.PositionJitterPx = (int)numPosPx.Value;
            Humanizer.PressDurationEnabled = chkHumanizePress.Checked;
            Humanizer.TrajectoryEnabled = chkHumanizeTraj.Checked;
        }

        /// <summary>拟人化总开关控制各子项可用状态。</summary>
        private void UpdateHumanizeChildState()
        {
            bool on = chkHumanizeEnabled.Checked;
            chkHumanizeTiming.Enabled = on;
            numTimingPct.Enabled = on;
            chkHumanizePos.Enabled = on;
            numPosPx.Enabled = on;
            chkHumanizePress.Enabled = on;
            chkHumanizeTraj.Enabled = on;
        }

        private void SaveSettings()
        {
            var hk = _hotkeys.GetBinding(HotkeyAction.Clicker);
            if (hk != null) _cfg.ClickerHotkey = hk.ToString();
            hk = _hotkeys.GetBinding(HotkeyAction.Record);
            if (hk != null) _cfg.RecordHotkey = hk.ToString();
            hk = _hotkeys.GetBinding(HotkeyAction.Play);
            if (hk != null) _cfg.PlayHotkey = hk.ToString();
            hk = _hotkeys.GetBinding(HotkeyAction.Keyboard);
            if (hk != null) _cfg.KeyboardHotkey = hk.ToString();
            hk = _hotkeys.GetBinding(HotkeyAction.StopAll);
            if (hk != null) _cfg.StopAllHotkey = hk.ToString();

            _cfg.ClickIntervalMs = (int)numInterval.Value;
            _cfg.ClickButton = cboButton.SelectedIndex;
            _cfg.ClickFixedPosition = rbFixed.Checked;
            _cfg.ClickFixedX = (int)numX.Value;
            _cfg.ClickFixedY = (int)numY.Value;
            _cfg.ClickRepeatCount = (int)numRepeat.Value;

            string keyText = txtKey.Text == null ? "" : txtKey.Text.Trim();
            if (keyText.Length > 0)
            {
                int vk = ResolveKeyVk();
                if (vk > 0) _cfg.SpamVk = vk;
                _cfg.SpamKeyText = keyText;
            }
            else
            {
                _cfg.SpamVk = _keyOptions[cboKey.SelectedIndex].Value;
                _cfg.SpamKeyText = "";
            }
            _cfg.SpamIntervalMs = (int)numKeyInterval.Value;
            _cfg.SpamHold = rbHold.Checked;

            _cfg.PlaySpeed = (double)numSpeed.Value;
            _cfg.PlayLoop = chkLoop.Checked;
            _cfg.PlayLoops = (int)numPlayLoops.Value;
            _cfg.PlayMinutes = (int)numPlayMinutes.Value;
            _cfg.PlayUntilTime = chkUntilTime.Checked ? txtUntilTime.Text.Trim() : "";
            _cfg.LaunchOnStart = chkLaunchStart.Checked;
            _cfg.LaunchOnEnd = chkLaunchEnd.Checked;
            _cfg.Topmost = chkTopmost.Checked;
            _cfg.AutoStart = chkAutoStart.Checked;
            _cfg.StartMinimized = chkSilentStart.Checked;

            _cfg.InjectionMethod = cboMethod.SelectedIndex == 0 ? "SendInput"
                : (cboMethod.SelectedIndex == 1 ? "SendMessage" : "InterceptionDriver");
            _cfg.TargetNamed = rbTargetNamed.Checked;
            _cfg.TargetWindowTitle = txtTargetWindow.Text;
            _cfg.KeyboardScanCode = chkScanCode.Checked;
            _cfg.HumanizeEnabled = chkHumanizeEnabled.Checked;
            _cfg.HumanizeTiming = chkHumanizeTiming.Checked;
            _cfg.HumanizeTimingPct = (int)numTimingPct.Value;
            _cfg.HumanizePosition = chkHumanizePos.Checked;
            _cfg.HumanizePositionPx = (int)numPosPx.Value;
            _cfg.HumanizePressDuration = chkHumanizePress.Checked;
            _cfg.HumanizeTrajectory = chkHumanizeTraj.Checked;

            _cfg.SfxEnabled = chkSfx.Checked; // SfxBindings/SfxBindingVolumes 在增删/滑块调整时已直接写入 _cfg
            _cfg.SfxVolume = (int)sldGlobalVolume.Value;

            _cfg.Language = Lang.Code;
            _cfg.ThemeName = Theme.Current.Id;
            _cfg.AnimationsEnabled = chkAnimations.Checked;

            _cfg.Save();
        }

        // ---------- 状态刷新 ----------

        private static int Clamp(int v, int min, int max)
        {
            return Math.Max(min, Math.Min(max, v));
        }

        private void Ui(Action action)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }

        private void SetStatus(string text)
        {
            if (lblStatus != null)
            {
                lblStatus.Text = text;
                _hintTip.SetToolTip(lblStatus, text); // 过长时省略号显示, 悬停看全文
            }
        }

        private void UpdateHotkeyUi()
        {
            string text = Lang.T("Click") + " " + _hotkeys.Describe(HotkeyAction.Clicker)
                + " | " + Lang.T("Record") + " " + _hotkeys.Describe(HotkeyAction.Record)
                + " | " + Lang.T("Play") + " " + _hotkeys.Describe(HotkeyAction.Play)
                + " | " + Lang.T("Keyboard") + " " + _hotkeys.Describe(HotkeyAction.Keyboard)
                + " | " + Lang.T("Stop") + " " + _hotkeys.Describe(HotkeyAction.StopAll);
            lblHotkeyHint.Text = text;
            _hintTip.SetToolTip(lblHotkeyHint, text);
            for (int i = 0; i < _hkButtons.Length; i++)
                _hkButtons[i].Text = _hotkeys.Describe((HotkeyAction)i);
        }

        private void UpdateClickerUi()
        {
            string hk = _hotkeys.Describe(HotkeyAction.Clicker);
            btnClickerToggle.Text = _clicker.Running ? Lang.F("Stop clicking ({0})", hk) : Lang.F("Start clicking ({0})", hk);
            var cb = (ClayButton)btnClickerToggle;
            cb.Accent = !_clicker.Running;
            cb.Danger = _clicker.Running;
            cb.Invalidate();
            lblClickerState.Text = _clicker.Running ? Lang.T("Status: Running") : Lang.T("Status: Idle");
            lblClickerState.ForeColor = _clicker.Running ? Clay.Run : Clay.InkSoft;
        }

        private void UpdateKeyboardUi()
        {
            string hk = _hotkeys.Describe(HotkeyAction.Keyboard);
            btnKeyboardToggle.Text = _spammer.Running ? Lang.F("Stop spam ({0})", hk) : Lang.F("Start spam ({0})", hk);
            var cb = (ClayButton)btnKeyboardToggle;
            cb.Accent = !_spammer.Running;
            cb.Danger = _spammer.Running;
            cb.Invalidate();
            lblKeyboardState.Text = _spammer.Running ? Lang.T("Status: Running") : Lang.T("Status: Idle");
            lblKeyboardState.ForeColor = _spammer.Running ? Clay.Run : Clay.InkSoft;
        }

        private void UpdateRecordUi()
        {
            btnRecord.Text = _recorder.Recording ? Lang.T("Stop recording") : Lang.T("Start recording");
            var rb = (ClayButton)btnRecord;
            rb.Accent = !_recorder.Recording;
            rb.Danger = _recorder.Recording;
            rb.Invalidate();
            UpdateEventEditButtons();
        }

        /// <summary>事件编辑按钮可用状态: 录制/回放中禁用, 编辑/删除需选中事件。</summary>
        private void UpdateEventEditButtons()
        {
            bool busy = _recorder.Recording || _player.Playing;
            bool has = SelectedEventIndex() >= 0;
            btnEditDelay.Enabled = !busy && has;
            btnAddEvent.Enabled = !busy;
            btnDeleteEvent.Enabled = !busy && has;
        }

        private void UpdatePlayUi()
        {
            btnPlay.Text = _player.Playing ? Lang.T("Stop playback") : Lang.T("Start playback");
            var pb = (ClayButton)btnPlay;
            pb.Accent = !_player.Playing;
            pb.Danger = _player.Playing;
            pb.Invalidate();
            if (lblLoopHint != null)
                lblLoopHint.Text = Lang.F("Loop playback hotkey: {0}", _hotkeys.Describe(HotkeyAction.Play));
        }

        private void UpdateAllUi()
        {
            UpdateClickerUi();
            UpdateKeyboardUi();
            UpdateRecordUi();
            UpdatePlayUi();
            UpdateHotkeyUi();
        }

        // ---------- 窗口生命周期 ----------

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Shutdown();
            base.OnFormClosing(e);
        }

        /// <summary>静默启动: 拦截首次显示, 让程序以托盘方式后台运行。</summary>
        protected override void SetVisibleCore(bool value)
        {
            if (value && _firstShow)
            {
                _firstShow = false;
                if (_cfg != null && _cfg.StartMinimized)
                {
                    base.SetVisibleCore(false);
                    return;
                }
            }
            base.SetVisibleCore(value);
        }

        /// <summary>创建系统托盘图标(常驻): 双击/菜单打开主界面, 右键菜单退出。</summary>
        private void BuildTray()
        {
            try { _trayAppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { _trayAppIcon = null; }

            var menu = new ContextMenuStrip();
            _trayOpen = new ToolStripMenuItem { Name = "Show main window", Text = Lang.T("Show main window") };
            _trayExit = new ToolStripMenuItem { Name = "Exit", Text = Lang.T("Exit") };
            _trayOpen.Click += delegate { ShowFromTray(); };
            _trayExit.Click += delegate { QuitFromTray(); };
            menu.Items.Add(_trayOpen);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_trayExit);

            _trayIcon = new NotifyIcon
            {
                Icon = _trayAppIcon ?? SystemIcons.Application,
                Text = Lang.F("Auto Clicker {0}", VersionInfo.Version),
                ContextMenuStrip = menu,
                Visible = true
            };
            _trayIcon.DoubleClick += delegate { ShowFromTray(); };
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void QuitFromTray()
        {
            Shutdown();
            Application.Exit();
        }

        /// <summary>统一收尾: 停引擎/松键、释放钩子与音效、保存设置。仅执行一次(窗口关闭与托盘退出共用)。</summary>
        private void Shutdown()
        {
            if (_shutdownDone) return;
            _shutdownDone = true;
            StopAll();
            _recorder.Dispose();
            _hotkeys.Dispose();
            _sfx.Dispose();
            if (_statusTimer != null) _statusTimer.Stop();
            Anim.Clear();
            DisposeFadeBitmaps();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            if (_trayAppIcon != null)
            {
                _trayAppIcon.Dispose();
                _trayAppIcon = null;
            }
            SaveSettings();
        }

        /// <summary>崩溃兜底: 仅停引擎(触发各引擎 finally 松键), 不做任何 UI 操作, 不吞异常。</summary>
        private void StopEnginesSilent()
        {
            try { _clicker.Stop(); } catch (Exception) { }
            try { _spammer.Stop(); } catch (Exception) { }
            try { _player.Stop(); } catch (Exception) { }
            try { if (_recorder.Recording) _recorder.Stop(); } catch (Exception) { }
        }
    }
}
