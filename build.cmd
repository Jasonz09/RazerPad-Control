@echo off
setlocal
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Windows .NET Framework C# compiler not found.
  exit /b 1
)
if not exist "%~dp0bin\." mkdir "%~dp0bin"
"%CSC%" /nologo /optimize /target:winexe /out:"%~dp0bin\RazerPadFan.exe" /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "%~dp0src\RazerPadFan.cs"
if errorlevel 1 exit /b 1
echo Built "%~dp0bin\RazerPadFan.exe"
