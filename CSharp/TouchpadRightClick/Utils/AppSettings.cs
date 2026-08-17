using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TouchpadRightClick.Core;

namespace TouchpadRightClick.Utils
{
    /// <summary>
    /// 使用者設定:%LOCALAPPDATA%\TouchpadTapRight\config.json。
    /// 「基本」三項是主畫面滑桿（改了就存）;「進階」是沒實機沒辦法校準的門檻,開出來讓社群改文字檔就能調,
    /// 調到對的值回報後再改 Core 的 DEFAULT_*。刪掉檔案＝回預設。
    /// 讀檔失敗（手改壞了）:壞檔改名 config.json.bad 保留、用預設值跑,而且**這次執行不再寫回**——
    /// 否則使用者辛苦調的參數會在關閉時被預設值覆蓋掉。
    /// ponytail: 純 POCO＋System.Text.Json,沒有版本升級機制——欄位只增不改名,舊檔缺的欄位就是預設值
    /// </summary>
    public sealed class AppSettings
    {
        public static readonly string FilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FileLogger.AppFolderName, "config.json");

        /// <summary>唯讀:每次都輸出最新說明,使用者改了也不會被讀回（反序列化忽略唯讀屬性）。</summary>
        [JsonPropertyName("_說明")]
        public string Help =>
            "改完存檔、重新啟動程式生效（基本三項在主畫面改會自動存回這裡）。比例都是 0.0–1.0（0.06＝觸控板的 6%）,時間單位毫秒。刪掉本檔即回到預設值。檔案格式壞掉時會被改名成 config.json.bad 並改用預設值。";

        // ── 基本（主畫面滑桿）──
        public double RightZoneStartX { get; set; } = TapZoneDetector.DEFAULT_RIGHT_ZONE_START_X;
        public double VerticalSplitY { get; set; } = TapZoneDetector.DEFAULT_VERTICAL_SPLIT_Y;
        public double TapDistanceThreshold { get; set; } = TapZoneDetector.DEFAULT_TAP_DISTANCE_THRESHOLD;

        // ── 外觀 ──
        /// <summary>"system"（跟 Windows 應用程式模式,預設）/ "light" / "dark";改了重開程式生效</summary>
        public string Theme { get; set; } = "system";

        // ── 行為 ──
        /// <summary>程式啟動後自動按下「開始監控」（配合開機自啟才有意義）</summary>
        public bool AutoStartMonitoring { get; set; } = false;
        /// <summary>登記開機自啟時的 exe 路徑;啟動時跟目前路徑比,不同就重登（不解析 schtasks 輸出,避免碼頁問題）</summary>
        public string AutoStartExePath { get; set; }

        // ── 進階（沒實機沒辦法校準的門檻,社群調）──
        public int TapTimeMs { get; set; } = TapZoneDetector.DEFAULT_TAP_TIME_MS;
        public int DragHoldMs { get; set; } = TapZoneDetector.DEFAULT_DRAG_HOLD_THRESHOLD_MS;
        public double DragMoveTolerance { get; set; } = TapZoneDetector.DEFAULT_DRAG_MOVE_TOLERANCE;
        public int TouchEndTimeoutMs { get; set; } = TouchpadMonitor.DEFAULT_TOUCH_END_TIMEOUT_MS;
        public double EdgeLeftRatio { get; set; } = TapZoneDetector.DEFAULT_EDGE_LEFT_RATIO;
        public double EdgeRightRatio { get; set; } = TapZoneDetector.DEFAULT_EDGE_RIGHT_RATIO;
        public double EdgeTopRatio { get; set; } = TapZoneDetector.DEFAULT_EDGE_TOP_RATIO;

        /// <summary>true＝這次啟動時檔案讀不進來（已改名 .bad）,本次執行不寫回,免得覆蓋使用者的東西。</summary>
        [JsonIgnore]
        public bool LoadFailed { get; private set; }

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,  // 中文說明不要變 \uXXXX
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,   // 手打小寫鍵也吃
        };

        public static AppSettings Load()
        {
            if (!File.Exists(FilePath))
                return new AppSettings();
            try
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
                s.Clamp();
                FileLogger.Write("⚙ 已載入設定 " + FilePath);
                return s;
            }
            catch (Exception ex)
            {
                FileLogger.Write("⚙ 設定檔讀取失敗,改用預設值並保留原檔為 config.json.bad: " + ex.Message);
                try { File.Copy(FilePath, FilePath + ".bad", true); } catch { }
                return new AppSettings { LoadFailed = true };
            }
        }

        /// <summary>寫檔（UTF-8 含 BOM,記事本／PowerShell 5 才不會亂碼;先寫 .tmp 再換名,斷電不會留半個檔）。讀檔失敗那次不寫。</summary>
        public void Save()
        {
            if (LoadFailed) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options), new UTF8Encoding(true));
                File.Move(tmp, FilePath, true);
            }
            catch (Exception ex)
            {
                FileLogger.Write("⚙ 設定檔寫入失敗: " + ex.Message);
            }
        }

        /// <summary>把設定套到觸控核心。</summary>
        public void ApplyTo(TouchpadMonitor monitor)
        {
            var d = monitor.TapDetector;
            d.RightZoneStartX = RightZoneStartX;
            d.VerticalSplitY = VerticalSplitY;
            d.TapDistanceThreshold = TapDistanceThreshold;
            d.TapTimeThreshold = TapTimeMs;
            d.DragHoldThresholdMs = DragHoldMs;
            d.DragMoveTolerance = DragMoveTolerance;
            d.EdgeLeftRatio = EdgeLeftRatio;
            d.EdgeRightRatio = EdgeRightRatio;
            d.EdgeTopRatio = EdgeTopRatio;
            monitor.TouchEndTimeoutMs = TouchEndTimeoutMs;
        }

        /// <summary>手改檔案可能亂填:夾到合理範圍（與 UI 滑桿範圍一致）,寧可保守也不要讓程式進入奇怪狀態。</summary>
        private void Clamp()
        {
            if (Theme != "light" && Theme != "dark") Theme = "system";
            RightZoneStartX = Math.Clamp(RightZoneStartX, 0.40, 0.92);
            VerticalSplitY = Math.Clamp(VerticalSplitY, 0.10, 0.90);
            TapDistanceThreshold = Math.Clamp(TapDistanceThreshold, 0.01, 0.05);
            TapTimeMs = Math.Clamp(TapTimeMs, 100, 1000);
            DragHoldMs = Math.Clamp(DragHoldMs, 200, 2000);
            DragMoveTolerance = Math.Clamp(DragMoveTolerance, 0.01, 0.30);
            TouchEndTimeoutMs = Math.Clamp(TouchEndTimeoutMs, 30, 500);
            EdgeLeftRatio = Math.Clamp(EdgeLeftRatio, 0.0, 0.20);
            EdgeRightRatio = Math.Clamp(EdgeRightRatio, 0.0, 0.20);
            EdgeTopRatio = Math.Clamp(EdgeTopRatio, 0.0, 0.30);
        }
    }
}
