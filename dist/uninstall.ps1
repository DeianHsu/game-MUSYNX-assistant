param(
    [string]$GameRoot = "D:\Game\Steam\steamapps\common\MUSYNX"
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path (Join-Path $GameRoot "MUSYNX.exe"))) { throw "找不到 MUSYNX.exe：$GameRoot" }

$backup = Join-Path $GameRoot "BepInEx\MusynxAutoplayBackup\globalgamemanagers.orig"
if (Test-Path $backup) {
    Copy-Item -LiteralPath $backup -Destination (Join-Path $GameRoot "MUSYNX_Data\globalgamemanagers") -Force
    Write-Host "已恢复 globalgamemanagers"
} else {
    Write-Warning "没有找到备份，未修改 globalgamemanagers"
}

Remove-Item -LiteralPath (Join-Path $GameRoot "BepInEx\plugins\MusynxAutoplay") -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "已移除插件"

if (Test-Path (Join-Path $GameRoot "BepInEx\.musynx-autoplay")) {
    Remove-Item -LiteralPath (Join-Path $GameRoot "BepInEx") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $GameRoot "winhttp.dll") -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $GameRoot "doorstop_config.ini") -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $GameRoot ".doorstop_version") -Force -ErrorAction SilentlyContinue
    Write-Host "已移除 BepInEx 加载器"
}
Write-Host "卸载完成。"
