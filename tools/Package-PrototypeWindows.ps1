<#
.SYNOPSIS
Packages a successful Windows build and verifies a fresh ZIP extraction by SHA-256.
.DESCRIPTION
Copies the entire Windows runtime into PackageName, adds UTF-8 Chinese
instructions, creates a ZIP containing that directory, and extracts into a new
Extracted directory. Every source copy and every extracted file is checked.
Does not launch the player or replace manual GUI acceptance.
.PARAMETER BuildDirectory
Windows directory produced by a successful Run-PrototypeChecks.ps1 Build stage.
.PARAMETER OutputDirectory
A new, nonexistent directory for the package, ZIP, extraction and manifest.json.
.PARAMETER BuildSummaryPath
Defaults to summary.json in the parent of BuildDirectory.
.PARAMETER InstructionsPath
Defaults to Game/PLAY_WINDOWS.md under this script's repository.
.PARAMETER PackageName
Package directory and ZIP basename. Defaults to ExpeditionPrototype for older calls.
Use a simple name containing letters, digits, underscores or hyphens.
.EXAMPLE
.\tools\Package-PrototypeWindows.ps1 -BuildDirectory .\outputs\prototype_checks\RUN\Windows -OutputDirectory .\outputs\round2_package
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BuildDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$BuildSummaryPath,
    [string]$InstructionsPath,
    [string]$ExecutableName = 'Prototype.exe',
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]*$')]
    [string]$PackageName = 'ExpeditionPrototype'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($InstructionsPath)) {
    $toolScriptPath = $PSCommandPath
    if ([string]::IsNullOrWhiteSpace($toolScriptPath)) { $toolScriptPath = $MyInvocation.MyCommand.Path }
    if ([string]::IsNullOrWhiteSpace($toolScriptPath)) { throw 'Cannot locate this script; pass -InstructionsPath explicitly.' }
    $packageRepository = Split-Path -Parent (Split-Path -Parent $toolScriptPath)
    $InstructionsPath = Join-Path $packageRepository 'Game\PLAY_WINDOWS.md'
}

