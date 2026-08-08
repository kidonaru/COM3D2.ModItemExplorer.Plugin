<#
.SYNOPSIS
    プラグインのバージョンを更新する。bump-version.bat から呼ばれる。

.DESCRIPTION
    以下を一括で書き換える。
      - source\COM3D2.ModItemExplorer.Plugin\PluginInfo.cs の PluginVersion
      - README.md 冒頭のバージョン表記
      - README.md 変更履歴の目次と本文（見出しのみ。内容は手で書く）
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('major', 'minor', 'patch', 'build')]
    [string]$Part
)

$ErrorActionPreference = 'Stop'

$repoDir = $PSScriptRoot
$pluginInfoPath = Join-Path $repoDir 'source\COM3D2.ModItemExplorer.Plugin\PluginInfo.cs'
$readmePath = Join-Path $repoDir 'README.md'

# ファイルは UTF-8 (BOM なし) / CRLF なので、読み書きで維持する
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
function Read-TextFile([string]$path) {
    if (-not (Test-Path $path)) { throw "ファイルが見つかりません: $path" }
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}
function Write-TextFile([string]$path, [string]$text) {
    [System.IO.File]::WriteAllText($path, $text, $utf8NoBom)
}

# ============ 現在のバージョンを取得 ============
$pluginInfo = Read-TextFile $pluginInfoPath
$versionPattern = 'PluginVersion\s*=\s*"(\d+)\.(\d+)\.(\d+)\.(\d+)"'
$versionMatch = [regex]::Match($pluginInfo, $versionPattern)
if (-not $versionMatch.Success) {
    throw "PluginInfo.cs から PluginVersion を読み取れませんでした: $pluginInfoPath"
}

$major = [int]$versionMatch.Groups[1].Value
$minor = [int]$versionMatch.Groups[2].Value
$patch = [int]$versionMatch.Groups[3].Value
$build = [int]$versionMatch.Groups[4].Value
$oldVersion = "$major.$minor.$patch.$build"

switch ($Part) {
    'major' { $major++; $minor = 0; $patch = 0; $build = 0 }
    'minor' { $minor++; $patch = 0; $build = 0 }
    'patch' { $patch++; $build = 0 }
    'build' { $build++ }
}
$newVersion = "$major.$minor.$patch.$build"

$date = Get-Date -Format 'yyyy/MM/dd'
# 目次のアンカーは GitHub の生成規則に合わせて記号を落とす (例: 2025/07/06 v1.7.0.1 -> #20250706-v1701)
$anchor = '#' + $date.Replace('/', '') + '-v' + $newVersion.Replace('.', '')

Write-Host "バージョン: $oldVersion -> $newVersion ($date)"

# ============ 書き換え内容を組み立てる ============
# 片方だけ書き換わった中途半端な状態にならないよう、両方の検証が通ってから書き込む

# 置換は先頭 1 件だけに限定したいので、件数を指定できる Regex インスタンスの Replace を使う
# (-replace 演算子や [regex]::Replace の静的版には件数指定が無い)
$newPluginInfo = (New-Object regex $versionPattern).Replace($pluginInfo, "PluginVersion = `"$newVersion`"", 1)

$readme = Read-TextFile $readmePath

# 変更履歴の目次・本文の起点。README の他セクションに似た行があっても誤挿入しないよう、
# 挿入位置の検索はここから後ろだけを対象にする
$tocSectionPattern = '(?m)^[ \t]*- \[変更履歴\]\(#'
$bodySectionPattern = '(?m)^## 変更履歴[ \t]*\r?$'
$tocSectionMatch = [regex]::Match($readme, $tocSectionPattern)
if (-not $tocSectionMatch.Success) {
    throw "README.md に変更履歴の目次 (- [変更履歴](#...)) が見つかりませんでした"
}
$bodySectionMatch = [regex]::Match($readme, $bodySectionPattern)
if (-not $bodySectionMatch.Success) {
    throw "README.md に変更履歴の見出し (## 変更履歴) が見つかりませんでした"
}

$bodyPattern = '(?m)^### \d{4}/\d{2}/\d{2} v([\d.]+)[ \t]*\r?$'
$bodyRegex = New-Object regex $bodyPattern

$duplicated = $bodyRegex.Matches($readme) | Where-Object { $_.Groups[1].Value -eq $newVersion }
if ($duplicated) {
    throw "README.md の変更履歴に v$newVersion が既にあります。二重実行の可能性があるため中止します"
}

# 冒頭のバージョン表記 ※改行を巻き込まないよう行末は先読みで判定する
$headerPattern = "(?m)^v$([regex]::Escape($oldVersion))(?=[ \t]*\r?$)"
if ($readme -notmatch $headerPattern) {
    throw "README.md 冒頭に v$oldVersion の行が見つかりませんでした"
}
$readme = (New-Object regex $headerPattern).Replace($readme, "v$newVersion", 1)

# 変更履歴の目次 (既存の先頭エントリの上に挿入)
$tocPattern = '(?m)^([ \t]*)- \[\d{4}/\d{2}/\d{2} v[\d.]+\]\(#'
$tocMatch = (New-Object regex $tocPattern).Match($readme, $tocSectionMatch.Index)
if (-not $tocMatch.Success) {
    throw "README.md の変更履歴の目次エントリが見つかりませんでした"
}
$indent = $tocMatch.Groups[1].Value
$tocEntry = "$indent- [$date v$newVersion]($anchor)`r`n"
$readme = $readme.Insert($tocMatch.Index, $tocEntry)

# 変更履歴の本文 (既存の先頭エントリの上に挿入) ※目次挿入で位置がずれるので起点を取り直す
$bodySectionIndex = [regex]::Match($readme, $bodySectionPattern).Index
$bodyMatch = $bodyRegex.Match($readme, $bodySectionIndex)
if (-not $bodyMatch.Success) {
    throw "README.md の変更履歴の本文エントリが見つかりませんでした"
}
$bodyEntry = "### $date v$newVersion`r`n`r`n- TODO: 変更内容を記載`r`n`r`n`r`n"
$readme = $readme.Insert($bodyMatch.Index, $bodyEntry)

# ============ 書き込み ============
# README の書き込みに失敗したら PluginInfo.cs を元に戻す (バージョンだけ進んだ状態を残さない)
Write-TextFile $pluginInfoPath $newPluginInfo
try {
    Write-TextFile $readmePath $readme
}
catch {
    Write-TextFile $pluginInfoPath $pluginInfo
    throw
}
Write-Host "  更新: source\COM3D2.ModItemExplorer.Plugin\PluginInfo.cs"
Write-Host "  更新: README.md (冒頭 / 変更履歴の目次・本文)"

Write-Host ''
Write-Host "README.md の変更履歴に『TODO: 変更内容を記載』を挿入しました。内容を書いてから release.bat を実行してください"
