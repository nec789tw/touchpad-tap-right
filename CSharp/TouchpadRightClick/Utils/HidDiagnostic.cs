using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace TouchpadRightClick.Utils
{
    /// <summary>
    /// HID API 硬體檢測工具
    /// 用於診斷觸控板的 HID Descriptor 結構,找出所有可能的 X/Y 座標來源
    /// </summary>
    public class HidDiagnostic
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
            public ushort Reserved2a;
            public ushort Reserved2b;
            public ushort Reserved2c;
            public ushort Reserved2d;
            public ushort Reserved2e;
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

            public ushort Usage => IsRange ? UsageMin : UsageMin;
        }

        [DllImport("hid.dll", SetLastError = true)]
        private static extern uint HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS caps);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern uint HidP_GetValueCaps(
            HIDP_REPORT_TYPE reportType,
            [Out] HIDP_VALUE_CAPS[] valueCaps,
            ref ushort valueCapsLength,
            IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern uint HidP_GetUsageValue(
            HIDP_REPORT_TYPE reportType,
            ushort usagePage,
            ushort linkCollection,
            ushort usage,
            out uint usageValue,
            IntPtr preparsedData,
            IntPtr report,
            uint reportLength);

        private const uint HIDP_STATUS_SUCCESS = 0x00110000;

        // Usage Page 定義
        private const ushort USAGE_PAGE_GENERIC_DESKTOP = 0x01;
        private const ushort USAGE_PAGE_DIGITIZER = 0x0D;

        // Generic Desktop Usage 定義
        private const ushort USAGE_X = 0x30;
        private const ushort USAGE_Y = 0x31;

        // Digitizer Usage 定義
        private const ushort USAGE_TIP_SWITCH = 0x42;
        private const ushort USAGE_CONTACT_ID = 0x51;
        private const ushort USAGE_CONTACT_COUNT = 0x54;
        private const ushort USAGE_SCAN_TIME = 0x56;
        private const ushort USAGE_CONFIDENCE = 0x47;

        #endregion

        public class HidCapInfo
        {
            public ushort UsagePage { get; set; }
            public ushort Usage { get; set; }
            public ushort LinkCollection { get; set; }
            public int LogicalMin { get; set; }
            public int LogicalMax { get; set; }
            public ushort BitSize { get; set; }
            public ushort ReportCount { get; set; }
            public string UsageName { get; set; }
        }

        /// <summary>
        /// 掃描 HID Descriptor,列出所有 Value Caps
        /// </summary>
        public static List<HidCapInfo> EnumerateAllCaps(IntPtr preparsedData)
        {
            var result = new List<HidCapInfo>();

            // 獲取 HID Caps
            if (HidP_GetCaps(preparsedData, out HIDP_CAPS caps) != HIDP_STATUS_SUCCESS)
            {
                return result;
            }

            // 獲取所有 Input Value Caps
            if (caps.NumberInputValueCaps > 0)
            {
                var valueCaps = new HIDP_VALUE_CAPS[caps.NumberInputValueCaps];
                ushort length = caps.NumberInputValueCaps;

                if (HidP_GetValueCaps(HIDP_REPORT_TYPE.HidP_Input, valueCaps, ref length, preparsedData) == HIDP_STATUS_SUCCESS)
                {
                    foreach (var cap in valueCaps)
                    {
                        result.Add(new HidCapInfo
                        {
                            UsagePage = cap.UsagePage,
                            Usage = cap.Usage,
                            LinkCollection = cap.LinkCollection,
                            LogicalMin = cap.LogicalMin,
                            LogicalMax = cap.LogicalMax,
                            BitSize = cap.BitSize,
                            ReportCount = cap.ReportCount,
                            UsageName = GetUsageName(cap.UsagePage, cap.Usage)
                        });
                    }
                }
            }

            return result;
        }

        private static string GetUsageName(ushort usagePage, ushort usage)
        {
            if (usagePage == USAGE_PAGE_GENERIC_DESKTOP)
            {
                return usage switch
                {
                    USAGE_X => "X",
                    USAGE_Y => "Y",
                    _ => $"Generic Desktop 0x{usage:X2}"
                };
            }
            else if (usagePage == USAGE_PAGE_DIGITIZER)
            {
                return usage switch
                {
                    USAGE_TIP_SWITCH => "Tip Switch",
                    USAGE_CONTACT_ID => "Contact ID",
                    USAGE_CONTACT_COUNT => "Contact Count",
                    USAGE_SCAN_TIME => "Scan Time",
                    USAGE_CONFIDENCE => "Confidence",
                    _ => $"Digitizer 0x{usage:X2}"
                };
            }
            return $"Unknown (Page=0x{usagePage:X2}, Usage=0x{usage:X2})";
        }

    }
}
