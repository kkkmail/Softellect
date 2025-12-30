@echo off
setlocal enableextensions

rem --- Parameters / relative paths ---
set "KEY_DIR=..\..\..\..\!Keys"
set "OUTPUT_DIR=bin\ARM64\Release\net10.0-android"
set "APK_NAME=com.softellect.vpn-Signed.apk"

rem --- Args ---
set "USER_NAME=%~1"

rem --- Derived paths (safe: not inside a (...) block) ---
set "CONFIG=%KEY_DIR%\vpn_config.json"
if not "%USER_NAME%"=="" set "USER_CONFIG=%KEY_DIR%\vpn_config_%USER_NAME%.json"
if not "%USER_NAME%"=="" set "USER_OUT=%OUTPUT_DIR%\%USER_NAME%"
set "APK_PATH=%OUTPUT_DIR%\%APK_NAME%"

rem --- If user name is provided, copy user config ---
if "%USER_NAME%"=="" goto build

if not exist "%USER_CONFIG%" (
    echo ERROR: User config not found: "%USER_CONFIG%"
    exit /b 2
)

copy /y "%USER_CONFIG%" "%CONFIG%" >nul
if errorlevel 1 (
    echo ERROR: Failed to copy "%USER_CONFIG%" to "%CONFIG%"
    exit /b 3
)

:build
dotnet build -c Release -f net10.0-android /p:AndroidBuildApplicationPackage=True /p:AndroidPackageFormat=apk /p:GenerateAppBundle=false
if errorlevel 1 (
    echo ERROR: Build failed
    exit /b %errorlevel%
)

rem --- Copy APK to user-specific folder (only if user provided) ---
if "%USER_NAME%"=="" exit /b 0

if not exist "%APK_PATH%" (
    echo ERROR: APK not found: "%APK_PATH%"
    exit /b 4
)

if not exist "%USER_OUT%" mkdir "%USER_OUT%" >nul
copy /y "%APK_PATH%" "%USER_OUT%\%APK_NAME%" >nul
if errorlevel 1 (
    echo ERROR: Failed to copy APK to "%USER_OUT%"
    exit /b 5
)

exit /b 0
