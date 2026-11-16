param([string]$OutputDirectory = $PSScriptRoot)
# Run with PowerShell 7 (pwsh); this tool uses System.Text.Json field DTO support.
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
function Read-Scenario([string]$Name) { return [System.Text.Json.JsonSerializer]::Deserialize([IO.File]::ReadAllText((Join-Path $configurationRoot $Name)), [Prototype.Combat.CombatScenario], $jsonOptions) }
function Read-Expedition([string]$Name) { return [System.Text.Json.JsonSerializer]::Deserialize([IO.File]::ReadAllText((Join-Path $configurationRoot $Name)), [Prototype.Combat.CombatExpeditionConfig], $jsonOptions) }
function Serialize($Value) {
    if ($Value.GetType().IsArray -or $Value.GetType().FullName -eq 'System.Management.Automation.PSCustomObject') { return ConvertTo-Json -InputObject $Value -Depth 100 }
    return [System.Text.Json.JsonSerializer]::Serialize($Value, $Value.GetType(), $jsonOptions)
}
function Write-Json([string]$Name, $Value) { [IO.File]::WriteAllText((Join-Path $OutputDirectory $Name), (Serialize $Value) + "`n", $utf8) }
function Write-Text([string]$Name, [string]$Value) { [IO.File]::WriteAllText((Join-Path $OutputDirectory $Name), $Value.Replace("`r`n", "`n"), $utf8) }
function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Unit($Snapshot, [string]$Id) { return @($Snapshot.units | Where-Object id -eq $Id)[0] }
function Alive-Friendly($Run) { return @($Run.snapshot.units | Where-Object { $_.team -eq 'friendly' -and $_.hp -gt 0 }).Count }

$library = Read-Scenario 'round4_mage_library.json'
$meditationConfig = Read-Scenario 'round4_meditation.json'
$meditationRiskConfig = Read-Scenario 'round4_meditation_risk.json'
$areaConfig = Read-Scenario 'round4_area.json'
$cleanseConfig = Read-Scenario 'round4_cleanse.json'
$controlConfig = Read-Scenario 'round4_control.json'
$expeditionConfig = Read-Expedition 'round4_expedition.json'
$meditation = [Prototype.Combat.CombatMageSamples]::CompareMeditation($meditationConfig)
$meditationRisk = [Prototype.Combat.CombatMageSamples]::CompareMeditation($meditationRiskConfig, 0.35)
$area = [Prototype.Combat.CombatMageSamples]::CompareArea($areaConfig)
$cleanse = [Prototype.Combat.CombatMageSamples]::CompareCleanse($cleanseConfig)
$control = [Prototype.Combat.CombatMageSamples]::CompareControl($controlConfig)
$cases = @(@{Name='MEDITATION'; Report=$meditation}, @{Name='MEDITATION_RISK'; Report=$meditationRisk}, @{Name='AREA'; Report=$area}, @{Name='CLEANSE'; Report=$cleanse}, @{Name='CONTROL'; Report=$control})
foreach ($case in $cases) {
    Require $case.Report.onlyDeclaredSettingChanged ('Undeclared configuration change: ' + $case.Name)
    Require ($null -ne $case.Report.firstDivergence) ('No real action divergence: ' + $case.Name)
    Write-Json ('ROUND4_' + $case.Name + '_REPORT.json') $case.Report
    Write-Text ('ROUND4_' + $case.Name + '_REPORT.md') ([Prototype.Combat.CombatMageSamples]::FormatComparisonMarkdown($case.Report))
}
Require ($meditation.firstDivergence.variant.actionId -eq 'mage_meditate') 'Low-MP branch did not meditate.'
Require (@($meditation.variant.steps | Where-Object actionId -eq 'mage_firebolt').Count -gt 0) 'No spell cast after MP recovery.'
Require ($meditation.firstDivergence.variant.mpAfter -gt $meditation.firstDivergence.variant.mpBefore -and $meditation.firstDivergence.variant.actualWait -gt 0) 'Meditation must restore real MP and spend real WAIT.'
Require ((Alive-Friendly $meditationRisk.baseline) -gt (Alive-Friendly $meditationRisk.variant)) 'Premature meditation must expose its WAIT cost in the risk opening.'
Require ($area.firstDivergence.baseline.targetIds.Count -eq 1 -and $area.firstDivergence.variant.targetIds.Count -eq 3) 'Area skill did not actually cover three targets.'
Require ($area.variant.time -lt $area.baseline.time -and (Unit $area.variant.snapshot 'mage').mp -lt (Unit $area.baseline.snapshot 'mage').mp) 'Area example must trade more MP for earlier completion.'
Require ((Unit $cleanse.variant.snapshot 'ally').hp -gt (Unit $cleanse.baseline.snapshot 'ally').hp) 'Timely cleansing did not help its controlled teammate.'
Require ((Alive-Friendly $control.variant) -gt (Alive-Friendly $control.baseline)) 'Control did not keep the threatened teammate alive.'
Require ($control.variant.time -gt $control.baseline.time -and (Unit $control.variant.snapshot 'mage').mp -lt (Unit $control.baseline.snapshot 'mage').mp) 'Control example must retain its time/MP cost.'
Require (@($control.variant.steps | Where-Object controlReason -eq 'Stunned').Count -gt 0) 'No real stunned action was blocked.'
Require ((Serialize $meditation) -ceq (Serialize ([Prototype.Combat.CombatMageSamples]::CompareMeditation($meditationConfig)))) 'Meditation comparison is not replayable.'
Require ((Serialize $area) -ceq (Serialize ([Prototype.Combat.CombatMageSamples]::CompareArea($areaConfig)))) 'Area comparison is not replayable.'

