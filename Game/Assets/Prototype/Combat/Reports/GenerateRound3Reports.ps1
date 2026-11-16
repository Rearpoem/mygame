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
[void][IO.Directory]::CreateDirectory($OutputDirectory)
function Read-Scenario([string]$Name) {
    return [System.Text.Json.JsonSerializer]::Deserialize([IO.File]::ReadAllText((Join-Path $configurationRoot $Name)), [Prototype.Combat.CombatScenario], $jsonOptions)
}
function Read-Expedition([string]$Name) {
    return [System.Text.Json.JsonSerializer]::Deserialize([IO.File]::ReadAllText((Join-Path $configurationRoot $Name)), [Prototype.Combat.CombatExpeditionConfig], $jsonOptions)
}
function Serialize($Value) {
    if ($Value.GetType().IsArray -or $Value.GetType().FullName -eq 'System.Management.Automation.PSCustomObject') { return ConvertTo-Json -InputObject $Value -Depth 100 }
    return [System.Text.Json.JsonSerializer]::Serialize($Value, $Value.GetType(), $jsonOptions)
}
function Write-Json([string]$Name, $Value) { [IO.File]::WriteAllText((Join-Path $OutputDirectory $Name), (Serialize $Value) + "`n", $utf8) }
function Write-Text([string]$Name, [string]$Value) { [IO.File]::WriteAllText((Join-Path $OutputDirectory $Name), $Value.Replace("`r`n", "`n"), $utf8) }
function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Alive-Friendly($Run) { return @($Run.snapshot.units | Where-Object { $_.team -eq 'friendly' -and $_.hp -gt 0 }).Count }

$reserveConfig = Read-Scenario 'round3_mp_reserve.json'
$focusConfig = Read-Scenario 'round3_focus_mark.json'
$reserve = [Prototype.Combat.CombatTacticsSamples]::CompareMpReserve($reserveConfig)
$focus = [Prototype.Combat.CombatTacticsSamples]::CompareFocusMark($focusConfig)
foreach ($case in @(@{Name='MP_RESERVE'; Report=$reserve}, @{Name='FOCUS_MARK'; Report=$focus})) {
    Require $case.Report.onlyDeclaredSettingChanged ('More than one configuration setting changed: ' + $case.Name)
    Require ($null -ne $case.Report.firstDivergence) ('No actual action divergence: ' + $case.Name)
    Write-Json ('ROUND3_' + $case.Name + '_REPORT.json') $case.Report
    Write-Text ('ROUND3_' + $case.Name + '_REPORT.md') ([Prototype.Combat.CombatTacticsSamples]::FormatComparisonMarkdown($case.Report))
}
Require ((Alive-Friendly $reserve.variant) -gt (Alive-Friendly $reserve.baseline)) 'Reserving MP must keep an additional teammate alive.'
Require ($reserve.firstDivergence.baseline.time -eq 40 -and $reserve.firstDivergence.variant.time -eq 40) 'MP decisions should first diverge at the second mage action, not the initial matching action.'
Require ($reserve.firstDivergence.baseline.actionId -eq 'tactics_power_attack' -and $reserve.firstDivergence.variant.actionId -eq 'tactics_quick_attack') 'MP threshold must change the actual selected attack.'
Require ((Alive-Friendly $focus.variant) -gt (Alive-Friendly $focus.baseline)) 'Focus mark must keep an additional teammate alive.'
Require ($focus.firstDivergence.baseline.targetId -eq 'enemy_decoy' -and $focus.firstDivergence.variant.targetId -eq 'enemy_caster') 'Focus selector must change the actual attack target.'
Require ((Serialize $reserve) -ceq (Serialize ([Prototype.Combat.CombatTacticsSamples]::CompareMpReserve($reserveConfig)))) 'MP experiment is not replayable.'
Require ((Serialize $focus) -ceq (Serialize ([Prototype.Combat.CombatTacticsSamples]::CompareFocusMark($focusConfig)))) 'Focus experiment is not replayable.'

