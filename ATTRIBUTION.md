# 代碼出處與第三方聲明

本專案使用了以下技術、文件和參考資源。

---

## 📚 官方文件與 API

### Microsoft Windows SDK
- **用途**: Windows HID API、Raw Input API、SendInput API
- **授權**: Microsoft Platform SDK License Agreement
- **來源**: [Microsoft Docs - Human Interface Devices](https://docs.microsoft.com/en-us/windows-hardware/drivers/hid/)
- **引用部分**:
  - `HidP_GetCaps` - 取得 HID 裝置能力
  - `HidP_GetValueCaps` - 取得 HID 數值範圍
  - `HidP_GetUsageValue` - 讀取觸控板座標
  - `GetRawInputData` - 處理 Raw Input 資料
  - `SendInput` - 模擬滑鼠事件

### .NET 9.0（v8.72 起；v8.71 以前為 .NET 8.0）
- **用途**: 開發框架、Windows Forms UI
- **授權**: MIT License
- **來源**: [.NET Foundation](https://dotnet.microsoft.com/)

---

## 🔍 參考專案

### 1. ichisadashioko/windows-touchpad
- **Repository**: https://github.com/ichisadashioko/windows-touchpad
- **授權**: MIT License
- **參考內容**:
  - HID API 使用範例
  - 觸控板資料解析方式
  - Raw Input 註冊機制
- **使用方式**: 參考概念，本專案為原創實作

**MIT License (ichisadashioko/windows-touchpad)**:
```
MIT License

Copyright (c) 2020 ichisadashioko

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### 2. jrymk/precision-touchpad-advanced-gestures
- **Repository**: https://github.com/jrymk/precision-touchpad-advanced-gestures
- **授權**: GPL-3.0 License
- **參考內容**:
  - Precision Touchpad 手勢處理概念
  - 多點觸控事件處理
- **使用方式**: 僅參考概念和架構設計思路，**未使用任何 GPL-3.0 授權的代碼**

**重要聲明**：
本專案 (`touchpad-tap-right`，TouchpadTapRight) 採用 **MIT License**，與 `jrymk/precision-touchpad-advanced-gestures` 的 GPL-3.0 授權相容。我們僅參考該專案的**概念和設計思路**，所有實作代碼均為原創，未直接複製或衍生 GPL-3.0 授權的代碼。

---

## 🤖 AI 協作聲明

### Claude AI (Anthropic)
- **用途**: 開發協作、代碼審查、文件撰寫
- **服務提供者**: Anthropic
- **使用方式**:
  - 輔助程式設計與除錯
  - 技術文件撰寫
  - 架構設計諮詢
  - docs/architecture.md 架構文件

**聲明**:
- 所有代碼的最終決策權和版權歸專案作者所有
- AI 生成的內容經過人工審查和修改
- 本專案符合 MIT License 的開源要求

---

## 🛠️ 開發工具

### Visual Studio / Visual Studio Code
- **用途**: 整合開發環境
- **授權**: Microsoft Software License Terms
- **來源**: https://visualstudio.microsoft.com/

### Git
- **用途**: 版本控制
- **授權**: GPL-2.0 License
- **來源**: https://git-scm.com/

---

## 📦 第三方庫與依賴

本專案**不使用**任何第三方 NuGet 套件，僅使用 .NET 9.0 內建的標準庫：

- `System.Windows.Forms` - Windows Forms UI 框架
- `System.Runtime.InteropServices` - Windows API P/Invoke
- `System.Drawing` - 圖形繪製

---

## ✍️ 原創部分

以下部分為本專案的**原創設計與實作**，不基於任何第三方代碼：

### 核心演算法
- **右鍵區域偵測演算法** (`TapZoneDetector.cs`)
  - 可自訂區域百分比
  - 垂直分割線計算
  - 輕觸/長按/拖曳判定邏輯

- **多廠商 HID API 動態適應機制** (`HidApiParser.cs`)
  - 自動偵測觸控板尺寸
  - 動態解析座標範圍
  - 支援 ELAN、Liteon、Synaptics 等廠商

- **觸控板事件處理** (`TouchpadMonitor.cs`)
  - 輕觸右鍵模擬 (<150ms)
  - 長按拖曳模式 (>600ms)
  - 防誤觸機制

### 使用者介面
- **Modern UI 設計** (`ModernMainForm.cs`)
  - TableLayoutPanel 自適應佈局
  - 600×300 觸控板預覽區
  - Windows 10 標準配色
  - 即時座標顯示

- **診斷工具** (`DiagnosticWindow.cs`)
  - HID 裝置資訊診斷
  - 觸控資料即時監控
  - 診斷資料匯出功能

### 設定管理
- **Windows Registry 整合** (`WindowsTouchpadSettings.cs`)
  - 觸控板設定讀取
  - 系統相容性檢測

---

## 🙏 致謝

感謝以下資源和社群的支持：

- **Microsoft** - 提供完整的 Windows API 文件
- **GitHub 開源社群** - 提供參考專案和概念啟發
- **Anthropic** - 提供 Claude AI 協作工具
- **無障礙科技社群** - 提供使用者需求和回饋

---

## 📄 授權兼容性聲明

本專案採用 **MIT License**，與所有參考資源的授權兼容：

| 資源 | 授權 | 兼容性 |
|------|------|--------|
| Microsoft Windows SDK | Microsoft Platform SDK License | ✅ 兼容 |
| .NET 9.0 | MIT License | ✅ 兼容 |
| ichisadashioko/windows-touchpad | MIT License | ✅ 兼容 |
| jrymk/precision-touchpad-advanced-gestures | GPL-3.0 | ✅ 兼容（僅參考概念） |

**重要**: 本專案未包含任何 GPL-3.0 授權的代碼，所有實作均為原創。

---

## 📞 聯絡與問題回報

如有任何授權相關問題或疑慮，請透過以下方式聯絡：

- **GitHub Issues**: [專案 Issues 頁面]
- **Email**: [聯絡 Email]

---

**最後更新**: 2025-10-27
**專案版本**: v8.59
