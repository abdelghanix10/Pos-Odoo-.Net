@echo off
echo ========================================================
echo   1. Publishing Odoo 20 POS (.NET 10 Self-Contained)
echo ========================================================
dotnet publish -c Release -r win-x64 --self-contained true -o .\publish\win-x64
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] dotnet publish failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ========================================================
echo   2. Building Windows Installer (OdooPOS_Setup.exe)
echo ========================================================
set ISCC_PATH="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if exist %ISCC_PATH% (
    %ISCC_PATH% setup.iss
    echo.
    echo [SUCCESS] Installer created in .\Installer\OdooPOS_Setup_v1.0.0.exe
    explorer .\Installer
) else (
    echo [INFO] Inno Setup not found at default path.
    echo Please open setup.iss in Inno Setup Compiler and press Ctrl+F9.
    explorer .\publish\win-x64
)
pause