@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

cd /d %~dp0

set PLUGIN_NAME=COM3D2.ModItemExplorer.Plugin
set SOURCE_DIR=%~dp0
set REPO_DIR=%~dp0..\..

set MSBUILD_PATH="C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"

rem 引数: %1 = debug/release (既定 release), %2 = com3d2/com3d25/all (既定 all)
set CONFIG=Release
if /i "%~1"=="debug" set CONFIG=Debug

set TARGET=all
if /i "%~2"=="com3d2" set TARGET=com3d2
if /i "%~2"=="com3d25" set TARGET=com3d25

rem .env からゲームのインストール先を読み込む ※開発者ごとの設定
set ENV_FILE=%REPO_DIR%\.env
if not exist "%ENV_FILE%" (
    echo .env が見つかりません: %ENV_FILE%
    echo .env.sample をコピーして .env を作成し、パスを設定してください
    exit /b 1
)
for /f "usebackq eol=# tokens=1,* delims==" %%a in ("%ENV_FILE%") do set "%%a=%%b"

if "%COM3D2_DIR%"=="" (
    echo .env に COM3D2_DIR が設定されていません
    exit /b 1
)
if "%COM3D25_DIR%"=="" (
    echo .env に COM3D25_DIR が設定されていません
    exit /b 1
)

echo COM3D2_DIR: %COM3D2_DIR%
echo COM3D25_DIR: %COM3D25_DIR%

if "%CONFIG%"=="Release" (
    if not "%TARGET%"=="com3d25" (
        %MSBUILD_PATH% %PLUGIN_NAME%.csproj /t:Clean /p:Configuration=Debug
        %MSBUILD_PATH% %PLUGIN_NAME%.csproj /t:Clean /p:Configuration=Release
    )
    if not "%TARGET%"=="com3d2" (
        %MSBUILD_PATH% %PLUGIN_NAME%.csproj /t:Clean /p:Configuration=Debug /p:GameVersion=COM3D25
        %MSBUILD_PATH% %PLUGIN_NAME%.csproj /t:Clean /p:Configuration=Release /p:GameVersion=COM3D25
    )
    if !ERRORLEVEL! neq 0 (
        echo クリーンビルドに失敗しました
        exit /b 1
    )
)

rem ============ COM3D2 版 ============
if not "%TARGET%"=="com3d25" (
    echo === COM3D2 版をビルド中 ^(%CONFIG%^) ===
    %MSBUILD_PATH% %PLUGIN_NAME%.csproj /p:Configuration=%CONFIG% /p:GameVersion=COM3D2 "/p:COM3D2_DIR=%COM3D2_DIR%" "/p:COM3D25_DIR=%COM3D25_DIR%"
    if !ERRORLEVEL! neq 0 (
        echo COM3D2 版のビルドに失敗しました
        exit /b 1
    )

    rem リリースパッケージ用に リポジトリ内 UnityInjector へコピー
    copy /y bin\%CONFIG%\%PLUGIN_NAME%.dll "%REPO_DIR%\UnityInjector\"
    if !ERRORLEVEL! neq 0 (
        echo dllのコピーに失敗しました
        exit /b 1
    )

    rem ゲームへのデプロイ ※ゲーム起動中はロックされるため失敗しても続行
    copy /y bin\%CONFIG%\%PLUGIN_NAME%.dll "%COM3D2_DIR%\Sybaris\UnityInjector\" >nul
    if !ERRORLEVEL! neq 0 (
        echo 警告: COM3D2 へのデプロイに失敗しました ^(ゲーム起動中?^)
    ) else (
        echo COM3D2 へデプロイしました
    )
)

rem ============ COM3D2.5 版 ============
if not "%TARGET%"=="com3d2" (
    echo === COM3D2.5 版をビルド中 ^(%CONFIG%^) ===
    %MSBUILD_PATH% %PLUGIN_NAME%.csproj /p:Configuration=%CONFIG% /p:GameVersion=COM3D25 "/p:COM3D2_DIR=%COM3D2_DIR%" "/p:COM3D25_DIR=%COM3D25_DIR%"
    if !ERRORLEVEL! neq 0 (
        echo COM3D2.5 版のビルドに失敗しました
        exit /b 1
    )

    rem リリースパッケージ用に リポジトリ内 UnityInjector25 へコピー
    if not exist "%REPO_DIR%\UnityInjector25" mkdir "%REPO_DIR%\UnityInjector25"
    copy /y bin\%CONFIG%\COM3D25\%PLUGIN_NAME%.dll "%REPO_DIR%\UnityInjector25\"
    if !ERRORLEVEL! neq 0 (
        echo dllのコピーに失敗しました
        exit /b 1
    )

    rem ゲームへのデプロイ ※ゲーム起動中はロックされるため失敗しても続行
    copy /y bin\%CONFIG%\COM3D25\%PLUGIN_NAME%.dll "%COM3D25_DIR%\Sybaris\UnityInjector\" >nul
    if !ERRORLEVEL! neq 0 (
        echo 警告: COM3D2.5 へのデプロイに失敗しました ^(ゲーム起動中?^)
    ) else (
        echo COM3D2.5 へデプロイしました
    )
)

exit /b 0
