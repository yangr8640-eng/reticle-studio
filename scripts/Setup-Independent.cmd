@echo off
setlocal
set "ReticlePowerShell=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "ReticlePowerShell=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
"%ReticlePowerShell%" -NoLogo -NoProfile -STA -ExecutionPolicy RemoteSigned -File "%~dp0setup-independent.ps1"
if errorlevel 1 (
  echo.
  echo Setup could not start. Read README.md for the manual installation steps.
  echo If Windows blocked a downloaded script, unblock the original ZIP in its
  echo Properties, then extract it once into a new folder and try again.
  pause
)
endlocal