function Get-TreeManifest {
    param([string]$Root)
    $prefix = $Root.TrimEnd([char[]]'\/') + [IO.Path]::DirectorySeparatorChar
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force | Sort-Object FullName)) {
        [pscustomobject][ordered]@{
            path = $file.FullName.Substring($prefix.Length).Replace('\', '/')
            bytes = $file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
}

function Assert-MatchingFiles {
    param([object[]]$Expected, [string]$ActualRoot, [switch]$RequireExactCount)
    if ($RequireExactCount) {
        $actualFiles = @(Get-ChildItem -LiteralPath $ActualRoot -File -Recurse -Force)
        if ($actualFiles.Count -ne $Expected.Count) { throw 'Extracted file count does not match the packaged runtime.' }
    }
    foreach ($entry in $Expected) {
        $actualPath = Join-Path $ActualRoot $entry.path.Replace('/', [IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $actualPath -PathType Leaf)) { throw "Missing copied/extracted file: $actualPath" }
        $file = Get-Item -LiteralPath $actualPath
        $hash = (Get-FileHash -LiteralPath $actualPath -Algorithm SHA256).Hash
        if ($file.Length -ne $entry.bytes -or $hash -cne $entry.sha256) { throw "SHA-256 mismatch: $actualPath" }
    }
}

$sourceDirectory = [IO.Path]::GetFullPath($BuildDirectory).TrimEnd([char[]]'\/')
$packageOutput = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd([char[]]'\/')
if ([string]::IsNullOrWhiteSpace($BuildSummaryPath)) {
    $BuildSummaryPath = Join-Path (Split-Path -Parent $sourceDirectory) 'summary.json'
}
$sourceSummaryPath = [IO.Path]::GetFullPath($BuildSummaryPath)
$manualSourcePath = [IO.Path]::GetFullPath($InstructionsPath)
$packageDirectory = Join-Path $packageOutput $PackageName
$zipPath = Join-Path $packageOutput ($PackageName + '-Windows.zip')
$extractionDirectory = Join-Path $packageOutput 'Extracted'
$extractedPackage = Join-Path $extractionDirectory $PackageName
$manifestPath = Join-Path $packageOutput 'manifest.json'
$manifest = $null
$scriptExitCode = 1

try {
    if (Test-Path -LiteralPath $packageOutput) { throw "OutputDirectory must be new and nonexistent: $packageOutput" }
    if ($packageOutput.StartsWith($sourceDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputDirectory must be outside the source Windows runtime.'
    }
    if ([IO.Path]::GetFileName($ExecutableName) -cne $ExecutableName -or [IO.Path]::GetExtension($ExecutableName) -ine '.exe') {
        throw 'ExecutableName must be an EXE filename without a directory.'
    }
    if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) { throw "Missing Windows runtime: $sourceDirectory" }
    if (-not (Test-Path -LiteralPath $sourceSummaryPath -PathType Leaf)) { throw "Missing build summary: $sourceSummaryPath" }
    if (-not (Test-Path -LiteralPath $manualSourcePath -PathType Leaf)) { throw "Missing Chinese instructions: $manualSourcePath" }
    $sourceSummary = Get-Content -LiteralPath $sourceSummaryPath -Raw | ConvertFrom-Json
    $buildResults = @($sourceSummary.stages | Where-Object name -eq 'Build')
    if ($sourceSummary.overallResult -ne 'Passed' -or $buildResults.Count -ne 1 -or
        $buildResults[0].status -ne 'Passed' -or $buildResults[0].exitCode -ne 0) {
        throw 'The source summary does not record a successful Build stage with exit code 0.'
    }
    $sourceExecutable = Join-Path $sourceDirectory $ExecutableName
    $recordedExecutable = @($buildResults[0].artifacts | Where-Object {
        [string]::Equals([IO.Path]::GetFullPath($_), $sourceExecutable, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($recordedExecutable.Count -ne 1) { throw 'BuildDirectory does not match the executable recorded by the successful Build stage.' }
    foreach ($requiredFile in @($sourceExecutable, (Join-Path $sourceDirectory 'UnityPlayer.dll'))) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) { throw "Incomplete Windows runtime: $requiredFile" }
        if ((Get-Item -LiteralPath $requiredFile).Length -le 0) { throw "Empty runtime file: $requiredFile" }
    }
    $dataDirectory = Join-Path $sourceDirectory ([IO.Path]::GetFileNameWithoutExtension($ExecutableName) + '_Data')
    if (-not (Test-Path -LiteralPath $dataDirectory -PathType Container)) { throw "Missing Unity data directory: $dataDirectory" }
    $reparsePoints = @(Get-ChildItem -LiteralPath $sourceDirectory -Recurse -Force | Where-Object {
        ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    })
    if ($reparsePoints.Count -gt 0) { throw 'Source runtime contains a reparse point; package a regular complete build directory.' }
    if (Test-Path -LiteralPath (Join-Path $sourceDirectory 'START_HERE_zh-CN.txt')) { throw 'Source runtime already contains the reserved instruction filename.' }

    New-Item -ItemType Directory -Path $packageOutput -ErrorAction Stop | Out-Null
    $zone = [TimeZoneInfo]::FindSystemTimeZoneById('China Standard Time')
    $manifest = [pscustomobject][ordered]@{
        schemaVersion = 1
        createdAtShanghai = [TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $zone).ToString('o')
        status = 'Running'
        error = $null
        sourceBuildDirectory = $sourceDirectory
        sourceBuildSummary = $sourceSummaryPath
        sourceBuildSummarySha256 = (Get-FileHash -LiteralPath $sourceSummaryPath -Algorithm SHA256).Hash
        sourceBuildExitCode = $buildResults[0].exitCode
        packageDirectory = $packageDirectory
        packageName = $PackageName
        zipPath = $zipPath
        zipSha256 = $null
        extractedDirectory = $extractedPackage
        executableRelativePath = $PackageName + '/' + $ExecutableName
        extractedExecutable = Join-Path $extractedPackage $ExecutableName
        instructions = 'START_HERE_zh-CN.txt'
        sourceCopyMatches = $false
        extractedFilesMatch = $false
        guiLaunchVerified = $false
        guiVerificationNote = 'Not performed by this script. Launch the extracted EXE and complete manual GUI acceptance.'
        sourceFileCount = 0
        packagedFileCount = 0
        files = @()
    }
    $sourceFiles = @(Get-TreeManifest $sourceDirectory)
    $manifest.sourceFileCount = $sourceFiles.Count
    New-Item -ItemType Directory -Path $packageDirectory -ErrorAction Stop | Out-Null
    foreach ($entry in @(Get-ChildItem -LiteralPath $sourceDirectory -Force)) {
        Copy-Item -LiteralPath $entry.FullName -Destination $packageDirectory -Recurse -Force -ErrorAction Stop
    }
    Assert-MatchingFiles $sourceFiles $packageDirectory
    $manifest.sourceCopyMatches = $true
    [IO.File]::WriteAllText((Join-Path $packageDirectory 'START_HERE_zh-CN.txt'),
        [IO.File]::ReadAllText($manualSourcePath), (New-Object Text.UTF8Encoding($false)))
    $manifest.files = @(Get-TreeManifest $packageDirectory)
    $manifest.packagedFileCount = $manifest.files.Count

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($packageDirectory, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
    $manifest.zipSha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    New-Item -ItemType Directory -Path $extractionDirectory -ErrorAction Stop | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $extractionDirectory)
    Assert-MatchingFiles $manifest.files $extractedPackage -RequireExactCount
    $manifest.extractedFilesMatch = $true
    $manifest.status = 'Passed'
    $scriptExitCode = 0
    Write-Host "Packaged and verified $($manifest.packagedFileCount) files."
    Write-Host "ZIP: $zipPath"
    Write-Host "Manual GUI target: $($manifest.extractedExecutable)"
}
catch {
    if ($null -ne $manifest) {
        $manifest.status = 'Failed'
        $manifest.error = $_.Exception.Message
    }
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
}
finally {
    if ($null -ne $manifest) {
        [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 7), (New-Object Text.UTF8Encoding($false)))
        Write-Host "Manifest: $manifestPath"
    }
}
exit $scriptExitCode
