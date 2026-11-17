@echo off
rem Build the tray app + dashboard. Uses the C# compiler that ships with Windows (.NET Framework 4.x).
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
%CSC% /nologo /optimize /target:winexe /out:RazerPadFan.exe /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll src\RazerPadFan.cs
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo BUILD OK: RazerPadFan.exe
