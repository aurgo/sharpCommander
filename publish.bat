@echo off
setlocal enabledelayedexpansion

:: SharpCommander - publish script for Windows (cmd).
::
:: A thin wrapper over 'dotnet publish'. The publish configuration (self-contained, single-file, partial trimming, no debug
:: symbols) lives in src\SharpCommander.Desktop\SharpCommander.Desktop.csproj so every platform and every script
:: ships the same binaries, and the version is read from Directory.Build.props.
::
:: Usage: publish.bat [--aot] [--no-zip] [PLATFORM^|all]
::   Without a platform a menu is shown. macOS platforms are published as loose files here; run publish.sh on
::   macOS (or Linux) to get the SharpCommander.app bundle.

pushd "%~dp0"

set "PROJECT_PATH=src\SharpCommander.Desktop\SharpCommander.Desktop.csproj"
set "PROPS_FILE=Directory.Build.props"
set "OUTPUT_BASE=publish"
set "APP_NAME=SharpCommander"
set "AOT_ARGS="
set "CREATE_ZIP=1"
set "PLATFORM="
set "INTERACTIVE=1"
set "EXIT_CODE=0"

echo ============================================
echo   SharpCommander - Build and Publish Script
echo ============================================
echo.

:: Read the version from Directory.Build.props, the single source of the version.
set "VERSION_LINE="
for /f "usebackq delims=" %%v in (`findstr /c:"<Version>" "%PROPS_FILE%"`) do if not defined VERSION_LINE set "VERSION_LINE=%%v"
if not defined VERSION_LINE (
    echo ERROR: no ^<Version^> element found in %PROPS_FILE%
    set "EXIT_CODE=1"
    goto :end
)
set "VERSION=%VERSION_LINE:*<Version>=%"
set "VERSION=%VERSION:</Version>=%"
set "VERSION=%VERSION: =%"
echo Version %VERSION% (from %PROPS_FILE%)

:parse_args
if "%~1"=="" goto :args_done
if /i "%~1"=="--aot" (set "AOT_ARGS=-p:PublishAot=true") else if /i "%~1"=="-a" (set "AOT_ARGS=-p:PublishAot=true") else if /i "%~1"=="--no-zip" (set "CREATE_ZIP=0") else if /i "%~1"=="-h" (goto :usage) else if /i "%~1"=="--help" (goto :usage) else (set "PLATFORM=%~1")
shift
goto :parse_args
:args_done

:: Create output directory
if not exist "%OUTPUT_BASE%" mkdir "%OUTPUT_BASE%"

:: A platform on the command line skips the menu.
if defined PLATFORM (
    set "INTERACTIVE=0"
    if /i "!PLATFORM!"=="all" goto :all
    call :validate_rid "!PLATFORM!" || (
        echo Unknown platform: !PLATFORM!
        set "EXIT_CODE=1"
        goto :usage
    )
    call :build_platform !PLATFORM! "!PLATFORM!"
    if errorlevel 1 set "EXIT_CODE=1"
    goto :end
)

:: Menu
echo.
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
    goto :after_single
)
if "%CHOICE%"=="2" (
    call :build_platform win-x86 "Windows x86"
    goto :after_single
)
if "%CHOICE%"=="3" (
    call :build_platform win-arm64 "Windows ARM64"
    goto :after_single
)
if "%CHOICE%"=="4" (
    call :build_platform linux-x64 "Linux x64"
    goto :after_single
)
if "%CHOICE%"=="5" (
    call :build_platform linux-arm64 "Linux ARM64"
    goto :after_single
)
if "%CHOICE%"=="6" (
    call :build_platform osx-x64 "macOS x64 Intel"
    goto :after_single
)
if "%CHOICE%"=="7" (
    call :build_platform osx-arm64 "macOS ARM64 Apple Silicon"
    goto :after_single
)
if "%CHOICE%"=="8" goto :all
if "%CHOICE%"=="9" goto :end
echo Invalid choice.
set "EXIT_CODE=1"
goto :end

:after_single
if errorlevel 1 set "EXIT_CODE=1"
goto :end

:validate_rid
for %%r in (win-x64 win-x86 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64) do if /i "%~1"=="%%r" exit /b 0
exit /b 1

:build_platform
set "RID=%~1"
set "PLATFORM_NAME=%~2"
set "OUTPUT_DIR=%OUTPUT_BASE%\%RID%"
set "ZIP_NAME=%APP_NAME%-v%VERSION%-%RID%.zip"
set "ZIP_PATH=%OUTPUT_BASE%\%ZIP_NAME%"

echo.
echo [Building for %PLATFORM_NAME%...]
if defined AOT_ARGS echo   AOT compilation enabled

:: Start from a clean folder so files of an earlier publish never end up in the archive.
if exist "%OUTPUT_DIR%" rmdir /s /q "%OUTPUT_DIR%"

:: The csproj supplies the publish settings; only configuration, runtime and output are passed here.
dotnet publish "%PROJECT_PATH%" -c Release -r %RID% -o "%OUTPUT_DIR%" %AOT_ARGS%
if %errorlevel% neq 0 (
    echo ERROR: Build failed for %PLATFORM_NAME%.
    exit /b 1
)
echo Build completed: %OUTPUT_DIR%

if "%CREATE_ZIP%"=="0" exit /b 0

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

set SUCCESS_COUNT=0
set FAIL_COUNT=0

for %%r in (win-x64 win-x86 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64) do (
    call :build_platform %%r "%%r"
    if !errorlevel! neq 0 (set /a FAIL_COUNT+=1) else (set /a SUCCESS_COUNT+=1)
)

echo.
echo ============================================
echo   Build Summary
echo ============================================
echo.
echo Successful: %SUCCESS_COUNT%
echo Failed: %FAIL_COUNT%
if "%CREATE_ZIP%"=="1" (
    echo.
    echo ZIP files created:
    for %%f in ("%OUTPUT_BASE%\*.zip") do (
        set SIZE=%%~zf
        set /a SIZE_MB=!SIZE!/1048576
        echo   %%~nxf - !SIZE_MB! MB
    )
)
if %FAIL_COUNT% gtr 0 set "EXIT_CODE=1"
goto :end

:usage
echo.
echo Usage: publish.bat [--aot] [--no-zip] [PLATFORM^|all]
echo   PLATFORM  win-x64, win-x86, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64
echo   --aot     Native AOT compilation (-p:PublishAot=true)
echo   --no-zip  Skip creating ZIP archives
echo Without arguments a menu is shown.
set "INTERACTIVE=0"
goto :end

:end
echo.
if "%EXIT_CODE%"=="0" (echo Done.) else (echo Done with errors.)
if "%INTERACTIVE%"=="1" pause
popd
endlocal & exit /b %EXIT_CODE%