$proofs = [Prototype.Combat.CombatMageSamples]::RunSkillProofs($library)
Require ($proofs.Count -ge 24) 'Mage needs a substantially expanded real learned-action library.'
Require (@($proofs | Where-Object { -not $_.actualChangeObserved -or $_.firstStep.status -ne 'Executed' -or $_.firstStep.actionId -ne $_.actionId }).Count -eq 0) 'A learned action lacks real execution evidence.'
Require (@($proofs | Where-Object { $_.actionId -eq 'mage_freeze' -and $_.controlObserved }).Count -eq 1) 'Freeze probe did not block an actual action.'
Require (@($proofs | Where-Object { $_.actionId -eq 'mage_silence' -and $_.controlObserved }).Count -eq 1) 'Silence probe did not block an actual magic action.'
foreach ($coverage in @(@{Id='mage_chain_lightning'; Count=2}, @{Id='mage_piercing_ray'; Count=2}, @{Id='mage_fire_rain'; Count=3}, @{Id='mage_group_heal'; Count=2}, @{Id='mage_group_cleanse'; Count=2}, @{Id='mage_mass_guard'; Count=2}, @{Id='mage_mass_weaken'; Count=3})) {
    $proof = @($proofs | Where-Object actionId -eq $coverage.Id)[0]
    Require ($proof.firstStep.targetIds.Count -eq $coverage.Count) ('Unexpected real target coverage: ' + $coverage.Id)
}
Write-Json 'ROUND4_SKILL_EXECUTION_PROOFS.json' $proofs
Write-Text 'ROUND4_SKILL_EXECUTION_PROOFS.md' ([Prototype.Combat.CombatMageSamples]::FormatSkillProofsMarkdown($proofs))

