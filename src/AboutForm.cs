using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClickerTool
{
    /// <summary>
    /// 「关于」对话框: 版本 / 作者 / 网址 / 开源申明 / 免责声明。
    /// 需要替换作者与网址时, 修改下方 AUTHOR / WEBSITE 常量即可(双语版也在 ApplyTexts 里对应修改)。
    /// </summary>
    internal class AboutForm : Form
    {
        // ---- 需要用户替换的占位信息 ----
        private const string AUTHOR = "R2S-ver";
        private const string AUTHOR_EN = "R2S-ver";
        private const string WEBSITE = "https://github.com/R2S-ver/AutoClickerTool";
        // --------------------------------

        private Label _lblTitle;
        private Label _lblBody;
        private ClayButton _btnOk;

        public AboutForm()
        {
            Text = "About";
            ClientSize = new Size(Dpi.X(470), Dpi.X(420));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Clay.WindowBg;

            var card = new ClayPanel
            {
                Location = new Point(Dpi.X(10), Dpi.X(10)),
                Size = new Size(Dpi.X(450), Dpi.X(400)),
                BackColor = Clay.CardBg
            };
            HandleCreated += delegate { Clay.ApplyFrameTheme(Handle); };

            _lblTitle = new Label
            {
                Location = new Point(Dpi.X(20), Dpi.X(18)),
                Size = new Size(Dpi.X(410), Dpi.X(26)),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                ForeColor = Clay.Ink,
                BackColor = Clay.CardBg
            };

            _lblBody = new Label
            {
                Location = new Point(Dpi.X(24), Dpi.X(58)),
                Size = new Size(Dpi.X(402), Dpi.X(290)),
                ForeColor = Clay.Ink,
                BackColor = Clay.CardBg
            };

            _btnOk = new ClayButton
            {
                Location = new Point(Dpi.X(320), Dpi.X(358)),
                Size = new Size(Dpi.X(110), Dpi.X(30)),
                Accent = true,
                BackColor = Clay.CardBg
            };
            _btnOk.Click += delegate { DialogResult = DialogResult.OK; Close(); };

            card.Controls.Add(_lblTitle);
            card.Controls.Add(_lblBody);
            card.Controls.Add(_btnOk);
            Controls.Add(card);

            ApplyTexts();
        }

        private void ApplyTexts()
        {
            bool zh = Lang.Code == "zh";
            Text = zh ? "关于 自动点击器" : "About AutoClicker";
            _lblTitle.Text = zh ? "自动点击器 AutoClicker  " + VersionInfo.Version : "AutoClicker  " + VersionInfo.Version;
            _btnOk.Text = Lang.T("OK");

            _lblBody.Text = zh
                ? "Windows 鼠标键盘自动化工具\r\n连点 · 连按 · 录制回放 · 全局热键 · 按键音效 · 拟人化\r\n\r\n作者: " + AUTHOR + "\r\n项目主页: " + WEBSITE + "\r\n\r\n开源协议: MIT License\r\n本软件以 MIT 许可证开源, 允许自由使用、复制、修改、合并、\r\n发布、分发, 但不提供任何明示或暗示的担保。\r\n\r\n免责声明:\r\n本软件仅供学习与技术交流, 请勿用于违反游戏服务条款或\r\n法律法规的用途, 使用者需自行承担相应责任。\r\n\r\n技术栈: .NET Framework 4.0 · WinForms · 零第三方依赖"
                : "Mouse & keyboard automation tool for Windows\r\nAuto-click · Key spam · Record & replay · Global hotkeys · Sound FX · Humanization\r\n\r\nAuthor: " + AUTHOR_EN + "\r\nHomepage: " + WEBSITE + "\r\n\r\nLicense: MIT License\r\nThis software is released under the MIT License: you are free to\r\nuse, copy, modify, merge, publish and distribute it, with no warranty.\r\n\r\nDisclaimer:\r\nFor learning and technical exchange only. Do not use it to violate\r\ngame terms of service or laws; the user bears all responsibility.\r\n\r\nTech: .NET Framework 4.0 · WinForms · zero third-party dependencies";
        }
    }
}
