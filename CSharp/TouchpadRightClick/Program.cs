using System;
using System.Windows.Forms;
using TouchpadRightClick.UI;

namespace TouchpadRightClick
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            // .NET 9 才有:Windows 11 下讓 TrackBar/NumericUpDown/CheckBox/StatusStrip/捲軸這些原生控制項跟著深色
            //（Win10 只會是我們自己畫的部分深色,跟 v8.71 相同;MessageBox 是系統對話框,不跟）。
            // 跟 Theme.Dark 綁在一起（同一個開關,含 TAPRIGHT_THEME 測試鉤子）,不用 System 免得兩邊不同步。
#pragma warning disable WFO5001 // 實驗性 API:行為若在未來版本改變,最壞就是原生控制項回到淺色
            Application.SetColorMode(UI.Theme.Dark ? SystemColorMode.Dark : SystemColorMode.Classic);
#pragma warning restore WFO5001

            // v8.70 以前的 Mutex 名不同;新舊版同時跑會各自註冊 Raw Input＋各裝一個滑鼠鉤子,一次輕觸送兩次右鍵
            if (System.Threading.Mutex.TryOpenExisting("TouchpadRightClick_SingleInstance", out var legacy))
            {
                legacy.Dispose();
                MessageBox.Show("偵測到舊版「觸控板輕觸右鍵」正在執行,請先關閉舊版再開啟這個新版本。", "已有舊版在執行", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool createdNew;
            using (var mutex = new System.Threading.Mutex(true, "TouchpadTapRight_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    // Application.Restart()（切換外觀）會在舊實例還沒退出前就把新實例拉起來:等它釋放,最多 5 秒
                    bool acquired = false;
                    try { acquired = mutex.WaitOne(5000); }
                    catch (System.Threading.AbandonedMutexException) { acquired = true; }   // 舊實例沒正常釋放就死了,照樣接手
                    if (!acquired)
                    {
                        MessageBox.Show("觸控板右鍵設定已經在執行中！", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                // D5: 明確持有/釋放 Mutex,避免下次啟動遇到 AbandonedMutexException
                try
                {
                    Utils.FileLogger.WriteStartupSnapshot();
                    // 未處理例外:寫 log 之後仍要跳對話框——一旦掛了 ThreadException handler,WinForms 就不再顯示預設錯誤框,
                    // 使用者會以為「畫面沒反應」。非 UI 執行緒的也記一筆。
                    Application.ThreadException += (s, e) =>
                    {
                        Utils.FileLogger.Write("💥 未處理例外: " + e.Exception);
                        MessageBox.Show(
                            $"程式發生錯誤,已記錄到日誌:\n{Utils.FileLogger.LogPath}\n\n{e.Exception.Message}",
                            "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    };
                    AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                        Utils.FileLogger.Write("💥 未處理例外(非 UI 執行緒): " + e.ExceptionObject);

                    // 使用者操作說明:以任務為主軸,移除版本號和開發者細節。
                    // 由工作排程器帶起來（/autostart）時跳過——modal 視窗會擋住「開機自啟＋自動開始監控」的無人值守。
                    if (!Utils.AutoStart.SkipWelcome)
                    MessageBox.Show(
                        "歡迎使用「觸控板輕觸右鍵」\n" +
                        "\n" +
                        "這個工具讓您「輕輕一碰」就能按右鍵,\n" +
                        "不需要實際按壓觸控板。\n" +
                        "\n" +
                        "━━━━━━━━━━━━━━━━━━━━\n" +
                        "【怎麼使用】\n" +
                        "\n" +
                        "  🖱  按右鍵\n" +
                        "      在觸控板的「右下角」輕輕點一下\n" +
                        "      (輕輕碰到即可,不用按壓)\n" +
                        "\n" +
                        "  ✋  拖曳項目\n" +
                        "      在「右下角」按住不放超過半秒\n" +
                        "      進入拖曳模式後即可移動項目\n" +
                        "\n" +
                        "  👆  其他動作\n" +
                        "      觸控板其他區域維持原本功能\n" +
                        "      (移動游標、捲動、雙指縮放等)\n" +
                        "\n" +
                        "━━━━━━━━━━━━━━━━━━━━\n" +
                        "【開始使用】\n" +
                        "\n" +
                        "  1. 點擊下方的「確定」\n" +
                        "  2. 在設定視窗按「▶ 開始監控」\n" +
                        "  3. 開始嘗試在右下角輕觸\n" +
                        "\n" +
                        "如有問題,可到設定視窗的「進階診斷」查看。",
                        "觸控板輕觸右鍵",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );

                    Application.Run(new ModernMainForm());
                }
                catch (Exception ex)
                {
                    Utils.FileLogger.Write("💥 啟動失敗: " + ex);
                    MessageBox.Show(
                        $"啟動失敗：\n\n{ex.Message}\n\n{ex.StackTrace}",
                        "錯誤",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
                finally
                {
                    Utils.FileLogger.Shutdown();
                    // D5: 正常結束時釋放 Mutex (using 會 Dispose,但明確 Release 更保險)
                    try { mutex.ReleaseMutex(); } catch { }
                }
            }
        }
    }
}
