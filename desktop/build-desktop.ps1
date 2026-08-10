param(
  [string]$OutputName = "关系图编辑器.exe",
  [string]$CertificateThumbprint = "",
  [string]$TimestampUrl = "https://timestamp.digicert.com",
  [string]$SignToolPath = "",
  [switch]$UseMachineCertificateStore
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$nativeRoot = Join-Path $projectRoot "native"
$defaultGraphPath = Join-Path $projectRoot "system-function-graph.json"
$iconPath = Join-Path $projectRoot "assets\app-icon.ico"
$manifestPath = Join-Path $PSScriptRoot "app.manifest"
$outputPath = if ([IO.Path]::IsPathRooted($OutputName)) {
  [IO.Path]::GetFullPath($OutputName)
} else {
  [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputName))
}
$outputDirectory = Split-Path -Parent $outputPath
$compilerCandidates = @(
  "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
  "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$compilerPath = $compilerCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $compilerPath) { throw "Windows C# compiler was not found." }
if (-not (Test-Path $nativeRoot)) { throw "native source folder was not found." }
if (-not (Test-Path $defaultGraphPath)) { throw "system-function-graph.json was not found." }
if (-not (Test-Path $iconPath)) { throw "assets\app-icon.ico was not found." }
if (-not (Test-Path $manifestPath)) { throw "desktop\app.manifest was not found." }
if ([String]::IsNullOrWhiteSpace($outputDirectory)) { throw "The output directory could not be determined." }

$sourcePaths = Get-ChildItem -LiteralPath $nativeRoot -Filter "*.cs" -File | Sort-Object Name | ForEach-Object { $_.FullName }
if ($sourcePaths.Count -lt 4) { throw "Native application sources are incomplete." }

$signingRequested = -not [String]::IsNullOrWhiteSpace($CertificateThumbprint)
$normalizedThumbprint = ""
$resolvedSignTool = ""
if ($signingRequested) {
  $normalizedThumbprint = ($CertificateThumbprint -replace '\s', '').ToUpperInvariant()
  if ($normalizedThumbprint -notmatch '^[0-9A-F]{40}$') {
    throw "CertificateThumbprint must be a 40-character SHA-1 certificate thumbprint."
  }

  if ($TimestampUrl) {
    $timestampUri = $null
    if (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestampUri) -or
        ($timestampUri.Scheme -ne "http" -and $timestampUri.Scheme -ne "https")) {
      throw "TimestampUrl must be an absolute HTTP or HTTPS URL, or an empty string."
    }
  }

  if ($SignToolPath) {
    if (-not (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) {
      throw "SignToolPath does not point to a file."
    }
    $resolvedSignTool = (Resolve-Path -LiteralPath $SignToolPath -ErrorAction Stop).Path
  } else {
    $signToolCommand = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if ($signToolCommand) {
      $resolvedSignTool = $signToolCommand.Source
    } else {
      $programFilesX86 = ${env:ProgramFiles(x86)}
      if ($programFilesX86) {
        $windowsKitsRoot = Join-Path $programFilesX86 "Windows Kits\10\bin"
        if (Test-Path $windowsKitsRoot) {
          $signToolCandidates = @(Get-ChildItem -LiteralPath $windowsKitsRoot -Filter "signtool.exe" -File -Recurse |
            Where-Object { $_.FullName -match '\\(x64|x86|arm64)\\signtool\.exe$' })
          $resolvedSignTool = $signToolCandidates |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -ExpandProperty FullName -First 1
          if (-not $resolvedSignTool) {
            $resolvedSignTool = $signToolCandidates |
              Sort-Object FullName -Descending |
              Select-Object -ExpandProperty FullName -First 1
          }
        }
      }
    }
  }

  if (-not $resolvedSignTool) {
    throw "signtool.exe was not found. Install a Windows SDK or pass -SignToolPath."
  }

  $certificateStoreLocation = if ($UseMachineCertificateStore) { "LocalMachine" } else { "CurrentUser" }
  $certificatePath = "Cert:\$certificateStoreLocation\My\$normalizedThumbprint"
  $certificate = Get-Item -LiteralPath $certificatePath -ErrorAction SilentlyContinue
  if (-not $certificate) { throw "The signing certificate was not found in $certificateStoreLocation\My." }
  if (-not $certificate.HasPrivateKey) { throw "The signing certificate does not have an accessible private key." }
  $now = [DateTime]::Now
  if ($now -lt $certificate.NotBefore -or $now -gt $certificate.NotAfter) {
    throw "The signing certificate is not currently valid."
  }
  $enhancedKeyUsages = @($certificate.EnhancedKeyUsageList)
  if ($enhancedKeyUsages.Count -gt 0 -and
      -not ($enhancedKeyUsages | Where-Object { $_.ObjectId.Value -eq "1.3.6.1.5.5.7.3.3" })) {
    throw "The signing certificate is not valid for code signing."
  }
}

if (-not (Test-Path -LiteralPath $outputDirectory)) {
  New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}
$temporaryOutputPath = Join-Path $outputDirectory ("." + [IO.Path]::GetFileName($outputPath) + "." + [Guid]::NewGuid().ToString("N") + ".tmp.exe")
$publicationBackupPath = ""

$compilerArguments = @(
  "/nologo"
  "/target:winexe"
  "/platform:anycpu"
  "/optimize+"
  "/codepage:65001"
  "/main:RelationshipGraphNative.Program"
  "/win32icon:$iconPath"
  "/win32manifest:$manifestPath"
  "/out:$temporaryOutputPath"
  "/reference:System.dll"
  "/reference:System.Core.dll"
  "/reference:System.Drawing.dll"
  "/reference:System.Windows.Forms.dll"
  "/reference:System.Web.Extensions.dll"
  "/reference:System.Xml.dll"
  "/resource:$defaultGraphPath,RelationshipGraphNative.Data.default.json"
  "/resource:$iconPath,RelationshipGraphNative.Assets.app-icon.ico"
) + $sourcePaths

try {
  & $compilerPath @compilerArguments
  if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $temporaryOutputPath -PathType Leaf)) {
    throw "Native desktop application compilation failed."
  }

  if ($signingRequested) {
    $signArguments = @("sign", "/sha1", $normalizedThumbprint, "/s", "My", "/fd", "SHA256")
    if ($UseMachineCertificateStore) { $signArguments += "/sm" }
    if ($TimestampUrl) { $signArguments += @("/tr", $TimestampUrl, "/td", "SHA256") }
    $signArguments += $temporaryOutputPath

    & $resolvedSignTool @signArguments
    if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed." }

    & $resolvedSignTool "verify" "/pa" $temporaryOutputPath
    if ($LASTEXITCODE -ne 0) { throw "Authenticode signature verification failed." }
  }

  if (Test-Path -LiteralPath $outputPath) {
    $publicationBackupPath = Join-Path $outputDirectory ("." + [IO.Path]::GetFileName($outputPath) + "." + [Guid]::NewGuid().ToString("N") + ".publish-backup")
    [IO.File]::Replace($temporaryOutputPath, $outputPath, $publicationBackupPath, $true)
  } else {
    [IO.File]::Move($temporaryOutputPath, $outputPath)
  }
  $temporaryOutputPath = ""
  if ($publicationBackupPath) {
    try {
      if (Test-Path -LiteralPath $publicationBackupPath) { Remove-Item -LiteralPath $publicationBackupPath -Force -ErrorAction Stop }
      $publicationBackupPath = ""
    } catch {
      Write-Warning "Unable to remove publication backup: $publicationBackupPath"
    }
  }
} finally {
  if ($publicationBackupPath -and (Test-Path -LiteralPath $publicationBackupPath)) {
    try {
      if (-not (Test-Path -LiteralPath $outputPath)) {
        [IO.File]::Move($publicationBackupPath, $outputPath)
      } else {
        Remove-Item -LiteralPath $publicationBackupPath -Force -ErrorAction Stop
      }
      $publicationBackupPath = ""
    } catch {
      Write-Warning "Unable to clean publication backup: $publicationBackupPath"
    }
  }
  if ($temporaryOutputPath -and (Test-Path -LiteralPath $temporaryOutputPath)) {
    try { Remove-Item -LiteralPath $temporaryOutputPath -Force -ErrorAction Stop }
    catch { Write-Warning "Unable to remove temporary build output: $temporaryOutputPath" }
  }
}

if ($signingRequested) { Write-Output "Signed with certificate: $normalizedThumbprint" }
Write-Output "Created native Windows application: $outputPath"
