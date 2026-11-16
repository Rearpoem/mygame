<#
.SYNOPSIS
Runs the prototype's Unity checks sequentially and records their actual results.
.DESCRIPTION
Requires Unity 6000.3.28f1 and a closed Game project. Never deletes a lock file.
All creates a fresh Shanghai-timestamped output directory and runs Generate,
EditMode, PlayMode, Examples and Build in that order. A single stage runs only
that stage. Test stages deliberately omit -quit so Unity Test Framework can exit.
.PARAMETER Stage
All, Generate, EditMode, PlayMode, Examples, or Build. Defaults to All.
.PARAMETER RepositoryRoot
Repository directory containing Game. Defaults to this script's parent directory.
.PARAMETER UnityPath
Unity Editor executable. Defaults to E:\Unity\Editors\6000.3.28f1\Editor\Unity.exe.
.PARAMETER OutputDirectory
A new, nonexistent output directory. Relative paths resolve under RepositoryRoot.
Defaults to outputs/prototype_checks/<Shanghai timestamp>. All logs, test XML,
examples, summary.json and the Windows build are written under this directory.
.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Run-PrototypeChecks.ps1
.EXAMPLE
.\tools\Run-PrototypeChecks.ps1 -Stage EditMode
.EXAMPLE
.\tools\Run-PrototypeChecks.ps1 -Stage Build -OutputDirectory outputs\manual_build
#>
[CmdletBinding()]
param(
    [ValidateSet('All', 'Generate', 'EditMode', 'PlayMode', 'Examples', 'Build')]
    [string]$Stage = 'All',
    [string]$RepositoryRoot,
    [string]$UnityPath = 'E:\Unity\Editors\6000.3.28f1\Editor\Unity.exe',
    [string]$OutputDirectory,
    [string]$TestFilter
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $toolScriptPath = $PSCommandPath
    if ([string]::IsNullOrWhiteSpace($toolScriptPath)) { $toolScriptPath = $MyInvocation.MyCommand.Path }
    if ([string]::IsNullOrWhiteSpace($toolScriptPath)) { throw 'Cannot locate this script; pass -RepositoryRoot explicitly.' }
    $RepositoryRoot = Split-Path -Parent (Split-Path -Parent $toolScriptPath)
}
$requiredVersion = '6000.3.28f1'
$stageOrder = @('Generate', 'EditMode', 'PlayMode', 'Examples', 'Build')
$shanghaiZone = [TimeZoneInfo]::FindSystemTimeZoneById('China Standard Time')

function Get-ShanghaiTimestamp {
    param([string]$Format = 'yyyy-MM-ddTHH:mm:ss.fffzzz')
    [TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $shanghaiZone).ToString(
        $Format, [Globalization.CultureInfo]::InvariantCulture)
}

function Get-FullInputPath {
    param([string]$Path, [string]$RelativeTo)
    if (-not [IO.Path]::IsPathRooted($Path)) { $Path = Join-Path $RelativeTo $Path }
    [IO.Path]::GetFullPath($Path)
}

