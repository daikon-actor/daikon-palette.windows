@echo off
rem DaikonPalette build script (uses csc.exe bundled with Windows / .NET Framework 4.x)
rem Output: dist\DaikonPalette.exe
rem Usage : build.bat [/nopause]
setlocal EnableDelayedExpansion
cd /d "%~dp0"

set "NOPAUSE="
for %%A in (%*) do (
  if /i "%%~A"=="/nopause" set "NOPAUSE=1"
)

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found. .NET Framework 4.x is required.
  goto :fail
)

set "SRC=src"
set "ICON=%SRC%\Resources\app.ico"

if not exist "dist" mkdir "dist"

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
  /win32icon:%ICON% /resource:%ICON%,DaikonPalette.app.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
  /out:dist\DaikonPalette.exe %SRC%\*.cs
if errorlevel 1 (
  echo.
  echo Build FAILED.
  goto :fail
)

rem User-editable files: copy only when missing (never overwrite user changes)
if not exist "dist\data" mkdir "dist\data"
if not exist "dist\data\memo" mkdir "dist\data\memo"
for %%F in (data\*.json) do (
  if not exist "dist\data\%%~nxF" copy /y "%%F" "dist\data\" >nul
)

rem Not user-edited: always update
copy /y "README.md" "dist\" >nul
if exist "LICENSE.md" copy /y "LICENSE.md" "dist\" >nul

echo.
echo Build OK: dist\DaikonPalette.exe
if not defined NOPAUSE pause
exit /b 0

:fail
if not defined NOPAUSE pause
exit /b 1
