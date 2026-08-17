@echo off
chcp 65001 >nul
color 0B
echo.
echo ========================================
echo    簽署 v8.5 和 v8.6 兩個版本
echo ========================================
echo.
echo 此工具將簽署兩個測試版本:
echo   v8.5 穩定版 - 觸碰結束時觸發
echo   v8.6 快速版 - 80ms 快速觸發
echo.
echo 重要: 請以管理員身分執行
echo.
echo 步驟:
echo   1. 右鍵點擊此檔案
echo   2. 選擇「以系統管理員身分執行」
echo.
pause
echo.

powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%~dp0建立自簽憑證_修正版.ps1"

if %errorlevel% equ 0 (
    echo.
    echo ========================================
    echo 成功！兩個版本都已簽署
    echo ========================================
    echo.
) else (
    echo.
    echo ========================================
    echo 錯誤！請檢查上方的錯誤訊息
    echo ========================================
    echo.
)

pause
