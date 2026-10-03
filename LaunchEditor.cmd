@echo off
setlocal
set "EDITOR_EXE=%~dp0out\bin\NcmaEngine.exe"
if not exist "%EDITOR_EXE%" (
    echo NcmaEngine.exe was not found. Building Debug first...
    call "%~dp0Build.bat"
    if errorlevel 1 exit /b %ERRORLEVEL%
)
start "NcmaEngine" "%EDITOR_EXE%" %*