$actionDefinitions = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
foreach ($sample in @($library,$meditationConfig,$meditationRiskConfig,$areaConfig,$cleanseConfig,$controlConfig,$expeditionConfig.encounters[0].scenario,$expeditionConfig.encounters[1].scenario)) {
    foreach ($action in $sample.actions) {
        $definition = Serialize $action
        if ($actionDefinitions.ContainsKey($action.id)) { Require ($actionDefinitions[$action.id] -ceq $definition) ('Conflicting fourth-round action definition: ' + $action.id) }
        else { $actionDefinitions.Add($action.id, $definition) }
    }
}
$librarySession = [Prototype.Combat.CombatSession]::new($library)
$libraryOpening = $librarySession.GetSnapshot()
$first = $librarySession.Step()
Require ($first.actionId -eq 'mage_meditate' -and $first.mpBefore -eq 4 -and $first.mpAfter -eq 16 -and $first.actualWait -eq 60) 'GUI library must immediately demonstrate actual MP recovery and WAIT.'
Require ((Unit $libraryOpening 'mage').instructionCapacity -eq 5 -and (Unit $libraryOpening 'ally').instructionCapacity -eq 2) 'Expanded learned skills must not silently expand instruction capacity.'
$libraryRun = $librarySession.RunToCompletion()
Require ($libraryRun.outcome -eq 'Victory' -or $libraryRun.outcome -eq 'Defeat') 'Default Mage library failed to complete its real battle.'
$libraryStep = [Prototype.Combat.CombatSession]::new($library)
$stepCount = 0
while (-not $libraryStep.GetSnapshot().finished -and $stepCount -lt 3000) { [void]$libraryStep.Step(); $stepCount++ }
Require ((Serialize $libraryStep.GetSnapshot()) -ceq (Serialize $libraryRun.snapshot) -and $libraryStep.ExportLogText() -ceq $librarySession.ExportLogText()) 'Default library single-step and automatic play differ.'

$full = [Prototype.Combat.CombatSession]::CopyScenario($library)
(Unit $full 'mage').mp = (Unit $full 'mage').maxMp
(Unit $full 'mage').instructions[0].condition.terms[0].kind = 'always'
$fullSession = [Prototype.Combat.CombatSession]::new($full)
$fullStep = $fullSession.Step()
Require ($fullStep.actionId -ne 'mage_meditate') 'Full MP meditation must fall back without wasting a restore.'
$silenced = [Prototype.Combat.CombatSession]::CopyScenario($library)
$silencedMage = Unit $silenced 'mage'
$silencedMage.mp = 0
$silencedMage.statuses = @([Prototype.Combat.CombatStatusConfig]@{id='round4_test_silence';kind='silenced';magnitude=0;appliedAt=0;expiresAt=80})
$silencedSession = [Prototype.Combat.CombatSession]::new($silenced)
$silencedMeditation = $silencedSession.Step()
Require ($silencedMeditation.actionId -eq 'mage_meditate' -and $silencedMeditation.mpAfter -eq 12) 'Zero-MP silenced Mage must still be able to meditate.'
$silencedMage.mp = $silencedMage.maxMp
$silencedFullSession = [Prototype.Combat.CombatSession]::new($silenced)
$silencedStaff = $silencedFullSession.Step()
Require ($silencedStaff.actionId -eq 'mage_staff') 'Silenced Mage needs a real non-magic staff fallback.'
Write-Json 'ROUND4_LIBRARY_RUN.json' ([PSCustomObject]@{ opening=$libraryOpening; firstStep=$first; run=$libraryRun; fullMpFallback=$fullStep; silencedZeroMpMeditation=$silencedMeditation; silencedFullMpStaff=$silencedStaff })

