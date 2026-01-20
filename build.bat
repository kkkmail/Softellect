@echo off
setlocal enabledelayedexpansion

REM ============================================================
REM Deterministic F# build script
REM - Forced restore
REM - Single-threaded build
REM - No node reuse
REM - Release configuration
REM ============================================================

set SOLUTION=SoftellectMain.slnx
set CONFIG=Release

REM Ensure we are running from the script directory
cd /d "%~dp0"

echo ============================================
echo Restoring NuGet packages (forced)...
echo ============================================

dotnet restore "%SOLUTION%" ^
    --force ^
    --no-cache ^
    -v:minimal

if errorlevel 1 (
    echo ERROR: dotnet restore failed.
    exit /b 1
)

echo.
echo ============================================
echo Building solution (strict, deterministic)...
echo ============================================

dotnet build "%SOLUTION%" ^
    --no-restore ^
    -c %CONFIG% ^
    /m:1 ^
    /nodeReuse:false ^
    -v:minimal

if errorlevel 1 (
    echo ERROR: dotnet build failed.
    exit /b 1
)

echo.
echo ============================================
echo Build completed successfully.
echo ============================================

endlocal
exit /b 0
