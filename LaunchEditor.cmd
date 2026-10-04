@echo off
setlocal
if exist "%~dp0out\deployment\editor-journal.json" (
    powershell.exe -NoLogo -NoProfile -Command "$ErrorActionPreference='Stop'; $j=Get-Content -Raw -LiteralPath '%~dp0out\deployment\editor-journal.json' | ConvertFrom-Json; if($j.phase -notin @('Complete','RolledBack')) { Write-Error 'Editor deployment interrupted. Run scripts\Recover-Editor.bat before launch.'; exit 1 }"
    if errorlevel 1 exit /b 1
)
set "EDITOR_EXE=%~dp0out\bin\NcmaEngine.exe"
if not exist "%EDITOR_EXE%" (
    echo NcmaEngine.exe was not found. Building Debug first...
    call "%~dp0Build.bat"
    if errorlevel 1 exit /b %ERRORLEVEL%
)
start "NcmaEngine" "%EDITOR_EXE%" %*
