@echo off
chcp 65001 >nul
echo ============================================
echo  觸控板輕觸右鍵專案 - 自動清理腳本
echo  v1.0 - 2025-10-25
echo ============================================
echo.

:: 建立 Archive 目錄結構
echo [1/5] 建立 Archive 目錄結構...
mkdir "Archive\Deprecated_Code\Core" 2>nul
mkdir "Archive\Deprecated_Code\UI" 2>nul
mkdir "Archive\Releases" 2>nul
mkdir "docs\hardware" 2>nul
mkdir "openspec\changes\archive\2025-01-15-intel-driver-tap-limitation" 2>nul
mkdir "openspec\changes\archive\2025-01-20-csharp-implementation" 2>nul
echo    √ 完成
echo.

:: 移動廢棄的 Core 檔案
echo [2/5] 移動廢棄的 Core 檔案...
if exist "CSharp\TouchpadRightClick\Core\RawDataParser.cs" (
    move "CSharp\TouchpadRightClick\Core\RawDataParser.cs" "Archive\Deprecated_Code\Core\" >nul
    echo    √ RawDataParser.cs
)
if exist "CSharp\TouchpadRightClick\Core\SimplifiedHIDMonitor.cs" (
    move "CSharp\TouchpadRightClick\Core\SimplifiedHIDMonitor.cs" "Archive\Deprecated_Code\Core\" >nul
    echo    √ SimplifiedHIDMonitor.cs
)
if exist "CSharp\TouchpadRightClick\Core\HIDRawInput.cs" (
    move "CSharp\TouchpadRightClick\Core\HIDRawInput.cs" "Archive\Deprecated_Code\Core\" >nul
    echo    √ HIDRawInput.cs
)
if exist "CSharp\TouchpadRightClick\Core\RightClickSimulator.cs" (
    move "CSharp\TouchpadRightClick\Core\RightClickSimulator.cs" "Archive\Deprecated_Code\Core\" >nul
    echo    √ RightClickSimulator.cs
)
if exist "CSharp\TouchpadRightClick\Core\CoordinateParser.cs" (
    move "CSharp\TouchpadRightClick\Core\CoordinateParser.cs" "Archive\Deprecated_Code\Core\" >nul
    echo    √ CoordinateParser.cs
)
echo    √ 完成
echo.

:: 移動廢棄的 UI 檔案
echo [3/5] 移動廢棄的 UI 檔案...
if exist "CSharp\TouchpadRightClick\UI\MainForm.cs" (
    move "CSharp\TouchpadRightClick\UI\MainForm.cs" "Archive\Deprecated_Code\UI\" >nul
    echo    √ MainForm.cs
)
if exist "CSharp\TouchpadRightClick\UI\SimpleMainForm.cs" (
    move "CSharp\TouchpadRightClick\UI\SimpleMainForm.cs" "Archive\Deprecated_Code\UI\" >nul
    echo    √ SimpleMainForm.cs
)
echo    √ 完成
echo.

:: 移動硬體文件
echo [4/5] 移動硬體文件到 docs/...
if exist "ELAN官方軟體資訊.md" (
    move "ELAN官方軟體資訊.md" "docs\hardware\" >nul
    echo    √ ELAN官方軟體資訊.md
)
if exist "HidP_API_完整說明.md" (
    move "HidP_API_完整說明.md" "docs\hardware\" >nul
    echo    √ HidP_API_完整說明.md
)
if exist "Intel_PTP_驗證清單.md" (
    move "Intel_PTP_驗證清單.md" "docs\hardware\" >nul
    echo    √ Intel_PTP_驗證清單.md
)
if exist "Lite-On觸控板軟體調查結果.md" (
    move "Lite-On觸控板軟體調查結果.md" "docs\hardware\" >nul
    echo    √ Lite-On觸控板軟體調查結果.md
)
if exist "MSI_Katana_17HX_觸控板識別結果.md" (
    move "MSI_Katana_17HX_觸控板識別結果.md" "docs\hardware\" >nul
    echo    √ MSI_Katana_17HX_觸控板識別結果.md
)
echo    √ 完成
echo.

:: 重組 OpenSpec 結構
echo [5/5] 重組 OpenSpec 結構...
if exist "openspec\changes\001-intel-driver-tap-limitation.md" (
    move "openspec\changes\001-intel-driver-tap-limitation.md" "openspec\changes\archive\2025-01-15-intel-driver-tap-limitation\proposal.md" >nul
    echo    √ 001-intel-driver-tap-limitation.md
)
if exist "openspec\proposals\002-csharp-implementation.md" (
    move "openspec\proposals\002-csharp-implementation.md" "openspec\changes\archive\2025-01-20-csharp-implementation\proposal.md" >nul
    echo    √ 002-csharp-implementation.md
)
:: 刪除空的 proposals 目錄
if exist "openspec\proposals" (
    rmdir "openspec\proposals" 2>nul
    echo    √ 移除空的 proposals 目錄
)
:: 移除根目錄的 AGENTS.md (已在 openspec/ 中)
if exist "AGENTS.md" (
    del "AGENTS.md" >nul
    echo    √ 移除重複的 AGENTS.md
)
echo    √ 完成
echo.

echo ============================================
echo  清理完成!
echo ============================================
echo.
echo 下一步:
echo  1. 檢查 Archive 目錄確認檔案已移動
echo  2. 執行編譯測試: dotnet build
echo  3. 查看 CLEANUP_PLAN.md 了解詳情
echo.
pause
