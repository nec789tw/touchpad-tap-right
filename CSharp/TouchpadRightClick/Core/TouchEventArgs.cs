using System;

namespace TouchpadRightClick.Core
{
    /// <summary>
    /// 觸控事件參數 (從 SimplifiedHIDMonitor 提取)
    /// 包含正規化座標資訊 (0.0-1.0)
    /// </summary>
    public class TouchEventArgs : EventArgs
    {
        public double X { get; set; }  // 0.0-1.0
        public double Y { get; set; }  // 0.0-1.0
        public bool IsTouching { get; set; }
        public int EventNumber { get; set; }
        public bool IsFirstTouch { get; set; }
        public string DebugInfo { get; set; } = "";  // Debug 資訊
    }
}
