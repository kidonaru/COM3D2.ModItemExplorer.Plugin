@echo off
chcp 65001
setlocal

rem 引数: %1 = com3d2/com3d25/all (既定 all)
set TARGET=%~1
if "%TARGET%"=="" set TARGET=all

call .\source\COM3D2.ModItemExplorer.Plugin\build.bat debug %TARGET%
if %ERRORLEVEL% neq 0 (
    echo ビルドに失敗しました
    exit /b 1
)

rem .env からゲームのインストール先を読み込んで Config をコピーする
set ENV_FILE=%~dp0.env
if not exist "%ENV_FILE%" (
    echo .env が見つかりません: %ENV_FILE%
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

if not "%TARGET%"=="com3d25" (
    if not exist "%COM3D2_DIR%\UnityInjector\Config" mkdir "%COM3D2_DIR%\UnityInjector\Config"
    copy /y .\UnityInjector\Config\*.csv "%COM3D2_DIR%\UnityInjector\Config" >nul
    if %ERRORLEVEL% neq 0 (
        echo 警告: COM3D2 への csv コピーに失敗しました
    )
)
if not "%TARGET%"=="com3d2" (
    if not exist "%COM3D25_DIR%\UnityInjector\Config" mkdir "%COM3D25_DIR%\UnityInjector\Config"
    copy /y .\UnityInjector\Config\*.csv "%COM3D25_DIR%\UnityInjector\Config" >nul
    if %ERRORLEVEL% neq 0 (
        echo 警告: COM3D2.5 への csv コピーに失敗しました
    )
)

echo ビルドに成功しました
exit /b 0
