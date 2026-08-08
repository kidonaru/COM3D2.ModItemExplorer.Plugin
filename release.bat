@echo off
chcp 65001
setlocal

call .\source\COM3D2.ModItemExplorer.Plugin\build.bat release all
if %ERRORLEVEL% neq 0 (
    echo ビルドに失敗しました
    exit /b 1
)

for /f "tokens=*" %%i in ('powershell -Command "$content = Get-Content 'source/COM3D2.ModItemExplorer.Plugin/PluginInfo.cs'; $version = [regex]::Match($content, 'PluginVersion = \""(.*?)\""').Groups[1].Value; echo $version"') do set VERSION=%%i
echo VERSION: %VERSION%

set PLUGIN_NAME=COM3D2.ModItemExplorer.Plugin

if exist output rmdir /s /q output

rem ============ COM3D2 版パッケージ ============
md output\%PLUGIN_NAME%
xcopy UnityInjector output\%PLUGIN_NAME%\UnityInjector /E /I

set README_TXT=output\%PLUGIN_NAME%\README.txt
echo このテキストはWeb上で見ることを推奨しています。 > %README_TXT%
echo https://github.com/kidonaru/COM3D2.ModItemExplorer.Plugin/blob/master/README.md >> %README_TXT%
echo. >> %README_TXT%
echo. >> %README_TXT%
type README.md >> %README_TXT%

powershell Compress-Archive -Path "output\%PLUGIN_NAME%" -DestinationPath "output\%PLUGIN_NAME%-v%VERSION%.zip" -Force

rmdir /s /q output\%PLUGIN_NAME%

rem ============ COM3D2.5 版パッケージ (dll のみ差し替え) ============
md output\%PLUGIN_NAME%
xcopy UnityInjector output\%PLUGIN_NAME%\UnityInjector /E /I
copy /y UnityInjector25\%PLUGIN_NAME%.dll output\%PLUGIN_NAME%\UnityInjector\
if %ERRORLEVEL% neq 0 (
    echo COM3D2.5 版 dll のコピーに失敗しました
    exit /b 1
)

set README_TXT=output\%PLUGIN_NAME%\README.txt
echo このテキストはWeb上で見ることを推奨しています。 > %README_TXT%
echo https://github.com/kidonaru/COM3D2.ModItemExplorer.Plugin/blob/master/README.md >> %README_TXT%
echo. >> %README_TXT%
echo. >> %README_TXT%
type README.md >> %README_TXT%

powershell Compress-Archive -Path "output\%PLUGIN_NAME%" -DestinationPath "output\%PLUGIN_NAME%-v%VERSION%-COM3D25.zip" -Force

rmdir /s /q output\%PLUGIN_NAME%

echo ビルドに成功しました
exit /b 0
