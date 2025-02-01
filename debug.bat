@echo off
chcp 65001

call .\source\COM3D2.ModItemExplorer.Plugin\build.bat debug
if %ERRORLEVEL% neq 0 (
    echo ビルドに失敗しました
    exit /b 1
)

copy .\UnityInjector\Config\*.csv ..\..\BepInEx\config
if %ERRORLEVEL% neq 0 (
    echo Failed to copy csv
    exit /b 1
)

echo ビルドに成功しました
exit /b 0
