@echo off
chcp 65001 >nul
echo ======================================================================
echo .NET 8 SDK 自動安裝腳本
echo ======================================================================
echo.

REM 檢查是否已安裝
where dotnet >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo ✅ 已安裝 .NET SDK
    dotnet --version
    echo.
    pause
    exit /b 0
)

echo ⚠️  未偵測到 .NET SDK
echo.
echo 正在下載 .NET 8 SDK 安裝程式...
echo.

REM 下載 .NET 8 SDK (x64)
set DOWNLOAD_URL=https://download.visualstudio.microsoft.com/download/pr/4e3d9615-b5e0-48c1-8f3c-a3c1c68e9e0c/4c02e23d80e1b9c85c48c7ab45d2f192/dotnet-sdk-8.0.101-win-x64.exe
set INSTALLER_FILE=%TEMP%\dotnet-sdk-8.0-installer.exe

echo 下載位置: %DOWNLOAD_URL%
echo 暫存檔案: %INSTALLER_FILE%
echo.

REM 使用 PowerShell 下載
powershell -Command "& {[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri '%DOWNLOAD_URL%' -OutFile '%INSTALLER_FILE%'}"

if %ERRORLEVEL% NEQ 0 (
    echo ❌ 下載失敗！
    echo.
    echo 請手動下載安裝：
    echo https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

echo ✅ 下載完成！
echo.
echo 正在啟動安裝程式...
echo 請按照安裝精靈的指示完成安裝
echo.
pause

REM 執行安裝程式
start /wait "" "%INSTALLER_FILE%"

echo.
echo 安裝完成後，請：
echo 1. 關閉此視窗
echo 2. 重新開啟命令提示字元
echo 3. 執行 build.bat 建置專案
echo.
pause

REM 清理暫存檔
del "%INSTALLER_FILE%"
