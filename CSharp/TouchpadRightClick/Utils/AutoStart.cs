using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TouchpadRightClick.Utils
{
    /// <summary>
    /// 開機自啟＝在工作排程器登記一筆「使用者登入時、以最高權限、執行這個 exe /autostart」。
    /// 為什麼不用登錄檔 Run 鍵:那條路用一般權限開程式,我們需要管理員（滑鼠鉤子／對管理員視窗送輸入）,
    /// 會每次跳 UAC 或被靜默降權;排程器登記時授權一次,之後每次登入都不再問。
    /// 免安裝:整個機制只有這一筆登記,取消勾選就刪掉,exe 刪掉就乾淨。
    /// 安全提醒:HIGHEST＋ONLOGON 指向的 exe 若放在一般使用者可寫的位置,等於把管理員權限交給任何能改那個檔的程式,
    /// 所以建議放 C:\Program Files\TouchpadTapRight\（一般使用者寫不進去）。
    /// ponytail: 直接呼叫 schtasks.exe（Windows 內建）,不引 Task Scheduler COM/NuGet;只看 exit code 不解析在地化訊息;
    ///           登記過的路徑存在 AppSettings 裡比對,不解析 schtasks 的輸出（碼頁不可靠）。
    /// </summary>
    public static class AutoStart
    {
        public const string TaskName = "TouchpadTapRight";
        public const string AutoStartArg = "/autostart";     // 排程啟動時帶這個參數:跳過歡迎視窗、失敗不彈框
        public const string RestartArg = "/restart";         // 切換外觀後自己重啟:跳過歡迎視窗
        public const string ResumeMonitorArg = "/monitor";   // 重啟前正在監控 → 啟動後自動按「開始監控」
        public const string RecommendedFolder = @"C:\Program Files\TouchpadTapRight\";

        public static string ExePath => Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath;

        public static bool HasArg(string arg) =>
            Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, arg, StringComparison.OrdinalIgnoreCase));

        /// <summary>目前這次執行是不是由排程器帶起來的。</summary>
        public static bool LaunchedByScheduler => HasArg(AutoStartArg);

        /// <summary>不該跳歡迎視窗的啟動:排程器帶起、或切換外觀後的自我重啟。</summary>
        public static bool SkipWelcome => HasArg(AutoStartArg) || HasArg(RestartArg);

        /// <summary>exe 是否在一般使用者可寫、之後又很可能被搬走的地方（下載資料夾／桌面）→ 勾選時提醒。</summary>
        public static bool IsInRiskyFolder()
        {
            try
            {
                string exe = ExePath;
                foreach (var dir in new[] { DownloadsFolder(), Environment.GetFolderPath(Environment.SpecialFolder.Desktop) })
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    string d = dir.TrimEnd('\\') + "\\";
                    if (exe.StartsWith(d, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }

        private static string DownloadsFolder()
        {
            // SpecialFolder 沒有 Downloads;OneDrive／改位置的下載夾要讀 Shell Folders 的 GUID
            try
            {
                var v = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders",
                    "{374DE290-123F-4565-9164-39C4925E467B}", null) as string;
                if (!string.IsNullOrEmpty(v)) return Environment.ExpandEnvironmentVariables(v);
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        public static bool IsEnabled() => Run("/Query /TN \"" + TaskName + "\"", out _) == 0;

        /// <summary>登記（已存在就覆蓋,順便修正路徑）。需要管理員權限（/RL HIGHEST）。成功時把路徑記進設定。</summary>
        public static bool Enable(AppSettings settings, out string error)
        {
            // /TR 的值要自己再包一層引號,路徑有空白才不會被拆開;加 /autostart 讓程式知道是排程帶起來的
            string tr = "\\\"" + ExePath + "\\\" " + AutoStartArg;
            int rc = Run("/Create /F /TN \"" + TaskName + "\" /SC ONLOGON /RL HIGHEST /TR \"" + tr + "\"", out error);
            if (rc == 0)
            {
                settings.AutoStartExePath = ExePath;
                settings.Save();
            }
            FileLogger.Write(rc == 0 ? "⏰ 已登記開機自啟: " + ExePath : "⏰ 登記開機自啟失敗 (rc=" + rc + "): " + error);
            return rc == 0;
        }

        public static bool Disable(AppSettings settings, out string error)
        {
            int rc = Run("/Delete /F /TN \"" + TaskName + "\"", out error);
            if (rc == 0)
            {
                settings.AutoStartExePath = null;
                settings.Save();
            }
            FileLogger.Write(rc == 0 ? "⏰ 已取消開機自啟" : "⏰ 取消開機自啟失敗 (rc=" + rc + "): " + error);
            return rc == 0;
        }

        /// <summary>
        /// 啟動時呼叫（背景執行緒）:回傳「目前是否已登記」。
        /// 從沒用過這個功能的人（設定裡沒記路徑）直接回 false、不開任何子行程——未簽章 exe 每次啟動 spawn schtasks 是 AV 常見誤判點。
        /// 有記路徑但 exe 搬家了 → 靜默改成現在的路徑（沒管理員就算了,下次有再修）。
        /// </summary>
        public static bool ProbeAndRepair(AppSettings settings)
        {
            if (string.IsNullOrEmpty(settings.AutoStartExePath)) return false;
            try
            {
                if (!IsEnabled()) return false;   // 使用者在工作排程器裡手動刪了
                if (!string.Equals(settings.AutoStartExePath, ExePath, StringComparison.OrdinalIgnoreCase))
                {
                    FileLogger.Write("⏰ 開機自啟登記的路徑與目前 exe 不同（" + settings.AutoStartExePath + "）,嘗試修正");
                    Enable(settings, out _);
                }
                return true;
            }
            catch (Exception ex) { FileLogger.Write("⏰ 檢查開機自啟登記時出錯: " + ex.Message); return false; }
        }

        private static int Run(string args, out string error)
        {
            error = "";
            try
            {
                // schtasks 用主控台字碼頁輸出（繁中＝cp950）,不註冊 provider 讀出來會是亂碼（只拿來顯示錯誤訊息）
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                Encoding oem;
                try { oem = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
                catch { oem = Encoding.Default; }

                var psi = new ProcessStartInfo("schtasks.exe", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = oem,
                    StandardErrorEncoding = oem,
                };
                using var p = Process.Start(psi);
                // 兩條管線都 redirect 時要一條非同步讀,否則某條塞滿 4KB 緩衝就死結
                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                string stderr = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(10000))
                {
                    try { p.Kill(true); } catch { }
                    error = "schtasks 逾時（10 秒）";
                    return -1;
                }
                string stdout = stdoutTask.GetAwaiter().GetResult();
                error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;   // schtasks 有時把錯誤印在 stdout
                return p.ExitCode;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return -1;
            }
        }
    }
}
