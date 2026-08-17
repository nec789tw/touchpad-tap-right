@echo off
chcp 65001 >nul
echo ======================================================================
echo 觸控板輕觸右鍵 (C#) - 建置腳本
echo ======================================================================
echo.

REM 檢查 dotnet CLI
where dotnet >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo ❌ 找不到 dotnet CLI！
    echo    請安裝 .NET 9 SDK: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo ✅ 找到 dotnet CLI
dotnet --version
echo.

REM 清理舊的建置檔案
echo 清理舊的建置檔案...
if exist bin rmdir /s /q bin
if exist obj rmdir /s /q obj
echo.

REM 建置專案 (Release)
echo 開始建置 (Release 模式)...
dotnet build -c Release
if %ERRORLEVEL% NEQ 0 (
    echo ❌ 建置失敗！
    pause
    exit /b 1
)
echo.

REM 發布單一 EXE
echo 發布單一 EXE (Self-Contained, Compressed)...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
if %ERRORLEVEL% NEQ 0 (
    echo ❌ 發布失敗！
    pause
    exit /b 1
)
echo.

REM 建立可攜式套件
echo 建立可攜式套件...
set OUTPUT_DIR=.\TouchpadTapRight_Portable
if exist %OUTPUT_DIR% rmdir /s /q %OUTPUT_DIR%
mkdir %OUTPUT_DIR%

copy "bin\Release\net9.0-windows\win-x64\publish\TouchpadTapRight.exe" %OUTPUT_DIR%\ >nul
copy README.md %OUTPUT_DIR%\ >nul

echo.
echo ======================================================================
echo ✅ 建置完成！
echo ======================================================================
echo.
echo 執行檔位置:
echo   bin\Release\net9.0-windows\win-x64\publish\TouchpadTapRight.exe
echo.
echo 可攜式套件:
echo   %OUTPUT_DIR%\
echo.
echo 檔案大小:
for %%F in ("%OUTPUT_DIR%\TouchpadTapRight.exe") do echo   %%~zF bytes (約 %%~zF/1024/1024 MB)
echo.
echo ======================================================================
pause
