# Unity prototype client

This client is a uGUI + TMP view over the Combat, BaseEconomy, Expedition and
Tactics modules. It contains no combat selection, damage, economy, movement,
production or transfer formulas. Editable inputs live under
`Assets/StreamingAssets/Prototype/`; the regression and workbench sessions
remain independent.

Run these explicit Editor entry points after importing the project:

1. `Prototype.Editor.PrototypeProjectBuilder.GenerateHub`
2. `Prototype.Editor.PrototypeProjectBuilder.ExportExamples` (optional)
3. `Prototype.Editor.PrototypeProjectBuilder.BuildWindowsDevelopment`

`GenerateHub` configures the template's Universal RP asset, new Input System,
1280 × 900 window defaults, Force Text serialization and Mono scripting. It
creates a Dynamic Noto TMP font asset, prewarms text from this prototype's C#
and JSON files, then saves `Assets/Prototype/Scenes/PrototypeHub.unity` and
sets that scene as the build entry. This method does not edit test JSON.
Scene generation is explicit; no import callback rebuilds or overwrites scenes.
The saved scene creates its UI on Start, with no manual component assignment.

The Windows development build defaults to `Builds/Windows/Prototype.exe`.
Set `PROTOTYPE_BUILD_PATH` to a full `.exe` path to change it.
The build copies the Noto OFL and source notice beside the executable under
`ThirdPartyNotices/NotoSansCJK/`.
Examples default to `Builds/Examples/`; set `PROTOTYPE_REPORT_PATH` to change it.
Both example exports use the domain sessions used by the client.

PlayMode tests live in `Prototype.UnityClient.PlayModeTests`. They construct
the real client via `PrototypeHubFactory.Create`, then invoke the actual
Button listener chains for priority, input settings, recruitment, meal,
kitchen selection and reset. Generate the Hub/font before running them.
They also check Chinese font coverage, independent module clocks and control
rectangles. Test results must be reported from the actual Unity run.

The public `PrototypeHubController` exposes sessions, `LastCombatStep`, input
fields, logs and action buttons for inspection. Stable object names include
`Combat_Step`, `Combat_ReplayFirst`, `Combat_Reset`, `Combat_Defaults`,
`Combat_HealUp`, `Combat_HealDown`, `Combat_ApplySettings`, `Base_Recruit`,
`Base_Step`, `Base_RunToMeal`, `Base_Reset`, `Base_Defaults`, `Base_NearKitchen`,
`Base_FarKitchen`, and `Base_Map_<x>_<y>`.

Both reset buttons retain current player choices. Restore Defaults returns to
the JSON loaded when the client started. Selecting another kitchen location
starts a fresh opening with that preplaced facility. This is a distance
comparison, not free construction. The base screen exposes no pretend tax,
welfare, daily settlement or construction controls.

Noto source and OFL are under `Art/Fonts`; `SOURCE.txt` records URLs. Required
TMP shaders, line-breaking text and settings were extracted unchanged from
the installed Unity 6000.3 editor's `com.unity.ugui` 2.0.0 bundled Essential
Resources, into `Art/Fonts/TMPResources`. The explicit builder updates the TMP
settings to use Noto, retain prewarmed glyphs and disable unused emoji assets.
The client uses `InputSystemUIInputModule.AssignDefaultActions()`.

## Third-round tactics workbench

`TacticsPanelController.Initialize(TMP_FontAsset)` is mounted by the Hub. The
preferred input is `Prototype/Combat/round4_mage_library.json` when present,
with `round3_tactics.json` as its fallback. The case dropdown discovers both
`round3_*.json` and `round4_*.json` beside them and uses configured titles.
Each case must explicitly identify `playerTeam`, so enemy array order cannot
change which team is edited or displayed. Switching cases loads a new
workbench; resetting restores that case's loaded input.

The left pane pins character selection, actual intelligence/capacities, the
selected rule and CRUD/order buttons. Its form scrolls independently. New
Blank obtains `CreateBlankInstruction()` from the workbench: condition,
scope, selector and action all remain unselected until the player chooses.
Actions come only from `GetCatalog(unitId).actions`, backed by the selected
role's real `GetAvailableActions`; the global enemy action pool is never used.
Threshold defaults/ranges, source subjects, conditions, groupings and selectors
come from the real catalog. Ratios use 0–1 input, explicitly shown as
`0.35 = 35%`. Actor conditions visibly say “施放者”. Mixed conditions show both
connectors and an explicit `(A op B) op C` or `A op (B op C)` grouping.

Insert/Replace/Remove/Move call the workbench's actual editing APIs. Invalid or
over-capacity rules retain the entered form and display the returned code and
reason. Existing overloaded slots remain visible; their read-only validation
uses the workbench's real kernel preview. Typing in the form does not alter the
accepted draft. Apply, Compare and Send require the form to be submitted with
Insert or Replace first. Playback refreshes never reload an unfinished form.

