param(
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$OutDir = Join-Path $PSScriptRoot "dist"
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$Exe = Join-Path $OutDir "LocalServiceManager.exe"
$Csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $Csc)) { throw "csc.exe not found: $Csc" }
$Sources = @(
  "Program.cs",
  "AppPaths.cs",
  "Models.cs",
  "StartupManager.cs",
  "ServiceConfig.cs",
  "ServiceManager.cs",
  "MainForm.cs"
) | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $Csc /nologo /target:winexe /platform:x64 /optimize+ /utf8output /out:$Exe `
  /reference:System.dll `
  /reference:System.Core.dll `
  /reference:System.Drawing.dll `
  /reference:System.Windows.Forms.dll `
  /reference:System.Web.Extensions.dll `
  /reference:System.Net.Http.dll `
  /reference:Microsoft.CSharp.dll `
  $Sources
if ($LASTEXITCODE -ne 0) { throw "csc build failed: $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $Exe)) { throw "build did not create $Exe" }
Write-Host "Published: $Exe"
