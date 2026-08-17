using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace TouchpadRightClick.Utils
{
    /// <summary>
    /// 純文字寫檔日誌:%LOCALAPPDATA%\TouchpadTapRight\logs\app.log,超過 1MB 轉成 app.1.log（只留兩份）。
    /// 只收 Normal 級以上的狀態訊息（逐筆座標的 Debug/Verbose 不會進來——那是每秒 125 筆的洪水）。
    /// 連續相同訊息壓成「×N」,遠端支援時使用者把這個資料夾的檔案貼過來就夠了。
    ///
    /// 寫檔在背景執行緒:呼叫端可能是 WH_MOUSE_LL 回呼（GlobalMouseHook.LeftClickSuppressed → UpdateStatus）,
    /// 在鉤子裡同步做 IO 會踩到 LowLevelHooksTimeout（預設 300ms）被系統拔掉鉤子。
    /// 用 LocalApplicationData 不用 Roaming:網域機器的 Roaming 可能是網路路徑。
    /// </summary>
    public static class FileLogger
    {
        public const string AppFolderName = "TouchpadTapRight";
        private const long MAX_BYTES = 1024 * 1024;
        private const int REPEAT_FLUSH_EVERY = 100;   // 同一則重複到這個數就先落地一行,支援者在程式還開著時也看得到頻率
        private const int MAX_CONSECUTIVE_FAILURES = 20;

        public static readonly string LogDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName, "logs");
        public static readonly string LogPath = Path.Combine(LogDirectory, "app.log");

        private static readonly object _lock = new object();
        private static readonly BlockingCollection<string> _queue = new BlockingCollection<string>(new ConcurrentQueue<string>());
        private static Thread _writer;
        private static string _lastMessage;
        private static int _repeatCount;
        private static int _consecutiveFailures;
        private static volatile bool _disabled;

        /// <summary>寫一行（自動加時間戳）。與上一行內容相同時只計數,等到不同訊息（或每 100 次）才補一行「×N」。呼叫端不做 IO。</summary>
        public static void Write(string message)
        {
            if (_disabled || string.IsNullOrEmpty(message)) return;
            lock (_lock)
            {
                if (message == _lastMessage)
                {
                    _repeatCount++;
                    if (_repeatCount % REPEAT_FLUSH_EVERY == 0)
                        Enqueue("    (上一則已重複 ×" + _repeatCount + ")");
                    return;
                }
                FlushRepeat();
                _lastMessage = message;
                Enqueue(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message.Replace("\r\n", "\n").Replace("\n", "\n    "));
            }
        }

        /// <summary>啟動時寫環境快照:版本、OS、是否管理員。HID caps 會隨「HID API 動態初始化成功」那則狀態訊息進來,不用另外抓。</summary>
        public static void WriteStartupSnapshot()
        {
            bool isAdmin = false;
            try { isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { }
            Write("══════════ 啟動 " + UpdateChecker.CurrentTag +
                  " | Windows " + Environment.OSVersion.Version +
                  " | .NET " + Environment.Version +
                  " | 管理員=" + (isAdmin ? "是" : "否") +
                  " | x64=" + Environment.Is64BitProcess);
        }

        /// <summary>結束前把「×N」與佇列裡的東西寫完（最多等 1.5 秒）。</summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                FlushRepeat();
                Enqueue(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " ══════════ 結束");
            }
            try
            {
                _queue.CompleteAdding();
                _writer?.Join(1500);
            }
            catch { }
        }

        // ── 以下都在 _lock 內或背景執行緒 ──

        private static void FlushRepeat()
        {
            if (_repeatCount > 0)
            {
                Enqueue("    (上一則重複 ×" + _repeatCount + ")");
                _repeatCount = 0;
            }
        }

        private static void Enqueue(string line)
        {
            if (_writer == null)
            {
                _writer = new Thread(WriterLoop) { IsBackground = true, Name = "FileLogger" };
                _writer.Start();
            }
            try { _queue.Add(line); } catch (InvalidOperationException) { /* 已 CompleteAdding */ }
        }

        private static void WriterLoop()
        {
            foreach (var line in _queue.GetConsumingEnumerable())
            {
                if (_disabled) continue;
                try
                {
                    AppendRaw(line);
                    _consecutiveFailures = 0;
                }
                catch
                {
                    // 磁碟滿／權限／被別的程式鎖住:連續失敗太多次才放棄,偶發的不算
                    if (++_consecutiveFailures >= MAX_CONSECUTIVE_FAILURES) _disabled = true;
                }
            }
        }

        private static void AppendRaw(string line)
        {
            Directory.CreateDirectory(LogDirectory);
            var fi = new FileInfo(LogPath);
            if (fi.Exists && fi.Length > MAX_BYTES)
            {
                // 滾動失敗（app.1.log 被編輯器開著之類）就繼續 append 到現在這份,不能因此不寫
                try
                {
                    string old = Path.Combine(LogDirectory, "app.1.log");
                    File.Delete(old);
                    File.Move(LogPath, old);
                }
                catch { }
            }
            File.AppendAllText(LogPath, line + Environment.NewLine, new UTF8Encoding(false));
        }
    }
}
