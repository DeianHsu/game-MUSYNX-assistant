param([string]$Out = "")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if ($Out -eq "") { $Out = Join-Path $root "dist\MUSYNX-Tool.exe" }
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src = Join-Path $root "src\MusynxTool\Program.cs"

$resDir = Join-Path $env:TEMP "musynxres"
if (Test-Path $resDir) { Remove-Item $resDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $resDir | Out-Null

$payload = Join-Path $root "dist\payload"
Copy-Item (Join-Path $payload "winhttp.dll") $resDir
Copy-Item (Join-Path $payload "doorstop_config.ini") $resDir
Copy-Item (Join-Path $payload ".doorstop_version") (Join-Path $resDir "dotstop_version")
Copy-Item (Join-Path $payload "BepInEx-LICENSE.txt") $resDir
Get-ChildItem (Join-Path $payload "BepInEx\core") -Filter *.dll | Copy-Item -Destination $resDir
Copy-Item (Join-Path $root "dist\BepInEx\plugins\MusynxAutoplay\MusynxAutoplay.dll") (Join-Path $resDir "plugin.dll")

$map = @(
  @("winhttp.dll", "res.winhttp.dll"),
  @("doorstop_config.ini", "res.doorstop_config.ini"),
  @("dotstop_version", "res._doorstop_version"),
  @("BepInEx-LICENSE.txt", "res.BepInEx-LICENSE.txt"),
  @("plugin.dll", "res.plugin.MusynxAutoplay.dll")
)
foreach ($f in Get-ChildItem (Join-Path $payload "BepInEx\core") -Filter *.dll) {
  $map += ,@($f.Name, ("res.BepInEx.core." + $f.Name))
}

$lines = @(
  "/nologo",
  "/target:winexe",
  "/platform:anycpu",
  ('/out:"{0}"' -f $Out),
  "/r:System.dll",
  "/r:System.Core.dll",
  "/r:System.Drawing.dll",
  "/r:System.Windows.Forms.dll",
  ('"{0}"' -f $src)
)
foreach ($m in $map) {
  $p = Join-Path $resDir $m[0]
  $lines += ("/resource:" + $p + "," + $m[1])
}

$rsp = Join-Path $env:TEMP "musynx-tool.rsp"
Set-Content -Path $rsp -Value $lines -Encoding ASCII
& $csc ("@" + $rsp)
if ($LASTEXITCODE -ne 0) { throw "build failed" }
Get-Item $Out | Select-Object Length, FullName | Format-List