$guiConfig = Read-Scenario 'round3_tactics.json'
# The integration importer rejects a shared action ID with a different definition.
$actionDefinitions = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
foreach ($sample in @($reserveConfig, $focusConfig, $guiConfig)) {
    foreach ($action in $sample.actions) {
        $definition = Serialize $action
        if ($actionDefinitions.ContainsKey($action.id)) { Require ($actionDefinitions[$action.id] -ceq $definition) ('Conflicting third-round action definition: ' + $action.id) }
        else { $actionDefinitions.Add($action.id, $definition) }
    }
}
$guiBaselineSession = [Prototype.Combat.CombatSession]::new($guiConfig)
$guiComparison = [Prototype.Combat.CombatTacticsSamples]::CompareEmergencyThreshold($guiConfig)
$guiBaseline = $guiComparison.baseline
$guiEdited = $guiComparison.variant
[void]$guiBaselineSession.RunToCompletion()
$baselineAlly = @($guiBaseline.snapshot.units | Where-Object id -eq 'ally')[0]
$editedAlly = @($guiEdited.snapshot.units | Where-Object id -eq 'ally')[0]
Require ($baselineAlly.hp -ne $editedAlly.hp -or $guiBaseline.time -ne $guiEdited.time -or $guiBaseline.outcome -ne $guiEdited.outcome) 'GUI default must expose an observable battle result from changing the first-aid threshold.'
Require $guiComparison.onlyDeclaredSettingChanged 'GUI default changed more than its HP threshold.'
Write-Json 'ROUND3_GUI_DEFAULT_RUN.json' $guiComparison
$guiStep = [Prototype.Combat.CombatSession]::new($guiConfig)
$eventCount = 0
while (-not $guiStep.GetSnapshot().finished -and $eventCount -lt 2000) { [void]$guiStep.Step(); $eventCount++ }
Require ($guiStep.GetSnapshot().finished) 'GUI default did not terminate in single-step mode.'
Require ((Serialize $guiStep.GetSnapshot()) -ceq (Serialize $guiBaseline.snapshot)) 'GUI single-step and automatic snapshots differ.'
Require ($guiStep.ExportLogText() -ceq $guiBaselineSession.ExportLogText()) 'GUI single-step and automatic logs differ.'

# Legacy regression calls the unchanged first/second-round data without rewriting historical reports.
$legacy = Read-Scenario 'default.json'
$legacySession = [Prototype.Combat.CombatSession]::new($legacy)
$legacyStep = $legacySession.Step()
Require ($legacyStep.actionId -eq 'first_aid' -and $legacyStep.targetId -eq 'ally' -and $legacyStep.mpAfter -eq 24) 'First-round first-aid regression changed.'
$legacyOrder = [Prototype.Combat.CombatSession]::new($legacy)
[void]$legacyOrder.MoveInstruction('mage',1,0)
Require ($legacyOrder.Step().actionId -eq 'basic_attack') 'First-round priority regression changed.'
$oldExpConfig = Read-Expedition 'round2_expedition.json'
$oldViewed = [Prototype.Combat.CombatExpeditionSession]::new($oldExpConfig)
[void]$oldViewed.RunNextEncounter()
[void]$oldViewed.RunNextEncounter()
$oldBatch = [Prototype.Combat.CombatExpeditionSession]::new($oldExpConfig).RunToEnd()
Require ((Serialize $oldViewed.GetReport()) -ceq (Serialize $oldBatch)) 'Second-round expedition step/batch regression changed.'
Require ($oldBatch.encounters.Count -eq 2 -and $oldBatch.finalSnapshot.time -eq 310 -and $oldBatch.publicCash -eq 25) 'Second-round actual carry/reward result changed.'
$oldGear = [Prototype.Combat.CombatSampleRuns]::CompareEquipment((Read-Scenario 'round2_equipment.json'))
Require ($oldGear.baseline.time -eq 120 -and $oldGear.variant.time -eq 60) 'Second-round equipment regression changed.'
Write-Json 'ROUND3_REGRESSION_SUMMARY.json' ([PSCustomObject]@{ firstRoundFirstStep = $legacyStep; secondRoundExpedition = $oldBatch; secondRoundEquipment = $oldGear })

$summary = [PSCustomObject]@{
    result = 'passed'
    engine = 'Actual CombatSession runtime compiled with PowerShell Add-Type'
    onlyDeclaredSettingsChanged = $true
    comparisonCount = 2
    sameInputReplayMatches = $true
    mpFirstDivergenceWait = $reserve.firstDivergence.baseline.time
    focusFirstDivergenceWait = $focus.firstDivergence.baseline.time
    guiSingleThresholdChangesBattle = $true
    guiFirstDivergenceWait = $guiComparison.firstDivergence.baseline.time
    actionDefinitionsCompatibleAcrossThirdRound = $true
    guiSingleStepEqualsAutomatic = $true
    firstRoundRegressionsPassed = $true
    secondRoundCarryAndEquipmentRegressionsPassed = $true
    legacyDefaultSha256 = (Get-FileHash -LiteralPath (Join-Path $configurationRoot 'default.json') -Algorithm SHA256).Hash
}
Write-Json 'ROUND3_SAMPLE_VERIFICATION.json' $summary
$summary | ConvertTo-Json -Depth 10
