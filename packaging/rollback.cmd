@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\rollback-install.ps1" -InstallDir "%~dp0."
if errorlevel 1 pause
