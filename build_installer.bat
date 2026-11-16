@echo off
:: Always switch to the folder where this .bat file is located
cd /d "%~dp0"

if not exist "Pos.csproj" (
    echo [ERROR] Pos.csproj was not found in: %CD%
    echo Please place build_installer.bat in the same folder as Pos.csproj!
    pause
    exit /b 1
)

echo ========================================================
echo   1. Publishing Odoo 20 POS (.NET 10 Self-Contained)
echo ========================================================
dotnet publish "Pos.csproj" -c Release -r win-x64 --self-contained true -o ".\publish\win-x64"
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] dotnet publish failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ========================================================
echo   2. Building Windows Installer (OdooPOS_Setup.exe)
echo ========================================================
set ISCC_PATH=""
if exist "C:\Program Files\Inno Setup 7\ISCC.exe" set ISCC_PATH="C:\Program Files\Inno Setup 7\ISCC.exe"
if exist "%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe" set ISCC_PATH="%LOCALAPPDATA%\Programs\Inno Setup 7\ISCC.exe"

if not %ISCC_PATH%=="" (
    %ISCC_PATH% "setup.iss"
    echo.
    echo [SUCCESS] Installer created in .\Installer\OdooPOS_Setup_v1.0.0.exe
    explorer .\Installer
) else (
    echo [INFO] Inno Setup not found at default path.
    echo Please open setup.iss in Inno Setup Compiler and press Ctrl+F9.
    explorer .\publish\win-x64
)
pause