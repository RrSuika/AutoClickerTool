using System;
using System.Drawing;

namespace AutoClickerTool
{
    /// <summary>
    /// 主题: 调色板 + 风格参数(圆角/光晕/拟物斜面/深色)。
    /// 每套主题参考 stylekit.top 的规范:
    ///   Clay      黏土风: 暖奶油 + 糖果粉彩 + 硬偏移阴影
    ///   ArtDeco   装饰艺术: 深海军蓝 + 唯一金色强调, 锐角, 金色光晕
    ///   Skeuo     拟物: 奶油纸/磨砂钢, 左上光源斜面, 底部暗边
    ///   Surreal   超现实: 午夜紫 + 玫金幽光, 有机大圆角(blob), 禁黑色阴影
    ///   Cyberpunk 赛博霓虹: 虚空黑 + 青/品红霓虹光晕, 圆角≤2px
    ///   Y2K       千禧年: 冰蓝白 + 彩虹渐变, 泡泡大圆角, 蓝色光晕
    /// </summary>
    internal class Theme
    {
        public string Id;
        public string NameZh;
        public string NameEn;
        public Color WindowBg, CardBg, Ink, InkSoft, Line, Run, Shadow, InputBg, CheckBg, TabHot, StatusBg;
        public Color AccentTop, AccentBottom;   // 主按钮渐变
        public Color DangerTop, DangerBottom;   // 停止态
        public Color MintTop, MintBottom;       // 薄荷/次级强调
        public Color CreamTop, CreamBottom;     // 普通按钮
        public bool Dark;    // 深色主题(输入框等底色)
        public bool Glow;    // 霓虹光晕阴影(代替黑色硬阴影)
        public bool Bevel;   // 拟物斜面(三阶渐变+底部暗边)
        public int Radius;   // 逻辑圆角半径

        private static readonly Theme[] Thems = BuildAll();

        /// <summary>当前主题, 默认第一套(Clay)。</summary>
        public static Theme Current = Thems[0];

        private Theme() { }

