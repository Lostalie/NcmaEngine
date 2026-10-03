@echo off
setlocal
set "CONFIGURATION=%~1"
if /I "%CONFIGURATION%"=="Release" (
    set "BUILD_DIR=%~dp0..\out\build\windows-ninja-release"
) else (
    set "BUILD_DIR=%~dp0..\out\build\windows-ninja-debug"
)
if not exist "%BUILD_DIR%" exit /b 0
for %%I in ("%BUILD_DIR%") do set "RESOLVED_BUILD_DIR=%%~fI"
for %%I in ("%~dp0..\out\build") do set "EXPECTED_ROOT=%%~fI"
echo %RESOLVED_BUILD_DIR%| findstr /I /B /L /C:"%EXPECTED_ROOT%\" >nul || exit /b 1
if exist "%RESOLVED_BUILD_DIR%\build.ninja" (
    cmake --build "%RESOLVED_BUILD_DIR%" --target clean
)
exit /b %ERRORLEVEL%
