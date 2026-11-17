@echo off
setlocal
if not exist "%~dp0bin\RazerPadFan.exe" (
  call "%~dp0build.cmd"
  if errorlevel 1 (
    echo RazerPad Control could not be built.
    pause
    exit /b 1
  )
)
if exist "%~dp0tray.cfg" if not exist "%~dp0bin\tray.cfg" (
  copy /-y "%~dp0tray.cfg" "%~dp0bin\tray.cfg" <nul >nul
  if errorlevel 1 (
    echo RazerPad Control could not copy your saved settings.
    pause
    exit /b 1
  )
)
start "" "%~dp0bin\RazerPadFan.exe"
