param(
  [string]$GameRoot = "D:\Game\Steam\steamapps\common\MUSYNX",
  [string]$BepInExRef = "D:\Game\Steam\steamapps\common\A2B\BepInEx\core"
)
$ErrorActionPreference = "Stop"
$managed = Join-Path $GameRoot "MUSYNX_Data\Managed"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out = Join-Path $PSScriptRoot "dist\BepInEx\plugins\MusynxAutoplay"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$refs = @(
  (Join-Path $managed "mscorlib.dll"),
  (Join-Path $managed "System.dll"),
  (Join-Path $managed "System.Core.dll"),
  (Join-Path $managed "UnityEngine.dll"),
  (Join-Path $managed "UnityEngine.CoreModule.dll"),
  (Join-Path $managed "Assembly-CSharp.dll"),
  (Join-Path $managed "Assembly-CSharp-firstpass.dll"),
  (Join-Path $BepInExRef "BepInEx.dll"),
  (Join-Path $BepInExRef "0Harmony.dll")
)
$refArgs = $refs | ForEach-Object { "/r:" + $_ }
$srcFiles = Get-ChildItem (Join-Path $PSScriptRoot "src\MusynxAutoplay") -Filter *.cs | ForEach-Object { $_.FullName }
$dll = Join-Path $out "MusynxAutoplay.dll"
$args = @("/nologo","/noconfig","/target:library","/platform:anycpu","/nostdlib+","/optimize+","/debug-","/out:$dll") + $refArgs + $srcFiles
& $csc $args 2>&1 | ForEach-Object { $_ }
if ($LASTEXITCODE -ne 0) { throw "compile failed: $LASTEXITCODE" }
Get-Item $dll | Select-Object Length, FullName | Format-List