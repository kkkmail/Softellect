@echo off
setlocal enabledelayedexpansion

set "SOLUTION=%~dp0SoftellectMain.slnx"
set "CONFIG=Release"
set "PLATFORM=x64"

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
  echo ERROR: vswhere.exe not found at "%VSWHERE%".
  exit /b 1
)

for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -property installationPath`) do set "VSINSTALL=%%I"
if "%VSINSTALL%"=="" (
  echo ERROR: Visual Studio installation not found by vswhere.
  exit /b 1
)

set "MSBUILD=%VSINSTALL%\MSBuild\Current\Bin\MSBuild.exe"
if not exist "%MSBUILD%" (
  echo ERROR: MSBuild.exe not found at "%MSBUILD%".
  exit /b 1
)

echo Using MSBuild: "%MSBUILD%"
echo Solution: "%SOLUTION%"
echo Configuration=%CONFIG% Platform=%PLATFORM%

set "NUGET_PACKAGES=%UserProfile%\.nuget\packages"
set "RestoreNoCache=true"
set "RestoreForce=true"

echo ============================================
echo Restore (forced, no-cache)...
echo ============================================
"%MSBUILD%" "%SOLUTION%" /t:Restore /p:Configuration=%CONFIG% /p:Platform=%PLATFORM% /p:RestoreNoCache=true /p:RestoreForce=true /p:RestoreForceEvaluate=true /p:RestoreUseLegacyDependencyResolver=true /p:RestoreDisableParallel=true /p:RestoreUseStaticGraphEvaluation=false /m:1 /nodeReuse:false /v:minimal
if errorlevel 1 exit /b 1

echo ============================================
echo Build (no restore, x64, single-proc)...
echo ============================================
"%MSBUILD%" "%SOLUTION%" /t:Build /p:Configuration=%CONFIG% /p:Platform=%PLATFORM% /p:RestoreDuringBuild=false /m:1 /nodeReuse:false /v:minimal
if errorlevel 1 exit /b 1

echo ============================================
echo OK
echo ============================================
exit /b 0
