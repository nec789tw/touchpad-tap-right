using System;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using TouchpadRightClick.Core;

namespace TouchpadRightClick.UI
{
    /// <summary>
    /// 主視窗。版面：標題列 → 分段導覽（基本設定／進階診斷）→ 內容 → 狀態列。
    /// 全部用 Dock/TableLayoutPanel 百分比,隨視窗與 DPI 縮放;色票／字級／按鈕尺寸集中在 Theme。
    /// 無障礙:按鈕 ≥44px、每個互動控制項有 AccessibleName 與 Alt 快捷鍵、TabIndex 依視覺順序。
    /// 觸控邏輯全部在 Core,這裡只負責顯示與把滑桿值寫進 TapZoneDetector。
    /// </summary>
    public class ModernMainForm : Form
    {
        private const int DIAGNOSTIC_UPDATE_INTERVAL_MS = 200;  // 診斷訊息批次更新間隔
        private const int DIAGNOSTIC_MAX_LINES = 50;            // 診斷日誌最大行數
        private const int DIAGNOSTIC_BUFFER_MAX_SIZE = 10000;   // 緩衝區大小上限 (bytes)

        private TouchpadMonitor _monitor;

        // 導覽與頁面
        private Button _navBasic;
        private Button _navDiag;
        private Panel _pageBasic;
        private Panel _pageDiag;

        // 基本設定頁
        private SimpleTouchpadPreview _preview;
        private NumericUpDown _zoneStartNumeric;
        private TrackBar _zoneStartSlider;
        private NumericUpDown _verticalSplitNumeric;  // 上邊界（上下分界）
        private TrackBar _verticalSplitSlider;
        private NumericUpDown _sensitivityNumeric;
        private TrackBar _sensitivitySlider;
        private CheckBox _enableCheckBox;
        private CheckBox _autoStartCheckBox;    // 開機自動啟動（工作排程器）
        private CheckBox _autoMonitorCheckBox;  // 啟動後自動開始監控
        private bool _suppressAutoStartEvent;   // 程式自己設 Checked 時不要跑登記
        private Button _quickStartButton;
        private Button _updateButton;
        private Button _themeButton;

        private Utils.AppSettings _settings;
        private System.Windows.Forms.Timer _saveTimer;   // 滑桿拖動時每步都會 ValueChanged,合併 400ms 存一次

        // 進階診斷頁
        private TextBox _diagnosticTextBox;
        private Label _coordinateLabel;
        private Label _hidInfoLabel;
        private Button _exportButton;
        private Button _clearButton;

        // 狀態欄
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;
        private ToolStripStatusLabel _modeLabel;

        private StringBuilder _diagnosticBuffer = new StringBuilder();
        private System.Windows.Forms.Timer _diagnosticUpdateTimer;
        private object _diagnosticBufferLock = new object();

