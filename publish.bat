@echo off
rem Thin wrapper: delegates to publish.ps1 (PowerShell handles Chinese filenames correctly).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1"
exit /b %errorlevel%
