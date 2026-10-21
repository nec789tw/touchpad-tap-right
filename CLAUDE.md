# 專案開發日誌

架構說明見 [docs/architecture.md](docs/architecture.md)。
**接手請先開 [docs/交接手冊.html](docs/交接手冊.html)**（由 `tools/產生交接網頁.py` 從 md 產生，不要手改 HTML）；
還有什麼沒做看 [docs/未完成盤點總表.md](docs/未完成盤點總表.md)，做了什麼看 [docs/工作狀態.md](docs/工作狀態.md)。

## v8.72 開源釋出版：設定檔＋開機自啟＋.NET 9 深色＋logo＋保險版＋測試指南 (程式碼完成，觸控行為交社群 ⏳)

- `%LOCALAPPDATA%\TouchpadTapRight\config.json`（`Utils/AppSettings.cs`）：滑桿持久化＋進階門檻（拖曳容差／長按／輕觸／離手／邊緣）全部可調。
- 開機自啟（`Utils/AutoStart.cs`，schtasks ONLOGON HIGHEST）＋「啟動後自動開始監控」。**成功路徑本機沙箱無法測**（Access denied），交社群。
- .NET 9 + `SetColorMode`：原生控制項跟著深色。logo：`tools/make_logo.py`。Release `v8.69-legacy` 當基準（pre-release）。
- 決策：不買憑證、不做實驗性 Tip Switch、實機驗證交社群。細目 `docs/工作狀態.md`。

## v8.71 寫檔日誌＋改名 TouchpadTapRight＋UI 翻新 (程式碼完成，UI 已截圖驗證，觸控行為待實機 ⏳)

- 對外名稱 **TouchpadTapRight ／ 觸控板輕觸右鍵**：repo `nec789tw/touchpad-tap-right`、exe `TouchpadTapRight.exe`、Release 資產 `TouchpadTapRight-vX.YY-win-x64.exe`。C# namespace／資料夾維持 `TouchpadRightClick`。
- 寫檔日誌 `Utils/FileLogger.cs` → `%LOCALAPPDATA%\TouchpadTapRight\logs\app.log`（只記狀態事件，不記逐筆座標；1 MB 滾動）。進階診斷頁「開啟日誌資料夾」。
- UI：`UI/Theme.cs` 色票／深色模式／44px 按鈕／圓角；`ModernMainForm` 改百分比版面＋`AutoScaleMode.Dpi`＋AccessibleName＋Alt 快捷鍵；預覽響應式並畫邊緣保護帶。細目見 `docs/工作狀態.md`。

## v8.70 review 修正＋檢查更新＋自動發布 (程式碼完成，觸控行為待實機驗證 ⏳)

- 版本單一來源：csproj `<Version>`（視窗標題／檢查更新／Release 資產名都讀它）。發新版＝改這行 → tag 同號 → push。
- 檢查更新按鈕（`Utils/UpdateChecker.cs`）：比對 GitHub Releases 最新 tag，有新版開瀏覽器到下載頁。
- `.github/workflows/release.yml`：推 `v*` tag 自動 publish 並上傳 `TouchpadTapRight-vX.YY-win-x64.exe`。
- Code review 修 11 個確認的 bug（含 A2：觸控結束偵測改 WinForms Timer，回到 UI 執行緒）。細目與證據見 `docs/工作狀態.md`。
- ⚠️ 開發機無觸控板；觸控路徑改動的實機驗證有限，公開後靠社群回報（`docs/未完成盤點總表.md`【高】）。

## v8.69 大規模代碼清理 (已完成 ✅)

純修繕、不改功能：修 4 個 bug（`Stop()` 完整釋放 RIDEV_REMOVE＋鉤子＋Timer、Form 關閉防 ObjectDisposedException、標準 Dispose Pattern、`MouseSimulator` 改 SendInput），刪 ~1,600 行死碼與 168 個版本註解，預設值統一到 `TapZoneDetector` 常數，openspec/ 併入 `docs/architecture.md`。剩下的 C1–C4 在 [docs/未完成盤點總表.md](docs/未完成盤點總表.md)。

---

## 開發規範

### 版本與紀錄
- 版本號單一來源：csproj `<Version>`；發新版＝改這行＋打同號 tag（`release.yml` 會檢查兩者一致）。
- 對外版本紀錄寫 README「版本說明」表；細節與程式碼證據寫 `docs/工作狀態.md`；未完成寫 `docs/未完成盤點總表.md`。改完 md 重跑 `tools/產生交接網頁.py`，跟程式碼放同一個 commit。
- **不另維護 CHANGELOG**（2026-10-21 刪除；v8.69 以前的舊日誌：`git show 556c15f:CSharp/TouchpadRightClick/CHANGELOG.md`）。
- **程式碼裡不寫版本註解**（`// v8.xx: …`）：v8.69 清掉過 168 個；改了什麼看 git 與 `docs/工作狀態.md`。
- 重大變更要更新本檔。

### 代碼品質標準
- 無 Magic Numbers（門檻預設值集中在 `TapZoneDetector.DEFAULT_*`）
- 方法長度 < 50 行
- 無代碼重複
- 資源總是釋放（Dispose Pattern）
- 異常處理完整
