using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TouchpadRightClick.UI
{
    /// <summary>
    /// 全 UI 的單一色票／字級／尺寸來源。淺色／深色跟隨 Windows「應用程式模式」。
    /// 無障礙底線:內文 ≥10.5pt、按鈕高 ≥44px、文字對比 ≥4.5:1（色票已挑過）。
    /// ponytail: 只做 WinForms 原生控制項做得到的（Region 圓角、平面按鈕、深色標題列）,
    ///           TrackBar/NumericUpDown 在深色下維持系統外觀,不自繪。
    /// </summary>
    public static class Theme
    {
        public static readonly bool Dark = DetectDark();

        // ── 色票（淺色 / 深色）──
        public static readonly Color Bg          = Dark ? Color.FromArgb(17, 24, 39)    : Color.FromArgb(243, 244, 246);
        public static readonly Color Card        = Dark ? Color.FromArgb(31, 41, 55)    : Color.White;
        public static readonly Color CardAlt     = Dark ? Color.FromArgb(38, 50, 66)    : Color.FromArgb(249, 250, 251);
        public static readonly Color Border      = Dark ? Color.FromArgb(55, 65, 81)    : Color.FromArgb(229, 231, 235);
        public static readonly Color Text        = Dark ? Color.FromArgb(243, 244, 246) : Color.FromArgb(17, 24, 39);
        public static readonly Color TextMuted   = Dark ? Color.FromArgb(156, 163, 175) : Color.FromArgb(75, 85, 99);
        public static readonly Color Accent      = Dark ? Color.FromArgb(96, 165, 250)  : Color.FromArgb(29, 78, 216);   // 白字對比 6.3:1
        public static readonly Color AccentText  = Dark ? Color.FromArgb(17, 24, 39)    : Color.White;
        public static readonly Color AccentSoft  = Dark ? Color.FromArgb(30, 58, 138)   : Color.FromArgb(219, 234, 254);
        public static readonly Color Danger      = Dark ? Color.FromArgb(248, 113, 113) : Color.FromArgb(185, 28, 28);
        public static readonly Color DangerText  = Dark ? Color.FromArgb(17, 24, 39)    : Color.White;
        public static readonly Color Success     = Dark ? Color.FromArgb(74, 222, 128)  : Color.FromArgb(21, 128, 61);
        public static readonly Color Neutral     = Dark ? Color.FromArgb(55, 65, 81)    : Color.FromArgb(229, 231, 235);
        public static readonly Color NeutralText = Text;
        public static readonly Color LogBg       = Dark ? Color.FromArgb(11, 15, 25)    : Color.FromArgb(24, 24, 27);
        public static readonly Color LogText     = Color.FromArgb(212, 212, 216);

        // 預覽圖
        public static readonly Color PreviewPad   = Dark ? Color.FromArgb(55, 65, 81)  : Color.FromArgb(226, 232, 240);
        public static readonly Color PreviewGuard = Dark ? Color.FromArgb(75, 85, 99)  : Color.FromArgb(203, 213, 225);
        public static readonly Color PreviewZone  = Dark ? Color.FromArgb(120, 96, 165, 250) : Color.FromArgb(110, 29, 78, 216);
        public static readonly Color PreviewSplit = Dark ? Color.FromArgb(251, 113, 133) : Color.FromArgb(190, 18, 60);
        public static readonly Color PreviewLabel = Dark ? Color.FromArgb(209, 213, 219) : Color.FromArgb(71, 85, 105);

        // ── 字級 ──
        public static readonly Font Body    = new Font("Segoe UI", 10.5F);
        public static readonly Font BodyBold= new Font("Segoe UI", 10.5F, FontStyle.Bold);
        public static readonly Font Small   = new Font("Segoe UI", 9.5F);
        public static readonly Font H1      = new Font("Segoe UI", 18F, FontStyle.Bold);
        public static readonly Font H2      = new Font("Segoe UI", 12F, FontStyle.Bold);
        public static readonly Font BtnFont = new Font("Segoe UI", 11F, FontStyle.Bold);
        public static readonly Font Mono    = new Font("Consolas", 10F);
        public static readonly Font MonoBig = new Font("Consolas", 12F, FontStyle.Bold);

        // ── 尺寸 ──
        public const int ButtonHeight = 44;   // 觸控／手抖友善的最小點擊高度
        public const int Gap = 12;
        public const int Radius = 8;

        public enum ButtonKind { Primary, Secondary, Danger }

        public static Button MakeButton(string text, ButtonKind kind, string accessibleName, int width = 0)
        {
            var b = new Button
            {
                Text = text,
                Font = BtnFont,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Height = ButtonHeight,
                MinimumSize = new Size(120, ButtonHeight),
                AutoSize = width == 0,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(14, 0, 14, 0),
                Margin = new Padding(0, 0, Gap, Gap),
                AccessibleName = accessibleName,
                UseVisualStyleBackColor = false,
                TextImageRelation = TextImageRelation.ImageBeforeText,
            };
            if (width > 0) b.Width = width;
            b.FlatAppearance.BorderSize = 0;
            Style(b, kind);
            Round(b, Radius);
            return b;
        }

        /// <summary>切換按鈕外觀（開始／停止用同一顆按鈕）。</summary>
        public static void Style(Button b, ButtonKind kind)
        {
            switch (kind)
            {
                case ButtonKind.Primary:  b.BackColor = Accent;  b.ForeColor = AccentText;  break;
                case ButtonKind.Danger:   b.BackColor = Danger;  b.ForeColor = DangerText;  break;
                default:                  b.BackColor = Neutral; b.ForeColor = NeutralText; break;
            }
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(b.BackColor, Dark ? 0.15f : 0.1f);
            b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(b.BackColor, 0.1f);
        }

        public static Panel MakeCard(int padding = 16)
        {
            var p = new Panel { BackColor = Card, Padding = new Padding(padding), Margin = new Padding(0, 0, Gap, Gap) };
            Round(p, Radius);
            return p;
        }

        public static Label MakeLabel(string text, Font font = null, Color? color = null)
        {
            return new Label
            {
                Text = text,
                Font = font ?? Body,
                ForeColor = color ?? Text,
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 4),
            };
        }

        /// <summary>用 Region 做圓角;大小改變時重算（Dock/Anchor 會改尺寸）。</summary>
        public static void Round(Control c, int radius)
        {
            void Apply()
            {
                if (c.Width <= 0 || c.Height <= 0) return;
                using var path = new GraphicsPath();   // new Region(path) 會複製,path 要自己釋放
                int d = radius * 2;
                var r = new Rectangle(0, 0, c.Width, c.Height);
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                c.Region?.Dispose();
                c.Region = new Region(path);
            }
            Apply();
            c.SizeChanged += (s, e) => Apply();
        }

        /// <summary>Windows 11 深色標題列（失敗就算了,不影響功能）。</summary>
        public static void ApplyTitleBar(Form f)
        {
            try
            {
                int useDark = Dark ? 1 : 0;
                DwmSetWindowAttribute(f.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref useDark, sizeof(int));
            }
            catch { }
        }

        private static bool DetectDark()
        {
            // 優先順序:環境變數（測試鉤子）> config.json 的 "Theme" > Windows 應用程式模式
            var force = Environment.GetEnvironmentVariable("TAPRIGHT_THEME");
            if (string.IsNullOrEmpty(force)) force = PeekThemeFromConfig();
            if (string.Equals(force, "dark", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(force, "light", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                object v = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return v is int i && i == 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// Theme 是靜態的、在 Program.Main 一開始就要決定（SetColorMode 必須在任何視窗建立前）,
        /// 那時 AppSettings 還沒載入,所以只用 regex 偷看 config.json 的 "Theme" 欄位;壞檔／沒檔就回 null（跟系統）。
        /// </summary>
        private static string PeekThemeFromConfig()
        {
            try
            {
                if (!System.IO.File.Exists(Utils.AppSettings.FilePath)) return null;
                var m = System.Text.RegularExpressions.Regex.Match(
                    System.IO.File.ReadAllText(Utils.AppSettings.FilePath), @"""Theme""\s*:\s*""(system|light|dark)""",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    }
}
