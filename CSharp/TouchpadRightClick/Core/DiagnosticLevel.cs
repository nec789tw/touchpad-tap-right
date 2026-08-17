namespace TouchpadRightClick.Core
{
    /// <summary>
    /// 診斷訊息詳細程度
    /// 添加訊息分級控制
    /// </summary>
    public enum DiagnosticLevel
    {
        /// <summary>
        /// 靜默：只顯示錯誤和警告
        /// </summary>
        Silent = 0,
        
        /// <summary>
        /// 正常：顯示重要事件（預設）
        /// 包括：啟動、右鍵點擊、攔截成功
        /// </summary>
        Normal = 1,
        
        /// <summary>
        /// 詳細：顯示所有事件
        /// 包括：預防性抑制、手指離開等
        /// </summary>
        Verbose = 2,
        
        /// <summary>
        /// 調試：顯示所有訊息包含 DEBUG 標記
        /// 包括：Raw Input 數據、座標解析等
        /// </summary>
        Debug = 3
    }
}
