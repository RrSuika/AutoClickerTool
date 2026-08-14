using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClickerTool
{
    /// <summary>
    /// 首次启动欢迎对话框: 选择默认语言 + 功能介绍 + 已阅关闭。
    /// </summary>
    internal class WelcomeForm : Form
    {
        public string SelectedLanguage = "zh";

        private Label _title;
        private Label _langLbl;
        private Label _intro;
        private ClayButton _btnClose;

        public WelcomeForm()
        {
            Text = "Auto Clicker";
            ClientSize = new Size(Dpi.X(460), Dpi.X(360));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Clay.WindowBg;
            HandleCreated += delegate { Clay.ApplyFrameTheme(Handle); };

            _title = new Label
            {
                Location = new Point(Dpi.X(22), Dpi.X(18)),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                ForeColor = Clay.Ink,
                BackColor = Clay.WindowBg
            };
            _langLbl = new Label
            {
                Location = new Point(Dpi.X(22), Dpi.X(58)),
                AutoSize = true,
                ForeColor = Clay.InkSoft,
                BackColor = Clay.WindowBg
            };

            var rbZh = new ClayRadio { Text = "中文", Location = new Point(Dpi.X(22), Dpi.X(82)), Checked = (SelectedLanguage == "zh"), BackColor = Clay.WindowBg };
            var rbEn = new ClayRadio { Text = "English", Location = new Point(Dpi.X(130), Dpi.X(82)), Checked = (SelectedLanguage == "en"), BackColor = Clay.WindowBg };
            rbZh.CheckedChanged += delegate { if (rbZh.Checked) { SelectedLanguage = "zh"; ApplyTexts(); } };
            rbEn.CheckedChanged += delegate { if (rbEn.Checked) { SelectedLanguage = "en"; ApplyTexts(); } };

            _intro = new Label
            {
                Location = new Point(Dpi.X(22), Dpi.X(114)),
                Size = new Size(Dpi.X(416), Dpi.X(200)),
                ForeColor = Clay.Ink,
                BackColor = Clay.WindowBg
            };

            _btnClose = new ClayButton
            {
                Location = new Point(Dpi.X(322), Dpi.X(320)),
                Size = new Size(Dpi.X(116), Dpi.X(30)),
                Accent = true,
                BackColor = Clay.WindowBg
            };
            _btnClose.Click += delegate { DialogResult = DialogResult.OK; Close(); };

            Controls.AddRange(new Control[] { _title, _langLbl, rbZh, rbEn, _intro, _btnClose });

            ApplyTexts();
        }

        private void ApplyTexts()
        {
            bool zh = SelectedLanguage == "zh";
            _title.Text = zh ? "欢迎使用自动点击器" : "Welcome to Auto Clicker";
            _langLbl.Text = zh ? "选择界面语言:" : "Choose interface language:";
            _btnClose.Text = zh ? "已阅关闭" : "I've read it, close";
            _intro.Text = zh
                ? "功能简介:\r\n\r\n1. 鼠标连点 — 固定间隔自动点击\r\n2. 键盘连按 — 定时点按或按住某个键\r\n3. 录制回放 — 录制真实操作并可循环回放\r\n4. 全局热键 — 5 个功能开关, 支持任意组合键\r\n5. 按键音效 — 任意按键绑定音效, 按下即播放\r\n6. 拟人化 — 随机间隔/轨迹, 降低被游戏检测的概率\r\n7. 6 套主题 + 中英双语, 窗口边框跟随主题\r\n\r\n提示: 建议先到「高级设置」页确认注入方式。"
                : "Features:\r\n\r\n1. Mouse Clicking - auto click at fixed interval\r\n2. Keyboard Spam - tap or hold a key on a timer\r\n3. Record & Play - record real input and replay\r\n4. Global Hotkeys - 5 toggles with any key combo\r\n5. Key Sound FX - bind a sound to any key\r\n6. Humanization - random timing/trajectory, lower detection risk\r\n7. 6 themes + bilingual, themed window frame\r\n\r\nTip: check the injection method on the Advanced page first.";
        }
    }
}
