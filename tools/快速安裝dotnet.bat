@echo off
chcp 65001 >nul
echo.
echo ======================================================================
echo  .NET 8 SDK 快速安裝
echo ======================================================================
echo.
echo 即將開啟 Microsoft Store 安裝 .NET SDK
echo (這是最快的安裝方式)
echo.
pause

REM 開啟 Microsoft Store 的 .NET SDK 頁面
start ms-windows-store://pdp/?ProductId=9PJLJW9W94PV

echo.
echo 請在 Microsoft Store 中：
echo 1. 點擊「取得」或「安裝」
echo 2. 等待安裝完成
echo 3. 安裝完成後，回到此視窗按任意鍵
echo.
pause

echo.
echo 正在驗證安裝...
"C:\Program Files\dotnet\dotnet.exe" --version 2>nul
if %ERRORLEVEL% EQU 0 (
    echo ✅ 安裝成功！
    echo.
    echo 現在可以執行：
    echo    cd TouchpadRightClick
    echo    build.bat
) else (
    echo ⚠️  尚未偵測到 SDK
    echo 請重新開啟命令提示字元後再試
)
echo.
pause
