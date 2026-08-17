# 架構總覽

## 專案目的

輔助科技專案 — 為無法按壓觸控板的使用者(行動不便、僅能輕觸)提供「輕觸即右鍵」功能。核心目標:

- 在右下角區域輕觸時模擬滑鼠右鍵
- 不干擾 Windows 原生觸控板功能
- 無需管理員權限運行(鉤子例外)
- 動態適應不同廠商觸控板

## 資料流

```
觸控板硬體 (HID Report)
    ↓
Windows Raw Input API (WM_INPUT)
    ↓
HidApiParser         ← 動態解析 HID Descriptor,正規化 0.0-1.0
    ↓
TapZoneDetector      ← 區域判定 + 輕觸/拖曳辨識
    ↓
MouseSimulator       ← 模擬右鍵 / 左鍵拖曳
    ↓
GlobalMouseHook      ← 抑制硬體驅動同時產生的誤觸發左鍵
```

## 主要元件

| 元件 | 職責 | 檔案 |
|---|---|---|
| `TouchpadMonitor` | 註冊 Raw Input、訊息泵、狀態機 | Core/TouchpadMonitor.cs |
| `HidApiParser` | 用 `HidP_*` API 解析 PreparsedData,抽出 X/Y/TipSwitch/Contact 等 Usage | Core/HidApiParser.cs |
| `TapZoneDetector` | 輕觸判定(距離+時間閾值)、區域分類、長按拖曳偵測 | Core/TapZoneDetector.cs |
| `MouseSimulator` | `SendInput` API 包裝(模擬右鍵/左鍵拖曳) | Core/MouseSimulator.cs |
| `GlobalMouseHook` | WH_MOUSE_LL 鉤子,攔截右下角觸控同時產生的左鍵 | Core/GlobalMouseHook.cs |
| `ModernMainForm` | WinForms UI,設定、診斷、預覽 | UI/ModernMainForm.cs |

## 觸控板相容性

| 品牌 | 狀態 | 備註 |
|---|---|---|
| ELAN(義隆電子) | ✅ 完全支援 | 主要開發測試對象 |
| Liteon(光寶科技) | ✅ v8.51 後支援 | 座標在 LinkCollection 3+,需遍歷所有 ValueCaps |
| Synaptics | ⚠️ 未實測 | 需使用 Microsoft Precision Touchpad 驅動 |
| Alps | ⚠️ 未實測 | — |

### HID Report 結構差異

```
ELAN:
  LinkCollection 0: X, Y, ContactCount, ScanTime, TipSwitch

Liteon:
  LinkCollection 0: ContactCount, ScanTime
  LinkCollection 3+: X, Y    ← 座標在不同的 LinkCollection
  特殊: 缺少 Confidence (0x47) 欄位
```

關鍵教訓:不能假設 X/Y 一定在第一個 LinkCollection,必須遍歷所有 ValueCaps 並選擇真正有數據的那組。

## 效能指標

| 指標 | 目標 | 實測 |
|---|---|---|
| 輕觸偵測延遲 | < 100 ms | ~50 ms |
| CPU 使用率 | < 5% | < 2% |
| 記憶體佔用 | < 50 MB | ~70 MB(single-file self-contained) |
| 啟動時間 | < 3 s | ~1 s |

## 外部依賴

### Windows API
- **User32.dll** — `SendInput` / `SetWindowsHookEx` / `RegisterRawInputDevices`
- **Hid.dll** — `HidP_GetCaps`, `HidP_GetValueCaps`, `HidP_GetUsageValue`
- **Raw Input API** — `RegisterRawInputDevices`, `GetRawInputData`, `GetRawInputDeviceInfo`

### .NET
- .NET 9.0（WinForms；v8.72 起，為了 SetColorMode 深色原生控制項）
- System.Windows.Forms
- System.Runtime.InteropServices(P/Invoke)

### 參考專案
- [emoacht/RawInput.Touchpad](https://github.com/emoacht/RawInput.Touchpad) — HID API 解析、LinkCollection 遍歷策略

## 技術約束

1. 必須運行於 Windows 10/11
2. 不能破壞現有觸控板功能(RIDEV_INPUTSINK,不攔截 Windows)
3. 全局滑鼠鉤子需要以管理員身分運行(這是目前唯一的權限需求)
4. 需要 .NET 9.0 Runtime,或使用 self-contained 部署(約 70 MB;Release 資產即為 self-contained 單檔)

## 建置與發佈

```powershell
# 開發建置
cd CSharp/TouchpadRightClick
dotnet build -c Release

# 發佈單檔案 (self-contained)
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true
```
