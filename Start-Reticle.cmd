@echo off
setlocal
cd /d "%~dp0"
if exist "artifacts\Reticle-win-x64\Reticle.exe" (
  start "" "artifacts\Reticle-win-x64\Reticle.exe"
  exit /b
)
if exist "src\Reticle.App\bin\Release\net10.0-windows\Reticle.exe" (
  start "" "src\Reticle.App\bin\Release\net10.0-windows\Reticle.exe"
  exit /b
)
echo Run scripts\build.ps1 -Portable first.
pause
