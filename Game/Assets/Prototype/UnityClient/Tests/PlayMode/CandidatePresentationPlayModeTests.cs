using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Prototype.Combat;
using Prototype.UnityClient.Candidates.Argon;
using Prototype.UnityClient.Candidates.Common;
using Prototype.UnityClient.Candidates.Hydrogen;
using Prototype.UnityClient.Candidates.Opus;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Prototype.UnityClient.Tests
{
    public sealed class CandidatePresentationPlayModeTests
    {
        private PrototypeHubController hub;
        private string reportRoot;
        private static readonly string[] CandidateIds = { "argon", "hydrogen" };

        [UnitySetUp]
        public IEnumerator CreateRealCandidateHub()
        {
            Assert.That(UnityEngine.Object.FindFirstObjectByType<Diagnostics.PrototypeRuntimeQaCapture>(), Is.Null,
                "Normal startup without the explicit QA flag must not create a diagnostics driver.");
            hub = PrototypeHubFactory.Create(TMP_Settings.defaultFontAsset);
            reportRoot = Path.Combine(Application.temporaryCachePath, "UICandidateTests", Guid.NewGuid().ToString("N"));
            Assert.That(hub.TacticsCandidates.IsAvailable("argon"), Is.True);
            Assert.That(hub.TacticsCandidates.IsAvailable("hydrogen"), Is.True);
            Assert.That(hub.TacticsCandidates.CurrentCandidateId, Is.EqualTo("argon"));
            Assert.That(hub.TacticsCandidates.IsAvailable("opus"), Is.False, "C is paused by the user's delivery scope, even though its source is retained.");
            Assert.That(hub.TacticsCandidateDropdown.options.Select(option => option.text),
                Is.EqualTo(new[] { "A · 石枢星盘", "B · 星象秘库", "旧版工作台（回退）" }));
            Assert.That(hub.SharedTactics.GetState().feedback, Does.Contain("C 方案暂停"));
            Assert.That(hub.TacticsView.BattleView.ExternalPlaybackClock, Is.True);
            foreach (var unitId in new[] { "mage", "ally", "enemy_front", "enemy_mage", "enemy_silencer" })
                Assert.That(hub.SharedTactics.GetPortrait(unitId), Is.Not.Null, "Approved portrait must be an imported runtime Sprite: " + unitId);
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator DestroyCandidateHub()
        {
            if (hub != null)
            {
                hub.SharedTactics.Pause();
                UnityEngine.Object.Destroy(hub.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ActualCandidateButtonsPreserveRawFormAndUseOneRealBattleAcrossPresentations()
        {
            foreach (var id in CandidateIds)
            {
                hub.TacticsView.LoadConfiguration(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Prototype", "Combat", "round4_mage_library.json")));
                hub.TacticsView.ReportDirectoryOverride = Path.Combine(reportRoot, id);
                Assert.That(hub.TacticsCandidates.SelectCandidate(id), Is.True);
                var controls = ControlsFor(id);
                yield return RenderFrames();
                if (controls.IsBattlePage()) Click(controls.EditorTab());
                yield return RenderFrames();
                var session = hub.TacticsSession;
                var ledgerCount = hub.ExpeditionSession.GetSnapshot().preparation.ledgerCount;
                Assert.That(session.GetBattleHistory(), Is.Empty);

                // These are the actual visible new presentation buttons, never hidden legacy proxies.
                yield return ApplyAndPause(controls);
                Click(controls.Step());
                yield return RenderFrames();
                Assert.That(session.GetBattleHistory().Length, Is.EqualTo(1), "A single click must execute one real event.");
                Assert.That(session.GetBattleHistory()[0].actionId, Is.EqualTo("mage_meditate"));
                Assert.That(session.GetBattleSnapshot().units.Single(unit => unit.id == "mage").mp, Is.EqualTo(16));
                AssertBoardMpVisible(controls, id == "argon" ? "16 / 30" : "16/30");
                Click(controls.EditorTab());
                yield return RenderFrames();
                Click(controls.RuleZero());
                yield return RenderFrames();
                SetText(controls.Threshold(), id == "argon" ? "0.10" : "10");
                SetText(controls.Search(), "冥想");
                Assert.That(ReadRawThreshold(), Is.EqualTo(.10).Within(1e-12));
                Assert.That(hub.SharedTactics.GetState().form.hasUnsubmittedChanges, Is.True);
                Assert.That(hub.SharedTactics.CommitBattle(), Is.False, "The command layer must reject an unsubmitted form too.");
                Assert.That(session.GetBattleHistory().Length, Is.EqualTo(1));

                var otherId = id == "argon" ? "hydrogen" : "argon";
                Assert.That(hub.TacticsCandidates.SelectCandidate(otherId), Is.True);
                var other = ControlsFor(otherId);
                yield return RenderFrames();
                Assert.That(ReferenceEquals(hub.TacticsSession, session), Is.True);
                Assert.That(ReadRawThreshold(), Is.EqualTo(.10).Within(1e-12));
                Assert.That(hub.SharedTactics.GetState().form.actionSearch, Is.EqualTo("冥想"));
                Assert.That(hub.SharedTactics.GetState().form.selectedInstructionIndex, Is.EqualTo(0));
                Assert.That(double.Parse(other.Threshold().text, CultureInfo.InvariantCulture), Is.EqualTo(otherId == "argon" ? .10 : 10).Within(1e-12));
                SetText(other.Threshold(), "not-a-number");
                Assert.That(hub.SharedTactics.GetState().form.terms[0].thresholdText, Is.EqualTo("not-a-number"));
                Assert.That(hub.TacticsCandidates.SelectCandidate(id), Is.True);
                controls = ControlsFor(id);
                yield return RenderFrames();
                Assert.That(controls.Threshold().text, Is.EqualTo("not-a-number"));
                Assert.That(hub.SharedTactics.GetState().form.hasUnsubmittedChanges, Is.True);
                yield return new WaitForSecondsRealtime(.12f);
                Assert.That(session.GetBattleHistory().Length, Is.EqualTo(1), "Paused and hidden views cannot tick.");
                Assert.That(hub.SharedTactics.GetState().isAutoPlaying, Is.False);

                // WAIT and point fields must bypass the ratio display conversion.
                SetText(controls.Threshold(), id == "argon" ? "0.10" : "10");
                var ratioForm = hub.SharedTactics.GetState().form;
                foreach (var unit in new[] { "wait", "points" })
                {
                    var catalog = hub.SharedTactics.GetState().catalog.conditions;
                    int option = Array.FindIndex(catalog, value => value.thresholdUnit == unit && value.usesThreshold && !value.usesValue);
                    Assert.That(option, Is.GreaterThanOrEqualTo(0), unit);
                    controls.SelectKind(catalog[option].id);
                    yield return RenderFrames();
                    SetText(controls.Threshold(), "17");
                    Assert.That(ReadRawThreshold(), Is.EqualTo(17), unit);
                    Assert.That(hub.TacticsCandidates.SelectCandidate(otherId), Is.True);
                    other = ControlsFor(otherId);
                    yield return RenderFrames();
                    Assert.That(other.Threshold().text, Is.EqualTo("17"), unit + " must not become a percentage.");
                    Assert.That(hub.TacticsCandidates.SelectCandidate(id), Is.True);
                    controls = ControlsFor(id);
                    yield return RenderFrames();
                }
                hub.SharedTactics.SetForm(ratioForm);
                yield return RenderFrames();
                Click(controls.Replace());
                Assert.That(hub.TacticsView.LastEditResult.success, Is.True, hub.TacticsView.LastEditResult.message);
                Assert.That(session.GetDraftScenario().units.Single(unit => unit.id == "mage").instructions[0].condition.terms[0].threshold, Is.EqualTo(.10).Within(1e-12));
                Assert.That(session.GetSnapshot().hasUncommittedChanges, Is.True);
                Assert.That(session.GetBattleHistory().Length, Is.EqualTo(1), "Editing the draft cannot alter committed battle history.");
                yield return ApplyAndPause(controls);
                Assert.That(session.GetBattleHistory(), Is.Empty);
                Click(controls.Step());
                yield return RenderFrames();
                Assert.That(session.GetBattleHistory().Length, Is.EqualTo(1));
                Assert.That(session.GetBattleHistory()[0].actionId, Is.Not.EqualTo("mage_meditate"), "The real core must receive .10, not .001 or 10.");
                Assert.That(session.GetCommittedScenario().units.Single(unit => unit.id == "mage").instructions[0].condition.terms[0].threshold, Is.EqualTo(.10).Within(1e-12));
                Click(controls.Replay());
                yield return RenderFrames();
                Assert.That(session.GetBattleHistory(), Is.Empty);
                Click(controls.Step());
                yield return RenderFrames();
                Assert.That(session.GetBattleHistory().Length, Is.EqualTo(1));
                Assert.That(hub.ExpeditionSession.GetSnapshot().preparation.ledgerCount, Is.EqualTo(ledgerCount));

                Click(controls.EditorTab());
                yield return RenderFrames();
                controls.OpenTools();
                yield return RenderFrames();
                var expected = JsonUtility.ToJson(session.GetAcceptance(), true);
                Click(controls.Export());
                var directory = hub.TacticsView.LastExportDirectory;
                Assert.That(directory, Is.Not.Null.And.Not.Empty, hub.TacticsView.LastExportError);
                Assert.That(File.ReadAllText(Path.Combine(directory, "session_result.json")), Is.EqualTo(expected));
                var exportedForm = JsonUtility.FromJson<TacticsFormInput>(File.ReadAllText(Path.Combine(directory, "instruction_form.json")));
                Assert.That(double.Parse(exportedForm.terms[0].thresholdText, CultureInfo.InvariantCulture), Is.EqualTo(.10).Within(1e-12));
                yield return RenderFrames();
                controls.CloseExport();
                if (id == "hydrogen") yield return HydrogenImportKeepsPurchasedCamp();
            }
        }

        [UnityTest]
        public IEnumerator OnePausedAreaEventKeepsAllThreeTargetsAndCoreCoordinatesAcrossAllCandidateBoards()
        {
            hub.TacticsView.LoadConfiguration(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Prototype", "Combat", "round4_area.json")));
            hub.TacticsCandidates.SelectCandidate("argon");
            var argon = (ArgonCandidateView)hub.TacticsCandidates.ActiveView;
            if (argon.IsBattlePage) Click(argon.EditorTab);
            Click(argon.EditorPage.RuleButtons[0]);
            SetText(argon.EditorPage.ActionSearchInput, "火雨");
            var fireRain = argon.EditorPage.ActionButtons.Single(button => button.name.Contains("mage_fire_rain"));
            Click(fireRain);
            Click(argon.EditorPage.ReplaceButton);
            Click(argon.ApplyButton);
            Click(argon.BattlePage.PauseButton);
            Click(argon.BattlePage.StepButton);
            var session = hub.TacticsSession;
            var step = session.GetBattleHistory().Single();
            Assert.That(step.actionId, Is.EqualTo("mage_fire_rain"));
            Assert.That(step.targetIds, Is.EqualTo(new[] { "enemy_a", "enemy_b", "enemy_c" }));
            Assert.That(step.mpCost, Is.EqualTo(14));
            Assert.That(step.actualWait, Is.EqualTo(65));
            var unchanged = JsonUtility.ToJson(session.GetAcceptance(), true);
            foreach (var id in CandidateIds)
            {
                Assert.That(hub.TacticsCandidates.SelectCandidate(id), Is.True);
                var controls = ControlsFor(id);
                yield return RenderFrames();
                if (!controls.IsBattlePage()) Click(controls.BattleTab());
                yield return RenderFrames();
                Assert.That(hub.SharedTactics.GetState().selectedEventIndex, Is.EqualTo(0));
                Assert.That(hub.SharedTactics.GetState().highlightedTargetIds, Is.EqualTo(step.targetIds));
                foreach (var targetId in step.targetIds)
                {
                    var unit = session.GetBattleSnapshot().units.Single(value => value.id == targetId);
                    Assert.That(unit.hp, Is.EqualTo(4));
                    var card = controls.BoardCells().Single(button => button.GetComponentsInChildren<TMP_Text>().Any(label => label.text.Contains(unit.name)));
                    Assert.That(hub.TacticsCandidates.CurrentCandidateId, Is.EqualTo(id));
                    Assert.That(card.gameObject.activeInHierarchy && card.transform.IsChildOf(hub.TacticsCandidates.ActiveView.transform), Is.True,
                        id + "/" + targetId + " must resolve to the active candidate's real card.");
                    var hp = card.GetComponentsInChildren<TMP_Text>().Single(label => label.text.StartsWith("HP ", StringComparison.Ordinal));
                    AssertTokenVisible(hp, id == "argon" ? "4 / " + unit.maxHp : "4/" + unit.maxHp);
                    var coverage = card.GetComponentsInChildren<TMP_Text>().Single(label => label.text.Contains("覆盖"));
                    Assert.That(coverage.isActiveAndEnabled, Is.True, id + "/" + targetId);
                    AssertTokenVisible(coverage, "覆盖");
                    Assert.That(card.GetComponentsInChildren<TMP_Text>().Any(label => label.text.Contains("(" + unit.row + "," + unit.column + ")")), Is.True);
                }
                Assert.That(JsonUtility.ToJson(session.GetAcceptance(), true), Is.EqualTo(unchanged), "Viewing another projection must not mutate core coordinates, history or time.");
            }
        }

        [UnityTest]
        public IEnumerator CanvasRelayoutDoesNotEmitRebuildErrorsOrChangeSharedInputAndBattle()
        {
            var scaler = hub.GetComponentInChildren<CanvasScaler>();
            Assert.That(scaler, Is.Not.Null);
            foreach (var id in CandidateIds)
            {
                hub.TacticsCandidates.SelectCandidate(id);
                var core = JsonUtility.ToJson(hub.TacticsSession.GetAcceptance(), true);
                var form = JsonUtility.ToJson(hub.SharedTactics.GetState().form, true);
                yield return RenderFrames();
                // On the first activation, the real skill search must already be reachable.
                // No rule click or shared change may be needed to repair a cached 100px body.
                EnsureVisible(ControlsFor(id).Search());
                foreach (var reference in new[] { new Vector2(1920, 1080), new Vector2(1280, 900) })
                {
                    // This exercises Unity layout callbacks, not a claim of native monitor size.
                    scaler.referenceResolution = reference;
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    yield return null;
                    Assert.That(JsonUtility.ToJson(hub.TacticsSession.GetAcceptance(), true), Is.EqualTo(core));
                    Assert.That(JsonUtility.ToJson(hub.SharedTactics.GetState().form, true), Is.EqualTo(form));
                    Assert.That(hub.SharedTactics.GetState().isAutoPlaying, Is.False);
                    // Unity Test Framework also fails any unexpected Canvas rebuild Error log.
                }
            }
        }

        [UnityTest]
        public IEnumerator PausedAndMissingCandidateCommandsKeepSelectionFormBattleAndMoney()
        {
            var state = JsonUtility.ToJson(hub.TacticsSession.GetAcceptance(), true);
            var form = JsonUtility.ToJson(hub.SharedTactics.GetState().form, true);
            var currentId = hub.TacticsCandidates.CurrentCandidateId;
            var active = hub.TacticsCandidates.ActiveView;
            var preparation = JsonUtility.ToJson(hub.ExpeditionSession.GetSnapshot().preparation, true);
            Assert.That(typeof(OpusCandidateView).IsSubclassOf(typeof(TacticsCandidateView)), Is.True,
                "Pause deployment without deleting the real C implementation.");
            Assert.That(hub.TacticsCandidates.IsAvailable("opus"), Is.False);
            Assert.That(hub.TacticsCandidates.SelectCandidate("opus"), Is.False,
                "The public command must not bypass the paused C selector policy.");
            Assert.That(hub.SharedTactics.GetState().feedback, Does.Contain("C 方案暂停"));
            Assert.That(hub.TacticsCandidates.SelectCandidate("unregistered-candidate"), Is.False);
            Assert.That(hub.TacticsCandidates.CurrentCandidateId, Is.EqualTo(currentId));
            Assert.That(hub.TacticsCandidates.ActiveView, Is.SameAs(active));
            yield return new WaitForSecondsRealtime(.12f);
            Assert.That(JsonUtility.ToJson(hub.TacticsSession.GetAcceptance(), true), Is.EqualTo(state));
            Assert.That(JsonUtility.ToJson(hub.SharedTactics.GetState().form, true), Is.EqualTo(form));
            Assert.That(JsonUtility.ToJson(hub.ExpeditionSession.GetSnapshot().preparation, true), Is.EqualTo(preparation));
        }

        [UnityTest]
        public IEnumerator HistoricalSelectionUsesOneRealAutomaticClockAndSurvivesAllCandidateSwitches()
        {
            Assert.That(hub.TacticsCandidates.SelectCandidate("hydrogen"), Is.True);
            yield return RenderFrames();
            var view = hub.TacticsCandidates.ActiveView as HydrogenCandidateView;
            Assert.That(view, Is.Not.Null, "Use the real active B controls.");
            var controls = ControlsFor("hydrogen");
            var session = hub.TacticsSession;
            var ledger = JsonUtility.ToJson(hub.ExpeditionSession.GetSnapshot().preparation, true);
            yield return ApplyAndPause(controls);
            Click(view.StepButton);
            yield return RenderFrames();
            Click(view.StepButton);
            yield return RenderFrames();
            Assert.That(session.GetBattleHistory().Length, Is.EqualTo(2));

            var beforeSelection = JsonUtility.ToJson(session.GetAcceptance(), true);
            Click(view.EventButtons.Single(button => button.name == "Hydrogen_Event_0"));
            Assert.That(hub.SharedTactics.GetState().selectedEventIndex, Is.Zero);
            Assert.That(hub.SharedTactics.GetState().followLatest, Is.False);
            Assert.That(JsonUtility.ToJson(session.GetAcceptance(), true), Is.EqualTo(beforeSelection));
            SetText(view.IntervalInput, "0.05");
            view.IntervalInput.onEndEdit.Invoke(view.IntervalInput.text);
            Click(view.AutoButton);
            var deadline = Time.realtimeSinceStartup + 3;
            while (session.GetBattleHistory().Length == 2 && Time.realtimeSinceStartup < deadline) yield return null;
            Click(view.PauseButton);
            Assert.That(session.GetBattleHistory().Length, Is.EqualTo(3), "One shared clock must deliver one new event before this pause; hidden views cannot add a second step.");
            Assert.That(hub.SharedTactics.GetState().selectedEventIndex, Is.Zero, "A new event must not replace the deliberately selected historical event.");
            Assert.That(hub.SharedTactics.GetState().followLatest, Is.False);
            var expected = new CombatSession(session.GetCommittedScenario());
            for (var index = 0; index < 3; index++) expected.Step();
            Assert.That(JsonUtility.ToJson(session.GetBattleSnapshot()), Is.EqualTo(JsonUtility.ToJson(expected.GetSnapshot())));
            var unchanged = JsonUtility.ToJson(session.GetAcceptance(), true);
            foreach (var id in CandidateIds)
            {
                Assert.That(hub.TacticsCandidates.SelectCandidate(id), Is.True);
                yield return RenderFrames();
                Assert.That(hub.TacticsSession, Is.SameAs(session));
                Assert.That(hub.SharedTactics.GetState().selectedEventIndex, Is.Zero);
                Assert.That(hub.SharedTactics.GetState().followLatest, Is.False);
                Assert.That(hub.SharedTactics.GetState().isAutoPlaying, Is.False);
                Assert.That(JsonUtility.ToJson(session.GetAcceptance(), true), Is.EqualTo(unchanged));
                Assert.That(hub.GetComponentInChildren<CanvasScaler>().matchWidthOrHeight, Is.EqualTo(.5f));
            }
            Click(view.LatestEventButton);
            Assert.That(hub.SharedTactics.GetState().followLatest, Is.True);
            Assert.That(hub.SharedTactics.GetState().selectedEventIndex, Is.EqualTo(2));
            Assert.That(JsonUtility.ToJson(session.GetAcceptance(), true), Is.EqualTo(unchanged));
            Click(view.ReplayButton);
            yield return RenderFrames();
            Assert.That(session.GetBattleHistory(), Is.Empty);
            Assert.That(hub.SharedTactics.GetState().followLatest, Is.True);
            Click(view.StepButton);
            Assert.That(session.GetBattleHistory().Length, Is.EqualTo(1));
            Assert.That(session.GetBattleSnapshot().units.Single(unit => unit.id == "mage").mp, Is.EqualTo(16));
            Assert.That(JsonUtility.ToJson(hub.ExpeditionSession.GetSnapshot().preparation, true), Is.EqualTo(ledger));
        }

        private IEnumerator HydrogenImportKeepsPurchasedCamp()
        {
            var view = (HydrogenCandidateView)hub.TacticsCandidates.ActiveView;
            var camp = hub.ExpeditionSession;
            Click(hub.GetComponentsInChildren<Button>(true).Single(button => button.name == "Nav_Expedition"));
            Click(hub.ExpeditionView.ExpeditionPurchaseButton);
            Assert.That(hub.ExpeditionView.LastCommand.success, Is.True, hub.ExpeditionView.LastCommand.reason);
            var purchased = JsonUtility.ToJson(camp.GetSnapshot().preparation, true);
            Click(hub.TacticsNavButton);
            yield return RenderFrames();
            Click(view.SendExpeditionButton);
            Assert.That(hub.TacticsView.LastSendResult.success, Is.True, hub.TacticsView.LastSendResult.message);
            Assert.That(hub.CurrentModule, Is.EqualTo("Expedition"));
            Assert.That(hub.ExpeditionSession, Is.SameAs(camp), "Import must not replace the purchased camp session.");
            Assert.That(JsonUtility.ToJson(camp.GetSnapshot().preparation, true), Is.EqualTo(purchased));
            foreach (var encounter in camp.GetCombatConfiguration().encounters)
            {
                var mage = encounter.scenario.units.Single(unit => unit.id == "mage");
                Assert.That(mage.instructions[0].condition.terms[0].threshold, Is.EqualTo(.10).Within(1e-12));
                Assert.That(mage.availableActionIdsExplicit, Is.True);
                Assert.That(mage.availableActionIds, Does.Contain("mage_meditate"));
                Assert.That(encounter.scenario.actions.Any(action => action.id == "mage_meditate"), Is.True);
            }
        }

        private IEnumerator ApplyAndPause(Controls controls)
        {
            var interval = hub.SharedTactics.GetState().intervalSeconds;
            hub.SharedTactics.SetPlaybackInterval(5);
            Click(controls.Apply());
            Assert.That(hub.SharedTactics.GetState().isAutoPlaying, Is.True);
            yield return RenderFrames();
            Click(controls.Pause());
            Assert.That(hub.TacticsSession.GetBattleHistory(), Is.Empty, "Rendering a newly enabled Pause must not precompute an event.");
            hub.SharedTactics.SetPlaybackInterval(interval);
            yield return RenderFrames();
        }

        private static IEnumerator RenderFrames()
        {
            // Unity layout/update frames only. Native screenshot evidence comes from the Player helper.
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();
        }

        private double ReadRawThreshold() { return double.Parse(hub.SharedTactics.GetState().form.terms[0].thresholdText, CultureInfo.InvariantCulture); }

        private Controls ControlsFor(string id)
        {
            if (id == "argon")
            {
                var view = hub.TacticsCandidates.ActiveView as ArgonCandidateView;
                Assert.That(view, Is.Not.Null);
                return new Controls
                {
                    EditorTab = () => view.EditorTab, BattleTab = () => view.BattleTab, Apply = () => view.ApplyButton, Pause = () => view.BattlePage.PauseButton,
                    Step = () => view.BattlePage.StepButton, Replay = () => view.ReplayButton, RuleZero = () => view.EditorPage.RuleButtons[0],
                    Replace = () => view.EditorPage.ReplaceButton, Threshold = () => view.EditorPage.Terms[0].ThresholdInput,
                    SelectKind = kind => view.EditorPage.Terms[0].KindDropdown.value = Array.FindIndex(hub.SharedTactics.GetState().catalog.conditions, option => option.id == kind) + 1,
                    Search = () => view.EditorPage.ActionSearchInput,
                    Export = () => view.ExportButton, IsBattlePage = () => view.IsBattlePage,
                    BoardCells = () => view.BattlePage.UnitButtons.ToArray(), OpenTools = () => { if (!view.IsToolsVisible) Click(view.ToolsButton); },
                    CloseExport = () => { if (view.IsToolsVisible) Click(view.ToolsButton); }
                };
            }
            Assert.That(id, Is.EqualTo("hydrogen"));
            var hydrogen = hub.TacticsCandidates.ActiveView as HydrogenCandidateView;
            Assert.That(hydrogen, Is.Not.Null);
            return new Controls
            {
                EditorTab = () => hydrogen.AssemblyTabButton, BattleTab = () => hydrogen.BattleTabButton, Apply = () => hydrogen.ApplyBattleButton, Pause = () => hydrogen.PauseButton,
                Step = () => hydrogen.StepButton, Replay = () => hydrogen.ReplayButton, RuleZero = () => hydrogen.InstructionButtons[0],
                Replace = () => hydrogen.ReplaceButton, Threshold = () => hydrogen.Terms[0].ThresholdInput,
                SelectKind = kind => hydrogen.Terms[0].KindDropdown.value = Array.FindIndex(hub.SharedTactics.GetState().catalog.conditions, option => option.id == kind) + 1,
                Search = () => hydrogen.ActionSearchInput,
                Export = () => hydrogen.ExportButton, IsBattlePage = () => hydrogen.IsBattlePage,
                BoardCells = () => hydrogen.FriendlyBoard.CellButtons.Concat(hydrogen.EnemyBoard.CellButtons).ToArray(), OpenTools = () => { }, CloseExport = () => { }
            };
        }

        private void AssertBoardMpVisible(Controls controls, string token)
        {
            Canvas.ForceUpdateCanvases();
            var label = controls.BoardCells().SelectMany(button => button.GetComponentsInChildren<TMP_Text>())
                .FirstOrDefault(text => text.text.Contains(token));
            Assert.That(label, Is.Not.Null, token + " must be on the actual visible unit card.");
            AssertTokenVisible(label, token);
        }

        private static void AssertTokenVisible(TMP_Text label, string token)
        {
            label.ForceMeshUpdate();
            int start = label.text.IndexOf(token, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), token);
            var clips = new[] { label.GetComponentInParent<Canvas>().GetComponent<RectTransform>(), label.GetComponentInParent<Button>().GetComponent<RectTransform>() };
            int visible = 0;
            foreach (var glyph in label.textInfo.characterInfo.Take(label.textInfo.characterCount))
            {
                if (!glyph.isVisible || glyph.index < start || glyph.index >= start + token.Length) continue;
                visible++;
                foreach (var clip in clips)
                {
                    var bottom = clip.InverseTransformPoint(label.transform.TransformPoint(glyph.bottomLeft));
                    var top = clip.InverseTransformPoint(label.transform.TransformPoint(glyph.topRight));
                    Assert.That(bottom.x, Is.GreaterThanOrEqualTo(clip.rect.xMin - 2), token);
                    Assert.That(bottom.y, Is.GreaterThanOrEqualTo(clip.rect.yMin - 2), token);
                    Assert.That(top.x, Is.LessThanOrEqualTo(clip.rect.xMax + 2), token);
                    Assert.That(top.y, Is.LessThanOrEqualTo(clip.rect.yMax + 2), token);
                }
            }
            var hierarchy = string.Join("/", label.GetComponentsInParent<Transform>().Reverse().Select(value => value.name));
            var glyphs = string.Join(";", label.textInfo.characterInfo.Take(label.textInfo.characterCount)
                .Select(value => "U+" + ((int)value.character).ToString("X4") + ":visible=" + value.isVisible));
            int expected = token.Count(value => !char.IsWhiteSpace(value));
            Assert.That(visible, Is.EqualTo(expected),
                token + " | " + hierarchy + " | rect=" + label.rectTransform.rect + " | size=" + label.fontSize
                + " | margin=" + label.margin + " | overflow=" + label.overflowMode + " | font=" + label.font.name
                + " | hasCoverageGlyphs=" + label.font.HasCharacter('覆') + "/" + label.font.HasCharacter('盖') + " | " + glyphs);
        }

        private static void SetText(TMP_InputField field, string text) { EnsureVisible(field); field.text = text; }
        private static void Click(Button button)
        {
            Assert.That(button.gameObject.activeInHierarchy, Is.True, button.name);
            Assert.That(button.interactable, Is.True, button.name);
            EnsureVisible(button);
            button.onClick.Invoke();
        }
        private static void EnsureVisible(Selectable control)
        {
            Assert.That(control.gameObject.activeInHierarchy, Is.True, control.name);
            Canvas.ForceUpdateCanvases();
            var scroll = control.GetComponentInParent<ScrollRect>();
            var rect = (RectTransform)control.transform;
            if (scroll != null && scroll.content != null && control.transform.IsChildOf(scroll.content))
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
                var points = new Vector3[4]; rect.GetWorldCorners(points);
                float bottom = scroll.viewport.InverseTransformPoint(points[0]).y;
                float top = scroll.viewport.InverseTransformPoint(points[2]).y;
                float shift = top > scroll.viewport.rect.yMax - 2 ? scroll.viewport.rect.yMax - 2 - top
                    : bottom < scroll.viewport.rect.yMin + 2 ? scroll.viewport.rect.yMin + 2 - bottom : 0;
                scroll.content.anchoredPosition += new Vector2(0, shift);
                Canvas.ForceUpdateCanvases();
            }
            var canvas = control.GetComponentInParent<Canvas>().GetComponent<RectTransform>();
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            foreach (var point in corners)
            {
                var local = canvas.InverseTransformPoint(point);
                Assert.That(local.x, Is.InRange(canvas.rect.xMin - 2, canvas.rect.xMax + 2), control.name);
                Assert.That(local.y, Is.InRange(canvas.rect.yMin - 2, canvas.rect.yMax + 2), control.name);
            }
        }
        private sealed class Controls
        {
            public Func<Button> EditorTab, BattleTab, Apply, Pause, Step, Replay, RuleZero, Replace, Export;
            public Func<TMP_InputField> Threshold, Search;
            public Func<bool> IsBattlePage;
            public Func<Button[]> BoardCells;
            public Action OpenTools, CloseExport;
            public Action<string> SelectKind;
        }
    }
}
