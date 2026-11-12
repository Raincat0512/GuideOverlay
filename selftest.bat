@echo off
cd /d "%~dp0"
cmd /c "GuideOverlay.exe --selftest > selftest.log 2>&1"
echo EXITCODE=%ERRORLEVEL%
pause
