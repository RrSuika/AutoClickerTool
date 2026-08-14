using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClickerTool
{
    /// <summary>修改事件延迟的小对话框。</summary>
    internal class DelayEditForm : Form
    {
        private readonly NumericUpDown _num;

        public int NewDelayMs { get { return (int)_num.Value; } }

        public DelayEditForm(int currentMs)
        {
            Text = Lang.T("Edit event delay");
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Clay.WindowBg;
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(Dpi.X(260), Dpi.X(96));

            var card = new ClayPanel { Location = new Point(Dpi.X(10), Dpi.X(10)), Size = new Size(Dpi.X(240), Dpi.X(76)), BackColor = Clay.CardBg };
            card.Controls.Add(new Label
            {
                Text = Lang.T("Delay (ms):"),
                Location = new Point(Dpi.X(16), Dpi.X(18)),
                AutoSize = true,
                ForeColor = Clay.InkSoft,
                BackColor = Clay.CardBg
            });
            _num = new ClayNumericUpDown
            {
                Minimum = 0,
                Maximum = 3600000,
                Value = Math.Max(0, Math.Min(3600000, currentMs)),
                BorderStyle = BorderStyle.None
            };
            card.Controls.Add(ClayKit.InputShell(_num, 100, 13, 120));
            var ok = new ClayButton
            {
                Text = Lang.T("OK"),
                Location = new Point(Dpi.X(90), Dpi.X(52)),
                Size = new Size(Dpi.X(70), Dpi.X(24)),
                Accent = true,
                BackColor = Clay.CardBg
            };
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            card.Controls.Add(ok);
            var cancel = new ClayButton
            {
                Text = Lang.T("Cancel"),
                Location = new Point(Dpi.X(168), Dpi.X(52)),
                Size = new Size(Dpi.X(70), Dpi.X(24)),
                BackColor = Clay.CardBg
            };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            card.Controls.Add(cancel);
            Controls.Add(card);
        }
    }

    /// <summary>添加宏事件对话框: 选择类型 + 参数 + 延迟。</summary>
    internal class EventAddForm : Form
    {
        private readonly ComboBox _cboType;
        private readonly ComboBox _cboSpecial;
        private readonly NumericUpDown _numX;
        private readonly NumericUpDown _numY;
        private readonly NumericUpDown _numWheel;
        private readonly NumericUpDown _numDelay;
        private readonly Label _lblX;
        private readonly Label _lblY;
        private readonly Label _lblWheel;
        private readonly Label _lblCur;
        private readonly ClayPanel _shellX;
        private readonly ClayPanel _shellY;
        private readonly ClayPanel _shellWheel;
        private readonly ClayButton _btnCapture;
        private readonly ClayPanel _shellSpecial;
        private Hotkey _captured;    // 捕获到的按键/组合键(null = 未捕获)
        private int _specialVk;      // 特殊按键下拉选中的键码(0 = 未选)
        private readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>> _specialKeys;

        public MacroEvent Result { get; private set; }

        public EventAddForm(System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>> keyOptions)
        {
            // 特殊按键列表: 去掉 A-Z / 0-9(这些用"捕获按键"直接按下录入), 保留音量/删除/小键盘/方向键等
            _specialKeys = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>();
            foreach (var kv in keyOptions)
            {
                if (kv.Value >= 0x41 && kv.Value <= 0x5A) continue; // A-Z
                if (kv.Value >= 0x30 && kv.Value <= 0x39) continue; // 0-9
                _specialKeys.Add(kv);
            }

            Text = Lang.T("Add macro event");
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Clay.WindowBg;
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(Dpi.X(340), Dpi.X(210));

            var card = new ClayPanel { Location = new Point(Dpi.X(10), Dpi.X(10)), Size = new Size(Dpi.X(320), Dpi.X(190)), BackColor = Clay.CardBg };

            // 行 1: 类型
            card.Controls.Add(new Label { Text = Lang.T("Event"), Location = new Point(Dpi.X(16), Dpi.X(19)), AutoSize = true, ForeColor = Clay.InkSoft, BackColor = Clay.CardBg });
            _cboType = new ClayComboBox();
            _cboType.Items.Add(Lang.T("Mouse move"));
            _cboType.Items.Add(Lang.T("Mouse left down"));
            _cboType.Items.Add(Lang.T("Mouse left up"));
            _cboType.Items.Add(Lang.T("Mouse right down"));
            _cboType.Items.Add(Lang.T("Mouse right up"));
            _cboType.Items.Add(Lang.T("Mouse middle down"));
            _cboType.Items.Add(Lang.T("Mouse middle up"));
            _cboType.Items.Add(Lang.T("Mouse wheel"));
            _cboType.Items.Add(Lang.T("Mouse left click"));
            _cboType.Items.Add(Lang.T("Mouse right click"));
            _cboType.Items.Add(Lang.T("Mouse middle click"));
            _cboType.Items.Add(Lang.T("Keyboard down"));
            _cboType.Items.Add(Lang.T("Keyboard up"));
            _cboType.Items.Add(Lang.T("Keyboard tap"));
            _cboType.Items.Add(Lang.T("Delay only"));
            _cboType.SelectedIndex = 0;
            _cboType.SelectedIndexChanged += delegate { UpdateParams(); };
            card.Controls.Add(ClayKit.InputShell(_cboType, 90, 14, 216));

            // 行 2: 参数(按类型显隐)
            _lblX = new Label { Text = Lang.T("X:"), Location = new Point(Dpi.X(16), Dpi.X(49)), AutoSize = true, ForeColor = Clay.InkSoft, BackColor = Clay.CardBg };
            _numX = new ClayNumericUpDown { Minimum = 0, Maximum = 20000, Value = 0, BorderStyle = BorderStyle.None };
            _shellX = ClayKit.InputShell(_numX, 38, 45, 105);
            _lblY = new Label { Text = Lang.T("Y:"), Location = new Point(Dpi.X(152), Dpi.X(49)), AutoSize = true, ForeColor = Clay.InkSoft, BackColor = Clay.CardBg };
            _numY = new ClayNumericUpDown { Minimum = 0, Maximum = 20000, Value = 0, BorderStyle = BorderStyle.None };
            _shellY = ClayKit.InputShell(_numY, 172, 45, 105);
            _lblWheel = new Label { Text = Lang.T("Wheel delta:"), Location = new Point(Dpi.X(16), Dpi.X(49)), AutoSize = true, ForeColor = Clay.InkSoft, BackColor = Clay.CardBg };
            _numWheel = new ClayNumericUpDown { Minimum = -1200, Maximum = 1200, Increment = 120, Value = 120, BorderStyle = BorderStyle.None };
            _shellWheel = ClayKit.InputShell(_numWheel, 90, 45, 120);

            _btnCapture = new ClayButton { Name = "Capture key", Text = Lang.T("Capture key"), Location = new Point(Dpi.X(16), Dpi.X(45)), Size = new Size(Dpi.X(100), Dpi.X(26)), BackColor = Clay.CardBg };
            _btnCapture.Click += delegate { CaptureKey(); };
            _cboSpecial = new ClayComboBox();
            foreach (var kv in _specialKeys) _cboSpecial.Items.Add(kv.Key);
            _cboSpecial.SelectedIndex = -1;
            _cboSpecial.SelectedIndexChanged += delegate
            {
                if (_cboSpecial.SelectedIndex >= 0)
                {
                    _specialVk = _specialKeys[_cboSpecial.SelectedIndex].Value;
                    _captured = null;
                    UpdateCurLabel();
                }
            };
            _shellSpecial = ClayKit.InputShell(_cboSpecial, 130, 45, 165);

            _lblCur = new Label { Text = "", Location = new Point(Dpi.X(16), Dpi.X(76)), Size = new Size(Dpi.X(288), Dpi.X(18)), AutoEllipsis = true, ForeColor = Clay.Ink, BackColor = Clay.CardBg };

            card.Controls.Add(_lblX);
            card.Controls.Add(_shellX);
            card.Controls.Add(_lblY);
            card.Controls.Add(_shellY);
            card.Controls.Add(_lblWheel);
            card.Controls.Add(_shellWheel);
            card.Controls.Add(_btnCapture);
            card.Controls.Add(_shellSpecial);
            card.Controls.Add(_lblCur);

            // 行 4: 延迟
            card.Controls.Add(new Label { Text = Lang.T("Delay (ms):"), Location = new Point(Dpi.X(16), Dpi.X(103)), AutoSize = true, ForeColor = Clay.InkSoft, BackColor = Clay.CardBg });
            _numDelay = new ClayNumericUpDown { Minimum = 0, Maximum = 3600000, Value = 100, BorderStyle = BorderStyle.None };
            card.Controls.Add(ClayKit.InputShell(_numDelay, 100, 99, 100));
            card.Controls.Add(new Label { Text = Lang.T("Insert after selection"), Location = new Point(Dpi.X(210), Dpi.X(103)), AutoSize = true, ForeColor = Clay.InkSoft, BackColor = Clay.CardBg });

            // 行 5: 按钮
            var ok = new ClayButton { Text = Lang.T("OK"), Location = new Point(Dpi.X(105), Dpi.X(132)), Size = new Size(Dpi.X(95), Dpi.X(26)), Accent = true, BackColor = Clay.CardBg };
            ok.Click += delegate { if (BuildResult()) { DialogResult = DialogResult.OK; Close(); } };
            card.Controls.Add(ok);
            var cancel = new ClayButton { Text = Lang.T("Cancel"), Location = new Point(Dpi.X(208), Dpi.X(132)), Size = new Size(Dpi.X(95), Dpi.X(26)), BackColor = Clay.CardBg };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            card.Controls.Add(cancel);

            Controls.Add(card);
            UpdateParams();
        }

        private void CaptureKey()
        {
            using (var f = new HotkeyCaptureForm(Lang.T("Press a key or combo...\r\nSingle key, or Ctrl/Alt/Shift/Win combos\r\nRelease all keys to finish, Esc to cancel")))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.Captured == null) return;
                _captured = f.Captured;
                _specialVk = 0;
                _cboSpecial.SelectedIndex = -1;
                UpdateCurLabel();
            }
        }

        private void UpdateCurLabel()
        {
            if (_captured != null)
            {
                _lblCur.Text = Lang.F("Current key: {0}", _captured.ToString());
            }
            else if (_specialVk != 0)
            {
                _lblCur.Text = Lang.F("Current key: {0}", Hotkey.GetName((uint)_specialVk));
            }
            else
            {
                _lblCur.Text = "";
            }
        }

        private void UpdateParams()
        {
            int t = _cboType.SelectedIndex;
            bool move = t == 0;
            bool wheel = t == 7;
            bool key = t == 11 || t == 12 || t == 13;
            _lblX.Visible = move;
            _shellX.Visible = move;
            _lblY.Visible = move;
            _shellY.Visible = move;
            _lblWheel.Visible = wheel;
            _shellWheel.Visible = wheel;
            _btnCapture.Visible = key;
            _shellSpecial.Visible = key;
            _lblCur.Visible = key;
        }

        private bool GetCurrentKey(out bool isCombo, out int vk, out string combo)
        {
            isCombo = false; vk = 0; combo = null;
            if (_captured != null)
            {
                if (_captured.Modifiers.Count == 0 && _captured.Keys.Count == 1)
                {
                    vk = (int)_captured.Keys[0];
                }
                else
                {
                    isCombo = true;
                    combo = _captured.ToString();
                }
                return true;
            }
            if (_specialVk != 0)
            {
                vk = _specialVk;
                return true;
            }
            return false;
        }

        private bool BuildResult()
        {
            var e = new MacroEvent { DelayMs = (int)_numDelay.Value };
            int t = _cboType.SelectedIndex;
            switch (t)
            {
                case 0: e.Kind = MacroEventKind.Move; e.X = (int)_numX.Value; e.Y = (int)_numY.Value; break;
                case 1: e.Kind = MacroEventKind.LeftDown; break;
                case 2: e.Kind = MacroEventKind.LeftUp; break;
                case 3: e.Kind = MacroEventKind.RightDown; break;
                case 4: e.Kind = MacroEventKind.RightUp; break;
                case 5: e.Kind = MacroEventKind.MiddleDown; break;
                case 6: e.Kind = MacroEventKind.MiddleUp; break;
                case 7: e.Kind = MacroEventKind.Wheel; e.Data = (int)_numWheel.Value; break;
                case 8: e.Kind = MacroEventKind.LeftClick; break;
                case 9: e.Kind = MacroEventKind.RightClick; break;
                case 10: e.Kind = MacroEventKind.MiddleClick; break;
                case 11:
                case 12:
                case 13:
                {
                    bool isCombo; int vk; string comboStr;
                    if (!GetCurrentKey(out isCombo, out vk, out comboStr))
                    {
                        MessageBox.Show(this, Lang.T("Please select or capture a key first"), Lang.T("Add macro event"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                    if (isCombo)
                    {
                        e.Combo = comboStr;
                        e.Kind = t == 11 ? MacroEventKind.KeyComboDown : (t == 12 ? MacroEventKind.KeyComboUp : MacroEventKind.KeyComboTap);
                    }
                    else
                    {
                        e.Data = vk;
                        e.Kind = t == 11 ? MacroEventKind.KeyDown : (t == 12 ? MacroEventKind.KeyUp : MacroEventKind.KeyTap);
                    }
                    break;
                }
                case 14: e.Kind = MacroEventKind.Delay; break;
            }
            Result = e;
            return true;
        }
    }

    /// <summary>单行文本输入对话框(重命名宏等)。</summary>
    internal class NamePromptForm : Form
    {
        private readonly TextBox _txt;

        public string ValueText { get { return _txt.Text; } }

        public NamePromptForm(string title, string labelText, string initial)
        {
            Text = title;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Clay.WindowBg;
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(Dpi.X(320), Dpi.X(116));

            var card = new ClayPanel { Location = new Point(Dpi.X(10), Dpi.X(10)), Size = new Size(Dpi.X(300), Dpi.X(96)), BackColor = Clay.CardBg };
            card.Controls.Add(new Label
            {
                Text = labelText,
                Location = new Point(Dpi.X(16), Dpi.X(14)),
                AutoSize = true,
                ForeColor = Clay.InkSoft,
                BackColor = Clay.CardBg
            });
            _txt = new TextBox { Text = initial != null ? initial : "", Location = new Point(Dpi.X(16), Dpi.X(38)), Width = Dpi.X(268), MaxLength = 80 };
            card.Controls.Add(_txt);
            var ok = new ClayButton
            {
                Text = Lang.T("OK"),
                Location = new Point(Dpi.X(140), Dpi.X(66)),
                Size = new Size(Dpi.X(70), Dpi.X(24)),
                Accent = true,
                BackColor = Clay.CardBg
            };
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            card.Controls.Add(ok);
            var cancel = new ClayButton
            {
                Text = Lang.T("Cancel"),
                Location = new Point(Dpi.X(218), Dpi.X(66)),
                Size = new Size(Dpi.X(70), Dpi.X(24)),
                BackColor = Clay.CardBg
            };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            card.Controls.Add(cancel);
            Controls.Add(card);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
