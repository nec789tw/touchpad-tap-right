using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TouchpadRightClick.Core
{
    /// <summary>
    /// 滑鼠事件模擬器。
    /// A7: 從已過時的 mouse_event 改用 SendInput,並將 DOWN+UP 一次送出,
    ///     不再需要 Thread.Sleep 阻塞呼叫執行緒。
    /// </summary>
    public static class MouseSimulator
    {
        #region P/Invoke

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        #endregion

        private static bool SendMouseEvents(params uint[] flags)
        {
            var inputs = new INPUT[flags.Length];
            for (int i = 0; i < flags.Length; i++)
            {
                inputs[i] = new INPUT
                {
                    type = INPUT_MOUSE,
                    U = new InputUnion
                    {
                        mi = new MOUSEINPUT
                        {
                            dx = 0,
                            dy = 0,
                            mouseData = 0,
                            dwFlags = flags[i],
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };
            }

            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (sent != inputs.Length)
            {
                Debug.WriteLine($"⚠️ MouseSimulator: SendInput 傳送失敗,成功 {sent}/{inputs.Length}, error={Marshal.GetLastWin32Error()}");
                return false;
            }
            return true;
        }

        /// <summary>模擬右鍵完整點擊 (DOWN + UP 一次送)。</summary>
        public static void SimulateRightClick()
            => SendMouseEvents(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);

        /// <summary>模擬左鍵完整點擊 (DOWN + UP 一次送)。</summary>
        public static void SimulateLeftClick()
            => SendMouseEvents(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);

        /// <summary>模擬左鍵按下 (拖曳開始)。</summary>
        public static void SimulateLeftDown()
            => SendMouseEvents(MOUSEEVENTF_LEFTDOWN);

        /// <summary>模擬左鍵放開 (拖曳結束)。</summary>
        public static void SimulateLeftUp()
            => SendMouseEvents(MOUSEEVENTF_LEFTUP);
    }
}
