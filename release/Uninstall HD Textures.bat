@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0hdtex.ps1" uninstall %*
pause
