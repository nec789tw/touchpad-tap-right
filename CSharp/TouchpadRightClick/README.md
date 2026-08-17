# 觸控板輕觸右鍵 (C# 版本)

> 為身障者提供觸控板輕觸右鍵功能的無障礙工具

## 專案背景

本工具專為只能用單指輕觸觸控板的使用者設計（身障者狀況不一，共通點是無法按壓與多指手勢）。透過 Windows HID Raw Input API 直接監聽觸控板接觸事件，實現完全靜態輕觸即可觸發右鍵選單的功能。

詳細架構說明請參閱：[architecture.md](../../docs/architecture.md)

## 系統需求

- **作業系統**: Windows 11 (Build 22000 或以上)
- **觸控板**: Precision Touchpad (符合 HID Protocol)
- **.NET Runtime**: 內建於 EXE 中 (Self-Contained, .NET 9)

## 功能特色

1. **靜態輕觸偵測**
   - 透過 HID Raw Input 監聯觸控板原始接觸事件
   - 支援完全靜止的手指輕觸 (無需移動)
   - 輕觸判定: 接觸時間 < 300ms、移動量 < 2%

2. **右鍵區域**
   - 觸控板右下角為右鍵區域
   - 可調整寬度比例 (10%-80%)
   - 可調整靈敏度 (垂直分割比例)
   - 視覺化預覽介面即時呈現區域範圍

3. **長按拖曳**
   - 在右鍵區域長按 > 600ms 進入拖曳模式
   - 模擬左鍵按住拖曳操作

4. **左鍵誤觸發抑制**
   - GlobalMouseHook (WH_MOUSE_LL) 攔截低階滑鼠訊息
   - 右鍵觸發後短暫時間內抑制硬體驅動同時產生的左鍵事件

5. **HID API 動態適應**
   - 使用 HidP_* API 動態解析觸控板 HID Descriptor
   - 自動偵測觸控板尺寸和座標範圍
   - 不限定特定廠商,已測試 ELAN 及 Liteon 觸控板

## 檔案結構

```
CSharp/TouchpadRightClick/
├── Core/
│   ├── DiagnosticLevel.cs        # 診斷等級列舉
│   ├── GlobalMouseHook.cs        # WH_MOUSE_LL 低階滑鼠鉤子
│   ├── HidApiParser.cs           # HID Descriptor 動態解析
│   ├── MouseSimulator.cs         # SendInput API 右鍵/拖曳模擬 (static)
│   ├── TapZoneDetector.cs        # 區域判定 + 輕觸/拖曳辨識
│   ├── TouchEventArgs.cs         # 觸控事件資料
│   └── TouchpadMonitor.cs        # 觸控板 Raw Input 監聽核心
├── UI/
│   ├── ModernMainForm.cs         # 主視窗 (WinForms)
│   └── SimpleTouchpadPreview.cs  # 觸控板視覺化預覽元件
├── Utils/
│   └── HidDiagnostic.cs          # HID 裝置診斷工具
├── Resources/
│   └── app.ico                   # 應用程式圖示
├── Program.cs                    # 進入點
├── TouchpadRightClick.csproj     # 專案檔 (net9.0-windows)
└── CHANGELOG.md                  # 更新日誌
```

## 建置說明

### 使用 dotnet CLI

```bash
cd CSharp/TouchpadRightClick

# 建置 (Debug)
dotnet build

# 發布單一 EXE (Release, Self-Contained)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true

# 產出位置
# bin\Release\net9.0-windows\win-x64\publish\TouchpadTapRight.exe
```

### 建置產物

- **EXE 大小**: 約 50 MB (含 .NET 9 Runtime)
- **無需安裝**: 直接執行 EXE，不需額外安裝 .NET Runtime

## 使用說明

### 第一次執行

1. 執行 `TouchpadTapRight.exe`
2. 若出現 Windows Defender 警告，點擊「詳細資訊」→「仍要執行」
3. 啟動後會顯示操作說明對話框，閱讀後按確定進入主視窗

### 調整右鍵區域

1. 在主視窗中調整「右鍵區域寬度」滑桿
2. 調整「靈敏度」與「垂直分割」等參數
3. 右側預覽面板即時顯示目前區域範圍

### 日常使用

- **右鍵觸發**: 輕觸觸控板右下角區域 (無需按壓，靜止輕觸即可)
- **拖曳操作**: 在右鍵區域長按 > 600ms 後移動手指
- **其他區域**: 左側及上方區域交由 Windows 原生處理，不受影響

## 效能指標

- **CPU 使用率**: < 2%
- **記憶體佔用**: ~70 MB (Self-Contained 含 .NET Runtime)
- **觸控延遲**: ~50ms

## 已知限制

1. **觸控板相容性**
   - 需要觸控板支援 HID Protocol (UsagePage=0x0D, Usage=0x05)
   - 已測試: ELAN、Liteon
   - 未測試: Synaptics

2. **作業系統**
   - 目前僅測試 Windows 11
   - Windows 10 理論上可用但未驗證

## 疑難排解

### Q: 程式無法偵測到觸控板

**A**: 檢查觸控板驅動：
1. 開啟「裝置管理員」
2. 展開「人性化介面裝置」
3. 確認有「HID-Compliant Touch Pad」或類似裝置
4. 若沒有，可能需要更新觸控板驅動程式

### Q: 輕觸沒有反應

**A**: 檢查以下項目：
1. 確認主視窗中監控狀態為「執行中」
2. 確認輕觸位置在右下角右鍵區域內
3. 嘗試增加右鍵區域寬度
4. 確認輕觸時手指保持靜止 (移動量需 < 2%)

## 授權

本專案採用 MIT 授權條款。

## 致謝

- 參考專案：[emoacht/RawInput.Touchpad](https://github.com/emoacht/RawInput.Touchpad)
- 技術文件：[Microsoft Raw Input API](https://docs.microsoft.com/en-us/windows/win32/inputdev/raw-input)

---

**為身障者賦能，讓科技更有溫度**
