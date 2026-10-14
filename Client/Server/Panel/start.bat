@echo off
REM The Sims Redux - server panel.
REM First run installs Electron (a few hundred MB, once). After that it just opens.
cd /d "%~dp0"
if not exist "node_modules\electron" (
  echo Installing the panel's one dependency. This happens once.
  call npm install || goto fail
)
call npx electron .
goto :eof
:fail
echo.
echo Install failed. You need Node.js on this machine: https://nodejs.org
pause
