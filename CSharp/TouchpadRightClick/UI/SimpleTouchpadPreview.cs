using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TouchpadRightClick.UI
{
    /// <summary>
    /// 觸控板即時預覽:畫出右鍵區、上下分界、邊緣保護帶、目前觸點與輕觸閃爍。
    /// 版面用最大的 2:1 區域置中,隨視窗縮放;顏色跟 Theme。
    /// </summary>
    public class SimpleTouchpadPreview : Control
    {
        private float _rightZoneStartX = 0.6f;
        private float _verticalSplitY = 0.5f;
        private PointF _currentTouch = PointF.Empty;
        private PointF _tapIndicator = PointF.Empty;
        private System.Windows.Forms.Timer _tapIndicatorTimer;
        private int _tapFadeCounter = 0;
        private const int TAP_FADE_DURATION = 20;  // 淡出持續時間（×50ms ≈ 1 秒）
        private static readonly StringFormat CenterFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        // 邊緣保護帶（由 ModernMainForm 從 TapZoneDetector 帶進來,預覽必須畫出實際判定邊界）
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float EdgeLeft { get; set; } = 0.04f;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float EdgeRight { get; set; } = 0.04f;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float EdgeTop { get; set; } = 0.06f;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float RightZoneStartX
        {
            get => _rightZoneStartX;
            set
            {
                // 不夾值:預覽必須畫出 TapZoneDetector 實際判定的邊界（UI 滑桿允許到 92%,夾在 0.9 會畫錯位置）
                _rightZoneStartX = value;
                Invalidate();
            }
        }

        /// <summary>垂直分隔線位置（上下分界）</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float VerticalSplitY
        {
            get => _verticalSplitY;
            set { _verticalSplitY = value; Invalidate(); }
        }

        public SimpleTouchpadPreview()
        {
            this.DoubleBuffered = true;
            this.BackColor = Theme.Card;
            this.ResizeRedraw = true;
            this.AccessibleName = "觸控板區域預覽";
            this.AccessibleRole = AccessibleRole.Graphic;

            _tapIndicatorTimer = new System.Windows.Forms.Timer();
            _tapIndicatorTimer.Interval = 50;
            _tapIndicatorTimer.Tick += TapIndicatorTimer_Tick;
        }

        private void TapIndicatorTimer_Tick(object sender, EventArgs e)
        {
            _tapFadeCounter++;
            if (_tapFadeCounter >= TAP_FADE_DURATION)
            {
                _tapIndicator = PointF.Empty;
                _tapIndicatorTimer.Stop();
            }
            Invalidate();
        }

        public void UpdateTouch(double x, double y)
        {
            _currentTouch = new PointF((float)x, (float)y);
            Invalidate();
        }

        public void ShowTapIndicator(double x, double y)
        {
            _tapIndicator = new PointF((float)x, (float)y);
            _tapFadeCounter = 0;
            _tapIndicatorTimer.Stop();
            _tapIndicatorTimer.Start();
            Invalidate();
        }

        /// <summary>置中的最大 2:1 矩形（觸控板長寬比）。</summary>
        private RectangleF PadRect()
        {
            float w = ClientSize.Width, h = ClientSize.Height;
            float pw = w, ph = w / 2f;
            if (ph > h) { ph = h; pw = h * 2f; }
            return new RectangleF((w - pw) / 2f, (h - ph) / 2f, pw, ph);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            RectangleF pad = PadRect();
            if (pad.Width < 10 || pad.Height < 10) return;

            using (var path = RoundedPath(pad, Math.Min(14f, pad.Height / 4f)))
            {
                // 觸控板本體
                using (var b = new SolidBrush(Theme.PreviewPad)) g.FillPath(b, path);

                // 之後所有區域都裁在圓角內
                g.SetClip(path);

                float zoneX = pad.X + pad.Width * _rightZoneStartX;
                float splitY = pad.Y + pad.Height * _verticalSplitY;

                // 右鍵區（右下）
                using (var b = new SolidBrush(Theme.PreviewZone))
                    g.FillRectangle(b, zoneX, splitY, pad.Right - zoneX, pad.Bottom - splitY);

                // 邊緣保護帶（左／右／上）:這裡輕觸不會被當成任何動作
                using (var b = new HatchBrush(HatchStyle.WideUpwardDiagonal, Theme.PreviewGuard, Color.Transparent))
                {
                    g.FillRectangle(b, pad.X, pad.Y, pad.Width * EdgeLeft, pad.Height);
                    g.FillRectangle(b, pad.Right - pad.Width * EdgeRight, pad.Y, pad.Width * EdgeRight, pad.Height);
                    g.FillRectangle(b, pad.X, pad.Y, pad.Width, pad.Height * EdgeTop);
                }

                // 分界線
                using (var p = new Pen(Theme.Accent, 2f)) g.DrawLine(p, zoneX, pad.Y, zoneX, pad.Bottom);
                using (var p = new Pen(Theme.PreviewSplit, 2f) { DashStyle = DashStyle.Dash }) g.DrawLine(p, pad.X, splitY, pad.Right, splitY);

                // 區域標籤（預覽太小時字會蓋出區域,乾脆不畫;門檻跟字型綁,DPI 放大也對）
                if (pad.Height >= Theme.BodyBold.Height * 6)
                {
                using (var muted = new SolidBrush(Theme.PreviewLabel))
                using (var accent = new SolidBrush(Theme.Accent))
                {
                    var f = Theme.BodyBold;
                    var sf = CenterFormat;
                    g.DrawString("原生", f, muted, new RectangleF(pad.X, pad.Y, zoneX - pad.X, splitY - pad.Y), sf);
                    g.DrawString("原生", f, muted, new RectangleF(pad.X, splitY, zoneX - pad.X, pad.Bottom - splitY), sf);
                    g.DrawString("原生", f, muted, new RectangleF(zoneX, pad.Y, pad.Right - zoneX, splitY - pad.Y), sf);
                    g.DrawString("輕觸＝右鍵\n長按＝拖曳", f, accent, new RectangleF(zoneX, splitY, pad.Right - zoneX, pad.Bottom - splitY), sf);
                }
                }

                // 目前觸點
                if (_currentTouch != PointF.Empty)
                {
                    float tx = pad.X + _currentTouch.X * pad.Width, ty = pad.Y + _currentTouch.Y * pad.Height;
                    bool inZone = _currentTouch.X >= _rightZoneStartX && _currentTouch.Y >= _verticalSplitY;
                    using (var b = new SolidBrush(inZone ? Theme.Accent : Theme.TextMuted))
                    using (var p = new Pen(Theme.Card, 2f))
                    {
                        g.FillEllipse(b, tx - 10, ty - 10, 20, 20);
                        g.DrawEllipse(p, tx - 10, ty - 10, 20, 20);
                    }
                }

                // 輕觸閃爍（淡出）
                if (_tapIndicator != PointF.Empty)
                {
                    float tx = pad.X + _tapIndicator.X * pad.Width, ty = pad.Y + _tapIndicator.Y * pad.Height;
                    float alpha = Math.Max(0f, Math.Min(1f, 1.0f - _tapFadeCounter / (float)TAP_FADE_DURATION));
                    int a = (int)(255 * alpha);
                    using (var b = new SolidBrush(Color.FromArgb(a, Theme.PreviewSplit)))
                    using (var p = new Pen(Color.FromArgb(a, Theme.Card), 2f))
                    {
                        g.FillEllipse(b, tx - 15, ty - 15, 30, 30);
                        g.DrawEllipse(p, tx - 15, ty - 15, 30, 30);
                    }
                }

                g.ResetClip();
                using (var p = new Pen(Theme.Border, 1.5f)) g.DrawPath(p, path);
            }
        }

        private static GraphicsPath RoundedPath(RectangleF r, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tapIndicatorTimer?.Dispose();
            base.Dispose(disposing);
        }
    }
}