function Quote-NativePath {
    param([string]$Path)
    if ($Path.Contains('"')) { throw 'A Windows path cannot contain a double quote.' }
    # Double terminal backslashes before the closing quote for Windows argv parsing.
    $trailingSlashes = [regex]::Match($Path, '\\+$').Length
    '"' + $Path + ('\' * $trailingSlashes) + '"'
}

function Initialize-ProcessTokenReader {
    if ($null -ne ('PrototypeChecksProcessToken' -as [type])) { return }
    # Read one non-sensitive token flag. No credentials, token bytes or environment changes.
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
public static class PrototypeChecksProcessToken {
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool GetTokenInformation(IntPtr token, int kind, out int value, int size, out int returned);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    public static bool IsAppContainer(int pid) {
        IntPtr process = OpenProcess(0x1000, false, pid), token = IntPtr.Zero;
        if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            if (!OpenProcessToken(process, 8, out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            int value, returned;
            if (!GetTokenInformation(token, 29, out value, 4, out returned)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return value != 0;
        } finally { if (token != IntPtr.Zero) CloseHandle(token); CloseHandle(process); }
    }
    public static string Owner(int pid) {
        IntPtr process = OpenProcess(0x1000, false, pid), token = IntPtr.Zero;
        if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            if (!OpenProcessToken(process, 8, out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            using (WindowsIdentity identity = new WindowsIdentity(token)) return identity.Name;
        } finally { if (token != IntPtr.Zero) CloseHandle(token); CloseHandle(process); }
    }
}
'@ -ErrorAction Stop
}

function Get-ExecutionContextMetadata {
    param([int]$ProcessId)
    Initialize-ProcessTokenReader
    $appContainer = [PrototypeChecksProcessToken]::IsAppContainer($ProcessId)
    # Native queries avoid a slow WMI method call losing short-lived Unity children.
    $instance = Get-Process -Id $ProcessId -ErrorAction Stop
    $owner = [PrototypeChecksProcessToken]::Owner($ProcessId)
    [pscustomobject][ordered]@{
        processId = $ProcessId
        executablePath = $instance.Path
        owner = $owner
        sessionId = $instance.SessionId
        isAppContainer = $appContainer
        capturedAtShanghai = Get-ShanghaiTimestamp
    }
}

function Assert-ProjectAvailable {
    param([string]$ProjectPath)
    # Hub's resources/unity.exe serve is not an Editor owning a project.
    $editors = @(Get-CimInstance -ClassName Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction Stop)
    foreach ($editor in $editors) {
        if ([string]::IsNullOrWhiteSpace($editor.CommandLine)) {
            if ($editor.ExecutablePath -match '(?i)[\\/]Editor[\\/]Unity\.exe$') {
                throw "Cannot inspect Editor process $($editor.ProcessId); cannot verify project exclusivity."
            }
            continue
        }
        $match = [regex]::Match($editor.CommandLine, '(?i)(?:^|\s)-projectPath\s+(?:"(?<quoted>[^"]+)"|(?<plain>\S+))')
        if (-not $match.Success) { continue }
        $openPath = $match.Groups['quoted'].Value
        if (-not $openPath) { $openPath = $match.Groups['plain'].Value }
        if (-not [IO.Path]::IsPathRooted($openPath)) {
            throw "Unity process $($editor.ProcessId) has a relative project path; cannot verify project exclusivity."
        }
        $openPath = [IO.Path]::GetFullPath($openPath).TrimEnd([char[]]'\/')
        if ([string]::Equals($openPath, $ProjectPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unity process $($editor.ProcessId) already owns this project. Close it before running checks."
        }
    }
    # Probe existing locks without deleting or writing them. Unity may take over an
    # unowned stale lock; a handle held by an active Editor still blocks this run.
    foreach ($lockPath in @((Join-Path $ProjectPath 'Temp\UnityLockfile'), (Join-Path $ProjectPath 'UnityLockfile'))) {
        if (-not (Test-Path -LiteralPath $lockPath)) { continue }
        $lockHandle = $null
        try {
            $lockHandle = [IO.File]::Open($lockPath, [IO.FileMode]::Open,
                [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        }
        catch {
            if (Test-Path -LiteralPath $lockPath) {
                throw "Cannot obtain exclusive access to project lock: $lockPath. The script will not remove it."
            }
        }
        finally { if ($null -ne $lockHandle) { $lockHandle.Dispose() } }
    }
}

function Read-NUnitSummary {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing NUnit results: $Path" }
    [xml]$document = [IO.File]::ReadAllText($Path)
    $root = $document.DocumentElement
    if ($root.LocalName -ne 'test-run') { throw "Expected an NUnit test-run root in $Path" }
    foreach ($required in @('result', 'total', 'failed')) {
        if (-not $root.HasAttribute($required)) { throw "NUnit results lack '$required': $Path" }
    }
    $counts = @{}
    foreach ($name in @('total', 'passed', 'failed', 'skipped', 'inconclusive')) {
        $counts[$name] = $null
        if ($root.HasAttribute($name)) {
            [long]$count = 0
            if (-not [long]::TryParse($root.GetAttribute($name), [ref]$count) -or $count -lt 0) {
                throw "Invalid NUnit '$name' count in $Path"
            }
            $counts[$name] = $count
        }
    }
    [pscustomobject][ordered]@{
        result = $root.GetAttribute('result')
        total = $counts.total
        passed = $counts.passed
        failed = $counts.failed
        skipped = $counts.skipped
        inconclusive = $counts.inconclusive
    }
}

$repositoryPath = [IO.Path]::GetFullPath($RepositoryRoot)
$projectPath = (Join-Path $repositoryPath 'Game').TrimEnd([char[]]'\/')
$unityExecutable = Get-FullInputPath $UnityPath $repositoryPath
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path 'outputs\prototype_checks' (Get-ShanghaiTimestamp 'yyyyMMdd_HHmmss_fff')
}
$runDirectory = Get-FullInputPath $OutputDirectory $repositoryPath
$summaryPath = Join-Path $runDirectory 'summary.json'
$examplesDirectory = Join-Path $runDirectory 'Examples'
$buildExecutable = Join-Path $runDirectory 'Windows\Prototype.exe'
$savedReportPath = [Environment]::GetEnvironmentVariable('PROTOTYPE_REPORT_PATH', 'Process')
$savedBuildPath = [Environment]::GetEnvironmentVariable('PROTOTYPE_BUILD_PATH', 'Process')
$summary = $null
$scriptExitCode = 1

try {
    if (Test-Path -LiteralPath $runDirectory) {
        throw "OutputDirectory must be new and nonexistent: $runDirectory"
    }
    New-Item -ItemType Directory -Path $runDirectory -ErrorAction Stop | Out-Null
    $records = @()
    foreach ($name in $stageOrder) {
        $selected = $Stage -eq 'All' -or $Stage -eq $name
        $records += [pscustomobject][ordered]@{
            name = $name
            status = $(if ($selected) { 'NotRun' } else { 'NotRequested' })
            reason = $(if ($selected) { '' } else { 'Not selected by -Stage.' })
            startedAtShanghai = $null
            completedAtShanghai = $null
            exitCode = $null
            processId = $null
            executionContext = $null
            commandLine = $null
            logFile = Join-Path $runDirectory ($name + '.log')
            testResults = $(if ($name -in @('EditMode', 'PlayMode')) { Join-Path $runDirectory ($name + '.xml') } else { $null })
            testSummary = $null
            artifacts = @()
        }
    }
    $summary = [pscustomobject][ordered]@{
        schemaVersion = 1
        requestedStage = $Stage
        testFilter = $TestFilter
        repositoryRoot = $repositoryPath
        projectPath = $projectPath
        unityPath = $unityExecutable
        requiredUnityVersion = $requiredVersion
        projectVersion = $null
        runDirectory = $runDirectory
        startedAtShanghai = Get-ShanghaiTimestamp
        completedAtShanghai = $null
        overallResult = 'NotRun'
        error = $null
        preflight = [pscustomobject]@{ status = 'NotRun'; reason = '' }
        runnerExecutionContext = $null
        desktopServiceContexts = @()
        stages = $records
    }
    Write-Host "Run directory: $runDirectory"

    $versionFile = Join-Path $projectPath 'ProjectSettings\ProjectVersion.txt'
    if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) { throw "Missing ProjectVersion.txt: $versionFile" }
    $versionMatch = [regex]::Match([IO.File]::ReadAllText($versionFile), '(?m)^m_EditorVersion:\s*(\S+)\s*$')
    if (-not $versionMatch.Success) { throw "Cannot read m_EditorVersion from $versionFile" }
    $summary.projectVersion = $versionMatch.Groups[1].Value
    if ($summary.projectVersion -ne $requiredVersion) {
        throw "Project version is $($summary.projectVersion); required $requiredVersion."
    }
    if (-not (Test-Path -LiteralPath $unityExecutable -PathType Leaf)) { throw "Unity executable not found: $unityExecutable" }
    $summary.runnerExecutionContext = Get-ExecutionContextMetadata $PID
    if ($summary.runnerExecutionContext.isAppContainer -or $summary.runnerExecutionContext.sessionId -eq 0) {
        throw 'Run this entire script in the approved interactive desktop-user context, outside AppContainer. No profile/environment substitution is supported.'
    }
    $services = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity Hub.exe' OR Name = 'Unity.Licensing.Client.exe'" -ErrorAction Stop)
    $summary.desktopServiceContexts = @($services | ForEach-Object {
        try { Get-ExecutionContextMetadata $_.ProcessId }
        catch { [pscustomobject]@{ processId = $_.ProcessId; metadataStatus = 'Unavailable'; reason = 'Process exited or context query unavailable.' } }
    })
    Assert-ProjectAvailable $projectPath
    $summary.preflight.status = 'Passed'

    foreach ($record in $records) {
        if ($record.status -eq 'NotRequested') { continue }
        try {
            Assert-ProjectAvailable $projectPath
            $arguments = @('-batchmode', '-nographics', '-projectPath', (Quote-NativePath $projectPath),
                '-logFile', (Quote-NativePath $record.logFile))
            if ($record.name -in @('EditMode', 'PlayMode')) {
                $arguments += @('-runTests', '-testPlatform', $record.name, '-testResults', (Quote-NativePath $record.testResults))
                if (-not [string]::IsNullOrWhiteSpace($TestFilter)) { $arguments += @('-testFilter', (Quote-NativePath $TestFilter)) }
            }
            else {
                $method = switch ($record.name) {
                    'Generate' { 'GenerateHub' }
                    'Examples' { 'ExportExamples' }
                    'Build' { 'BuildWindowsDevelopment' }
                }
                $arguments += @('-quit', '-executeMethod', ('Prototype.Editor.PrototypeProjectBuilder.' + $method))
            }
            if ($record.name -eq 'Examples') {
                [Environment]::SetEnvironmentVariable('PROTOTYPE_REPORT_PATH', $examplesDirectory, 'Process')
            }
            if ($record.name -eq 'Build') {
                [Environment]::SetEnvironmentVariable('PROTOTYPE_BUILD_PATH', $buildExecutable, 'Process')
            }
            $record.startedAtShanghai = Get-ShanghaiTimestamp
            $record.status = 'Running'
            $record.commandLine = (Quote-NativePath $unityExecutable) + ' ' + ($arguments -join ' ')
            Write-Host "[$($record.name)] Running"
            $process = Start-Process -FilePath $unityExecutable -ArgumentList ($arguments -join ' ') `
                -WorkingDirectory $projectPath -WindowStyle Hidden -PassThru -ErrorAction Stop
            $record.processId = $process.Id
            # Keep the handle before waiting so the real exit code remains available.
            $null = $process.Handle
            try {
                $record.executionContext = Get-ExecutionContextMetadata $process.Id
                $context = $record.executionContext
                if ($context.isAppContainer -or $context.sessionId -ne $summary.runnerExecutionContext.sessionId `
                    -or -not [string]::Equals($context.owner, $summary.runnerExecutionContext.owner, [StringComparison]::OrdinalIgnoreCase) `
                    -or -not [string]::Equals($context.executablePath, $unityExecutable, [StringComparison]::OrdinalIgnoreCase)) {
                    throw 'Unity child context differs from the approved desktop runner; stopping this newly launched process.'
                }
                Write-Host "[$($record.name)] Unity PID=$($context.processId), owner=$($context.owner), session=$($context.sessionId), AppContainer=$($context.isAppContainer)"
                $process.WaitForExit()
            }
            catch {
                # Only our exact new child is stopped if its context cannot be verified.
                if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
                $process.Refresh()
                $record.exitCode = $process.ExitCode
                throw
            }
            $process.Refresh()
            $record.exitCode = $process.ExitCode
            if ($null -eq $record.exitCode) { throw 'Unity did not expose a process exit code.' }
            if ($record.name -in @('EditMode', 'PlayMode')) {
                $record.testSummary = Read-NUnitSummary $record.testResults
            }
            if ($record.exitCode -ne 0) { throw "Unity exited with code $($record.exitCode). See $($record.logFile)" }
            if ($null -ne $record.testSummary) {
                $test = $record.testSummary
                if ($test.total -le 0 -or $test.failed -ne 0 -or $test.result -cne 'Passed') {
                    throw "NUnit result=$($test.result), total=$($test.total), failed=$($test.failed); require Passed, total>0, failed=0."
                }
            }
            if (-not (Test-Path -LiteralPath $record.logFile -PathType Leaf)) { throw "Unity log is missing: $($record.logFile)" }
            if ($record.name -eq 'Generate') {
                $hubScene = Join-Path $projectPath 'Assets\Prototype\Scenes\PrototypeHub.unity'
                if (-not (Test-Path -LiteralPath $hubScene -PathType Leaf)) { throw "Generated Hub scene is missing: $hubScene" }
                $record.artifacts = @($hubScene)
            }
            if ($record.name -eq 'Examples') {
                $record.artifacts = @(Get-ChildItem -LiteralPath $examplesDirectory -File | Select-Object -ExpandProperty FullName)
                if ($record.artifacts.Count -eq 0) { throw 'Examples exported no files.' }
            }
            if ($record.name -eq 'Build') {
                if (-not (Test-Path -LiteralPath $buildExecutable -PathType Leaf)) { throw "Build executable is missing: $buildExecutable" }
                if ((Get-Item -LiteralPath $buildExecutable).Length -le 0) { throw 'Build executable is empty.' }
                $record.artifacts = @($buildExecutable)
            }
            $record.status = 'Passed'
            Write-Host "[$($record.name)] Passed (exit=$($record.exitCode))"
        }
        catch {
            $record.status = 'Failed'
            $record.reason = $_.Exception.Message
            throw
        }
        finally { $record.completedAtShanghai = Get-ShanghaiTimestamp }
    }
    $summary.overallResult = 'Passed'
    $scriptExitCode = 0
}
catch {
    if ($null -ne $summary) {
        $summary.overallResult = 'Failed'
        $summary.error = $_.Exception.Message
        if ($summary.preflight.status -ne 'Passed') {
            $summary.preflight.status = 'Failed'
            $summary.preflight.reason = $_.Exception.Message
        }
    }
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
}
finally {
    [Environment]::SetEnvironmentVariable('PROTOTYPE_REPORT_PATH', $savedReportPath, 'Process')
    [Environment]::SetEnvironmentVariable('PROTOTYPE_BUILD_PATH', $savedBuildPath, 'Process')
    if ($null -ne $summary) {
        foreach ($record in $summary.stages) {
            if ($record.status -eq 'NotRun') {
                $record.status = 'Skipped'
                $record.reason = 'Not run because preflight or an earlier stage failed.'
            }
        }
        $summary.completedAtShanghai = Get-ShanghaiTimestamp
        [IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
        foreach ($record in $summary.stages) {
            $detail = ''
            if ($null -ne $record.testSummary) {
                $detail = " (total=$($record.testSummary.total), failed=$($record.testSummary.failed), skipped=$($record.testSummary.skipped))"
            }
            Write-Host "[$($record.name)] $($record.status)$detail"
        }
        Write-Host "Summary: $summaryPath"
    }
}
exit $scriptExitCode
