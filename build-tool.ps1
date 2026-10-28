param([string]$Out = "")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if ($Out -eq "") { $Out = Join-Path $root "dist\MUSYNX-Tool.exe" }
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src = Join-Path $root "src\MusynxTool\Program.cs"
$rsp = Join-Path $env:TEMP "musynx-tool.rsp"
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
Set-Content -Path $rsp -Value $lines -Encoding ASCII
& $csc ("@" + $rsp)
if ($LASTEXITCODE -ne 0) { throw "build failed" }
Get-Item $Out | Select-Object Length, FullName | Format-List
