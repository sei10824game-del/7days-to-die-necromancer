@echo off
setlocal
rem ==========================================================
rem  AllyOnKill - Build and Install
rem
rem  Usage:
rem    build.bat
rem    build.bat "D:\SteamLibrary\steamapps\common\7 Days To Die"
rem
rem  Requires: .NET SDK 8  ( https://dotnet.microsoft.com/download )
rem
rem  NOTE 1: ASCII-only + CRLF on purpose. cmd.exe on Japanese Windows
rem          reads batch files as Shift-JIS, so UTF-8 Japanese text is
rem          mangled and the mangled bytes break the script.
rem  NOTE 2: This script never pipes a path that may contain spaces or
rem          parentheses - that is what
rem          "The syntax of the command is incorrect" comes from.
rem  NOTE 3: bin and obj are deleted before every build so a stale DLL
rem          can never be installed by mistake.
rem ==========================================================

set "GAMEDIR=C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
if not "%~1"=="" set "GAMEDIR=%~1"

set "MANAGED=%GAMEDIR%\7DaysToDie_Data\Managed"
set "SRC=%~dp0"
set "MODDIR=%APPDATA%\7DaysToDie\Mods\AllyOnKill"

echo.
echo ==========================================================
echo  AllyOnKill - Build ^& Install
echo ==========================================================
echo  Project : %SRC%
echo  Game    : %GAMEDIR%
echo  Target  : %MODDIR%
echo.

rem --- [1] Preconditions ------------------------------------
if not exist "%MANAGED%\Assembly-CSharp.dll" goto :no_game

where dotnet >nul 2>nul
if errorlevel 1 goto :no_dotnet

goto :prechecks_done

:no_game
echo [ERROR] Assembly-CSharp.dll was not found.
echo.
echo   Looked in: %MANAGED%
echo.
echo   If 7DTD is installed somewhere else, pass that folder:
echo     build.bat "D:\SteamLibrary\steamapps\common\7 Days To Die"
echo.
pause
exit /b 1

:no_dotnet
echo [ERROR] The 'dotnet' command was not found.
echo.
echo   Install .NET SDK 8 from https://dotnet.microsoft.com/download
echo   Then open a NEW PowerShell window and run this script again.
echo.
pause
exit /b 1

:prechecks_done

rem --- [2] Locate 0Harmony.dll ------------------------------
set "HARMONY=%GAMEDIR%\Mods\0_TFP_Harmony\0Harmony.dll"
if exist "%HARMONY%" goto :harmony_found
set "HARMONY=%APPDATA%\7DaysToDie\Mods\0_TFP_Harmony\0Harmony.dll"
if exist "%HARMONY%" goto :harmony_found
set "HARMONY=%MANAGED%\0Harmony.dll"
if exist "%HARMONY%" goto :harmony_found

echo [WARN] 0Harmony.dll was not found in any known location.
echo   The build will most likely fail with MSB3245 / CS0246.
echo.
goto :harmony_done

:harmony_found
echo  0Harmony  : %HARMONY%

:harmony_done

rem --- [3] Warn if the project sits inside a Mods folder ----
if not exist "%SRC%..\0_TFP_Harmony" goto :layout_ok
echo [WARN] This project is inside a Mods folder.
echo   The build still works, but please move it outside Mods later,
echo   for example: %USERPROFILE%\Documents\AllyOnKill
echo.
:layout_ok

rem --- [4] FORCE a clean rebuild ----------------------------
echo === Clean ===
if exist "%SRC%src\bin" rd /s /q "%SRC%src\bin"
if exist "%SRC%src\obj" rd /s /q "%SRC%src\obj"
echo   Removed old bin and obj to guarantee a fresh compile.

rem --- [5] Clean stale files left in the target folder ------
if not exist "%MODDIR%" goto :clean_done
if exist "%MODDIR%\src"       rd /s /q "%MODDIR%\src"
if exist "%MODDIR%\mod"       rd /s /q "%MODDIR%\mod"
if exist "%MODDIR%\build.bat" del /q "%MODDIR%\build.bat"
if exist "%MODDIR%\check.bat" del /q "%MODDIR%\check.bat"
if exist "%MODDIR%\README.md" del /q "%MODDIR%\README.md"
:clean_done

if not exist "%MODDIR%" mkdir "%MODDIR%"

rem --- [6] Build --------------------------------------------
echo.
echo === Build ===
dotnet build "%SRC%src\AllyOnKill.csproj" -c Release -p:GameDir="%GAMEDIR%"
if errorlevel 1 goto :build_failed

if not exist "%SRC%src\bin\Release\AllyOnKill.dll" goto :no_dll

goto :install

:build_failed
echo.
echo [ERROR] Build failed. Please copy all messages above.
echo.
pause
exit /b 1

:no_dll
echo.
echo [ERROR] AllyOnKill.dll was not produced.
echo.
pause
exit /b 1

rem --- [7] Install ------------------------------------------
:install
echo.
echo === Install ===
copy /Y "%SRC%src\bin\Release\AllyOnKill.dll" "%MODDIR%" >nul
copy /Y "%SRC%mod\ModInfo.xml"                "%MODDIR%" >nul
if not exist "%MODDIR%\Settings.xml" goto :copy_settings
echo Settings.xml already exists - kept as is
goto :installed

:copy_settings
copy /Y "%SRC%mod\Settings.xml" "%MODDIR%" >nul

:installed
echo.
echo Installed files:
dir /b "%MODDIR%"
echo.
echo Timestamp check - this must show the CURRENT date and time:
for %%F in ("%MODDIR%\AllyOnKill.dll") do echo   AllyOnKill.dll   %%~tF

rem --- [8] Verify -------------------------------------------
echo.
set "OK=1"
if not exist "%MODDIR%\ModInfo.xml"    set "OK=0"
if not exist "%MODDIR%\AllyOnKill.dll" set "OK=0"
if "%OK%"=="1" goto :verify_ok

echo [ERROR] Required files are missing.
echo.
pause
exit /b 1

:verify_ok
echo [OK] Install complete.
echo.
echo Start the game with EAC OFF. Expected log lines:
echo   [MODS]       Loaded Mod: AllyOnKill ^(1.1.0^)
echo   [AllyOnKill] v1.1.0 loaded
echo.
echo If the log still says 1.0.0, the old DLL is still installed.
echo.
pause
