@echo off
cd /d "%~dp0"
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /out:..\AutoClicker.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll Program.cs NativeMethods.cs InputSimulator.cs Humanizer.cs InterceptionDriver.cs HotkeyManager.cs AutoClicker.cs KeyboardSpammer.cs MacroRecorder.cs MacroPlayer.cs SoundFx.cs AppConfig.cs Theme.cs Lang.cs EventEditForms.cs HotkeyCaptureForm.cs Clay.cs WelcomeForm.cs AboutForm.cs MainForm.cs
if %errorlevel% equ 0 (echo Build OK: ..\AutoClicker.exe) else (echo Build FAILED)
pause