“应用并开战” commits the draft to the same loaded HP/MP/seed opening and starts
the bound `TacticsBattleView` automatic player. Pause/Step and selected history
events use the same workbench session. “同开局重放” replays the committed input,
including when newer draft edits exist. “单项对照” uses `CompareWithLoaded`;
only an actual accepted single edit is eligible, and the comparison never
advances the current battle. Its pane includes real runs and full reason logs.

“发送战术到出征” invokes the Hub's `ExpeditionReceiver` once with a draft copy.
The receiver calls `ExpeditionSliceSession.ApplyTactics`; it preserves the
existing camp wallet/material ledger and reports real capacity/action
conflicts. It does not replace the expedition session. Started expeditions
reject import. Successful receipt is the only path that changes the Hub page.

“导出输入与战报” pauses playback and writes only on that click. Use
`--prototype-report-dir <absolute root>` or `ReportDirectoryOverride` in UI
tests; otherwise the root is `Application.persistentDataPath/Reports`. Each
export creates a unique `tactics_<UTC>_<id>` subdirectory containing:

- `session_result.json`: the actual `TacticsWorkbenchAcceptance` DTO.
- `loaded_scenario.json`, `tactics_input.json` (accepted draft), and
  `committed_scenario.json` (`null` when no battle has been committed).
- `instruction_form.json`: raw current form input, including unsubmitted
  threshold text and explicit source/grouping selections.
- `catalog_snapshot.json`: the selected character's actual catalog.
- `tactics_report.txt` and `battle_report.txt`: the actual workbench report.
- `export_manifest.json`: revisions, recorded event/comparison counts and path.

The bottom status area scrolls to preserve long reasons and export paths.
Stable controls include `Tactics_NewBlank`, `Tactics_Insert`,
`Tactics_Replace`, `Tactics_Delete`, `Tactics_MoveUp`, `Tactics_MoveDown`,
`Tactics_ApplyBattle`, `Tactics_Replay`, `Tactics_Compare`,
`Tactics_SendExpedition`, `Tactics_Export` and `Tactics_Reset`. Public
`InstructionEditor` exposes the selected unit/rule, four-part dropdowns,
condition term controls, grouping preview and scroll viewport for actual
button/listener tests. Public `BattleView` supplies playback/event controls.
Runtime implementation and test authors are separate; report Unity results
only after the root's real run.

## Fourth-round mage library

The existing four-part editor remains in place. The action area adds
`ActionSearchInput` (`Tactics_ActionSearch`), `ActionMatchText` and
`ActionDetailsText`. It searches only the selected character's real learned
action catalog, using actual names, IDs, effect descriptions, coverage and
status descriptions. An empty search keeps canonical whitelist order and the
original ActionDropdown index mapping. Searching is navigation: it does not
mark the form dirty or modify the draft. If the query excludes the selected
action, that real action remains visibly selected as the first item. Choosing
another action is the operation that changes the form.

The supplied mage-library opening uses its configured instruction slots.
When those slots are full, test a new rule by replacing a selected rule or
deleting one before insertion; the UI does not enlarge intelligence capacity.
Round-prefixed expedition envelopes with no root-level battle units are
excluded from the standalone case dropdown.

The details retain 16-point text and scroll with the form. Actual MP cost,
base WAIT, numeric effect or state parameters and selected main-target scope
appear before longer explanations. Coverage reads `targetMode` and its real
catalog description. A group action first selects a main target, then expands
within that target's team and the rule's original scope; covered members do
not repeat the anchor condition. It pays MP and WAIT once. `isMagic` explains
the silence check separately from a damage action's `damageType`. The GUI
does not calculate restoration, drain, cleansing, damage or covered targets.
The battle view displays actual core records for those effects.

Condition categories, defaults and units use the real `CombatTacticsOption`
metadata. Only `thresholdUnit=ratio` shows `0.35 = 35%`; points, counts, WAIT
and row numbers retain their distinct units. `integerThreshold` only controls
integer input. Value-based conditions initialize from `defaultValue` when the
player chooses a condition, while loading an existing rule preserves its
actual value. Conditions needing both a threshold and a value show separate
labels and fields, such as a status ID together with remaining WAIT.

Exports still serialize complete workbench, action, option and event DTOs,
including MP changes, target lists and state details. No action/effect field
whitelist is projected by the UI exporter. `instruction_form.json` also
records the current `actionSearch` text, and `catalog_snapshot.json` retains
the actual capability catalogs supplied by the workbench. Setting a report
destination or browsing the library performs no file writes or simulation;
the existing export button is the only export trigger.
