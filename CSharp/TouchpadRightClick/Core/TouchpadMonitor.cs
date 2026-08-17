using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TouchpadRightClick.Core
{
    /// <summary>
    /// 觸控板監控器 - 整合 Tap Zone 偵測和滑鼠模擬
    /// 新增 HID API 支援,自動適應不同廠商觸控板
    /// 修正右下角誤觸發左鍵問題 + 記憶體洩漏修復
    /// 修正 Timer 競態條件和記憶體洩漏風險
    /// Magic Numbers 重構 - 提取為命名常數
    /// 修正右下角同時觸發左鍵+右鍵問題 - 使用全局滑鼠鉤子
    /// </summary>
    public class TouchpadMonitor : NativeWindow, IDisposable
    {
        // Windows Message 常數
        private const int WM_INPUT = 0x00FF;
        private const int RID_INPUT = 0x10000003;
        private const int RIDI_PREPARSEDDATA = 0x20000005;

        // HID 常數
        private const int HID_USAGE_PAGE_DIGITIZER = 0x0D;
        private const int HID_USAGE_DIGITIZER_TOUCH_PAD = 0x05;

        public const int DEFAULT_TOUCH_END_TIMEOUT_MS = 100;    // 無新事件 100ms 視為離開（Timer 間隔同值）
        private int _touchEndTimeoutMs = DEFAULT_TOUCH_END_TIMEOUT_MS;

        /// <summary>多久沒有新 report 視為手指離開（毫秒）;config.json 可調</summary>
        public int TouchEndTimeoutMs
        {
            get => _touchEndTimeoutMs;
            set { _touchEndTimeoutMs = Math.Max(30, value); _touchEndTimer.Interval = _touchEndTimeoutMs; }
        }

        private const double POSITION_CHANGE_THRESHOLD = 0.001; // 座標變化閾值 (>0.1%)
        private const int DEVICE_REBIND_SILENCE_MS = 2000;      // 已綁定裝置靜默多久才允許改綁另一個 hDevice


        private bool _isRunning = false;
        private HidApiParser _hidApiParser = new HidApiParser();
        private TapZoneDetector _tapDetector = new TapZoneDetector();

        private IntPtr _preparsedData = IntPtr.Zero;
        private IntPtr _touchpadDevice = IntPtr.Zero;
        private DateTime _lastBoundDeviceReportTime = DateTime.MinValue;

        private int _eventCount = 0;
        private bool _lastTouchingState = false;
        private DateTime _lastEventTime = DateTime.MinValue;
        // A2: 用 WinForms Timer（在建立本 NativeWindow 的 UI 執行緒上 Tick）取代 System.Threading.Timer,
        //     觸控結束判定與 WM_INPUT 處理同一條執行緒,TapZoneDetector 與 _last* 狀態不再有競態,
        //     也不需要 _timerLock / ObjectDisposedException 補救。
        private readonly Timer _touchEndTimer;

        private double _lastX = -1;
        private double _lastY = -1;
        private DateTime _lastPositionChangeTime = DateTime.MinValue;

        private DiagnosticLevel _diagnosticLevel = DiagnosticLevel.Normal;
        private GlobalMouseHook _globalMouseHook;
        private bool _globalHookEnabled = false; // 追蹤鉤子狀態

        public event EventHandler<TouchEventArgs> TouchEvent;
        public event EventHandler<TapEventArgs> TapDetected;
        public event EventHandler<string> StatusUpdate;

        public TapZoneDetector TapDetector => _tapDetector;
        public HidApiParser HidParser => _hidApiParser;

        public TouchpadMonitor()
        {
            // 先建 Timer:TouchEndTimeoutMs setter 會碰它,任何人在建構子裡設屬性都不能 NRE
            // one-shot:每筆觸控事件 Stop()+Start() 重新計時,100ms 沒有新事件才 Tick
            _touchEndTimer = new Timer { Interval = _touchEndTimeoutMs };
            _touchEndTimer.Tick += (s, e) => { _touchEndTimer.Stop(); CheckTouchEnd(); };

            CreateHandle(new CreateParams());
            _tapDetector.TapDetected += OnTapDetected;
            _tapDetector.DragStarted += OnDragStarted;
            _tapDetector.DragEnded += OnDragEnded;

            _globalMouseHook = new GlobalMouseHook();
            _globalMouseHook.SuppressDurationMs = 300; // 右鍵後 300ms 內抑制左鍵
            _globalMouseHook.LeftClickSuppressed += (s, msg) => UpdateStatus($"🛡 {msg}");
        }

        // A1: RIDEV_REMOVE 旗標,用於 Stop() 解除 Raw Input 註冊
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIDEV_REMOVE = 0x00000001;

        public bool Start()
        {
            // A1: 防止重複啟動。之前沒有這個守衛,連按兩次「開始」會重複 RegisterRawInputDevices。
            if (_isRunning)
            {
                UpdateStatus("⚠ 已在運行中,忽略重複啟動");
                return true;
            }

            try
            {
                UpdateStatus($"💡 拖曳操作: 右下角按住不動超過 {_tapDetector.DragHoldThresholdMs} ms 再移動");

                // 註冊 Raw Input
                RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
                rid[0].usUsagePage = HID_USAGE_PAGE_DIGITIZER;
                rid[0].usUsage = HID_USAGE_DIGITIZER_TOUCH_PAD;
                rid[0].dwFlags = RIDEV_INPUTSINK;
                rid[0].hwndTarget = this.Handle;

                if (!RegisterRawInputDevices(rid, 1, Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
                {
                    int error = Marshal.GetLastWin32Error();
                    UpdateStatus($"❌ 註冊 Raw Input 失敗，錯誤碼: {error}");
                    return false;
                }

                // RegisterRawInputDevices 已經註冊了 UsagePage=0x0D, Usage=0x05
                // 當觸控板有事件時會自動收到 WM_INPUT 訊息

                _globalHookEnabled = _globalMouseHook.Start();
                if (!_globalHookEnabled)
                {
                    UpdateStatus($"❌ 錯誤: 全局滑鼠鉤子啟動失敗！", DiagnosticLevel.Normal);
                    UpdateStatus($"💡 請以「系統管理員身分執行」此程式", DiagnosticLevel.Normal);
                    UpdateStatus($"⚠️ 右鍵功能可能異常（左鍵+右鍵同時觸發）", DiagnosticLevel.Normal);
                }
                else
                {
                    UpdateStatus($"🛡 全局滑鼠鉤子已啟動 - 將攔截右下角觸控產生的左鍵事件");
                }


                _isRunning = true;
                UpdateStatus($"✅ 觸控板監控已啟動 [等待觸控板事件以動態初始化 HID API]");
                UpdateStatus("請輕觸觸控板進行初始化...");
                return true;
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ 啟動失敗: {ex.Message}");
                return false;
            }
        }

        // A1: Stop() 完整釋放資源,讓 Start/Stop 可以循環。
        //     之前只設了 _isRunning 旗標,鉤子/Timer/Raw Input 註冊都沒解除。
        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;

            // 先結束進行中的觸控/拖曳:拖曳中 Stop 會由 Reset() → DragEnded → OnDragEnded 補送 LeftUp,
            // 否則系統左鍵卡在按下狀態,且下次 Start 後第一次抬手會誤送 LeftUp 而不是右鍵。
            _touchEndTimer.Stop();
            _tapDetector.Reset();
            _globalMouseHook?.SetDraggingMode(false);

            // 解除 Raw Input 註冊 (RIDEV_REMOVE 時 hwndTarget 必須為 IntPtr.Zero)
            try
            {
                RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
                rid[0].usUsagePage = HID_USAGE_PAGE_DIGITIZER;
                rid[0].usUsage = HID_USAGE_DIGITIZER_TOUCH_PAD;
                rid[0].dwFlags = RIDEV_REMOVE;
                rid[0].hwndTarget = IntPtr.Zero;
                RegisterRawInputDevices(rid, 1, Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            }
            catch { /* 解除失敗不致命 */ }

            // 停止全局滑鼠鉤子
            if (_globalHookEnabled)
            {
                _globalMouseHook?.Stop();
                _globalHookEnabled = false;
            }

            // 重設狀態追蹤,下次 Start 從乾淨狀態開始
            _lastTouchingState = false;
            _lastX = -1;
            _lastY = -1;
            _lastPositionChangeTime = DateTime.MinValue;

            // 注意:不釋放 PreparsedData——停止後「硬體檢測」還要用它;裝置換掉由 ProcessRawInput 的 hDevice 檢查處理
            UpdateStatus("⏹ 監控已停止");
        }

        private void ReleasePreparsedData()
        {
            if (_preparsedData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_preparsedData);
                _preparsedData = IntPtr.Zero;
            }
            _touchpadDevice = IntPtr.Zero;
            _hidApiParser.Invalidate(); // parser 的 caps 屬於剛釋放的那份 preparsed data,不能再用
        }

        protected override void WndProc(ref Message m)
        {
            try
            {

                if (m.Msg == WM_INPUT && _isRunning)
                {
                    ProcessRawInput(m.LParam);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"⚠️ WndProc 異常: {ex.Message}");
            }
            finally
            {
                base.WndProc(ref m);
            }
        }

        private void ProcessRawInput(IntPtr lParam)
        {
            try
            {
                uint dwSize = 0;
                GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref dwSize, Marshal.SizeOf(typeof(RAWINPUTHEADER)));

                if (dwSize == 0)
                    return;

                IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
                try
                {
                    if (GetRawInputData(lParam, RID_INPUT, buffer, ref dwSize, Marshal.SizeOf(typeof(RAWINPUTHEADER))) != dwSize)
                        return;

                    RAWINPUT raw = Marshal.PtrToStructure<RAWINPUT>(buffer);

                    if (raw.header.dwType != 2) // RIM_TYPEHID
                        return;

                    // 註冊的是「所有 UsagePage 0x0D/Usage 0x05 裝置」,PreparsedData 卻只屬於一台,
                    // 不能拿 A 的 descriptor 解 B 的 report。已綁定裝置還在送 report 就忽略他機
                    // （否則兩台交錯會每筆重建 descriptor）;綁定裝置靜默超過 DEVICE_REBIND_SILENCE_MS
                    // 才視為裝置變更（重新列舉／換觸控板）,對新裝置重新初始化。
                    if (_preparsedData != IntPtr.Zero && raw.header.hDevice != _touchpadDevice)
                    {
                        if ((DateTime.Now - _lastBoundDeviceReportTime).TotalMilliseconds < DEVICE_REBIND_SILENCE_MS)
                            return;
                        UpdateStatus($"🔄 觸控板裝置變更 (hDevice {_touchpadDevice} → {raw.header.hDevice}),重新初始化 HID API");
                        ReleasePreparsedData();
                    }

                    if (_preparsedData == IntPtr.Zero)
                    {
                        // 從 WM_INPUT 訊息的設備 handle 獲取 PreparsedData
                        uint ppSize = 0;
                        GetRawInputDeviceInfo(raw.header.hDevice, RIDI_PREPARSEDDATA, IntPtr.Zero, ref ppSize);

                        if (ppSize > 0)
                        {
                            _preparsedData = Marshal.AllocHGlobal((int)ppSize);
                            // 失敗回傳 (UINT)-1,用 uint 比較 > 0 恆真,要轉 int 再比
                            if ((int)GetRawInputDeviceInfo(raw.header.hDevice, RIDI_PREPARSEDDATA, _preparsedData, ref ppSize) > 0
                                && _hidApiParser.Initialize(_preparsedData))
                            {
                                UpdateStatus("✅ HID API 動態初始化成功！");
                                UpdateStatus(_hidApiParser.GetDescriptorReport());
                                _touchpadDevice = raw.header.hDevice;
                            }
                            else
                            {
                                UpdateStatus($"❌ HID API 初始化失敗: {_hidApiParser.LastInitError}");
                                ReleasePreparsedData();
                                return;
                            }
                        }
                    }

                    // ppSize==0（失效的 hDevice 會讓 GetRawInputDeviceInfo 回 -1 且不動 pcbSize）時上面整塊被跳過,
                    // 沒有 preparsed data 就不能往下解析——NULL 丟進 hid.dll 是 AccessViolation,try/catch 接不到。
                    if (_preparsedData == IntPtr.Zero)
                        return;
                    _lastBoundDeviceReportTime = DateTime.Now;

                    UpdateStatus($"[DEBUG] 收到 WM_INPUT, dwSizeHid={raw.hid.dwSizeHid}, dwCount={raw.hid.dwCount}", DiagnosticLevel.Debug);

                    // 一個 WM_INPUT 可能夾帶 dwCount 筆 report（訊息佇列積壓時）。HidP_GetUsageValue 要求
                    // ReportLength 恰等於一筆 report 的長度,所以要逐筆切開處理,不能整段串起來丟進去。
                    int frameSize = (int)raw.hid.dwSizeHid;
                    int rawDataOffset = Marshal.OffsetOf<RAWINPUT>("hid").ToInt32() + Marshal.SizeOf(typeof(RAWHID));
                    for (int i = 0; i < raw.hid.dwCount; i++)
                    {
                        byte[] rawData = new byte[frameSize];
                        Marshal.Copy(IntPtr.Add(buffer, rawDataOffset + i * frameSize), rawData, 0, frameSize);
                        HandleReport(rawData);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"⚠ 處理事件錯誤: {ex.Message}");
            }
        }

        /// <summary>
        /// 處理單筆 HID input report:解析座標 → 更新觸碰狀態 → 餵給 TapZoneDetector。
        /// </summary>
        private void HandleReport(byte[] rawData)
        {
            double x, y;
            bool isTouching;
            string debugInfo;

            bool parseSuccess = _hidApiParser.TryParse(_preparsedData, rawData, out x, out y, out isTouching, out debugInfo);

            UpdateStatus($"[DEBUG] TryParse 結果: {parseSuccess}, X={x:F3}, Y={y:F3}, isTouching={isTouching}", DiagnosticLevel.Debug);
            if (!parseSuccess)
            {
                // 輸出詳細的失敗原因
                UpdateStatus($"[DEBUG] 解析失敗原因:\n{debugInfo}", DiagnosticLevel.Debug);
                return;
            }

            if (x < 0 || y < 0)
                return;

            _eventCount++;

            // 檢測觸碰狀態變化
            bool isFirstTouch = !_lastTouchingState;

            OnTouchEvent(new TouchEventArgs
            {
                X = x,
                Y = y,
                IsTouching = isTouching,
                EventNumber = _eventCount,
                IsFirstTouch = isFirstTouch,
                DebugInfo = debugInfo
            });

            if (isTouching)
            {
                // 這樣可以在 Windows 驅動生成左鍵事件時就攔截它。
                // 區域判定共用 TapZoneDetector 的（含邊緣保護）,否則右邊緣 4% 帶會被抑制左鍵卻又不發右鍵。
                if (_globalHookEnabled && isFirstTouch && _tapDetector.IsInRightClickZone(x, y))
                {
                    _globalMouseHook?.StartSuppression();
                    UpdateStatus($"🛡 預防性啟動左鍵抑制 (右下角觸控開始 X={x:F2}, Y={y:F2})", DiagnosticLevel.Verbose);
                }

                _tapDetector.ProcessTouchEvent(x, y, isFirstTouch);
                _lastTouchingState = true;
                _lastEventTime = DateTime.Now;

                double distanceChange = Math.Abs(x - _lastX) + Math.Abs(y - _lastY);
                if (distanceChange > POSITION_CHANGE_THRESHOLD)
                {
                    _lastX = x;
                    _lastY = y;
                    _lastPositionChangeTime = DateTime.Now;
                }

                // 重新計時:100ms 內沒有下一筆就視為手指離開
                _touchEndTimer.Stop();
                _touchEndTimer.Start();
            }
            else if (_lastTouchingState)
            {
                UpdateStatus($"👆 偵測到手指離開 (isTouching=False - 呼叫 ProcessTouchEnd", DiagnosticLevel.Verbose);
                _touchEndTimer.Stop();
                _tapDetector.ProcessTouchEnd();
                _lastTouchingState = false;
            }
        }

        private void CheckTouchEnd()
        {
            if (!_isRunning)
                return;

            // 1. 停止接收事件 (ELAN 觸控板會停止發送)
            // 2. 座標停止變化 (光寶科觸控板會持續發送相同座標)
            TimeSpan timeSinceLastEvent = DateTime.Now - _lastEventTime;
            TimeSpan timeSinceLastPositionChange = DateTime.Now - _lastPositionChangeTime;

            // -16:WM_TIMER 一格 15.6ms,早到一格就直接算到期,不然再排一輪會把離手延遲加倍、吃掉 300ms 的輕觸判定
            bool noNewEvents = timeSinceLastEvent.TotalMilliseconds >= _touchEndTimeoutMs - 16;
            bool positionNotChanging = _lastPositionChangeTime != DateTime.MinValue &&
                                       timeSinceLastPositionChange.TotalMilliseconds >= _touchEndTimeoutMs;

            if (!_lastTouchingState)
                return;

            if (!(noNewEvents || positionNotChanging))
            {
                // WM_TIMER 以 15.6ms 為單位,偶爾會比 100ms 早一格到;還沒到門檻就再等一輪
                _touchEndTimer.Start();
                return;
            }

            {
                string reason = noNewEvents ? "停止接收事件" : "座標停止變化";
                UpdateStatus($"👆 偵測到手指離開 ({reason} - 呼叫 ProcessTouchEnd", DiagnosticLevel.Verbose);

                // 觸碰結束
                _tapDetector.ProcessTouchEnd();
                _lastTouchingState = false;

                // 重設追蹤變數
                _lastX = -1;
                _lastY = -1;
                _lastPositionChangeTime = DateTime.MinValue;
            }
        }

        private void OnTapDetected(object sender, TapEventArgs e)
        {
            string posInfo = $"觸控板座標: X={e.Position.X:F3} ({e.Position.X*100:F0}%), Y={e.Position.Y:F3} ({e.Position.Y*100:F0}%)";

            // 其他區域由 TapZoneDetector 靜默忽略,讓 Windows 原生處理
            if (e.Zone == TapZone.BottomRight)
            {
                _globalMouseHook?.StartSuppression();

                // 右下角區域 → 發送右鍵
                MouseSimulator.SimulateRightClick();
                UpdateStatus($"🖱 右鍵點擊 [{e.Zone}] {posInfo} 距離:{e.MaxDistance:F3} 時間:{e.Duration:F0}ms");
            }

            // 轉發事件
            TapDetected?.Invoke(this, e);
        }

        private void OnDragStarted(object sender, DragEventArgs e)
        {
            _globalMouseHook?.SetDraggingMode(true);
            
            // 模擬滑鼠左鍵按下 (開始拖曳)
            MouseSimulator.SimulateLeftDown();
            UpdateStatus($"🟢 開始拖曳: 位置=({e.StartPosition.X:F2}, {e.StartPosition.Y:F2})");
        }

        private void OnDragEnded(object sender, EventArgs e)
        {
            _globalMouseHook?.SetDraggingMode(false);
            
            // 模擬滑鼠左鍵放開 (結束拖曳)
            MouseSimulator.SimulateLeftUp();
            UpdateStatus($"🔴 結束拖曳");
        }

        /// <summary>
        /// 帶診斷級別的 UpdateStatus 重載
        /// </summary>
        protected void UpdateStatus(string message, DiagnosticLevel level)
        {
            if (_diagnosticLevel >= level)
            {
                UpdateStatus(message);
            }
        }

        protected virtual void OnTouchEvent(TouchEventArgs e)
        {
            TouchEvent?.Invoke(this, e);
        }

        protected virtual void UpdateStatus(string message)
        {
            // 這裡只會收到 Normal 級以上（Verbose/Debug 在上面的重載就被 _diagnosticLevel 擋掉）,寫檔不會洪水
            Utils.FileLogger.Write(message);
            StatusUpdate?.Invoke(this, message);
        }

        public string GetHardwareDiagnosticReport()
        {
            if (_preparsedData == IntPtr.Zero)
            {
                return "⚠️ HID API 尚未初始化\n請觸摸觸控板以觸發初始化";
            }

            // 使用診斷工具產生完整報告
            // 注意: 這裡我們沒有即時的 report data,所以只顯示結構資訊
            var allCaps = Utils.HidDiagnostic.EnumerateAllCaps(_preparsedData);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("╔═══════════════════════════════════════════════════════════╗");
            sb.AppendLine($"║          HID API 硬體診斷報告 {Utils.UpdateChecker.CurrentTag,-6}                     ║");
            sb.AppendLine("╚═══════════════════════════════════════════════════════════╝");
            sb.AppendLine();

            sb.AppendLine($"【發現的 HID Value Caps】共 {allCaps.Count} 個");
            sb.AppendLine();

            // 按 UsagePage 分組顯示
            var grouped = new System.Collections.Generic.Dictionary<ushort, System.Collections.Generic.List<Utils.HidDiagnostic.HidCapInfo>>();
            foreach (var cap in allCaps)
            {
                if (!grouped.ContainsKey(cap.UsagePage))
                    grouped[cap.UsagePage] = new System.Collections.Generic.List<Utils.HidDiagnostic.HidCapInfo>();
                grouped[cap.UsagePage].Add(cap);
            }

            foreach (var kvp in grouped)
            {
                string pageTitle = kvp.Key switch
                {
                    0x01 => "Generic Desktop (0x01)",
                    0x0D => "Digitizer (0x0D)",
                    _ => $"Unknown (0x{kvp.Key:X2})"
                };

                sb.AppendLine($"UsagePage: {pageTitle}");
                foreach (var cap in kvp.Value)
                {
                    sb.AppendLine($"  • {cap.UsageName,-20} LinkCollection={cap.LinkCollection}, Range=[{cap.LogicalMin}, {cap.LogicalMax}], BitSize={cap.BitSize}");
                }
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("提示: 觸摸觸控板以查看即時讀取結果");

            return sb.ToString();
        }

        // A5: 標準 Dispose Pattern + Finalizer,確保 _preparsedData 在例外路徑
        //     也會被 GC 回收,不會洩漏 native memory。
        private bool _disposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~TouchpadMonitor()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 先 Stop() 釋放 Raw Input、鉤子、Timer
                try { Stop(); } catch { }

                _globalMouseHook?.Dispose();
                _globalMouseHook = null;

                _touchEndTimer.Dispose();

                try { DestroyHandle(); } catch { }
            }

            // Unmanaged: 無論是 disposing 還是 finalizer 都要釋放
            if (_preparsedData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_preparsedData);
                _preparsedData = IntPtr.Zero;
            }

            _disposed = true;
        }

        #region Win32 API

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        // 注意:FieldOffset(24) = x64 的 RAWINPUTHEADER 大小（x86 是 16）。csproj 鎖 win-x64,若改 RID 要一併改這裡。
        [StructLayout(LayoutKind.Explicit)]
        private struct RAWINPUT
        {
            [FieldOffset(0)] public RAWINPUTHEADER header;
            [FieldOffset(24)] public RAWHID hid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWHID
        {
            public uint dwSizeHid;
            public uint dwCount;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] RAWINPUTDEVICE[] pRawInputDevices,
            int uiNumDevices,
            int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(
            IntPtr hRawInput,
            uint uiCommand,
            IntPtr pData,
            ref uint pcbSize,
            int cbSizeHeader);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputDeviceInfo(
            IntPtr hDevice,
            uint uiCommand,
            IntPtr pData,
            ref uint pcbSize);

        #endregion
    }
}
