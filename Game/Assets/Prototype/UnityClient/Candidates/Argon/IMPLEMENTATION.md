# Argon candidate A

Design source: `outputs/ui_candidates_20261115/designs/argon/DESIGN.md`, candidate A / 石枢星盘. This directory contains presentation only; it does not construct a workbench, simulate combat, settle rewards or write exports independently.

`ArgonCandidateView` registers through `TacticsCandidate("argon", "A · 石枢星盘")` and binds to `TacticsSharedController`. The host supplies the full canvas below its single 40px global navigation bar. Argon adds a 56px workbench band, two full-area pages and a 28px footer; it does not duplicate global navigation.

- Preparation: proportional 256/368/608 columns for crew/capacity, ordered rule cards and the complete four-part form. Three source-labelled condition rows, explicit mixed parentheses, native scope chips, selectors, whitelist search, effect filters and a 2/3-column skill grid remain independent controls. The original raw threshold strings stay in the shared form; ratio metadata adds a percentage explanation without changing stored units.
- Playback: 76px WAIT/control band, 372px opposing formations and a three-column event/cause/unit inspection stage. Friendly row order is 2,1,0; hostile row order is 0,1,2; logical column is the vertical coordinate. Every click and highlight uses the actual unit ID. Selected historical effects never replace the current board snapshot.
- The authored 860px workbench surface fits the host's logical height, compensating for the existing CanvasScaler at 16:9. At 1920×1080 this yields three skill columns and inline secondary tools. No project setting is modified here. Native resolution and glyph visibility still require actual player validation.
- All visible command controls call shared commands directly. Successful commit changes to playback; the shared owner alone starts its existing clock. Returning to preparation pauses. Candidate switching is owned by the shared host.
- Comparison, export, expedition import and reset use existing shared operations. The view never generates a battle for export. A comparison modal blocks background clicks and displays the shared result.

## Real assets and placeholders

Font is the existing `Game/Assets/Prototype/Art/Fonts/PrototypeChinese.asset` / Noto source (provided by the host). Portraits use `shared.GetPortrait(unitId)`, backed by `Resources/PrototypeUI/Portraits/unit_<unitId>_256` and the approved five-image common manifest. Import/copy ownership belongs to the shared-layer owner.

Optional watermark, nine-slice frames, status icons and scope artwork are not generated. Native Images, brass corner marks and actual Chinese status names use the design's permitted substitutes. No new asset/model request is issued.

Current-rule availability is not predicted in the inspector: it labels its rule list accordingly and directs the user to actual event attempts. All event attempts, candidates, coverage, MP/status changes and tie reasons are formatted from actual returned DTOs.

## Stable real controls

Root type: `Prototype.UnityClient.Candidates.Argon.ArgonCandidateView`.

| Route | Public handle | Object name |
| --- | --- | --- |
| Preparation / playback tabs | `EditorTab` / `BattleTab` | `Argon_EditorTab` / `Argon_BattleTab` |
| First rule | `EditorPage.RuleButtons[0]` | `Argon_Rule_0` |
| First raw threshold | `EditorPage.Terms[0].ThresholdInput` | `Argon_TermThreshold_0` |
| Save to selected rule | `EditorPage.ReplaceButton` | `Argon_Replace` |
| Apply battle | `ApplyButton` | `Argon_Apply` |
| Pause / step / replay | `BattlePage.PauseButton` / `StepButton` / `ReplayButton` | `Argon_Pause` / `Argon_Step` / `Argon_BattleReplay` |
| Search / clear | `EditorPage.ActionSearchInput` / `ClearSearchButton` | `Argon_ActionSearch` / `Argon_ClearSearch` |
| Actual skill | `EditorPage.ActionButtons` | `Argon_Action_<actualActionId>` |
| History / detailed attempts | `BattlePage.EventButtons` / `DetailsButton` | `Argon_Event_<index>` / `Argon_ToggleRuleDetails` |
| Secondary tools | `ToolsButton`, then `CompareButton` / `ExportButton` / `SendExpeditionButton` / `ResetButton` | `Argon_Tools` and named actual buttons; wide-screen public handles return inline equivalents |

Validation at source freeze: all 45 current runtime/UI sources compiled together with 309 local Unity/.NET references using Roslyn. Unity import, PlayMode execution and native player screenshots have not yet been run for this candidate; the shared owner performs unified validation next.

Actual run08 update: strict A coverage glyph checks passed. The first real Player attempt exposed deferred-layout and short-line issues that static checks did not show; the failed raw evidence is retained under `outputs/ui_candidates_20261115/runtime_qa_capture_run08/f7fb35d5cc5848018176b7d73bdc8332/`. Argon now defers dimension-triggered redraws to LateUpdate outside Canvas rebuilds, preserves full line height in small labels, uses 22px rule segments and 19px condition hints. Font sizes and strict assertions remain unchanged. A new build and real rendered review are still required; the failed images are not visual acceptance.

## One concentrated original-designer R2 correction

The actual original Argon R2 review (`designs/argon/REVIEW.md`, request `20261115T141057857-4919e1`) was read completely. The reviewed801c build and six images remain frozen; no second model request is made. The per-item source map is `work/argon_review_fix/REVIEW_FIX_PLAN.md`.

- M1: conditions reserve138/188/238 logical pixels for1/2/3 actual rows instead of240 for every rule; explicit grouping and46px condition rows stay intact. The selected skill uses a three-line84px Chinese summary from real action/catalog fields, releasing space for two complete skill rows in the default one-term form.
- M2: primary coverage uses actual unit display names; skills use real catalog labels. Unknown tags/equipment names are not invented. Exact unit/tag/equipment/status IDs are available through muted technical fields, while standard exports retain their schema.
- M3: a safe layout-frame projection scrolls the event rail to its actual selection, including the newest event; it never selects or steps a battle during rendering. The default causal view shows the actual skipped rule/reason earlier and avoids duplicate MP lines; all full attempts/candidates remain expandable.
- S1/S2/S3: full-slot Insert now has a dark explicit“满槽”label; empty cells hide their tracks; bar values remain current, with real selected-event effect deltas explicitly labelled separately in the board headings; the divider says“前线交锋”. Only the closed scenario caption is compact: full titles remain in the dropdown options/tools. Shared global hint copy is maintained by the common owner.

Existing public controls/object names remain. Real presentation handles `BattlePage.EventScroll` and `InspectorTechnicalButton` were added. No core, JSON, raw form/draft/committed state, capacity, reward, playback clock, QA flag or export behavior changed. ArgonUi/ArgonTheme and all .meta files are untouched.

Work-mirror Roslyn/Add-Type compilation passed for55 actual current runtime/UI sources with309 local PowerShell/Unity/TMP/UGUI/InputSystem references (`work/argon_review_fix/static_a_review_315b7b4af10048859179bbf6131730fe.json`). This is source compilation only. Final unified Unity regressions and a new-build internal visual check remain required, including mixed3-term rows and selected-event/history presentation; they are not claimed passed here.