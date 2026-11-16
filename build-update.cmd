@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\release-windows.ps1" %*
set "deskbox_build_exit=%ERRORLEVEL%"
echo.
if not "%deskbox_build_exit%"=="0" echo Build failed. Please keep the error shown above.
pause
exit /b %deskbox_build_exit%
