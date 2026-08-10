param(
  [string]$OutputName = "关系图编辑器.exe"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$nativeRoot = Join-Path $projectRoot "native"
$defaultGraphPath = Join-Path $projectRoot "system-function-graph.json"
$iconPath = Join-Path $projectRoot "assets\app-icon.ico"
$outputPath = Join-Path $projectRoot $OutputName
$compilerCandidates = @(
  "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
  "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$compilerPath = $compilerCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $compilerPath) { throw "Windows C# compiler was not found." }
if (-not (Test-Path $nativeRoot)) { throw "native source folder was not found." }
if (-not (Test-Path $defaultGraphPath)) { throw "system-function-graph.json was not found." }
if (-not (Test-Path $iconPath)) { throw "assets\app-icon.ico was not found." }

$sourcePaths = Get-ChildItem -LiteralPath $nativeRoot -Filter "*.cs" -File | Sort-Object Name | ForEach-Object { $_.FullName }
if ($sourcePaths.Count -lt 4) { throw "Native application sources are incomplete." }

$compilerArguments = @(
  "/nologo"
  "/target:winexe"
  "/platform:anycpu"
  "/optimize+"
  "/codepage:65001"
  "/main:RelationshipGraphNative.Program"
  "/win32icon:$iconPath"
  "/out:$outputPath"
  "/reference:System.dll"
  "/reference:System.Core.dll"
  "/reference:System.Drawing.dll"
  "/reference:System.Windows.Forms.dll"
  "/reference:System.Web.Extensions.dll"
  "/reference:System.Xml.dll"
  "/resource:$defaultGraphPath,RelationshipGraphNative.Data.default.json"
  "/resource:$iconPath,RelationshipGraphNative.Assets.app-icon.ico"
) + $sourcePaths

& $compilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) { throw "Native desktop application compilation failed." }

Write-Output "Created native Windows application: $outputPath"
