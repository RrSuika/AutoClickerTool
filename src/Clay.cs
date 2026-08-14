using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoClickerTool
{
    /// <summary>
    /// DPI 缩放: 布局坐标按 96 DPI 设计, 运行时按实际 DPI 等比放大。
    /// 进程已声明 DPI 感知(SetProcessDPIAware), 文字本身按物理像素渲染,
    /// 因此只缩放容器与间距, 不缩放字体 —— 二者等比后布局精确匹配设计稿。
    /// </summary>
    internal static class Dpi
    {
        public static float S;

        static Dpi()
        {
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    S = g.DpiX / 96f;
                }
            }
            catch (Exception)
            {
                S = 1f;
            }
            if (S < 1f || S > 4f) S = 1f;
        }

        /// <summary>窗口跨 DPI 显示器移动时(WmDpiChanged)按新 DPI 重置缩放系数。</summary>
        public static void SetS(float dpi)
        {
            S = dpi / 96f;
            if (S < 1f || S > 4f) S = 1f;
        }

        public static int X(int v)
        {
            return (int)Math.Round(v * S);
        }
    }

    /// <summary>
    /// 自绘控件库(主题驱动)。所有颜色/圆角/阴影从 Theme.Current 读取,
    /// 切换主题后调用 Invalidate 即完成换肤。
    /// </summary>
    internal static class Clay
    {
        public static Color WindowBg { get { return Theme.Current.WindowBg; } }
        public static Color CardBg { get { return Theme.Current.CardBg; } }
        public static Color Ink { get { return Theme.Current.Ink; } }
        public static Color InkSoft { get { return Theme.Current.InkSoft; } }
        public static Color Line { get { return Theme.Current.Line; } }
        public static Color Run { get { return Theme.Current.Run; } }
        public static Color Shadow { get { return Theme.Current.Shadow; } }

        /// <summary>圆角矩形路径; 半径超过高度一半时自动收敛为胶囊。</summary>
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) return p;
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>给任意窗口应用主题边框色/标题栏色/深色模式(DWM 属性, 旧系统静默失败)。
        /// 主窗口与所有对话框(关于/欢迎/捕获等)共用, 保证弹窗边框与主题一致。</summary>
        public static void ApplyFrameTheme(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            try
            {
                int dark = Theme.Current.Dark ? 1 : 0;
                NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));
                int border = ColorTranslator.ToWin32(Theme.Current.WindowBg);
                NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));
                int caption = ColorTranslator.ToWin32(Theme.Current.CardBg);
                NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            }
            catch (Exception)
            {
                // 旧系统不支持这些属性, 静默忽略
            }
        }

        /// <summary>
        /// 阴影: 霓虹主题画光晕(多圈半透明描边), 常规主题画硬偏移阴影。
        /// bg 为阴影背后的背景色(已填充的四角底色), 用于把半透明阴影预混成不透明色,
        /// 避免半透明填充在未清空的缓冲上被渲染成黑边。pressed 时阴影收缩, 模拟按压。
        /// off: 硬阴影下移像素数(默认 3; 按压动画可传 1~3 实现平滑收缩)。
        /// </summary>
        public static void DrawShadow(Graphics g, Rectangle body, int radius, bool pressed, Color bg, float off = 3f)
        {
            if (Theme.Current.Glow)
            {
                // 霓虹光晕: 多圈半透明描边; 描边颜色按 bg 预混成不透明色,
                // 避免半透明填充在未清空的缓冲上被渲染成黑边(首次显示黑边、重绘后消失)。
                int baseA = Math.Max(18, Theme.Current.Shadow.A / 2);
                for (int k = 1; k <= 3; k++)
                {
                    int a = Math.Max(8, baseA / k);
                    var gr = Rectangle.Inflate(body, k * 2, k * 2);
                    using (var gp = Clay.Round(gr, radius + k * 2))
                    using (var pen = new Pen(Premix(bg, Theme.Current.Shadow, a), 2.5f))
                        g.DrawPath(pen, gp);
                }
            }
            else
            {
                // 硬偏移阴影: 直接下移 offset 像素, 并把阴影圆角加大 offset,
                // 使阴影下沿圆角与按钮下沿圆角在竖直方向上"接上", 消除左下/右下角的竖直线残影;
                // 不再横向膨胀(膨胀会在按钮四周露出黑色边)。
                float o = pressed ? Math.Min(off, 1.5f) : off;
                var s = body;
                s.Offset(0, (int)Math.Round(o));
                using (var sp = Clay.Round(s, radius + (int)Math.Round(o)))
                using (var sb = new SolidBrush(Premix(bg, Theme.Current.Shadow, Theme.Current.Shadow.A)))
                    g.FillPath(sb, sp);
            }
        }

        /// <summary>把半透明前景色按 alpha 与背景色预混成不透明色(视觉与 alpha 混合一致, 但不受缓冲残留影响)。</summary>
        private static Color Premix(Color bg, Color fg, int alpha)
        {
            return Color.FromArgb(
                bg.R + (fg.R - bg.R) * alpha / 255,
                bg.G + (fg.G - bg.G) * alpha / 255,
                bg.B + (fg.B - bg.B) * alpha / 255);
        }

        /// <summary>主题渐变刷: 拟物主题用三阶(顶部高光→基色→底部暗), 其余用双色渐变。</summary>
        public static LinearGradientBrush Gradient(Rectangle r, Color top, Color bottom)
        {
            if (Theme.Current.Bevel)
            {
                var cb = new ColorBlend(3);
                cb.Positions = new float[] { 0f, 0.45f, 1f };
                cb.Colors = new Color[] { Theme.Lighten(top, 45), top, Theme.Darken(bottom, 22) };
                return new LinearGradientBrush(r, Color.Black, Color.White, LinearGradientMode.Vertical)
                {
                    InterpolationColors = cb
                };
            }
            return new LinearGradientBrush(r, top, bottom, LinearGradientMode.Vertical);
        }

        /// <summary>顶部内高光描边。</summary>
        public static void DrawTopLight(Graphics g, Rectangle body, int radius, int alpha)
        {
            using (var gp = Clay.Round(Rectangle.Inflate(body, -1, -1), Math.Max(1, radius - 1)))
            using (var pen = new Pen(Color.FromArgb(alpha, 255, 255, 255), 1f))
                g.DrawPath(pen, gp);
        }
    }

    /// <summary>静态构建工具: 输入控件外壳等。</summary>
    internal static class ClayKit
    {
        /// <summary>把输入控件包进圆角内凹外壳(NumericUpDown / ComboBox / TextBox)。坐标按 96 DPI 传入, 内部自动缩放。</summary>
        public static ClayPanel InputShell(Control inner, int x, int y, int w)
        {
            var shell = new ClayPanel
            {
                Location = new Point(Dpi.X(x), Dpi.X(y)),
                Size = new Size(Dpi.X(w), Dpi.X(26)),
                CornerRadius = 0, // 0 = 由主题决定
                Inset = true,
                BackColor = Theme.Current.InputBg
            };
            if (inner is ComboBox)
                ((ComboBox)inner).ItemHeight = Dpi.X(15);
            inner.Width = Dpi.X(w) - 2 * Dpi.X(4);
            inner.Height = Math.Max(inner.Height, Dpi.X(19));
            inner.Location = new Point(Dpi.X(4), Math.Max(0, (shell.Height - inner.Height) / 2));
            inner.BackColor = Theme.Current.InputBg;
            inner.ForeColor = Theme.Current.Ink;
            shell.Controls.Add(inner);
            return shell;
        }
    }

    /// <summary>主题按钮: 渐变圆角 + 阴影/光晕 + 顶部内高光 + 按压回弹。
    /// 悬停/按压/焦点环/标签选中均走 Anim 指数平滑, 视觉上"快起慢收"(ease-out), 可中断不跳变。</summary>
    internal class ClayButton : Button
    {
        public bool Accent;   // 主渐变
        public bool Danger;   // 停止态
        public bool Mint;     // 薄荷
        public bool Tab;      // 标签页胶囊模式
        public bool Selected; // 标签页选中(逻辑态; 视觉过渡看 _selT)

        private bool _hover;
        private bool _pressed;

        // 动效进度 0~1
        private float _hoverT;
        private float _pressT;
        private float _focusT;
        private float _selT = 1f;

        // 文字放不下时自动缩小的字号缓存(键=文本+宽度+字号+样式, 命中则复用, 未缩小为 null)
        private Font _fitFont;
        private string _fitKey;

        private readonly Action<float> _animHover;
        private readonly Action<float> _animPress;
        private readonly Action<float> _animFocus;
        private readonly Action<float> _animSel;

        public ClayButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            ForeColor = Clay.Ink;
            BackColor = Clay.CardBg;
            _animHover = v => { _hoverT = v; Invalidate(); };
            _animPress = v => { _pressT = v; Invalidate(); };
            _animFocus = v => { _focusT = v; Invalidate(); };
            _animSel = v => { _selT = v; Invalidate(); };
            if (Selected) _selT = 1f;
        }

        /// <summary>标签页切换时设置选中目标值(触发平滑过渡); 非 Tab 按钮忽略。</summary>
        public void SetSelectionT(float target)
        {
            if (!Tab) return;
            Anim.To(_animSel, _selT, target, 120f);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Anim.To(_animHover, _hoverT, 1f, 70f); // 进入快(及时反馈)
            base.OnMouseEnter(e);
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Anim.To(_animHover, _hoverT, 0f, 100f);
            Anim.To(_animPress, _pressT, 0f, 90f);
            base.OnMouseLeave(e);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            _pressed = true;
            Anim.To(_animPress, _pressT, 1f, 50f); // 按下反馈要快
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Anim.To(_animPress, _pressT, 0f, 100f); // 松开略缓, 自然回弹
            base.OnMouseUp(e);
        }
        protected override void OnGotFocus(EventArgs e) { Anim.To(_animFocus, _focusT, 1f, 100f); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Anim.To(_animFocus, _focusT, 0f, 100f); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        private bool Highlighted { get { return Selected || Accent || Danger || Mint; } }

        private Color TopColor(bool sel)
        {
            if (!Enabled) return Theme.Blend(Theme.Current.CreamTop, Theme.Current.CardBg, 0.5f);
            Color baseC, hoverC;
            if (sel || Accent) { baseC = Theme.Current.AccentTop; hoverC = Theme.Lighten(baseC, 18); }
            else if (Danger) { baseC = Theme.Current.DangerTop; hoverC = Theme.Lighten(baseC, 18); }
            else if (Mint) { baseC = Theme.Current.MintTop; hoverC = Theme.Lighten(baseC, 18); }
            else { baseC = Theme.Current.CreamTop; hoverC = Theme.Lighten(baseC, 12); }
            return Theme.Blend(baseC, hoverC, Anim.EaseOutCubic(_hoverT));
        }

        private Color BottomColor(bool sel)
        {
            if (!Enabled) return Theme.Blend(Theme.Current.CreamBottom, Theme.Current.CardBg, 0.5f);
            Color baseC, hoverC;
            if (sel || Accent) { baseC = Theme.Current.AccentBottom; hoverC = Theme.Lighten(baseC, 18); }
            else if (Danger) { baseC = Theme.Current.DangerBottom; hoverC = Theme.Lighten(baseC, 18); }
            else if (Mint) { baseC = Theme.Current.MintBottom; hoverC = Theme.Lighten(baseC, 18); }
            else { baseC = Theme.Current.CreamBottom; hoverC = Theme.Lighten(baseC, 12); }
            return Theme.Blend(baseC, hoverC, Anim.EaseOutCubic(_hoverT));
        }

        /// <summary>文字超出按钮宽度时按比例缩小字号(英文文案普遍比中文长), 下限 7pt 后交给 EndEllipsis。返回 null 表示原字号放得下。</summary>
        private Font FitFont(int bodyWidth)
        {
            string key = Text + "|" + Width + "|" + Font.Size + "|" + Font.Style;
            if (_fitKey == key) return _fitFont;
            if (_fitFont != null) { _fitFont.Dispose(); _fitFont = null; }
            _fitKey = key;
            int avail = bodyWidth - 4;
            if (avail < 8) return null;
            Size sz = TextRenderer.MeasureText(Text, Font);
            if (sz.Width <= avail) return null;
            float scale = (float)avail / sz.Width;
            float size = Font.Size * scale;
            if (size < 7f) size = 7f;
            _fitFont = new Font(Font.FontFamily, size, Font.Style);
            return _fitFont;
        }

        protected override void Dispose(bool disposing)
        {
            if (_fitFont != null) { _fitFont.Dispose(); _fitFont = null; }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 先填满整个矩形背景, 避免圆角外的四角露出未初始化像素。
            // 用父容器背景色而非 BackColor: 否则按钮(默认 CardBg)放在页面(WindowBg)上时
            // 四角会露出近白色方块, 形成"白色方形边框"。
            Color squareBg = Tab ? Clay.WindowBg : (Parent != null ? Parent.BackColor : BackColor);
            using (var br = new SolidBrush(squareBg))
                g.FillRectangle(br, ClientRectangle);

            float hv = Anim.EaseOutCubic(_hoverT);
            float pv = Anim.EaseOutCubic(_pressT);

            var body = new Rectangle(0, 0, Width - 1, Height - 4);
            if (Tab)
            {
                // 未选中标签的悬停暖底(淡入)
                if (_selT < 0.99f && _hover)
                {
                    using (var bp = Clay.Round(new Rectangle(0, 0, Width - 1, Height - 3), Dpi.X(Theme.Current.Radius)))
                    using (var br = new SolidBrush(Theme.Blend(squareBg, Theme.Current.TabHot, hv * 0.7f)))
                        g.FillPath(br, bp);
                }
            }

            int r = Math.Min(Height - 4, Dpi.X(Theme.Current.Radius));

            // 标签选中交叉过渡: 颜色在"未选中(奶油)"与"选中(强调)"间按 _selT 插值(ease-in-out)
            float selT = 1f;
            bool shadowed = Highlighted;
            if (Tab)
            {
                selT = Anim.EaseInOutCubic(_selT);
                shadowed = selT > 0.5f;
            }
            Color top = Tab ? Theme.Blend(TopColor(false), TopColor(true), selT) : TopColor(Selected);
            Color bot = Tab ? Theme.Blend(BottomColor(false), BottomColor(true), selT) : BottomColor(Selected);

            if (!shadowed)
            {
                // 无阴影的扁平按钮(次级/未选中标签)
                using (var bp = Clay.Round(body, r))
                {
                    using (var br = Clay.Gradient(body, top, bot))
                        g.FillPath(br, bp);
                    Clay.DrawTopLight(g, body, r, 120);
                }
            }
            else
            {
                // 有阴影/光晕的实体按钮(主按钮/选中标签); 按压 = 下沉 + 阴影收缩 + 轻微变暗
                int sink = (int)Math.Round(pv);                 // 按压下沉 1px
                int shrink = (int)Math.Round(2f * pv);          // 整体内收 2px(模拟按压缩小)
                if (sink > 0 || shrink > 0) body = Rectangle.Inflate(body, -shrink, -shrink);
                if (sink > 0) body.Offset(0, sink);
                float off = 3f - 2f * pv;                       // 阴影从 3px 收窄到 1px
                Clay.DrawShadow(g, body, r, pv > 0.5f, squareBg, off);
                using (var bp = Clay.Round(body, r))
                {
                    Color pt = Theme.Blend(top, Theme.Darken(top, 8), pv);
                    Color pb = Theme.Blend(bot, Theme.Darken(bot, 8), pv);
                    using (var br = Clay.Gradient(body, pt, pb))
                        g.FillPath(br, bp);
                    Clay.DrawTopLight(g, body, r, 140);
                    if (_hover && !_pressed)
                    {
                        using (var br = new SolidBrush(Color.FromArgb((int)(28 * hv), 255, 255, 255)))
                            g.FillPath(br, bp);
                    }
                }
            }

            // 文字(左右各留 6px 内边距, 放不下时先缩小字号, 再放不下才截断)
            Rectangle textRect = Rectangle.Inflate(body, -6, 0);
            Font tf = FitFont(textRect.Width);
            TextRenderer.DrawText(g, Text, tf != null ? tf : Font, textRect, Enabled ? ForeColor : Clay.InkSoft,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // 焦点虚线环(淡入淡出)
            if (Focused && ShowFocusCues)
            {
                int fa = (int)(200 * Anim.EaseOutCubic(_focusT));
                if (fa > 4)
                {
                    using (var p = Clay.Round(Rectangle.Inflate(body, -4, -4), Math.Max(1, r - 4)))
                    using (var pen = new Pen(Color.FromArgb(fa, Clay.Line)) { DashStyle = DashStyle.Dash })
                        g.DrawPath(pen, p);
                }
            }
        }
    }

    /// <summary>主题复选框: 圆角方块 + 渐变勾选态。悬停底色与勾选对勾淡入淡出。</summary>
    internal class ClayCheck : CheckBox
    {
        private float _hoverT;
        private float _checkT;

        private readonly Action<float> _animHover;
        private readonly Action<float> _animCheck;

        public ClayCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = true;
            ForeColor = Clay.Ink;
            BackColor = Clay.CardBg;
            _animHover = v => { _hoverT = v; Invalidate(); };
            _animCheck = v => { _checkT = v; Invalidate(); };
            _checkT = Checked ? 1f : 0f;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var sz = TextRenderer.MeasureText(Text, Font);
            return new Size(22 + 4 + sz.Width + 2, Math.Max(18, sz.Height));
        }

        protected override void OnMouseEnter(EventArgs e) { Anim.To(_animHover, _hoverT, 1f, 70f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Anim.To(_animHover, _hoverT, 0f, 100f); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Anim.To(_animCheck, _checkT, Checked ? 1f : 0f, 110f); base.OnCheckedChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var br = new SolidBrush(Parent != null ? Parent.BackColor : BackColor))
                g.FillRectangle(br, ClientRectangle);
            float hv = Anim.EaseOutCubic(_hoverT);
            float cv = Anim.EaseOutCubic(_checkT);
            var box = new Rectangle(0, (Height - 17) / 2, 17, 17);
            int r = Math.Min(6, Math.Max(2, Theme.Current.Radius / 2));
            using (var bp = Clay.Round(box, r))
            {
                if (Checked)
                {
                    using (var br = Clay.Gradient(box, Theme.Current.AccentTop, Theme.Current.AccentBottom))
                        g.FillPath(br, bp);
                    if (cv > 0.05f)
                    {
                        using (var pen = new Pen(Color.FromArgb((int)(255 * cv), 255, 255, 255), 2f)
                        {
                            StartCap = LineCap.Round,
                            EndCap = LineCap.Round,
                            LineJoin = LineJoin.Round
                        })
                        {
                            g.DrawLine(pen, box.X + 4, box.Y + 9, box.X + 7, box.Y + 12);
                            g.DrawLine(pen, box.X + 7, box.Y + 12, box.X + 13, box.Y + 5);
                        }
                    }
                }
                else
                {
                    Color bg = Theme.Blend(Theme.Current.CheckBg, Theme.Lighten(Theme.Current.CheckBg, 12), hv);
                    using (var br = new SolidBrush(bg))
                        g.FillPath(br, bp);
                    using (var pen = new Pen(Clay.Line, 1.5f))
                        g.DrawPath(pen, bp);
                }
            }
            var tr = new Rectangle(23, 0, Width - 23, Height);
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? ForeColor : Clay.InkSoft,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>主题单选框: 圆形 + 渐变内芯。悬停底色与选中内芯淡入淡出。</summary>
    internal class ClayRadio : RadioButton
    {
        private float _hoverT;
        private float _checkT;

        private readonly Action<float> _animHover;
        private readonly Action<float> _animCheck;

        public ClayRadio()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = true;
            ForeColor = Clay.Ink;
            BackColor = Clay.CardBg;
            _animHover = v => { _hoverT = v; Invalidate(); };
            _animCheck = v => { _checkT = v; Invalidate(); };
            _checkT = Checked ? 1f : 0f;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var sz = TextRenderer.MeasureText(Text, Font);
            return new Size(20 + 4 + sz.Width + 2, Math.Max(18, sz.Height));
        }

        protected override void OnMouseEnter(EventArgs e) { Anim.To(_animHover, _hoverT, 1f, 70f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Anim.To(_animHover, _hoverT, 0f, 100f); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Anim.To(_animCheck, _checkT, Checked ? 1f : 0f, 110f); base.OnCheckedChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var br = new SolidBrush(Parent != null ? Parent.BackColor : BackColor))
                g.FillRectangle(br, ClientRectangle);
            float hv = Anim.EaseOutCubic(_hoverT);
            float cv = Anim.EaseOutCubic(_checkT);
            var ring = new Rectangle(0, (Height - 17) / 2, 17, 17);
            Color bg = Theme.Blend(Theme.Current.CheckBg, Theme.Lighten(Theme.Current.CheckBg, 12), hv);
            using (var br = new SolidBrush(bg))
                g.FillEllipse(br, ring);
            using (var pen = new Pen(Clay.Line, 1.5f))
                g.DrawEllipse(pen, ring);
            if (Checked && cv > 0.05f)
            {
                var core = Rectangle.Inflate(ring, -5, -5);
                using (var br = new SolidBrush(Color.FromArgb((int)(255 * cv), Theme.Current.AccentBottom)))
                    g.FillEllipse(br, core);
            }
            var tr = new Rectangle(22, 0, Width - 22, Height);
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? ForeColor : Clay.InkSoft,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>主题分组卡片: 圆角卡 + 阴影/光晕 + 顶部内高光。</summary>
    internal class ClayGroup : GroupBox
    {
        public ClayGroup()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Clay.CardBg;
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // 四角用父容器背景色填充, 使卡片真正呈现圆角(而非露出近白色方块)
            using (var br = new SolidBrush(Parent != null ? Parent.BackColor : BackColor))
                g.FillRectangle(br, ClientRectangle);
            var body = new Rectangle(0, 0, Width - 1, Height - 4);
            int r = Math.Min(Dpi.X(Theme.Current.Radius), body.Height / 2);
            Clay.DrawShadow(g, body, r, false, Parent != null ? Parent.BackColor : BackColor);
            using (var bp = Clay.Round(body, r))
            {
                using (var br = new SolidBrush(Clay.CardBg))
                    g.FillPath(br, bp);
                Clay.DrawTopLight(g, body, r, 130);
            }
            if (!string.IsNullOrEmpty(Text))
            {
                using (var f = new Font(Font, FontStyle.Bold))
                    TextRenderer.DrawText(g, Text, f, new Rectangle(Dpi.X(16), Dpi.X(7), Width - Dpi.X(32), Dpi.X(20)), Clay.InkSoft,
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>主题卡片面板: 圆角卡片或内凹输入壳。CornerRadius = 0 时由主题决定。</summary>
    internal class ClayPanel : Panel
    {
        public int CornerRadius = 0; // 0 = 由主题决定
        public bool Inset;           // true = 内凹(输入壳), false = 卡片

        public ClayPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Clay.CardBg;
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // 四角用父容器背景色填充, 让圆角卡片/内凹壳与所在容器自然融合
            using (var br = new SolidBrush(Parent != null ? Parent.BackColor : BackColor))
                g.FillRectangle(br, ClientRectangle);
            int r = CornerRadius > 0 ? CornerRadius
                : (Inset ? Dpi.X(Math.Max(2, Theme.Current.Radius / 2)) : Dpi.X(Theme.Current.Radius));
            if (Inset)
            {
                var body = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var bp = Clay.Round(body, r))
                {
                    using (var br = new SolidBrush(BackColor))
                        g.FillPath(br, bp);
                    // 内凹: 明显一点的外框描边(比背景略深, 避免输入框与背景融为一体)
                    using (var gp = Clay.Round(Rectangle.Inflate(body, -1, -1), Math.Max(1, r - 1)))
                    using (var pen = new Pen(Color.FromArgb(150, Clay.Line), 1.5f))
                        g.DrawPath(pen, gp);
                }
            }
            else
            {
                var body = new Rectangle(0, 0, Width - 1, Height - 4);
                Clay.DrawShadow(g, body, r, false, Parent != null ? Parent.BackColor : BackColor);
                using (var bp = Clay.Round(body, r))
                {
                    using (var br = new SolidBrush(BackColor))
                        g.FillPath(br, bp);
                    Clay.DrawTopLight(g, body, r, 130);
                }
            }
        }
    }

    /// <summary>
    /// 主题数值输入框: 自绘替换系统原生上下箭头(原生白色箭头与主题不搭)。
    /// 隐藏内部 UpDown 按钮控件, 右侧自绘 ▲▼ 区域, 点击/滚轮/键盘上下键均可调整。
    /// </summary>
    internal class ClayNumericUpDown : NumericUpDown
    {
        private const int BtnW = 13;      // 右侧 ▲▼ 区宽度(设计单位, 内部 Dpi 缩放)
        private const int RepeatDelay = 450; // 按住后首次连发前的延迟(ms), 避免单击误跳 2 格
        private const int RepeatRate = 60;   // 长按连发间隔(ms)
        private readonly Timer _repeatTimer;
        private bool _hoverUp;
        private bool _hoverDown;
        private bool _pressUp;
        private bool _pressDown;

        public ClayNumericUpDown()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Current.InputBg;
            ForeColor = Theme.Current.Ink;
            _repeatTimer = new Timer { Interval = RepeatDelay };
            _repeatTimer.Tick += delegate
            {
                if (IsDisposed || Disposing) { _repeatTimer.Stop(); return; }
                // 按住 ▲▼ 持续增减(首次点击立即生效, 后续按 RepeatRate 连发)
                if (_pressUp && _hoverUp) UpButton();
                else if (_pressDown && _hoverDown) DownButton();
                else { _repeatTimer.Stop(); return; }
                _repeatTimer.Interval = RepeatRate;
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _repeatTimer.Stop();
                _repeatTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // 隐藏系统 UpDown 箭头按钮(否则拦截右侧 ▲▼ 区域点击);
            // 保留内部编辑框(UpDownEdit)并样式化, 让用户能点击后键盘直接输入数字。
            foreach (Control c in Controls)
            {
                string t = c.GetType().Name;
                if (t == "UpDownButtons")
                {
                    c.Visible = false;
                }
                else if (t == "UpDownEdit")
                {
                    c.Visible = true;
                    c.BackColor = Theme.Current.InputBg;
                    c.ForeColor = Enabled ? Theme.Current.Ink : Clay.InkSoft;
                    if (c is TextBox) ((TextBox)c).BorderStyle = BorderStyle.None;
                }
            }
            LayoutEdit();
        }

        /// <summary>把内部编辑框对齐到文字区(左侧), 右侧留出 ▲▼ 自绘区。</summary>
        private void LayoutEdit()
        {
            foreach (Control c in Controls)
            {
                if (c.GetType().Name == "UpDownEdit")
                {
                    c.Location = new Point(Dpi.X(4), c.Location.Y);
                    c.Width = Width - Dpi.X(BtnW) - Dpi.X(8);
                    break;
                }
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutEdit(); }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            foreach (Control c in Controls)
            {
                if (c.GetType().Name == "UpDownEdit")
                    c.ForeColor = Enabled ? Theme.Current.Ink : Clay.InkSoft;
            }
            Invalidate();
        }

        protected override void OnValueChanged(EventArgs e)
        {
            Invalidate();
            base.OnValueChanged(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool up = e.X >= Width - Dpi.X(BtnW) && e.Y < Height / 2;
            bool dn = e.X >= Width - Dpi.X(BtnW) && e.Y >= Height / 2;
            if (up != _hoverUp || dn != _hoverDown)
            {
                _hoverUp = up;
                _hoverDown = dn;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverUp = _hoverDown = _pressUp = _pressDown = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.X >= Width - Dpi.X(BtnW))
            {
                if (e.Y < Height / 2)
                {
                    _pressUp = true;
                    UpButton();
                }
                else
                {
                    _pressDown = true;
                    DownButton();
                }
                _repeatTimer.Interval = RepeatDelay; // 每次按下都从长延迟开始, 保证单击只 +1
                _repeatTimer.Start();
                Invalidate();
                return; // 不调 base: 避免系统文本选择行为
            }
            Focus();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressUp = _pressDown = false;
            _repeatTimer.Stop();
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var br = new SolidBrush(Theme.Current.InputBg))
                g.FillRectangle(br, ClientRectangle);

            // 数值文本由内部编辑框(UpDownEdit)显示, 支持点击后键盘直接输入; 这里不再手动绘制。

            // 右侧 ▲▼ 区: 分隔线 + 悬停高亮 + 主题色三角
            int x0 = Width - Dpi.X(BtnW);
            using (var pen = new Pen(Color.FromArgb(110, Clay.Line), 1f))
                g.DrawLine(pen, x0, Dpi.X(3), x0, Height - Dpi.X(3) - 1);

            if (Enabled && (_hoverUp || _hoverDown))
            {
                var zone = new Rectangle(x0 + Dpi.X(1), (_hoverUp ? 0 : Height / 2) + Dpi.X(1),
                    Dpi.X(BtnW) - Dpi.X(2), Height / 2 - Dpi.X(2));
                using (var gp = Clay.Round(zone, Dpi.X(4)))
                using (var br = new SolidBrush(Color.FromArgb(46, Theme.Current.AccentTop)))
                    g.FillPath(br, gp);
            }

            // 主题强调色箭头(浅色主题向墨色微混以保证可读性)
            float k = Theme.Current.Dark ? 0f : 0.42f;
            Color arrowBase = Enabled ? Theme.Blend(Theme.Current.AccentBottom, Theme.Current.Ink, k) : Clay.InkSoft;
            Color arrowHot = Enabled ? Theme.Current.AccentBottom : Clay.InkSoft;
            int cx = x0 + Dpi.X(BtnW) / 2;
            DrawArrow(g, arrowBase, arrowHot, _hoverUp, _pressUp && _hoverUp, cx, Height / 2 - Dpi.X(3), true);
            DrawArrow(g, arrowBase, arrowHot, _hoverDown, _pressDown && _hoverDown, cx, Height / 2 + Dpi.X(3), false);

            // 焦点下划线
            if (Focused && ShowFocusCues)
            {
                using (var pen = new Pen(Theme.Current.AccentTop, 1.5f))
                    g.DrawLine(pen, Dpi.X(2), Height - 1, Width - Dpi.X(2), Height - 1);
            }
        }

        private void DrawArrow(Graphics g, Color baseCol, Color hotCol, bool hovered, bool pressed, int cx, int cy, bool up)
        {
            Color c;
            if (!Enabled) c = Clay.InkSoft;
            else if (pressed) c = Theme.Lighten(hotCol, 30);
            else if (hovered) c = hotCol;
            else c = baseCol;
            using (var br = new SolidBrush(c))
            {
                var pts = up
                    ? new[] { new Point(cx - Dpi.X(4), cy + Dpi.X(3)), new Point(cx + Dpi.X(4), cy + Dpi.X(3)), new Point(cx, cy - Dpi.X(3)) }
                    : new[] { new Point(cx - Dpi.X(4), cy - Dpi.X(3)), new Point(cx + Dpi.X(4), cy - Dpi.X(3)), new Point(cx, cy + Dpi.X(3)) };
                g.FillPolygon(br, pts);
            }
        }
    }

    /// <summary>
    /// 主题下拉框: OwnerDraw 自绘静态区(背景/文本/主题箭头)与下拉列表项,
    /// 替换系统原生白色下拉箭头与白底列表。仅用于 DropDownList 样式。
    /// </summary>
    internal class ClayComboBox : ComboBox
    {
        private IntPtr _listBrush = IntPtr.Zero; // GDI 背景刷(下拉列表窗口)
        private bool _dropped;
        private float _hoverT;

        private readonly Action<float> _animHover;

        public ClayComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            IntegralHeight = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Current.InputBg;
            ForeColor = Theme.Current.Ink;
            ItemHeight = Dpi.X(20);
            MaxDropDownItems = 9;
            _animHover = v => { _hoverT = v; Invalidate(); };
        }

        protected override void OnMouseEnter(EventArgs e) { Anim.To(_animHover, _hoverT, 1f, 80f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Anim.To(_animHover, _hoverT, 0f, 110f); base.OnMouseLeave(e); }
        protected override void OnDropDown(EventArgs e)
        {
            _dropped = true;
            RecreateListBrush();
            // 下拉高度贴合条目数, 避免底部露出系统白底
            DropDownHeight = Math.Min(Items.Count, MaxDropDownItems) * ItemHeight + 4;
            Invalidate();
            base.OnDropDown(e);
        }
        protected override void OnDropDownClosed(EventArgs e) { _dropped = false; Invalidate(); base.OnDropDownClosed(e); }
        protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        private void RecreateListBrush()
        {
            if (_listBrush != IntPtr.Zero) NativeMethods.DeleteObject(_listBrush);
            _listBrush = NativeMethods.CreateSolidBrush(ColorTranslator.ToWin32(Theme.Current.InputBg));
        }

        protected override void Dispose(bool disposing)
        {
            if (_listBrush != IntPtr.Zero) NativeMethods.DeleteObject(_listBrush);
            _listBrush = IntPtr.Zero;
            base.Dispose(disposing);
        }

        /// <summary>下拉列表窗口背景/文字颜色(WM_CTLCOLORLISTBOX)。</summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0134) // WM_CTLCOLORLISTBOX
            {
                if (_listBrush == IntPtr.Zero) RecreateListBrush();
                NativeMethods.SetBkColor(m.WParam, ColorTranslator.ToWin32(Theme.Current.InputBg));
                NativeMethods.SetTextColor(m.WParam, ColorTranslator.ToWin32(Theme.Current.Ink));
                m.Result = _listBrush;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var br = new SolidBrush(Enabled ? Theme.Current.InputBg : Theme.Blend(Theme.Current.InputBg, Clay.CardBg, 0.5f)))
                g.FillRectangle(br, ClientRectangle);

            // 选中项文本
            var tr = new Rectangle(Dpi.X(5), 0, Width - Dpi.X(20), Height);
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? Theme.Current.Ink : Clay.InkSoft,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // 右侧下拉箭头: 主题色小三角(悬停淡入), 展开时翻转
            int cx = Width - Dpi.X(11);
            int cy = Height / 2;
            Color arrowC = Theme.Blend(Clay.InkSoft, Theme.Current.AccentBottom, _dropped ? 1f : Anim.EaseOutCubic(_hoverT));
            using (var arrow = new SolidBrush(arrowC))
            {
                var pts = _dropped
                    ? new[] { new Point(cx - Dpi.X(4), cy - Dpi.X(2)), new Point(cx + Dpi.X(4), cy - Dpi.X(2)), new Point(cx, cy + Dpi.X(3)) }
                    : new[] { new Point(cx - Dpi.X(4), cy - Dpi.X(3)), new Point(cx + Dpi.X(4), cy - Dpi.X(3)), new Point(cx, cy + Dpi.X(2)) };
                g.FillPolygon(arrow, pts);
            }

            // 焦点下划线
            if (Focused && ShowFocusCues)
            {
                using (var pen = new Pen(Theme.Current.AccentTop, 1.5f))
                    g.DrawLine(pen, Dpi.X(2), Height - 1, Width - Dpi.X(2), Height - 1);
            }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= Items.Count) return;
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (var br = new SolidBrush(sel ? Theme.Blend(Theme.Current.AccentTop, Theme.Current.InputBg, 0.65f) : Theme.Current.InputBg))
                e.Graphics.FillRectangle(br, e.Bounds);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font,
                new Rectangle(e.Bounds.X + Dpi.X(5), e.Bounds.Y, e.Bounds.Width - Dpi.X(10), e.Bounds.Height),
                sel ? Theme.Current.Ink : Clay.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>
    /// 主题滑块: 圆角轨道 + 主题渐变已填充段 + 圆形拇指。点击/拖动调整, 值域 Minimum~Maximum。
    /// 用于音量等 0~100 调节, 与主题一致(不用系统原生 TrackBar)。
    /// </summary>
    internal class ClaySlider : Control
    {
        private int _value;
        private bool _drag;
        private float _hoverT;

        private readonly Action<float> _animHover;

        public event EventHandler ValueChanged;

        public int Minimum = 0;
        public int Maximum = 100;

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Clamp(value);
                if (v != _value)
                {
                    _value = v;
                    Invalidate();
                }
            }
        }

        public ClaySlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Clay.CardBg;
            _value = 100;
            _animHover = v => { _hoverT = v; Invalidate(); };
        }

        private int Clamp(int v)
        {
            int lo = Math.Min(Minimum, Maximum);
            int hi = Math.Max(Minimum, Maximum);
            return Math.Max(lo, Math.Min(hi, v));
        }

        protected override void OnMouseEnter(EventArgs e) { Anim.To(_animHover, _hoverT, 1f, 80f); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _drag = false; Anim.To(_animHover, _hoverT, 0f, 110f); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _drag = true;
                UpdateFromX(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag) UpdateFromX(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_drag) { _drag = false; UpdateFromX(e.X); }
            base.OnMouseUp(e);
        }

        private void UpdateFromX(int x)
        {
            int pad = Dpi.X(7);
            int innerW = Width - pad * 2;
            float frac = innerW > 0 ? (float)(x - pad) / innerW : 0f;
            frac = Math.Max(0f, Math.Min(1f, frac));
            int v = (int)Math.Round(Minimum + (Maximum - Minimum) * frac);
            if (v != _value)
            {
                _value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var br = new SolidBrush(Parent != null ? Parent.BackColor : BackColor))
                g.FillRectangle(br, ClientRectangle);

            int pad = Dpi.X(7);
            int trackH = Dpi.X(4);
            int cy = Height / 2;
            var track = new Rectangle(pad, cy - trackH / 2, Width - pad * 2, trackH);
            using (var bp = Clay.Round(track, trackH / 2))
            using (var br = new SolidBrush(Enabled ? Theme.Current.Line : Clay.InkSoft))
                g.FillPath(br, bp);

            float frac = Maximum > Minimum ? (float)(_value - Minimum) / (Maximum - Minimum) : 0f;
            int fillW = (int)((Width - pad * 2) * frac);
            if (fillW > trackH)
            {
                var fill = new Rectangle(pad, cy - trackH / 2, fillW, trackH);
                using (var bp = Clay.Round(fill, trackH / 2))
                using (var br = Clay.Gradient(fill, Theme.Current.AccentTop, Theme.Current.AccentBottom))
                    g.FillPath(br, bp);
            }

            int cx = pad + fillW;
            float ht = Anim.EaseOutCubic(_hoverT);
            int r = Dpi.X((int)Math.Round(7f + ht));
            var thumb = new Rectangle(cx - r, cy - r, r * 2, r * 2);
            using (var br = new SolidBrush(Enabled ? Theme.Current.AccentBottom : Clay.InkSoft))
                g.FillEllipse(br, thumb);
            using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1.5f))
                g.DrawEllipse(pen, thumb);
        }
    }

    /// <summary>主题右键菜单配色表: 背景/高亮/分隔线全部跟随 Theme.Current。</summary>
    internal class ClayMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Current.CardBg; } }
        public override Color MenuBorder { get { return Clay.Line; } }
        public override Color MenuItemBorder { get { return Clay.Line; } }
        public override Color MenuItemSelected { get { return Theme.Blend(Theme.Current.AccentTop, Theme.Current.CardBg, 0.45f); } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Blend(Theme.Current.AccentTop, Theme.Current.CardBg, 0.45f); } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Blend(Theme.Current.AccentBottom, Theme.Current.CardBg, 0.45f); } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.Blend(Theme.Current.AccentBottom, Theme.Current.CardBg, 0.6f); } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.Blend(Theme.Current.AccentBottom, Theme.Current.CardBg, 0.6f); } }
        public override Color ImageMarginGradientBegin { get { return Theme.Current.CardBg; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Current.CardBg; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Current.CardBg; } }
        public override Color SeparatorDark { get { return Clay.Line; } }
        public override Color SeparatorLight { get { return Clay.Line; } }
    }

    /// <summary>构建主题化右键菜单, 文字色跟随主题(切换主题后仍实时取色)。</summary>
    internal static class ClayMenu
    {
        public static ContextMenuStrip Build()
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new ToolStripProfessionalRenderer(new ClayMenuColorTable()),
                BackColor = Clay.CardBg,
                ForeColor = Clay.Ink,
                ShowImageMargin = false
            };
            // 打开前刷新文字色, 保证主题切换后菜单文字仍可读
            menu.Opening += delegate
            {
                menu.ForeColor = Clay.Ink;
                foreach (ToolStripItem it in menu.Items) it.ForeColor = Clay.Ink;
            };
            return menu;
        }

        public static ToolStripMenuItem Item(ContextMenuStrip owner, string en)
        {
            var it = new ToolStripMenuItem(Lang.T(en)) { Name = en };
            owner.Items.Add(it);
            return it;
        }
    }
}
