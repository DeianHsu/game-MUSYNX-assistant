param(
    [string]$GameRoot = "D:\Game\Steam\steamapps\common\MUSYNX"
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot

if (-not (Test-Path (Join-Path $GameRoot "MUSYNX.exe"))) {
    throw "找不到 MUSYNX.exe：$GameRoot"
}

Write-Host "== MUSYNX Autoplay 安装 ==" -ForegroundColor Cyan
Write-Host "游戏目录：$GameRoot"

# 1. BepInEx loader
$payload = Join-Path $here "payload"
Copy-Item -LiteralPath (Join-Path $payload "winhttp.dll") -Destination $GameRoot -Force
Copy-Item -LiteralPath (Join-Path $payload "doorstop_config.ini") -Destination $GameRoot -Force
Copy-Item -LiteralPath (Join-Path $payload ".doorstop_version") -Destination $GameRoot -Force
New-Item -ItemType Directory -Force -Path (Join-Path $GameRoot "BepInEx\core") | Out-Null
Copy-Item -Path (Join-Path $payload "BepInEx\core\*") -Destination (Join-Path $GameRoot "BepInEx\core") -Force -Recurse
New-Item -ItemType File -Force -Path (Join-Path $GameRoot "BepInEx\.musynx-autoplay") | Out-Null
Write-Host "[1/3] BepInEx 加载器已就位"

# 2. plugin
$pluginDir = Join-Path $GameRoot "BepInEx\plugins\MusynxAutoplay"
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item -LiteralPath (Join-Path $here "BepInEx\plugins\MusynxAutoplay\MusynxAutoplay.dll") -Destination $pluginDir -Force
Write-Host "[2/3] 插件已安装：$pluginDir"

# 3. skip Unity resolution dialog (one byte in PlayerSettings)
$f = Join-Path $GameRoot "MUSYNX_Data\globalgamemanagers"
$backupDir = Join-Path $GameRoot "BepInEx\MusynxAutoplayBackup"
$backup = Join-Path $backupDir "globalgamemanagers.orig"

$bytes = [System.IO.File]::ReadAllBytes($f)
$off = 4416
$expected = "FFFFFFFFFFFFFFFFFFFFFFFF0000000001000000010000010101010000000000"
function HexOf([byte[]]$a) { ($a | ForEach-Object { $_.ToString("X2") }) -join "" }

if ($bytes.Length -gt ($off + 16) -and $bytes[$off] -eq 1) {
    $window = HexOf $bytes[($off - 16)..($off + 15)]
    if ($window -ne $expected) {
        Write-Warning "PlayerSettings 指纹不匹配（游戏可能已更新），跳过启动对话框补丁。"
    } else {
        New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
        if (-not (Test-Path $backup)) { Copy-Item -LiteralPath $f -Destination $backup -Force }
        $bytes[$off] = 0
        [System.IO.File]::WriteAllBytes($f, $bytes)
        Write-Host "[3/3] 已关闭启动分辨率对话框（已备份原文件）"
    }
} elseif ($bytes[$off] -eq 0) {
    Write-Host "[3/3] 启动对话框此前已关闭"
} else {
    Write-Warning "无法识别 globalgamemanagers，跳过对话框补丁。"
}

Write-Host ""
Write-Host "完成。从 Steam 正常启动游戏即可；进入歌曲后自动打歌，F9 开关。" -ForegroundColor Green
Write-Host "配置：$GameRoot\BepInEx\config\local.musynx.autoplay.cfg（首次运行后生成）"
