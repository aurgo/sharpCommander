@echo off
setlocal enabledelayedexpansion

echo ============================================
echo   SharpCommander - Build and Publish Script
echo ============================================
echo.

set PROJECT_PATH=src\SharpCommander.Desktop\SharpCommander.Desktop.csproj
set OUTPUT_BASE=publish
set VERSION=2.0.0

:: Create output directory
if not exist "%OUTPUT_BASE%" mkdir "%OUTPUT_BASE%"

:: Menu
echo Select platform to publish:
echo.
echo   1. Windows x64
echo   2. Windows x86
echo   3. Windows ARM64
echo   4. Linux x64
echo   5. Linux ARM64
echo   6. macOS x64 (Intel)
echo   7. macOS ARM64 (Apple Silicon)
echo   8. All platforms
echo   9. Exit
echo.
set /p CHOICE="Enter your choice (1-9): "

if "%CHOICE%"=="1" (
    call :build_platform win-x64 "Windows x64"
    goto :end
)
if "%CHOICE%"=="2" (
    call :build_platform win-x86 "Windows x86"
    goto :end
)
if "%CHOICE%"=="3" (
    call :build_platform win-arm64 "Windows ARM64"
    goto :end
)
if "%CHOICE%"=="4" (
    call :build_platform linux-x64 "Linux x64"
    goto :end
)
if "%CHOICE%"=="5" (
    call :build_platform linux-arm64 "Linux ARM64"
    goto :end
)
if "%CHOICE%"=="6" (
    call :build_platform osx-x64 "macOS x64 Intel"
    goto :end
)
if "%CHOICE%"=="7" (
    call :build_platform osx-arm64 "macOS ARM64 Apple Silicon"
    goto :end
)
if "%CHOICE%"=="8" goto :all
if "%CHOICE%"=="9" goto :end
echo Invalid choice!
goto :end

:build_platform
set RID=%~1
set PLATFORM_NAME=%~2
set OUTPUT_DIR=%OUTPUT_BASE%\%RID%
set ZIP_NAME=SharpCommander-v%VERSION%-%RID%.zip
set ZIP_PATH=%OUTPUT_BASE%\%ZIP_NAME%

echo.
echo [Building for %PLATFORM_NAME%...]
REM Note: PublishSingleFile NOT used - Avalonia needs native DLLs (libSkiaSharp, etc.)
dotnet publish "%PROJECT_PATH%" -c Release -r %RID% --self-contained true -o "%OUTPUT_DIR%"
if %errorlevel% neq 0 (
    echo ERROR: Build failed for %PLATFORM_NAME%!
    exit /b 1
)
echo Build completed: %OUTPUT_DIR%

echo [Creating ZIP: %ZIP_NAME%...]
if exist "%ZIP_PATH%" del "%ZIP_PATH%"
powershell -NoProfile -Command "Compress-Archive -Path '%OUTPUT_DIR%\*' -DestinationPath '%ZIP_PATH%' -Force"
if %errorlevel% neq 0 (
    echo WARNING: Failed to create ZIP file
) else (
    for %%A in ("%ZIP_PATH%") do set SIZE=%%~zA
    set /a SIZE_MB=!SIZE!/1048576
    echo ZIP created: %ZIP_PATH% - !SIZE_MB! MB
)
exit /b 0

:all
echo.
echo [Building for ALL platforms...]
echo.

set SUCCESS_COUNT=0
set FAIL_COUNT=0

call :build_all win-x64 "Windows x64" 1 7
call :build_all win-x86 "Windows x86" 2 7
call :build_all win-arm64 "Windows ARM64" 3 7
call :build_all linux-x64 "Linux x64" 4 7
call :build_all linux-arm64 "Linux ARM64" 5 7
call :build_all osx-x64 "macOS x64" 6 7
call :build_all osx-arm64 "macOS ARM64" 7 7

echo.
echo ============================================
echo   Build Summary
echo ============================================
echo.
echo Successful: %SUCCESS_COUNT%
echo Failed: %FAIL_COUNT%
echo.
echo ZIP files created:
for %%f in ("%OUTPUT_BASE%\*.zip") do (
    set SIZE=%%~zf
    set /a SIZE_MB=!SIZE!/1048576
    echo   %%~nxf - !SIZE_MB! MB
)
goto :end

:build_all
set RID=%~1
set PLATFORM_NAME=%~2
set STEP=%~3
set TOTAL=%~4
set OUTPUT_DIR=%OUTPUT_BASE%\%RID%
set ZIP_NAME=SharpCommander-v%VERSION%-%RID%.zip
set ZIP_PATH=%OUTPUT_BASE%\%ZIP_NAME%

echo [%STEP%/%TOTAL%] Building %PLATFORM_NAME%...
dotnet publish "%PROJECT_PATH%" -c Release -r %RID% --self-contained true -o "%OUTPUT_DIR%" >nul 2>&1
if %errorlevel% neq 0 (
    echo   ERROR: %PLATFORM_NAME% build failed!
    set /a FAIL_COUNT+=1
    exit /b 1
)
echo   Build OK

if exist "%ZIP_PATH%" del "%ZIP_PATH%"
powershell -NoProfile -Command "Compress-Archive -Path '%OUTPUT_DIR%\*' -DestinationPath '%ZIP_PATH%' -Force" >nul 2>&1
if %errorlevel% equ 0 (
    echo   ZIP OK: %ZIP_NAME%
)
set /a SUCCESS_COUNT+=1
exit /b 0

:end
echo.
echo Done!
pause
