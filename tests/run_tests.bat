@echo off
rem Minimal unit tests: compile core pure-logic sources + Tests.cs with system csc, then run.
rem Covers Hotkey parse/serialize round-trip and AppConfig serialize round-trip (string-dict-key regression).
cd /d "%~dp0"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

%CSC% /nologo /target:exe /codepage:65001 /out:_tests.exe /r:System.Web.Extensions.dll ..\src\HotkeyManager.cs ..\src\NativeMethods.cs ..\src\Lang.cs ..\src\AppConfig.cs ..\src\Log.cs Tests.cs
if %errorlevel% neq 0 (
    echo TEST COMPILE FAILED
    exit /b 1
)

_tests.exe
set code=%errorlevel%
del _tests.exe >nul 2>nul

if %code% equ 0 (
    echo ===== ALL TESTS PASSED =====
) else (
    echo ===== SOME TESTS FAILED =====
)
exit /b %code%
