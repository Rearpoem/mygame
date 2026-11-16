param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$combatRoot = Split-Path -Parent $PSScriptRoot
$assetsRoot = Split-Path -Parent (Split-Path -Parent $combatRoot)
$configurationRoot = Join-Path $assetsRoot 'StreamingAssets\Prototype\Combat'
$runtimeFiles = @(Get-ChildItem -LiteralPath (Join-Path $combatRoot 'Runtime') -Filter '*.cs' -File)
if (-not ('Prototype.Combat.CombatSession' -as [type])) { Add-Type -Path $runtimeFiles.FullName }
$jsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
$jsonOptions.IncludeFields = $true
$jsonOptions.WriteIndented = $true
$utf8 = [System.Text.UTF8Encoding]::new($false)
[void][System.IO.Directory]::CreateDirectory($OutputDirectory)

function Read-Configuration([string]$Name, [type]$Type) {
    $json = [System.IO.File]::ReadAllText((Join-Path $configurationRoot $Name))
    return [System.Text.Json.JsonSerializer]::Deserialize($json, $Type, $jsonOptions)
}
function Serialize-Result($Value) {
    if ($Value.GetType().IsArray -or $Value.GetType().FullName -eq 'System.Management.Automation.PSCustomObject') {
        return ConvertTo-Json -InputObject $Value -Depth 100
    }
    return [System.Text.Json.JsonSerializer]::Serialize($Value, $Value.GetType(), $jsonOptions)
}
function Write-JsonResult([string]$Name, $Value) {
    [System.IO.File]::WriteAllText((Join-Path $OutputDirectory $Name), (Serialize-Result $Value) + "`n", $utf8)
}
function Write-TextResult([string]$Name, [string]$Value) {
    [System.IO.File]::WriteAllText((Join-Path $OutputDirectory $Name), $Value.Replace("`r`n", "`n"), $utf8)
}
function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Alive-Friendly($Run) { return @($Run.snapshot.units | Where-Object { $_.team -eq 'friendly' -and $_.hp -gt 0 }).Count }

$ambush = [Prototype.Combat.CombatSampleRuns]::ComparePriority((Read-Configuration 'round2_ambush.json' ([Prototype.Combat.CombatScenario])))
$pressure = [Prototype.Combat.CombatSampleRuns]::ComparePriority((Read-Configuration 'round2_pressure.json' ([Prototype.Combat.CombatScenario])))
$capacity = [Prototype.Combat.CombatSampleRuns]::CompareIntelligenceCapacity((Read-Configuration 'round2_capacity.json' ([Prototype.Combat.CombatScenario])))
$equipment = [Prototype.Combat.CombatSampleRuns]::CompareEquipment((Read-Configuration 'round2_equipment.json' ([Prototype.Combat.CombatScenario])))
$comparisons = @(
    @{ Name = 'AMBUSH'; Value = $ambush },
    @{ Name = 'PRESSURE'; Value = $pressure },
    @{ Name = 'CAPACITY'; Value = $capacity },
    @{ Name = 'EQUIPMENT'; Value = $equipment }
)
foreach ($comparison in $comparisons) {
    Require $comparison.Value.onlyDeclaredSettingChanged ('Undeclared configuration difference: ' + $comparison.Name)
    Require ($null -ne $comparison.Value.firstDivergence) ('No real decision/effect divergence: ' + $comparison.Name)
    Write-JsonResult ('ROUND2_' + $comparison.Name + '_REPORT.json') $comparison.Value
    Write-TextResult ('ROUND2_' + $comparison.Name + '_REPORT.md') ([Prototype.Combat.CombatSampleRuns]::FormatComparisonMarkdown($comparison.Value))
}
Require ((Alive-Friendly $ambush.variant) -gt (Alive-Friendly $ambush.baseline)) 'Ambush must reward attack priority.'
Require ((Alive-Friendly $pressure.baseline) -gt (Alive-Friendly $pressure.variant)) 'Pressure must reward conditional first aid.'
Require ($capacity.variant.outcome -eq 'Victory') 'High capacity sample should win.'
Require ($capacity.baseline.outcome -ne 'Victory') 'Low capacity sample should expose the missing emergency branch.'
$lowMage = @($capacity.baseline.snapshot.units | Where-Object id -eq 'mage')[0]
$highMage = @($capacity.variant.snapshot.units | Where-Object id -eq 'mage')[0]
Require ($lowMage.instructionCapacity -eq 2 -and $highMage.instructionCapacity -eq 3) 'Capacity test must isolate the third slot.'

