$ErrorActionPreference = 'Stop'
$testSource = Join-Path $PSScriptRoot 'RuntimeAssetReloadTests.cs'
$runtimeSource = Join-Path $PSScriptRoot '../source/COM3D2.ModItemExplorer.Plugin/ModelPlacement/RuntimeAssetReload.cs'
Add-Type -Path @($testSource, $runtimeSource)
[RuntimeAssetReloadTests]::Run()
