using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace TouchpadRightClick.Core
{
    /// <summary>
    /// 使用 Windows HID API 動態解析觸控板資料 (重構版)
    /// 參考: emoacht/RawInput.Touchpad
    /// 優勢: 自動適應不同廠商觸控板的 HID Descriptor 佈局
    /// </summary>
    public class HidApiParser
    {
        #region HID API P/Invoke 宣告

        private enum HIDP_REPORT_TYPE
        {
            HidP_Input,
            HidP_Output,
            HidP_Feature
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;

            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_VALUE_CAPS
        {
            public ushort UsagePage;
            public byte ReportID;

            [MarshalAs(UnmanagedType.U1)]
            public bool IsAlias;

            public ushort BitField;
            public ushort LinkCollection;
            public ushort LinkUsage;
            public ushort LinkUsagePage;

            [MarshalAs(UnmanagedType.U1)]
            public bool IsRange;
            [MarshalAs(UnmanagedType.U1)]
            public bool IsStringRange;
            [MarshalAs(UnmanagedType.U1)]
            public bool IsDesignatorRange;
            [MarshalAs(UnmanagedType.U1)]
            public bool IsAbsolute;
            [MarshalAs(UnmanagedType.U1)]
            public bool HasNull;

            public byte Reserved;
            public ushort BitSize;
            public ushort ReportCount;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
            public ushort[] Reserved2;

            public uint UnitsExp;
            public uint Units;
            public int LogicalMin;
            public int LogicalMax;
            public int PhysicalMin;
            public int PhysicalMax;

            public ushort UsageMin;
            public ushort UsageMax;
            public ushort StringMin;
            public ushort StringMax;
            public ushort DesignatorMin;
            public ushort DesignatorMax;
            public ushort DataIndexMin;
            public ushort DataIndexMax;

            // 屬性用於簡化存取
            public ushort Usage => UsageMin;
            public ushort StringIndex => StringMin;
            public ushort DesignatorIndex => DesignatorMin;
            public ushort DataIndex => DataIndexMin;
        }

        [DllImport("Hid.dll", SetLastError = true)]
        private static extern uint HidP_GetCaps(
            IntPtr PreparsedData,
            out HIDP_CAPS Capabilities);

        [DllImport("Hid.dll", CharSet = CharSet.Auto)]
        private static extern uint HidP_GetValueCaps(
            HIDP_REPORT_TYPE ReportType,
            [Out] HIDP_VALUE_CAPS[] ValueCaps,
            ref ushort ValueCapsLength,
            IntPtr PreparsedData);

        [DllImport("Hid.dll", CharSet = CharSet.Auto)]
        private static extern uint HidP_GetUsageValue(
            HIDP_REPORT_TYPE ReportType,
            ushort UsagePage,
            ushort LinkCollection,
            ushort Usage,
            out uint UsageValue,
            IntPtr PreparsedData,
            IntPtr Report,
            uint ReportLength);

        private const uint HIDP_STATUS_SUCCESS = 0x00110000;

        #endregion

        #region HID Usage 定義 (根據 Microsoft 規範)

        // Usage Page
        private const ushort USAGE_PAGE_GENERIC_DESKTOP = 0x01;
        private const ushort USAGE_PAGE_DIGITIZERS = 0x0D;

        // Generic Desktop Usages
        private const ushort USAGE_X = 0x30;
        private const ushort USAGE_Y = 0x31;

        // Digitizer Usages
        private const ushort USAGE_TIP_SWITCH = 0x42;
        private const ushort USAGE_CONFIDENCE = 0x47;
        private const ushort USAGE_CONTACT_ID = 0x51;
        private const ushort USAGE_SCAN_TIME = 0x56;
        private const ushort USAGE_CONTACT_COUNT = 0x54;

        #endregion

        // HID Descriptor 解析結果
        private HIDP_CAPS _caps;
        private HIDP_VALUE_CAPS[] _valueCaps;
        private bool _initialized = false;

        // 儲存找到的 Usage 位置
        private HIDP_VALUE_CAPS? _xCaps;
        private HIDP_VALUE_CAPS? _yCaps;
        private HIDP_VALUE_CAPS? _tipSwitchCaps;
        private HIDP_VALUE_CAPS? _confidenceCaps;
        private HIDP_VALUE_CAPS? _contactIdCaps;
        private HIDP_VALUE_CAPS? _scanTimeCaps;
        private HIDP_VALUE_CAPS? _contactCountCaps;

        // 診斷資訊
        public int LastRawX { get; private set; }
        public int LastRawY { get; private set; }
        public double LastNormalizedX { get; private set; }
        public double LastNormalizedY { get; private set; }
        public bool LastConfidenceBit { get; private set; }
        public int LastContactId { get; private set; } = -1;
        public int LastScanTime { get; private set; } = -1;
        public int LastContactCount { get; private set; } = -1;

        public string LastInitError { get; private set; } = "";

        /// <summary>
        /// 使 parser 失效:對應的 preparsed data 已釋放時呼叫,之後 TryParse 一律回「未初始化」直到重新 Initialize。
        /// </summary>
        public void Invalidate() => _initialized = false;

        /// <summary>
        /// 初始化 HID API Parser
        /// 必須先呼叫此方法來解析 HID Descriptor
        /// </summary>
        public bool Initialize(IntPtr preparsedData)
        {
            try
            {
                LastInitError = "";
                // 可重複初始化（換裝置 / Stop 後 Start）:先清掉上一台的 caps,不能殘留
                _initialized = false;
                _xCaps = _yCaps = _tipSwitchCaps = _confidenceCaps = _contactIdCaps = _scanTimeCaps = _contactCountCaps = null;

                // 取得 HID Capabilities
                uint status = HidP_GetCaps(preparsedData, out _caps);
                if (status != HIDP_STATUS_SUCCESS)
                {
                    LastInitError = $"HidP_GetCaps 失敗，狀態碼: 0x{status:X8}";
                    return false;
                }

                // 取得所有 Value Capabilities
                _valueCaps = new HIDP_VALUE_CAPS[_caps.NumberInputValueCaps];
                ushort capsLength = _caps.NumberInputValueCaps;
                status = HidP_GetValueCaps(HIDP_REPORT_TYPE.HidP_Input, _valueCaps, ref capsLength, preparsedData);
                if (status != HIDP_STATUS_SUCCESS)
                {
                    LastInitError = $"HidP_GetValueCaps 失敗，狀態碼: 0x{status:X8}";
                    return false;
                }

                bool foundX = false;
                bool foundY = false;

                foreach (var cap in _valueCaps)
                {
                    switch (cap.UsagePage)
                    {
                        case USAGE_PAGE_GENERIC_DESKTOP:
                            if (cap.Usage == USAGE_X)
                            {
                                if (!_xCaps.HasValue) _xCaps = cap;
                                foundX = true;
                            }
                            else if (cap.Usage == USAGE_Y)
                            {
                                if (!_yCaps.HasValue) _yCaps = cap;
                                foundY = true;
                            }
                            break;

                        case USAGE_PAGE_DIGITIZERS:
                            if (cap.Usage == USAGE_TIP_SWITCH) _tipSwitchCaps = cap;
                            else if (cap.Usage == USAGE_CONFIDENCE) _confidenceCaps = cap;
                            else if (cap.Usage == USAGE_CONTACT_ID) _contactIdCaps = cap;
                            else if (cap.Usage == USAGE_SCAN_TIME) _scanTimeCaps = cap;
                            else if (cap.Usage == USAGE_CONTACT_COUNT) _contactCountCaps = cap;
                            break;
                    }
                }

                // 檢查必要的 Usage 是否找到
                if (!foundX || !foundY)
                {
                    LastInitError = $"找不到必要的 X/Y Usage (X found: {foundX}, Y found: {foundY})";
                    return false;
                }

                _initialized = true;
                return true;
            }
            catch (Exception ex)
            {
                LastInitError = $"初始化異常: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 使用 HID API 動態解析觸控板資料 (重構版)
        /// </summary>
        public bool TryParse(IntPtr preparsedData, byte[] rawData, out double x, out double y, out bool isTouching, out string debugInfo)
        {
            x = 0;
            y = 0;
            isTouching = false;
            debugInfo = "";

            // 驗證輸入
            if (!ValidateInput(rawData, out debugInfo))
                return false;

            try
            {
                // 固定記憶體並解析
                GCHandle handle = GCHandle.Alloc(rawData, GCHandleType.Pinned);
                try
                {
                    IntPtr pData = handle.AddrOfPinnedObject();

                    var coords = ExtractCoordinates(preparsedData, pData, rawData.Length);
                    var touchData = ExtractTouchData(preparsedData, pData, rawData.Length);

                    // 儲存診斷資訊
                    SaveDiagnosticInfo(coords, touchData);

                    // 判斷觸控狀態
                    isTouching = DetermineTouchState(touchData);

                    // 驗證結果
                    if (!ValidateCoordinates(coords, isTouching, out debugInfo))
                        return false;

                    // 正規化座標
                    x = (double)coords.RawX / coords.XLogicalMax;
                    y = (double)coords.RawY / coords.YLogicalMax;

                    LastNormalizedX = x;
                    LastNormalizedY = y;

                    debugInfo = $"✅ 解析成功！正規化座標: X={x:F3} ({x * 100:F1}%), Y={y:F3} ({y * 100:F1}%)";
                    return true;
                }
                finally
                {
                    handle.Free();
                }
            }
            catch (Exception ex)
            {
                debugInfo = $"❌ HID API 解析錯誤: {ex.Message}";
                return false;
            }
        }

        #region 重構後的私有方法

        /// <summary>
        /// 驗證輸入資料
        /// </summary>
        private bool ValidateInput(byte[] rawData, out string errorMsg)
        {
            if (!_initialized)
            {
                errorMsg = "❌ HID API Parser 未初始化";
                return false;
            }

            if (rawData == null)
            {
                errorMsg = "❌ 資料為 null";
                return false;
            }

            if (rawData.Length < _caps.InputReportByteLength)
            {
                errorMsg = $"❌ 資料長度不足: {rawData.Length} < {_caps.InputReportByteLength}";
                return false;
            }

            errorMsg = "";
            return true;
        }

        /// <summary>
        /// 座標資料結構
        /// </summary>
        private struct CoordinateData
        {
            public uint RawX;
            public uint RawY;
            public int XLogicalMax;
            public int YLogicalMax;
            public bool FoundX;
            public bool FoundY;
        }

        /// <summary>
        /// 觸控資料結構
        /// </summary>
        private struct TouchData
        {
            public uint TipSwitch;
            public uint Confidence;
            public uint ContactId;
            public uint ScanTime;
            public uint ContactCount;
        }

        /// <summary>
        /// 提取座標資料
        /// </summary>
        private CoordinateData ExtractCoordinates(IntPtr preparsedData, IntPtr pData, int dataLength)
        {
            var result = new CoordinateData
            {
                XLogicalMax = 1,
                YLogicalMax = 1
            };

            // 遍歷所有 Value Caps 尋找座標
            foreach (var cap in _valueCaps)
            {
                if (cap.UsagePage != USAGE_PAGE_GENERIC_DESKTOP)
                    continue;

                uint value;
                uint status = HidP_GetUsageValue(
                    HIDP_REPORT_TYPE.HidP_Input,
                    cap.UsagePage,
                    cap.LinkCollection,
                    cap.Usage,
                    out value,
                    preparsedData,
                    pData,
                    (uint)dataLength);

                if (status != HIDP_STATUS_SUCCESS || value == 0)
                    continue;

                if (cap.Usage == USAGE_X)
                {
                    result.RawX = value;
                    result.XLogicalMax = cap.LogicalMax;
                    result.FoundX = true;
                }
                else if (cap.Usage == USAGE_Y)
                {
                    result.RawY = value;
                    result.YLogicalMax = cap.LogicalMax;
                    result.FoundY = true;
                }
            }

            return result;
        }

        /// <summary>
        /// 提取觸控資料
        /// </summary>
        private TouchData ExtractTouchData(IntPtr preparsedData, IntPtr pData, int dataLength)
        {
            var result = new TouchData();

            if (_tipSwitchCaps.HasValue)
                result.TipSwitch = ReadUsageValue(preparsedData, pData, dataLength, _tipSwitchCaps.Value);

            if (_confidenceCaps.HasValue)
                result.Confidence = ReadUsageValue(preparsedData, pData, dataLength, _confidenceCaps.Value);

            if (_contactIdCaps.HasValue)
                result.ContactId = ReadUsageValue(preparsedData, pData, dataLength, _contactIdCaps.Value);

            if (_scanTimeCaps.HasValue)
                result.ScanTime = ReadUsageValue(preparsedData, pData, dataLength, _scanTimeCaps.Value);

            if (_contactCountCaps.HasValue)
                result.ContactCount = ReadUsageValue(preparsedData, pData, dataLength, _contactCountCaps.Value);

            return result;
        }

        /// <summary>
        /// 讀取單個 Usage 值 (消除重複代碼)
        /// </summary>
        private uint ReadUsageValue(IntPtr preparsedData, IntPtr pData, int dataLength, HIDP_VALUE_CAPS caps)
        {
            uint value;
            HidP_GetUsageValue(
                HIDP_REPORT_TYPE.HidP_Input,
                caps.UsagePage,
                caps.LinkCollection,
                caps.Usage,
                out value,
                preparsedData,
                pData,
                (uint)dataLength);
            return value;
        }

        /// <summary>
        /// 儲存診斷資訊
        /// </summary>
        private void SaveDiagnosticInfo(CoordinateData coords, TouchData touchData)
        {
            LastRawX = (int)coords.RawX;
            LastRawY = (int)coords.RawY;
            LastConfidenceBit = touchData.Confidence != 0;
            LastContactId = (int)touchData.ContactId;
            LastScanTime = (int)touchData.ScanTime;
            LastContactCount = (int)touchData.ContactCount;
        }

        /// <summary>
        /// 判斷觸控狀態
        /// </summary>
        private bool DetermineTouchState(TouchData touchData)
        {
            if (_tipSwitchCaps.HasValue)
                return touchData.TipSwitch != 0;
            else
                return touchData.ContactCount > 0;
        }

        /// <summary>
        /// 驗證座標資料
        /// </summary>
        private bool ValidateCoordinates(CoordinateData coords, bool isTouching, out string errorMsg)
        {
            if (coords.RawX == 0 && coords.RawY == 0 && !isTouching)
            {
                errorMsg = "❌ 座標為 0,0 且無觸控";
                return false;
            }

            errorMsg = "";
            return true;
        }

        #endregion

        /// <summary>
        /// 取得 HID Descriptor 診斷報告
        /// </summary>
        public string GetDescriptorReport()
        {
            if (!_initialized)
                return "❌ 未初始化";

            StringBuilder report = new StringBuilder();
            report.AppendLine("=== 📋 HID Descriptor 報告 ===\n");
            report.AppendLine($"Usage Page: 0x{_caps.UsagePage:X2}");
            report.AppendLine($"Usage: 0x{_caps.Usage:X2}");
            report.AppendLine($"Input Report Length: {_caps.InputReportByteLength} bytes");
            report.AppendLine($"Number of Input Value Caps: {_caps.NumberInputValueCaps}");
            report.AppendLine();

            report.AppendLine("找到的 Usages:");
            if (_xCaps.HasValue)
                report.AppendLine($"  ✅ X (0x{USAGE_X:X2}): LogicalMin={_xCaps.Value.LogicalMin}, LogicalMax={_xCaps.Value.LogicalMax}");
            else
                report.AppendLine($"  ❌ X (0x{USAGE_X:X2}): 未找到");

            if (_yCaps.HasValue)
                report.AppendLine($"  ✅ Y (0x{USAGE_Y:X2}): LogicalMin={_yCaps.Value.LogicalMin}, LogicalMax={_yCaps.Value.LogicalMax}");
            else
                report.AppendLine($"  ❌ Y (0x{USAGE_Y:X2}): 未找到");

            if (_tipSwitchCaps.HasValue)
                report.AppendLine($"  ✅ Tip Switch (0x{USAGE_TIP_SWITCH:X2})");
            else
                report.AppendLine($"  ❌ Tip Switch (0x{USAGE_TIP_SWITCH:X2}): 未找到");

            if (_confidenceCaps.HasValue)
                report.AppendLine($"  ✅ Confidence (0x{USAGE_CONFIDENCE:X2})");
            else
                report.AppendLine($"  ⚠️  Confidence (0x{USAGE_CONFIDENCE:X2}): 未找到 (可能不支援)");

            if (_contactIdCaps.HasValue)
                report.AppendLine($"  ✅ Contact ID (0x{USAGE_CONTACT_ID:X2})");
            else
                report.AppendLine($"  ⚠️  Contact ID (0x{USAGE_CONTACT_ID:X2}): 未找到");

            if (_scanTimeCaps.HasValue)
                report.AppendLine($"  ✅ Scan Time (0x{USAGE_SCAN_TIME:X2})");
            else
                report.AppendLine($"  ⚠️  Scan Time (0x{USAGE_SCAN_TIME:X2}): 未找到");

            if (_contactCountCaps.HasValue)
                report.AppendLine($"  ✅ Contact Count (0x{USAGE_CONTACT_COUNT:X2})");
            else
                report.AppendLine($"  ⚠️  Contact Count (0x{USAGE_CONTACT_COUNT:X2}): 未找到");

            return report.ToString();
        }
    }
}