$expeditionConfig = Read-Configuration 'round2_expedition.json' ([Prototype.Combat.CombatExpeditionConfig])
$viewed = [Prototype.Combat.CombatExpeditionSession]::new($expeditionConfig)
[void]$viewed.RunNextEncounter()
[void]$viewed.RunNextEncounter()
$viewedReport = $viewed.GetReport()
$batched = [Prototype.Combat.CombatExpeditionSession]::new($expeditionConfig)
$batchedReport = $batched.RunToEnd()
Require ((Serialize-Result $viewedReport) -ceq (Serialize-Result $batchedReport)) 'Viewed vs batch expedition differs.'
Require ($batchedReport.encounters.Count -eq 2 -and $batchedReport.stopReason -eq 'CompletedAllEncounters') 'Expected two completed encounters.'
Require ((Serialize-Result $batchedReport) -ceq (Serialize-Result $batched.GetReport())) 'Report read changes state or rewards.'
Require ((Serialize-Result $batchedReport) -ceq (Serialize-Result $batched.RunToEnd())) 'Repeated final run duplicates settlement.'
$secondStart = $batchedReport.encounters[1].battleStart
$firstEnd = $batchedReport.encounters[0].battleEnd
Require ($secondStart.time -eq $firstEnd.time + 15) 'Intermission must consume exactly 15 WAIT.'
foreach ($unit in @($secondStart.units | Where-Object team -eq 'friendly')) {
    $old = @($firstEnd.units | Where-Object id -eq $unit.id)[0]
    Require ($old.hp -eq $unit.hp -and $old.mp -eq $unit.mp) ('HP/MP were reset across encounters: ' + $unit.id)
}
$afterGapAlly = @($secondStart.units | Where-Object id -eq 'ally')[0]
Require (@($afterGapAlly.statuses | Where-Object id -eq 'short_vulnerability').Count -eq 0) 'Expired intermission status was resurrected.'
Require (@($afterGapAlly.statuses | Where-Object id -eq 'camp_guard').Count -eq 1) 'Valid guard must carry.'
Write-JsonResult 'ROUND2_EXPEDITION_REPORT.json' $batchedReport
Write-TextResult 'ROUND2_EXPEDITION_REPORT.md' ([Prototype.Combat.CombatSampleRuns]::FormatExpeditionMarkdown($batchedReport))

$queueCases = @('round2_time_tie.json', 'round2_one_idle.json', 'round2_stalemate.json')
$queueRows = [System.Collections.Generic.List[object]]::new()
$queueText = [System.Text.StringBuilder]::new()
[void]$queueText.Append("# 队列与停止原因实跑`n`n同刻顺序、无指令休眠及僵局均采用 ASSUMPTIONS.md 的第二轮试玩口径。下列来自同一 CombatSession，单步与自动共用结算。`n")
foreach ($name in $queueCases) {
    $scenario = Read-Configuration $name ([Prototype.Combat.CombatScenario])
    $automatic = [Prototype.Combat.CombatSession]::new($scenario)
    $run = $automatic.RunToCompletion()
    $single = [Prototype.Combat.CombatSession]::new($scenario)
    $guardCount = 0
    while (-not $single.GetSnapshot().finished -and $guardCount -lt 10000) { [void]$single.Step(); $guardCount++ }
    Require ($single.GetSnapshot().finished) ('Single step did not stop: ' + $name)
    Require ((Serialize-Result $single.GetSnapshot()) -ceq (Serialize-Result $run.snapshot)) ('Step/auto state differs: ' + $name)
    Require ($single.ExportLogText() -ceq $automatic.ExportLogText()) ('Step/auto trace differs: ' + $name)
    $queueRows.Add([PSCustomObject]@{ configuration = $name; result = $run })
    [void]$queueText.Append("`n## $name`n`n结局：$($run.outcome)；停止原因：$($run.stopReason)；结束WAIT：$($run.time)。`n`n")
    [void]$queueText.Append('```text' + "`n" + $automatic.ExportLogText() + '```' + "`n")
}
Write-JsonResult 'ROUND2_QUEUE_REPORT.json' $queueRows.ToArray()
Write-TextResult 'ROUND2_QUEUE_REPORT.md' $queueText.ToString()
$summary = [PSCustomObject]@{
    result = 'passed'
    engine = 'Actual CombatSession runtime compiled with PowerShell Add-Type'
    comparisonCount = 4
    queueReplayCount = 3
    expeditionEncounterCount = $batchedReport.encounters.Count
    expeditionViewedEqualsBatch = $true
    reportReadIsPure = $true
    firstBattleEnd = $firstEnd.time
    intermissionWait = 15
    secondBattleStart = $secondStart.time
    publicCashReceivable = $batchedReport.publicCash
    publicDropsReceivable = $batchedReport.publicDrops
    experienceReceivable = $batchedReport.experience
}
Write-JsonResult 'ROUND2_SAMPLE_VERIFICATION.json' $summary
$summary | ConvertTo-Json -Depth 10
