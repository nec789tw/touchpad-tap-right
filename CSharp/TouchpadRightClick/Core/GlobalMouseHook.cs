using System;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace TouchpadRightClick.Core
{
    /// <summary>
    /// 全局滑鼠鉤子 - 用於攔截右下角觸控產生的左鍵事件
    /// 修正右下角同時觸發左鍵+右鍵的問題
    /// 性能優化 - 使用 TickCount 替代 DateTime
    /// 優化 - 利用 LLMHF_INJECTED 標誌更精準攔截
    /// 修正 TickCount 溢位計算,清除委派防止記憶體洩漏
    /// </summary>
    public class GlobalMouseHook : IDisposable
    {
        // Windows Hook 常數
        private const int WH_MOUSE_LL = 14;
        
        // 滑鼠事件常數
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;

        private const uint LLMHF_INJECTED = 0x00000001;  // 事件是由 SendInput 或其他軟體注入的
        
        // Hook 委派
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        
        // Windows API
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        
        // 滑鼠鉤子結構
        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }
        
        // 私有欄位
        private IntPtr _hookID = IntPtr.Zero;
        private LowLevelMouseProc _proc;
        private volatile bool _suppressLeftClick = false;
        private int _suppressStartTime = 0;  // 改用 TickCount (性能更好)
        private int _suppressDurationMs = 300;
        private volatile bool _isDragging = false;  // 拖曳模式標記
        
        // 事件：當攔截左鍵時觸發
        public event EventHandler<string> LeftClickSuppressed;
        
        /// <summary>
        /// 左鍵抑制持續時間（毫秒）
        /// </summary>
        public int SuppressDurationMs
        {
            get => _suppressDurationMs;
            set => _suppressDurationMs = value;
        }
        
        /// <summary>
        /// 啟動全局滑鼠鉤子
        /// </summary>
        public bool Start()
        {
            if (_hookID != IntPtr.Zero)
            {
                return true; // 已經啟動
            }
            
            _proc = HookCallback;
            
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                _hookID = SetWindowsHookEx(WH_MOUSE_LL, _proc, 
                    GetModuleHandle(curModule.ModuleName), 0);
            }
            
            return _hookID != IntPtr.Zero;
        }
        
        /// <summary>
        /// 停止全局滑鼠鉤子
        /// 清除委派引用,防止多次 Start/Stop 造成記憶體累積
        /// </summary>
        public void Stop()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
            _proc = null;  // 清除委派引用
        }
        
        /// <summary>
        /// 啟動左鍵抑制（在觸發右鍵後調用）
        /// </summary>
        public void StartSuppression()
        {
            _suppressLeftClick = true;
            _suppressStartTime = Environment.TickCount;  // 使用 TickCount
        }
        
        /// <summary>
        /// 停止左鍵抑制
        /// </summary>
        public void StopSuppression()
        {
            _suppressLeftClick = false;
        }
        
        /// <summary>
        /// 設置拖曳模式（拖曳期間不攔截左鍵）
        /// </summary>
        public void SetDraggingMode(bool isDragging)
        {
            _isDragging = isDragging;
            if (isDragging)
            {
                _suppressLeftClick = false; // 拖曳時停止抑制
            }
        }
        
        /// <summary>
        /// 滑鼠鉤子回調函數
        /// 添加異常處理，使用 TickCount 優化性能
        /// 優化 - 只攔截硬體產生的左鍵，不攔截軟體注入的
        /// </summary>
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();

                    // 檢查是否需要抑制左鍵（拖曳期間不抑制）
                    if (_suppressLeftClick && !_isDragging)
                    {
                        int elapsed = Environment.TickCount - _suppressStartTime;

                        // TickCount 從 int.MaxValue 溢位後會變成 int.MinValue（不是 0）
                        // 使用 unchecked 讓溢位自然處理,結果會是正確的經過時間
                        if (elapsed < 0)
                        {
                            // 溢位情況: 直接重置抑制狀態,避免複雜計算
                            // 這種情況極少發生(需連續運行 49.7 天),直接停止抑制最安全
                            _suppressLeftClick = false;
                            elapsed = 0;
                        }

                        if (elapsed > _suppressDurationMs)
                        {
                            _suppressLeftClick = false;
                        }
                        else
                        {
                            // 攔截左鍵事件
                            if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP || msg == WM_LBUTTONDBLCLK)
                            {
                                MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                                bool isInjected = (hookStruct.flags & LLMHF_INJECTED) != 0;

                                // 不攔截「軟體注入」的左鍵（來自我們自己的 SendInput 或其他程式）
                                if (!isInjected)
                                {
                                    LeftClickSuppressed?.Invoke(this,
                                        $"攔截左鍵 (右鍵後 {elapsed}ms, 硬體來源)");

                                    // 返回 1 表示攔截此事件，不傳遞給其他應用程式
                                    return new IntPtr(1);
                                }
                                // 軟體注入的左鍵不攔截，讓它正常傳遞
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ GlobalMouseHook 異常: {ex.Message}");
                Debug.WriteLine($"   類型: {ex.GetType().Name}");
                Debug.WriteLine($"   堆疊: {ex.StackTrace}");
            }

            // 傳遞給下一個鉤子
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }
        
        public void Dispose()
        {
            Stop();
        }
    }
}