$watched = [Prototype.Combat.CombatExpeditionSession]::new($expeditionConfig)
[void]$watched.RunNextEncounter()
[void]$watched.RunNextEncounter()
$batched = [Prototype.Combat.CombatExpeditionSession]::new($expeditionConfig).RunToEnd()
Require ((Serialize $watched.GetReport()) -ceq (Serialize $batched)) 'Fourth-round viewed and batched expeditions differ.'
Require ($batched.encounters.Count -eq 2 -and $batched.stopReason -eq 'CompletedAllEncounters') 'Expected two real completed Mage encounters.'
Require ($batched.encounters[1].battleStart.time -eq $batched.encounters[0].battleEnd.time + 20) 'Mage expedition intermission is not 20 WAIT.'
foreach ($member in @($batched.encounters[1].battleStart.units | Where-Object team -eq 'friendly')) {
    $previous = Unit $batched.encounters[0].battleEnd $member.id
    Require ($previous.hp -eq $member.hp -and $previous.mp -eq $member.mp) ('Resource reset across Mage encounters: ' + $member.id)
}
Write-Json 'ROUND4_EXPEDITION_REPORT.json' $batched
Write-Text 'ROUND4_EXPEDITION_REPORT.md' ([Prototype.Combat.CombatSampleRuns]::FormatExpeditionMarkdown($batched).Replace('第二轮试玩假设','第四轮试玩假设').Replace('连续两场出征实跑','法师新技能与状态的连续两场实跑'))

# Keep old data and reports untouched. Re-run only their actual runtime entry points.
$legacy = Read-Scenario 'default.json'
$firstAid = [Prototype.Combat.CombatSession]::new($legacy).Step()
Require ($firstAid.actionId -eq 'first_aid' -and $firstAid.mpAfter -eq 24) 'First-round regression changed.'
$round2Exp = [Prototype.Combat.CombatExpeditionSession]::new((Read-Expedition 'round2_expedition.json')).RunToEnd()
Require ($round2Exp.encounters.Count -eq 2 -and $round2Exp.finalSnapshot.time -eq 310 -and $round2Exp.publicCash -eq 25) 'Second-round carry/rewards regression changed.'
$gear = [Prototype.Combat.CombatSampleRuns]::CompareEquipment((Read-Scenario 'round2_equipment.json'))
Require ($gear.baseline.time -eq 120 -and $gear.variant.time -eq 60) 'Equipment regression changed.'
$oldReserve = [Prototype.Combat.CombatTacticsSamples]::CompareMpReserve((Read-Scenario 'round3_mp_reserve.json'))
$oldFocus = [Prototype.Combat.CombatTacticsSamples]::CompareFocusMark((Read-Scenario 'round3_focus_mark.json'))
Require ($oldReserve.firstDivergence.baseline.time -eq 40 -and $oldFocus.firstDivergence.baseline.time -eq 15) 'Third-round real decision regression changed.'
Write-Json 'ROUND4_REGRESSION_SUMMARY.json' ([PSCustomObject]@{ firstRoundFirstAid=$firstAid; secondRoundExpedition=$round2Exp; equipment=$gear; thirdRoundReserve=$oldReserve; thirdRoundFocus=$oldFocus })
$summary = [PSCustomObject]@{
    result='passed'; engine='Actual CombatSession compiled with PowerShell 7 Add-Type'; formalComparisonCount=5;
    actualMageSkillCount=$proofs.Count; everyMageSkillExecuted=$true; sameInputSingleSettingComparisons=$true;
    actualRowColumnAllCoverage=$true; controlActuallyObserved=$true; meditationHasRealMpAndWait=$true;
    libraryOpeningMp='4 -> 16'; libraryOpeningWait=60; instructionSlotsMage=5; instructionSlotsAlly=2;
    fullMpFallsBack=$true; zeroMpSilenceMeditates=$true; silencedStaffFallback=$true;
    fourthRoundExpeditionStart=$batched.encounters[1].battleStart.time; fourthRoundExpeditionIntermission=20;
    fourthRoundExpeditionFinish=$batched.finalSnapshot.time; viewedEqualsBatched=$true;
    legacyCarryRewardsEquipmentAndTacticsPassed=$true;
    legacyDefaultSha256=(Get-FileHash -LiteralPath (Join-Path $configurationRoot 'default.json') -Algorithm SHA256).Hash
}
Write-Json 'ROUND4_SAMPLE_VERIFICATION.json' $summary
$summary | ConvertTo-Json -Depth 10