        private static Theme[] BuildAll()
        {
            return new[]
            {
                new Theme
                {
                    Id = "Clay", NameZh = "黏土 Claymorphism", NameEn = "Claymorphism",
                    WindowBg = C(0xF9, 0xF2, 0xE7), CardBg = C(0xFF, 0xFD, 0xF8),
                    Ink = C(0x4A, 0x3F, 0x35), InkSoft = C(0x8A, 0x7F, 0x72), Line = C(0xE4, 0xD5, 0xC0),
                    Run = C(0xE0, 0x52, 0x7D), Shadow = C(33, 0, 0, 0), InputBg = Color.White,
                    CheckBg = Color.White, TabHot = C(0xF3, 0xE9, 0xD8), StatusBg = C(0xF3, 0xE9, 0xD9),
                    AccentTop = C(0xC4, 0xB5, 0xFD), AccentBottom = C(0xF8, 0xB4, 0xD9),
                    DangerTop = C(0xFC, 0xA5, 0xA5), DangerBottom = C(0xFC, 0xD3, 0x4D),
                    MintTop = C(0xA7, 0xF3, 0xD0), MintBottom = C(0x8F, 0xF0, 0xC5),
                    CreamTop = C(0xFE, 0xF3, 0xC7), CreamBottom = C(0xFF, 0xF3, 0xD6),
                    Radius = 14
                },
                new Theme
                {
                    Id = "ArtDeco", NameZh = "装饰艺术 Art Deco", NameEn = "Art Deco",
                    WindowBg = C(0x1A, 0x1A, 0x2E), CardBg = C(0x2D, 0x2D, 0x44),
                    Ink = C(0xF5, 0xF5, 0xDC), InkSoft = C(0xB8, 0xB8, 0xA0), Line = C(0xD4, 0xAF, 0x37),
                    Run = C(0xD4, 0xAF, 0x37), Shadow = C(60, 0xD4, 0xAF, 0x37), InputBg = C(0x24, 0x24, 0x38),
                    CheckBg = C(0x24, 0x24, 0x38), TabHot = C(0x3A, 0x3A, 0x55), StatusBg = C(0x24, 0x24, 0x38),
                    AccentTop = C(0xD4, 0xAF, 0x37), AccentBottom = C(0xC9, 0xA2, 0x27),
                    DangerTop = C(0xB8, 0x73, 0x33), DangerBottom = C(0x8B, 0x5E, 0x3C),
                    MintTop = C(0x2E, 0x5A, 0x3C), MintBottom = C(0x1F, 0x3F, 0x2A),
                    CreamTop = C(0x3A, 0x3A, 0x55), CreamBottom = C(0x2D, 0x2D, 0x44),
                    Dark = true, Glow = true, Radius = 2
                },
                new Theme
                {
                    Id = "Skeuo", NameZh = "拟物 Skeuomorphism", NameEn = "Skeuomorphism",
                    WindowBg = C(0xD4, 0xC4, 0xA8), CardBg = C(0xF5, 0xF5, 0xDC),
                    Ink = C(0x5C, 0x40, 0x33), InkSoft = C(0x8B, 0x73, 0x55), Line = C(0xB0, 0xA0, 0x88),
                    Run = C(0xA0, 0x3C, 0x2C), Shadow = C(70, 0x5C, 0x40, 0x33), InputBg = C(0xFA, 0xF7, 0xEC),
                    CheckBg = C(0xFA, 0xF7, 0xEC), TabHot = C(0xE8, 0xE0, 0xCC), StatusBg = C(0xC9, 0xB9, 0x9D),
                    AccentTop = C(0xEC, 0xF0, 0xF6), AccentBottom = C(0xA8, 0xB4, 0xC4),
                    DangerTop = C(0xD9, 0x8C, 0x7A), DangerBottom = C(0xB8, 0x55, 0x3D),
                    MintTop = C(0x9F, 0xC7, 0xA8), MintBottom = C(0x6F, 0x9A, 0x7D),
                    CreamTop = C(0xF7, 0xF4, 0xE8), CreamBottom = C(0xD9, 0xCF, 0xB8),
                    Bevel = true, Radius = 8
                },
                new Theme
                {
                    Id = "Surreal", NameZh = "超现实 Surrealism", NameEn = "Surrealism",
                    WindowBg = C(0x1A, 0x1A, 0x3E), CardBg = C(0x26, 0x26, 0x4D),
                    Ink = C(0xF0, 0xEC, 0xE4), InkSoft = C(0xA9, 0xA2, 0xC0), Line = C(0xC3, 0x8D, 0x94),
                    Run = C(0xC3, 0x8D, 0x94), Shadow = C(70, 0xC3, 0x8D, 0x94), InputBg = C(0x20, 0x20, 0x42),
                    CheckBg = C(0x20, 0x20, 0x42), TabHot = C(0x33, 0x2F, 0x55), StatusBg = C(0x20, 0x20, 0x42),
                    AccentTop = C(0xD4, 0xA5, 0x74), AccentBottom = C(0xC3, 0x8D, 0x94),
                    DangerTop = C(0xB0, 0x6A, 0x7A), DangerBottom = C(0x8F, 0x55, 0x66),
                    MintTop = C(0x6F, 0x9A, 0x8A), MintBottom = C(0x55, 0x78, 0x6B),
                    CreamTop = C(0x33, 0x2F, 0x55), CreamBottom = C(0x26, 0x26, 0x4D),
                    Dark = true, Glow = true, Radius = 26
                },
                new Theme
                {
                    Id = "Cyber", NameZh = "赛博霓虹 Cyberpunk", NameEn = "Cyberpunk Neon",
                    WindowBg = C(0x0A, 0x0A, 0x0F), CardBg = C(0x14, 0x14, 0x1C),
                    Ink = C(0xE0, 0xFA, 0xFF), InkSoft = C(0x5F, 0xA8, 0xB8), Line = C(0x00, 0xFF, 0xFF),
                    Run = C(0xFF, 0x2D, 0x95), Shadow = C(80, 0x00, 0xFF, 0xFF), InputBg = C(0x0E, 0x0E, 0x16),
                    CheckBg = C(0x0E, 0x0E, 0x16), TabHot = C(0x1C, 0x1C, 0x2A), StatusBg = C(0x0E, 0x0E, 0x16),
                    AccentTop = C(0x00, 0xFF, 0xFF), AccentBottom = C(0x00, 0xA3, 0xCC),
                    DangerTop = C(0xFF, 0x00, 0xFF), DangerBottom = C(0xCC, 0x00, 0x77),
                    MintTop = C(0x00, 0xFF, 0x00), MintBottom = C(0x00, 0xBB, 0x55),
                    CreamTop = C(0x1C, 0x1C, 0x2A), CreamBottom = C(0x14, 0x14, 0x1C),
                    Dark = true, Glow = true, Radius = 2
                },
                new Theme
                {
                    Id = "Y2K", NameZh = "千禧年 Y2K", NameEn = "Y2K",
                    WindowBg = C(0xEA, 0xF3, 0xFF), CardBg = C(0xFB, 0xFD, 0xFF),
                    Ink = C(0x24, 0x35, 0x5E), InkSoft = C(0x5C, 0x74, 0xA8), Line = C(0xC4, 0xD8, 0xF2),
                    Run = C(0xFF, 0x4D, 0x8D), Shadow = C(55, 0x5C, 0x7A, 0xB8), InputBg = Color.White,
                    CheckBg = Color.White, TabHot = C(0xD8, 0xE8, 0xFF), StatusBg = C(0xD8, 0xE8, 0xFF),
                    AccentTop = C(0x7F, 0xD4, 0xFF), AccentBottom = C(0xC7, 0x9B, 0xFF),
                    DangerTop = C(0xFF, 0x9E, 0xB0), DangerBottom = C(0xFF, 0x6F, 0x91),
                    MintTop = C(0xB8, 0xF7, 0xD4), MintBottom = C(0x6F, 0xE3, 0xB0),
                    CreamTop = C(0xFF, 0xFF, 0xFF), CreamBottom = C(0xD8, 0xE8, 0xFF),
                    Glow = true, Radius = 22
                }
            };
        }

        public static Theme[] All { get { return Thems; } }

        public static Theme ById(string id)
        {
            foreach (var t in Thems)
            {
                if (t.Id == id) return t;
            }
            return Thems[0];
        }

        private static Color C(int r, int g, int b)
        {
            return Color.FromArgb(r, g, b);
        }

        private static Color C(int a, int r, int g, int b)
        {
            return Color.FromArgb(a, r, g, b);
        }

        public static Color Lighten(Color c, int amt)
        {
            return Color.FromArgb(
                Math.Min(255, c.R + amt),
                Math.Min(255, c.G + amt),
                Math.Min(255, c.B + amt));
        }

        public static Color Darken(Color c, int amt)
        {
            return Color.FromArgb(
                Math.Max(0, c.R - amt),
                Math.Max(0, c.G - amt),
                Math.Max(0, c.B - amt));
        }

        public static Color Blend(Color a, Color b, float f)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * f),
                (int)(a.G + (b.G - a.G) * f),
                (int)(a.B + (b.B - a.B) * f));
        }
    }
}