        public ModernMainForm()
        {
            _settings = Utils.AppSettings.Load();
            _saveTimer = new System.Windows.Forms.Timer { Interval = 400 };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); _settings.Save(); };
            InitializeUI();
            InitializeMonitor();
            this.FormClosing += OnFormClosingCleanup;
            this.Disposed += (s, e) => { DisposeDiagnosticTimer(); _saveTimer.Dispose(); };
        }

        /// <summary>
        /// A4: 反訂閱事件後再停止 monitor,避免事件在 Form 釋放後仍觸發 BeginInvoke。
        /// </summary>
        private void OnFormClosingCleanup(object sender, FormClosingEventArgs e)
        {
            _saveTimer.Stop();
            _settings.Save();
            if (_monitor != null)
            {
                _monitor.TouchEvent -= Monitor_TouchEvent;
                _monitor.TapDetected -= Monitor_TapDetected;
                _monitor.StatusUpdate -= Monitor_StatusUpdate;
                _monitor.Dispose(); // 內含 Stop();順便釋放鉤子、Timer、NativeWindow handle、preparsed data
            }
        }

        private void DisposeDiagnosticTimer()
        {
            if (_diagnosticUpdateTimer != null)
            {
                _diagnosticUpdateTimer.Stop();
                _diagnosticUpdateTimer.Tick -= DiagnosticUpdateTimer_Tick;
                _diagnosticUpdateTimer.Dispose();
                _diagnosticUpdateTimer = null;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyTitleBar(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // DPI 放大後視窗可能比螢幕還大（1366×768 @125% 很常見）:夾進工作區再置中
            var wa = Screen.FromControl(this).WorkingArea;
            // MinimumSize 也被 DPI 放大過,先夾它,否則 Size 設不下去
            MinimumSize = new Size(Math.Min(MinimumSize.Width, wa.Width - 24), Math.Min(MinimumSize.Height, wa.Height - 24));
            Size = new Size(Math.Min(Width, wa.Width - 24), Math.Min(Height, wa.Height - 24));
            CenterToScreen();
            _quickStartButton.Focus();

            // 排程器查詢要開子行程（~100ms）,丟背景做;查完前勾選框停用,免得使用者剛勾的被舊狀態蓋回去。
            // 從沒用過的人（設定沒記路徑）不會開任何子行程。
            _autoStartCheckBox.Enabled = false;
            System.Threading.Tasks.Task.Run(() =>
            {
                bool enabled = Utils.AutoStart.ProbeAndRepair(_settings);
                InvokeIfRequired(() =>
                {
                    _suppressAutoStartEvent = true;
                    _autoStartCheckBox.Checked = enabled;
                    _suppressAutoStartEvent = false;
                    _autoStartCheckBox.Enabled = true;
                });
            });

            if (_settings.AutoStartMonitoring || Utils.AutoStart.HasArg(Utils.AutoStart.ResumeMonitorArg))
                BeginInvoke(new Action(() => { if (_quickStartButton.Text.Contains("開始")) QuickStartButton_Click(this, EventArgs.Empty); }));
        }

        // ───────────────────────────── 版面 ─────────────────────────────

        private void InitializeUI()
        {
            SuspendLayout();
            Text = $"觸控板輕觸右鍵 {Utils.UpdateChecker.CurrentTag}";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Size = new Size(1000, 680);
            MinimumSize = new Size(800, 560);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            Icon = LoadAppIcon(0);   // 標題列／工作列圖示;不設的話是 WinForms 預設圖示

            // Dock 的疊放順序＝反向加入順序:先加 Fill,再加 Top/Bottom,外層的最後加
            var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 8), BackColor = Theme.Bg };
            _pageBasic = BuildBasicPage();
            _pageDiag = BuildDiagnosticPage();
            content.Controls.Add(_pageDiag);
            content.Controls.Add(_pageBasic);
            Controls.Add(content);
            Controls.Add(BuildNav());
            Controls.Add(BuildHeader());
            Controls.Add(BuildStatusBar());

            InitializeDiagnosticUpdateTimer();
            ShowPage(false);
            ResumeLayout();
        }

        /// <summary>從內嵌的 app.ico 取圖示;size=0 給 Form.Icon（保留全部尺寸,系統自己挑）,其他給 PictureBox。失敗回 null 不影響功能。</summary>
        private static Icon LoadAppIcon(int size)
        {
            try
            {
                using var st = typeof(ModernMainForm).Assembly.GetManifestResourceStream("app.ico");
                if (st == null) return null;
                return size == 0 ? new Icon(st) : new Icon(st, size, size);
            }
            catch { return null; }
        }

        private Control BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.Card, Padding = new Padding(20, 14, 20, 14) };

            // 標題列左側放 logo（跟 exe 圖示同一顆）
            var logoIcon = LoadAppIcon(64);
            var logo = new PictureBox
            {
                Dock = DockStyle.Left, Width = 76, SizeMode = PictureBoxSizeMode.Zoom,
                Image = logoIcon?.ToBitmap(), BackColor = Color.Transparent, Margin = new Padding(0),
                Padding = new Padding(0, 0, 12, 0), AccessibleName = "TouchpadTapRight 標誌", AccessibleRole = AccessibleRole.Graphic,
            };

            var titles = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
            titles.Controls.Add(Theme.MakeLabel("觸控板輕觸右鍵", Theme.H1));
            titles.Controls.Add(Theme.MakeLabel($"單指輕觸就能按右鍵｜無障礙輔助設計　{Utils.UpdateChecker.CurrentTag}　元新電腦 × Claude AI", Theme.Small, Theme.TextMuted));

            _updateButton = Theme.MakeButton("🔄 檢查更新(&U)", Theme.ButtonKind.Secondary, "檢查更新", 170);
            _updateButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _updateButton.TabIndex = 90;
            _updateButton.Click += UpdateButton_Click;

            // 一鍵切換淺／深色:存檔→自動重啟（跳過歡迎視窗、原本在監控就自動恢復）,使用者只按一下。
            // 主題必須在任何視窗建立前決定（SetColorMode）,所以做不到不重啟;重啟只閃 1–2 秒。
            _themeButton = Theme.MakeButton(Theme.Dark ? "☀ 淺色(&T)" : "🌙 深色(&T)", Theme.ButtonKind.Secondary,
                                            Theme.Dark ? "切換為淺色外觀" : "切換為深色外觀", 130);
            _themeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _themeButton.TabIndex = 89;
            _themeButton.Click += ThemeButton_Click;

            // 按鈕直接掛在 header 上、垂直置中;titles 用 Dock Fill 吃剩下的寬度（按鈕比 Fill 先加,z-order 在上面才看得到）
            header.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border });
            header.Controls.Add(_updateButton);
            header.Controls.Add(_themeButton);
            header.Controls.Add(titles);
            if (logoIcon != null) header.Controls.Add(logo);   // 最後加的 Dock Left 排最外側（Dock 順序反向）
            titles.Padding = new Padding(0, 0, _updateButton.Width + _themeButton.Width + Theme.Gap * 2, 0);
            header.Layout += (s, e) =>
            {
                int y = (header.ClientSize.Height - _updateButton.Height) / 2;
                _updateButton.Location = new Point(header.ClientSize.Width - header.Padding.Right - _updateButton.Width, y);
                _themeButton.Location = new Point(_updateButton.Left - Theme.Gap - _themeButton.Width, y);
            };
            return header;
        }

        private Control BuildNav()
        {
            var nav = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(16, 12, 16, 0), BackColor = Theme.Bg, WrapContents = false };
            _navBasic = Theme.MakeButton("⚙ 基本設定(&1)", Theme.ButtonKind.Primary, "基本設定頁");
            _navDiag = Theme.MakeButton("🔍 進階診斷(&2)", Theme.ButtonKind.Secondary, "進階診斷頁");
            _navBasic.TabIndex = 0;
            _navDiag.TabIndex = 1;
            _navBasic.Click += (s, e) => ShowPage(false);
            _navDiag.Click += (s, e) => ShowPage(true);
            nav.Controls.Add(_navBasic);
            nav.Controls.Add(_navDiag);
            return nav;
        }

        private void ShowPage(bool diagnostic)
        {
            _pageBasic.Visible = !diagnostic;
            _pageDiag.Visible = diagnostic;
            Theme.Style(_navBasic, diagnostic ? Theme.ButtonKind.Secondary : Theme.ButtonKind.Primary);
            Theme.Style(_navDiag, diagnostic ? Theme.ButtonKind.Primary : Theme.ButtonKind.Secondary);

            if (diagnostic)
            {
                _diagnosticUpdateTimer?.Start();     // 只在診斷頁開著時批次刷 TextBox
            }
            else
            {
                _diagnosticUpdateTimer?.Stop();
                lock (_diagnosticBufferLock) { _diagnosticBuffer.Clear(); }  // 避免下次切換時倒出過時資訊
            }
        }

        // ── 基本設定頁 ──

        private Panel BuildBasicPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // 左欄:預覽（吃掉剩餘高度）＋ 快速啟動
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Bg, Margin = new Padding(0) };
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));   // 沒設的話欄寬＝子控制項偏好寬度,會被長文字撐到溢出
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var previewCard = Theme.MakeCard(12);
            previewCard.Dock = DockStyle.Fill;
            _preview = new SimpleTouchpadPreview { Dock = DockStyle.Fill, TabStop = false };
            previewCard.Controls.Add(_preview);
            left.Controls.Add(previewCard, 0, 0);
            left.Controls.Add(BuildQuickStartCard(), 0, 1);

            grid.Controls.Add(left, 0, 0);
            grid.Controls.Add(BuildSettingsCard(), 1, 0);
            page.Controls.Add(grid);
            return page;
        }

        private Control BuildQuickStartCard()
        {
            var card = Theme.MakeCard();
            card.Dock = DockStyle.Fill;
            card.AutoSize = true;
            card.Margin = new Padding(0, 0, Theme.Gap, 0);

            // 左欄:按鈕＋三個勾選（不會換行,高度穩定）;右欄:說明文字（Dock Fill 依寬度換行,rowspan 蓋滿）
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, AutoSize = true, BackColor = Color.Transparent };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 4; i++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _quickStartButton = Theme.MakeButton("▶ 開始監控(&S)", Theme.ButtonKind.Primary, "開始監控", 220);
            _quickStartButton.Height = 52;   // 主要動作,再大一點
            _quickStartButton.Font = new Font("Segoe UI", 12.5F, FontStyle.Bold);
            _quickStartButton.TabIndex = 10;
            _quickStartButton.Click += QuickStartButton_Click;

            CheckBox MakeCheck(string text, string accessible, int tab) => new CheckBox
            {
                Text = text, Font = Theme.Body, ForeColor = Theme.Text, AutoSize = true,
                Margin = new Padding(4, 6, 0, 0), AccessibleName = accessible, TabIndex = tab, Cursor = Cursors.Hand,
            };
            _enableCheckBox = MakeCheck("啟用輕觸右鍵功能(&E)", "啟用輕觸右鍵功能", 11);
            _enableCheckBox.CheckedChanged += EnableCheckBox_Changed;

            _autoStartCheckBox = MakeCheck("開機自動啟動(&A)", "開機自動啟動", 12);
            _autoStartCheckBox.CheckedChanged += AutoStartCheckBox_Changed;

            _autoMonitorCheckBox = MakeCheck("啟動後自動開始監控(&M)", "啟動後自動開始監控", 13);
            _autoMonitorCheckBox.Checked = _settings.AutoStartMonitoring;
            _autoMonitorCheckBox.CheckedChanged += (s, e) => { _settings.AutoStartMonitoring = _autoMonitorCheckBox.Checked; _settings.Save(); };

            var hint = Theme.MakeLabel(
                "💡 右下角「輕觸」＝右鍵\n「按住不動 0.6 秒再移動」＝拖曳\n其他區域維持 Windows 原生（移動、捲動、雙指手勢）\n\n" +
                "「開機自動啟動」需要以系統管理員身分執行本程式才能登記（勾選＝已在工作排程器登記）。",
                Theme.Small, Theme.TextMuted);
            hint.Margin = new Padding(Theme.Gap, 4, 0, 0);
            hint.Dock = DockStyle.Fill;   // Percent 欄＋Dock Fill 才會依欄寬換行

            grid.Controls.Add(_quickStartButton, 0, 0);
            grid.Controls.Add(_enableCheckBox, 0, 1);
            grid.Controls.Add(_autoStartCheckBox, 0, 2);
            grid.Controls.Add(_autoMonitorCheckBox, 0, 3);
            grid.Controls.Add(hint, 1, 0);
            grid.SetRowSpan(hint, 4);
            card.Controls.Add(grid);
            return card;
        }

        private Control BuildSettingsCard()
        {
            var card = Theme.MakeCard();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0);
            card.AutoScroll = true;   // 小螢幕（1366×768@125%）放不下三列時出直捲軸,不能把最後一列裁掉

            // Dock Top＋AutoSize 才能捲;寬度由 hint 的 MaximumSize 綁住（見 BuildSettingRow）,不會把卡片撐寬
            var stack = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, BackColor = Color.Transparent };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            stack.Controls.Add(Theme.MakeLabel("區域設定", Theme.H2));
            stack.Controls.Add(Theme.MakeLabel("調整後立即生效,不需重新啟動", Theme.Small, Theme.TextMuted));

            // 右鍵區域起始位置
            stack.Controls.Add(BuildSettingRow(
                "右鍵區域起始位置", "從左邊算起;調小＝右鍵區變大",
                40, 92, (decimal)(_settings.RightZoneStartX * 100), 0, 10, 20,
                out _zoneStartNumeric, out _zoneStartSlider,
                v =>
                {
                    _settings.RightZoneStartX = (double)v / 100; ScheduleSave();
                    if (_monitor?.TapDetector != null) _monitor.TapDetector.RightZoneStartX = (double)v / 100;
                    if (_preview != null) _preview.RightZoneStartX = (float)(v / 100);
                }));

            // 上邊界
            stack.Controls.Add(BuildSettingRow(
                "右鍵區域上邊界位置", "從上面算起;調小＝右鍵區變高",
                10, 90, (decimal)(_settings.VerticalSplitY * 100), 0, 10, 30,
                out _verticalSplitNumeric, out _verticalSplitSlider,
                v =>
                {
                    _settings.VerticalSplitY = (double)v / 100; ScheduleSave();
                    if (_monitor?.TapDetector != null) _monitor.TapDetector.VerticalSplitY = (double)v / 100;
                    if (_preview != null) _preview.VerticalSplitY = (float)(v / 100);
                }));

            // 靈敏度（滑桿 10 倍換算:10–50 ↔ 1.0–5.0）
            stack.Controls.Add(BuildSettingRow(
                "輕觸靈敏度", "允許的手指移動量;手會抖就調大",
                1, 5, (decimal)(_settings.TapDistanceThreshold * 100), 1, 5, 40,
                out _sensitivityNumeric, out _sensitivitySlider,
                v =>
                {
                    _settings.TapDistanceThreshold = (double)v / 100; ScheduleSave();
                    if (_monitor?.TapDetector != null) _monitor.TapDetector.TapDistanceThreshold = (double)v / 100;
                }));

            card.Controls.Add(stack);
            return card;
        }

        /// <summary>
        /// 一列設定:標題＋數字框＋%（第一行）、滑桿（第二行）、說明（第三行）。數字框與滑桿雙向同步。
        /// decimals=1 時滑桿以 10 倍整數表示（靈敏度用）。
        /// </summary>
        private Control BuildSettingRow(string title, string hint, int min, int max, decimal value, int decimals, int tick, int tabIndex,
                                        out NumericUpDown numeric, out TrackBar slider, Action<decimal> apply)
        {
            int scale = decimals == 1 ? 10 : 1;
            var row = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, RowCount = 3, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, Theme.Gap, 0, 0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var titleLabel = Theme.MakeLabel(title, Theme.BodyBold);
            titleLabel.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

            var num = new NumericUpDown
            {
                Minimum = min, Maximum = max, Value = value,
                DecimalPlaces = decimals, Increment = decimals == 1 ? 0.1m : 1m,
                Font = Theme.Body, Width = 84,
                TextAlign = HorizontalAlignment.Right,
                BackColor = Theme.CardAlt, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = title, TabIndex = tabIndex,
                Anchor = AnchorStyles.Right,
            };
            var pct = Theme.MakeLabel("%", Theme.Body, Theme.TextMuted);
            pct.Anchor = AnchorStyles.Left;
            pct.Margin = new Padding(4, 0, 0, 0);

            var bar = new TrackBar
            {
                Minimum = min * scale, Maximum = max * scale, Value = (int)(value * scale),
                TickFrequency = tick, SmallChange = 1, LargeChange = scale,
                Dock = DockStyle.Fill, Height = 45,
                BackColor = Theme.Card,
                AccessibleName = title + " 滑桿", TabIndex = tabIndex + 1,
                Margin = new Padding(0, 4, 0, 0),
            };
            var hintLabel = Theme.MakeLabel(hint, Theme.Small, Theme.TextMuted);
            // 說明文字最大寬度跟著列寬,才會換行而不是把 AutoScroll 容器撐寬
            row.SizeChanged += (s, e) => hintLabel.MaximumSize = new Size(Math.Max(60, row.ClientSize.Width), 0);

            // 雙向同步（防遞迴:值相同就不寫回）
            num.ValueChanged += (s, e) =>
            {
                int sv = (int)(num.Value * scale);
                if (bar.Value != sv) bar.Value = sv;
                apply(num.Value);
            };
            bar.ValueChanged += (s, e) =>
            {
                decimal nv = (decimal)bar.Value / scale;
                if (num.Value != nv) num.Value = nv;
            };

            row.Controls.Add(titleLabel, 0, 0);
            row.Controls.Add(num, 1, 0);
            row.Controls.Add(pct, 2, 0);
            row.Controls.Add(bar, 0, 1); row.SetColumnSpan(bar, 3);
            row.Controls.Add(hintLabel, 0, 2); row.SetColumnSpan(hintLabel, 3);

            numeric = num; slider = bar;
            return row;
        }

        // ── 進階診斷頁 ──

        private Panel BuildDiagnosticPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Bg };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // 即時狀態
            var statusCard = Theme.MakeCard();
            statusCard.Dock = DockStyle.Top;
            statusCard.AutoSize = true;
            statusCard.Margin = new Padding(0, 0, 0, Theme.Gap);
            var statusStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, BackColor = Color.Transparent };
            statusStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            // 這兩個 Label 每筆 HID report（~125/秒）都會改字:固定尺寸、不 AutoSize,改字只重繪不重排版
            _coordinateLabel = Theme.MakeLabel("📍 即時座標: 等待資料…", Theme.MonoBig, Theme.Accent);
            _coordinateLabel.AutoSize = false; _coordinateLabel.Height = 26; _coordinateLabel.Dock = DockStyle.Top;
            _hidInfoLabel = Theme.MakeLabel("🔧 HID 資訊: 未初始化", Theme.Mono, Theme.TextMuted);
            _hidInfoLabel.AutoSize = false; _hidInfoLabel.Height = 22; _hidInfoLabel.Dock = DockStyle.Top;
            var modeLine = Theme.MakeLabel("解析模式:HID API — 自動適應各廠商觸控板（ELAN、Lite-On…）,動態偵測座標範圍", Theme.Small, Theme.TextMuted);
            modeLine.Dock = DockStyle.Fill;   // Percent 欄＋Dock Fill → 依卡片寬換行
            statusStack.Controls.AddRange(new Control[] { _coordinateLabel, _hidInfoLabel, modeLine });
            statusCard.Controls.Add(statusStack);

            // 日誌
            var logCard = Theme.MakeCard(8);
            logCard.Dock = DockStyle.Fill;
            logCard.BackColor = Theme.LogBg;
            logCard.Margin = new Padding(0, 0, 0, Theme.Gap);
            _diagnosticTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = Theme.Mono,
                BackColor = Theme.LogBg,
                ForeColor = Theme.LogText,
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                MaxLength = 0,
                AccessibleName = "診斷日誌",
                TabIndex = 50,
            };
            logCard.Controls.Add(_diagnosticTextBox);

            // 按鈕列
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Theme.Bg, Margin = new Padding(0) };
            var hardwareTestButton = Theme.MakeButton("🔍 硬體檢測(&H)", Theme.ButtonKind.Primary, "硬體檢測");
            hardwareTestButton.TabIndex = 60;
            hardwareTestButton.Click += HardwareTestButton_Click;

            _exportButton = Theme.MakeButton("💾 匯出診斷資料(&X)", Theme.ButtonKind.Secondary, "匯出診斷資料");
            _exportButton.TabIndex = 61;
            _exportButton.Click += ExportButton_Click;

            _clearButton = Theme.MakeButton("🗑 清除畫面(&C)", Theme.ButtonKind.Secondary, "清除診斷畫面");
            _clearButton.TabIndex = 62;
            _clearButton.Click += (s, e) => _diagnosticTextBox.Clear();

            var openLogButton = Theme.MakeButton("📂 開啟日誌資料夾(&L)", Theme.ButtonKind.Secondary, "開啟日誌資料夾");
            openLogButton.TabIndex = 63;
            openLogButton.Click += OpenLogFolder_Click;

            buttons.Controls.AddRange(new Control[] { hardwareTestButton, _exportButton, _clearButton, openLogButton });

            grid.Controls.Add(statusCard, 0, 0);
            grid.Controls.Add(logCard, 0, 1);
            grid.Controls.Add(buttons, 0, 2);
            page.Controls.Add(grid);
            return page;
        }

        private void OpenLogFolder_Click(object sender, EventArgs e)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Utils.FileLogger.LogDirectory);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Utils.FileLogger.LogDirectory) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"無法開啟日誌資料夾:\n{Utils.FileLogger.LogDirectory}\n\n{ex.Message}", "日誌", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void InitializeDiagnosticUpdateTimer()
        {
            _diagnosticUpdateTimer = new System.Windows.Forms.Timer();
            _diagnosticUpdateTimer.Interval = DIAGNOSTIC_UPDATE_INTERVAL_MS;
            _diagnosticUpdateTimer.Tick += DiagnosticUpdateTimer_Tick;
            // 預設不啟動,只在切換到診斷頁面時啟動
        }

        /// <summary>批次把緩衝區倒進 TextBox,並裁到最後 DIAGNOSTIC_MAX_LINES 行。</summary>
        private void DiagnosticUpdateTimer_Tick(object sender, EventArgs e)
        {
            lock (_diagnosticBufferLock)
            {
                if (_diagnosticBuffer.Length > 0)
                {
                    _diagnosticTextBox.AppendText(_diagnosticBuffer.ToString());
                    _diagnosticBuffer.Clear();

                    // Lines 屬性每次存取都會建立新陣列,先數換行再決定要不要裁
                    var text = _diagnosticTextBox.Text;
                    int lineCount = 1;
                    for (int i = 0; i < text.Length; i++)
                        if (text[i] == '\n') lineCount++;

                    if (lineCount > DIAGNOSTIC_MAX_LINES)
                    {
                        var lines = _diagnosticTextBox.Lines;
                        var startIndex = lines.Length - DIAGNOSTIC_MAX_LINES;
                        _diagnosticTextBox.Text = string.Join(Environment.NewLine, lines.Skip(startIndex));
                        _diagnosticTextBox.ClearUndo();
                    }

                    _diagnosticTextBox.SelectionStart = _diagnosticTextBox.Text.Length;
                    _diagnosticTextBox.ScrollToCaret();
                }
            }
        }

        // ── 狀態欄 ──

        private Control BuildStatusBar()
        {
            _statusStrip = new StatusStrip
            {
                BackColor = Theme.Card,
                ForeColor = Theme.Text,
                Font = Theme.Body,
                SizingGrip = false,
                Padding = new Padding(12, 4, 12, 4),
            };
            _statusLabel = new ToolStripStatusLabel { Text = "就緒", Spring = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text };
            _modeLabel = new ToolStripStatusLabel { Text = "HID API 模式", ForeColor = Theme.TextMuted, BorderSides = ToolStripStatusLabelBorderSides.Left };
            _statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel, _modeLabel });
            return _statusStrip;
        }

        // ───────────────────────────── 事件處理 ─────────────────────────────

        private void QuickStartButton_Click(object sender, EventArgs e)
        {
            if (_monitor == null)
                return;

            if (_quickStartButton.Text.Contains("開始"))
            {
                if (_monitor.Start())
                {
                    _quickStartButton.Text = "■ 停止監控(&S)";
                    _quickStartButton.AccessibleName = "停止監控";
                    Theme.Style(_quickStartButton, Theme.ButtonKind.Danger);
                    _enableCheckBox.Checked = true;
                    _statusLabel.Text = "監控中…";
                }
                else
                {
                    // 還原勾選,否則 checkbox 顯示「已啟用」但沒在監控,再取消勾選也不會有反應
                    // （此時按鈕文字仍是「開始」,CheckedChanged 不會遞迴進來）
                    _enableCheckBox.Checked = false;
                    _statusLabel.Text = "❌ 啟動失敗";
                    // 只有使用者親手按的才彈框;開機自動開始（sender 是 Form）失敗時登入瞬間彈 modal 很擾人,狀態列提示就好
                    if (sender != this)
                        MessageBox.Show(this, "HID API 初始化失敗!\n\n請檢查:\n1. 觸控板是否正常工作\n2. 進階診斷頁的訊息\n3. 是否有其他程式佔用觸控板",
                                        "啟動失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    else
                        _statusLabel.Text = "❌ 自動開始監控失敗（觸控板可能尚未就緒）,請按「開始監控」重試";
                }
            }
            else
            {
                _monitor.Stop();
                _quickStartButton.Text = "▶ 開始監控(&S)";
                _quickStartButton.AccessibleName = "開始監控";
                Theme.Style(_quickStartButton, Theme.ButtonKind.Primary);
                _enableCheckBox.Checked = false;
                _statusLabel.Text = "已停止";
            }
        }

        private void ScheduleSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>開機自啟:登記／取消工作排程。失敗（多半是沒管理員）就把勾選還原並說明。</summary>
        private async void AutoStartCheckBox_Changed(object sender, EventArgs e)
        {
            if (_suppressAutoStartEvent) return;
            bool want = _autoStartCheckBox.Checked;
            // schtasks 在網域／AV 環境可能卡好幾秒:丟背景,期間停用勾選框,不凍結 UI 也不會被連點
            _autoStartCheckBox.Enabled = false;
            _statusLabel.Text = want ? "登記開機自動啟動中…" : "取消開機自動啟動中…";
            string error = "";
            bool ok = false;
            try
            {
                ok = await System.Threading.Tasks.Task.Run(() =>
                    want ? Utils.AutoStart.Enable(_settings, out error) : Utils.AutoStart.Disable(_settings, out error));
            }
            catch (Exception ex) { error = ex.Message; }
            if (IsDisposed) return;
            _autoStartCheckBox.Enabled = true;

            if (!ok)
            {
                _suppressAutoStartEvent = true;
                _autoStartCheckBox.Checked = !want;
                _suppressAutoStartEvent = false;
                _statusLabel.Text = want ? "登記開機自動啟動失敗" : "取消開機自動啟動失敗";
                MessageBox.Show(this,
                    (want ? "無法登記開機自動啟動。" : "無法取消開機自動啟動。") +
                    "\n\n最常見的原因是沒有以「系統管理員身分」執行本程式——請右鍵 exe → 以系統管理員身分執行,再勾一次。\n\n" +
                    "詳細:" + (string.IsNullOrWhiteSpace(error) ? "(無)" : error.Trim()),
                    "開機自動啟動", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (want)
            {
                _statusLabel.Text = "已登記開機自動啟動";
                if (Utils.AutoStart.IsInRiskyFolder())
                    MessageBox.Show(this,
                        "已登記:下次登入 Windows 時會自動啟動本程式（不會再問 UAC）。\n\n" +
                        "⚠ 目前 exe 放在「下載」或「桌面」。兩個問題:(1) 之後把檔案搬走,開機就找不到它;" +
                        "(2) 排程是以最高權限執行這個檔案,放在一般使用者可寫的位置等於把管理員權限交出去。\n\n" +
                        "建議把 exe 搬到 " + Utils.AutoStart.RecommendedFolder + " 之後再勾一次。",
                        "開機自動啟動", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                _statusLabel.Text = "已取消開機自動啟動";
            }
        }

        private void EnableCheckBox_Changed(object sender, EventArgs e)
        {
            if (_enableCheckBox.Checked && _quickStartButton.Text.Contains("開始"))
                QuickStartButton_Click(sender, e);
            else if (!_enableCheckBox.Checked && _quickStartButton.Text.Contains("停止"))
                QuickStartButton_Click(sender, e);
        }

        /// <summary>切換淺／深色:寫入設定 → 開新實例（/restart 跳過歡迎視窗;監控中就帶 /monitor 讓它自動恢復）→ 自己結束。</summary>
        private void ThemeButton_Click(object sender, EventArgs e)
        {
            _settings.Theme = Theme.Dark ? "light" : "dark";
            _settings.Save();
            bool monitoring = _quickStartButton.Text.Contains("停止");
            Utils.FileLogger.Write("🎨 切換外觀為 " + _settings.Theme + ",重新啟動" + (monitoring ? "（恢復監控）" : ""));
            try
            {
                string args = Utils.AutoStart.RestartArg + (monitoring ? " " + Utils.AutoStart.ResumeMonitorArg : "");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Utils.AutoStart.ExePath, args) { UseShellExecute = true });
                Close();   // FormClosing 存設定、釋放鉤子與 Raw Input;新實例會等我們的 Mutex 釋放
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "無法重新啟動,請手動關閉再開啟程式即可套用外觀。\n\n" + ex.Message, "外觀", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void UpdateButton_Click(object sender, EventArgs e)
        {
            _updateButton.Enabled = false;
            _updateButton.Text = "檢查中…";
            try
            {
                var r = await Utils.UpdateChecker.CheckAsync();
                Utils.FileLogger.Write($"🔄 檢查更新: 目前 {Utils.UpdateChecker.CurrentTag}, 最新 {r.LatestTag}, 有新版={r.HasNewer}");
                if (IsDisposed) return; // await 期間視窗被關掉,不能再拿 this 當 owner
                if (!r.HasNewer)
                {
                    MessageBox.Show(this, $"已是最新版本 {Utils.UpdateChecker.CurrentTag}。",
                                    "檢查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (MessageBox.Show(this,
                             $"有新版本 {r.LatestTag}（目前 {Utils.UpdateChecker.CurrentTag}）。\n\n要開啟下載頁嗎？\n下載後請先關閉這個程式,再以系統管理員身分執行新的 exe。",
                             "檢查更新", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Utils.UpdateChecker.ReleasesPageUrl) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Utils.FileLogger.Write("🔄 檢查更新失敗: " + ex.Message);
                if (IsDisposed) return;
                MessageBox.Show(this, $"無法檢查更新（不影響本體功能）:\n{ex.Message}\n\n可手動前往:\n{Utils.UpdateChecker.ReleasesPageUrl}",
                                "檢查更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (!IsDisposed)
                {
                    _updateButton.Text = "🔄 檢查更新(&U)";
                    _updateButton.Enabled = true;
                }
            }
        }

        private void ExportButton_Click(object sender, EventArgs e)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "文字檔案 (*.txt)|*.txt",
                    FileName = $"觸控板診斷資料_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    System.IO.File.WriteAllText(dialog.FileName, _diagnosticTextBox.Text);
                    MessageBox.Show(this, $"診斷資料已匯出至:\n{dialog.FileName}", "匯出成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"匯出失敗: {ex.Message}", "錯誤",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>硬體檢測:把 HID Value Caps 結構列進診斷畫面（監控停止後也能看,PreparsedData 不會被 Stop 釋放）。</summary>
        private void HardwareTestButton_Click(object sender, EventArgs e)
        {
            if (_monitor == null)
                return;

            try
            {
                string report = _monitor.GetHardwareDiagnosticReport();

                if (string.IsNullOrEmpty(report))
                {
                    _diagnosticTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] ⚠️ 無法獲取硬體診斷報告\r\n");
                }
                else
                {
                    _diagnosticTextBox.Clear();
                    _diagnosticTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 🔍 硬體檢測報告\r\n");
                    _diagnosticTextBox.AppendText("═══════════════════════════════════════\r\n\r\n");
                    _diagnosticTextBox.AppendText(report);
                    _diagnosticTextBox.SelectionStart = 0;
                    _diagnosticTextBox.ScrollToCaret();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"硬體檢測失敗:\n\n{ex.Message}", "錯誤",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _diagnosticTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] ❌ 硬體檢測異常: {ex.Message}\r\n");
            }
        }

        // ───────────────────────────── 監控 ─────────────────────────────

        private void InitializeMonitor()
        {
            _monitor = new TouchpadMonitor();
            _monitor.TouchEvent += Monitor_TouchEvent;
            _monitor.TapDetected += Monitor_TapDetected;
            _monitor.StatusUpdate += Monitor_StatusUpdate;

            // 設定檔（含滑桿沒有的進階參數）套進觸控核心;預覽跟 detector 對齊（含邊緣保護帶）
            _settings.ApplyTo(_monitor);
            if (_preview != null)
            {
                _preview.RightZoneStartX = (float)_monitor.TapDetector.RightZoneStartX;
                _preview.VerticalSplitY = (float)_monitor.TapDetector.VerticalSplitY;
                _preview.EdgeLeft = (float)_monitor.TapDetector.EdgeLeftRatio;
                _preview.EdgeRight = (float)_monitor.TapDetector.EdgeRightRatio;
                _preview.EdgeTop = (float)_monitor.TapDetector.EdgeTopRatio;
            }
        }

        // A3: 加入 IsDisposed / IsHandleCreated 守衛,防止 Form 關閉後
        //     事件 handler 仍呼叫 BeginInvoke 導致 ObjectDisposedException
        private void InvokeIfRequired(Action action)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(action);
                }
                catch (ObjectDisposedException) { /* Form 正在關閉,丟棄 */ }
                catch (InvalidOperationException) { /* Handle 已銷毀 */ }
            }
            else
            {
                action();
            }
        }

        private void Monitor_TouchEvent(object sender, TouchEventArgs e)
        {
            InvokeIfRequired(() =>
            {
                // 這裡跟 WM_INPUT／觸控結束判定同一條執行緒:看不見的頁面就不要花時間重繪或重排
                if (_pageBasic.Visible)
                {
                    _preview?.UpdateTouch(e.X, e.Y);
                    if (e.IsTouching)
                        _preview?.ShowTapIndicator(e.X, e.Y);
                }
                if (!_pageDiag.Visible)
                    return;

                _coordinateLabel.Text = string.Format(
                    "📍 X={0:F3} ({1:F1}%)  Y={2:F3} ({3:F1}%)  觸控={4}  事件#{5}",
                    e.X, e.X * 100, e.Y, e.Y * 100, e.IsTouching, e.EventNumber);

                if (_monitor.HidParser != null)
                {
                    _hidInfoLabel.Text = string.Format(
                        "🔧 Confidence={0}  ContactID={1}  ScanTime={2}",
                        _monitor.HidParser.LastConfidenceBit,
                        _monitor.HidParser.LastContactId,
                        _monitor.HidParser.LastScanTime);
                }

                // 此路徑每筆觸控事件都會進來（~125 筆/秒）,停在基本設定頁時 Timer 不 drain,所以這裡也要套上限
                if (!string.IsNullOrEmpty(e.DebugInfo))
                {
                    lock (_diagnosticBufferLock)
                    {
                        // 丟掉前半段,不整個 Clear——裡面還夾著 Monitor_StatusUpdate 寫進來的狀態訊息
                        if (_diagnosticBuffer.Length > DIAGNOSTIC_BUFFER_MAX_SIZE)
                            _diagnosticBuffer.Remove(0, _diagnosticBuffer.Length / 2);
                        _diagnosticBuffer.AppendLine(e.DebugInfo);
                    }
                }
            });
        }

        private void Monitor_TapDetected(object sender, TapEventArgs e)
        {
            InvokeIfRequired(() =>
            {
                string action = (e.Zone == TapZone.BottomRight) ? "右鍵點擊" : "左鍵點擊";
                _statusLabel.Text = $"{action} [{e.Zone}] 座標:({e.Position.X:F2}, {e.Position.Y:F2})";
            });
        }

        private void Monitor_StatusUpdate(object sender, string message)
        {
            InvokeIfRequired(() =>
            {
                _statusLabel.Text = message;

                // 批次更新由 Timer 處理,避免高頻事件導致 UI 卡頓
                lock (_diagnosticBufferLock)
                {
                    var now = DateTime.Now;
                    _diagnosticBuffer.Append('[')
                                     .Append(now.Hour.ToString("D2"))
                                     .Append(':')
                                     .Append(now.Minute.ToString("D2"))
                                     .Append(':')
                                     .Append(now.Second.ToString("D2"))
                                     .Append("] ")
                                     .AppendLine(message);

                    if (_diagnosticBuffer.Length > DIAGNOSTIC_BUFFER_MAX_SIZE)
                    {
                        _diagnosticBuffer.Clear();
                        _diagnosticBuffer.Append('[')
                                         .Append(now.Hour.ToString("D2"))
                                         .Append(':')
                                         .Append(now.Minute.ToString("D2"))
                                         .Append(':')
                                         .Append(now.Second.ToString("D2"))
                                         .AppendLine("] ⚠️ 緩衝區已滿,已清空");
                    }
                }
            });
        }
    }
}
