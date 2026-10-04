@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command ". '%~dp0EditorDeployment.ps1'; Restore-EditorDeployment '%~dp0..' '%~dp0..\out\bin' '%~dp0..\out\deployment\editor-journal.json'"
exit /b %ERRORLEVEL%
