using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Prototype.BaseEconomy;
using Prototype.Combat;
using Prototype.Expedition;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Prototype.UnityClient.Tests
{
    public sealed class ExpeditionHubPlayModeTests
    {
        private PrototypeHubController hub;
        private ExpeditionPanelController view;
        private string initialModule;

        [UnitySetUp]
        public IEnumerator CreateTheSameHubAsThePlayer()
        {
            Assert.That(TMP_Settings.defaultFontAsset, Is.Not.Null, "GenerateHub must run before PlayMode checks.");
            hub = PrototypeHubFactory.Create(TMP_Settings.defaultFontAsset);
            initialModule = hub.CurrentModule;
            hub.ShowExpedition();
            view = hub.ExpeditionView;
            Assert.That(view, Is.Not.Null);
            Assert.That(view.IsConfigured, Is.True, view.LastError);
            Assert.That(hub.ExpeditionSession, Is.SameAs(view.SliceSession));
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator DestroyHub()
        {
            if (hub != null) UnityEngine.Object.Destroy(hub.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DefaultExpeditionControlsAndInitialChineseBalancesAreActuallyVisible()
        {
            Assert.That(initialModule, Is.EqualTo("Tactics"));
            AssertControlsInsideCanvas();
            Assert.That(view.SummaryText.text, Does.Contain("公库 100"));
            AssertVisibleToken(view.SummaryText, view.SummaryText.rectTransform, "公库 100");
            AssertVisibleToken(view.SummaryText, view.SummaryText.rectTransform, "许禾 20");
            AssertVisibleToken(view.SummaryText, view.SummaryText.rectTransform, "Lv0 / +0");
            Assert.That(view.ExpeditionNextButton.interactable, Is.True);
            Assert.That(view.ExpeditionDispatchButton.interactable, Is.True);
            Assert.That(view.LastExportDirectory, Is.Null);
            Assert.That(view.LastComparison, Is.Null);
            Assert.That(view.Snapshot.preparation.ledgerCount, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator NextAndDispatchButtonsProduceTheSameCompleteReportsAndRewardsAfterReset()
        {
            Click(view.ExpeditionNextButton);
            Assert.That(view.LastAdvance.success, Is.True, view.LastAdvance.reason);
            Assert.That(view.Snapshot.report.encounters.Length, Is.EqualTo(1));
            Assert.That(view.Snapshot.report.finalSnapshot.time, Is.EqualTo(190));
            Assert.That(view.Snapshot.preparation.treasuryCash, Is.EqualTo(110));
            Assert.That(view.Snapshot.experience.Sum(value => value.amount), Is.EqualTo(21));
            Click(view.ExpeditionNextButton);
            var watched = view.Snapshot;
            Assert.That(watched.report.finalSnapshot.time, Is.EqualTo(310));
            Assert.That(watched.preparation.treasuryCash, Is.EqualTo(125));
            Assert.That(watched.preparation.publicOre, Is.EqualTo(5));
            Assert.That(watched.experience.Single(value => value.unitId == "mage").amount, Is.EqualTo(27));
            Assert.That(watched.experience.Single(value => value.unitId == "ally").amount, Is.EqualTo(25));
            var watchedReport = CanonicalReport(watched.report);
            var watchedLedger = view.SliceSession.ExportLedgerCsv().Replace(watched.currentExpeditionId, "<dispatch>");
            Click(view.ExpeditionResetButton);
            Assert.That(view.Snapshot.phase, Is.EqualTo("Camp"));
            Assert.That(view.Snapshot.experience, Is.Empty);
            Assert.That(view.Snapshot.preparation.treasuryCash, Is.EqualTo(100));
            Assert.That(view.Snapshot.preparation.ledgerCount, Is.EqualTo(1));
            Click(view.ExpeditionDispatchButton);
            var batched = view.Snapshot;
            Assert.That(batched.currentExpeditionId, Is.Not.EqualTo(watched.currentExpeditionId));
            Assert.That(CanonicalReport(batched.report), Is.EqualTo(watchedReport));
            Assert.That(JsonUtility.ToJson(batched.preparation), Is.EqualTo(JsonUtility.ToJson(watched.preparation)));
            Assert.That(view.SliceSession.ExportLedgerCsv().Replace(batched.currentExpeditionId, "<dispatch>"), Is.EqualTo(watchedLedger));
            Assert.That(view.ExpeditionDispatchButton.interactable, Is.False);
            yield return null;
            Canvas.ForceUpdateCanvases();
            AssertControlsInsideCanvas();
            AssertVisibleToken(view.SummaryText, view.SummaryText.rectTransform, "WAIT 310");
            AssertVisibleToken(view.SummaryText, view.SummaryText.rectTransform, "XP 52");
            AssertVisibleToken(view.SummaryText, view.SummaryText.rectTransform, "公库 125");
            var attempt = batched.report.encounters.SelectMany(value => value.steps).SelectMany(value => value.attempts)
                .First(value => !string.IsNullOrWhiteSpace(value.reason) && value.candidates.Any(candidate => !string.IsNullOrWhiteSpace(candidate.reason)));
            RevealAndAssertReportToken(attempt.reason);
            RevealAndAssertReportToken(attempt.candidates.First(value => !string.IsNullOrWhiteSpace(value.reason)).reason);
        }

        [UnityTest]
        public IEnumerator RealButtonsPurchaseEnhanceCompareAndContinueWithTheActualCarriedParty()
        {
            CompleteAndPrepareThroughButtons();
            AssertPreparedBalances(113, 32);
            var beforeComparison = RewardFingerprint(true);
            Click(view.ExpeditionCompareButton);
            Assert.That(view.LastComparison.success, Is.True, view.LastComparison.reason);
            Assert.That(view.ActiveReportPage, Is.EqualTo("Comparison"));
            Assert.That(view.LastComparison.sourceSnapshot.time, Is.EqualTo(310));
            Assert.That(view.LastComparison.baseline.time, Is.EqualTo(490));
            Assert.That(view.LastComparison.enhanced.time, Is.EqualTo(430));
            Assert.That(view.LastComparison.firstDivergence.baseline.time, Is.EqualTo(370));
            Assert.That(view.LastComparison.firstDivergence.variant.time, Is.EqualTo(370));
            Assert.That(view.LastComparison.enhancedEquipmentBonus, Is.EqualTo(4));
            Assert.That(RewardFingerprint(true), Is.EqualTo(beforeComparison));
            yield return null;
            Canvas.ForceUpdateCanvases();
            AssertControlsInsideCanvas();
            AssertVisibleToken(view.SummaryText, view.SummaryText.rectTransform, "公库 113");
            AssertVisibleToken(view.SummaryText, view.SummaryText.rectTransform, "许禾 32");
            AssertVisibleToken(view.SummaryText, view.SummaryText.rectTransform, "Lv1 / +4");
            AssertVisibleToken(view.ReportText, view.ReportScroll.viewport, "同起点 WAIT 310");
            AssertVisibleToken(view.ReportText, view.ReportScroll.viewport, "未强化结束 WAIT 490");
            AssertVisibleToken(view.ReportText, view.ReportScroll.viewport, "已强化结束 WAIT 430");
            Assert.That(view.StateText.text, Does.Contain("guard-linen → ally"));
            Assert.That(view.StateText.text, Does.Contain("guard-linen-sword-001"));
            AssertVisibleToken(view.StateText, view.StateScroll.viewport, "HP 100/100");
            AssertVisibleToken(view.StateText, view.StateScroll.viewport, "MP 18/30");

            var beforeRewards = RewardFingerprint(false);
            Click(view.ExpeditionContinueButton);
            Assert.That(view.LastVerification.success, Is.True, view.LastVerification.reason);
            Assert.That(view.LastVerification.rewardsEligible, Is.False);
            Assert.That(view.LastVerification.battleStart.time, Is.EqualTo(310));
            Assert.That(view.LastVerification.result.time, Is.EqualTo(430));
            Assert.That(view.Snapshot.partyState.time, Is.EqualTo(430));
            Assert.That(RewardFingerprint(false), Is.EqualTo(beforeRewards));
            AssertPreparedBalances(113, 32);
            yield return null;
            Canvas.ForceUpdateCanvases();
            AssertVisibleToken(view.StateText, view.StateScroll.viewport, "当前队伍 · WAIT 430");
            Assert.That(view.FeedbackText.text, Does.Contain("无现金、物资与经验奖励"));
        }

        [UnityTest]
        public IEnumerator ReportTabsNavigationAndSettlementRetryButtonsNeverAdvanceOrReward()
        {
            Click(view.ExpeditionDispatchButton);
            var before = RewardFingerprint(true);
            for (var pass = 0; pass < 3; pass++)
            {
                Click(view.ExpeditionLedgerTabButton);
                Assert.That(view.ActiveReportPage, Is.EqualTo("Ledger"));
                Assert.That(view.ReportText.text, Is.EqualTo(view.SliceSession.ExportLedgerText()));
                Click(view.ExpeditionComparisonTabButton);
                Assert.That(view.LastComparison, Is.Null, "A tab displays records; it must not generate a battle.");
                Click(view.ExpeditionReportTabButton);
                Click(view.ExpeditionReadReportButton);
                Click(view.ExpeditionReplaySettlementButton);
                Assert.That(view.LastCommand.success, Is.True, view.LastCommand.reason);
                Assert.That(view.LastCommand.isReplay, Is.True);
                Assert.That(RewardFingerprint(true), Is.EqualTo(before));
            }
            Click(FindButton("Nav_Combat"));
            Assert.That(hub.CurrentModule, Is.EqualTo("Combat"));
            Click(FindButton("Nav_Base"));
            Assert.That(hub.CurrentModule, Is.EqualTo("BaseEconomy"));
            Click(FindButton("Nav_Expedition"));
            Assert.That(hub.CurrentModule, Is.EqualTo("Expedition"));
            Assert.That(RewardFingerprint(true), Is.EqualTo(before));
            Assert.That(hub.CombatSession.GetSnapshot().completedStepCount, Is.Zero);
            Assert.That(hub.BaseSession.GetSnapshot().Tick, Is.Zero);
            yield return null;
            AssertControlsInsideCanvas();
        }

        [UnityTest]
        public IEnumerator ARealLowWalletPurchaseFailureCanRecoverUsingBattleRewardsWithoutReloading()
        {
            var integrationJson = ReadJson("Prototype/Expedition/default.json");
            var integration = JsonUtility.FromJson<ExpeditionSliceConfig>(integrationJson);
            var prep = JsonUtility.FromJson<PreparationScenario>(ReadJson(integration.preparationConfigPath));
            prep.initialTreasuryCash = 0;
            view.LoadConfiguration(integrationJson, ReadJson(integration.combatConfigPath), JsonUtility.ToJson(prep));
            var opening = RewardFingerprint(true);
            Click(view.ExpeditionPurchaseButton);
            Assert.That(view.LastCommand.success, Is.False);
            Assert.That(view.LastCommand.code, Is.EqualTo("InsufficientCash"));
            Assert.That(RewardFingerprint(true), Is.EqualTo(opening));
            Assert.That(view.FeedbackText.text, Does.Contain("InsufficientCash"));
            yield return null;
            Canvas.ForceUpdateCanvases();
            AssertVisibleToken(view.FeedbackText, view.FeedbackText.rectTransform, "InsufficientCash");
            Click(view.ExpeditionNextButton);
            var afterFirst = RewardFingerprint(true);
            Assert.That(view.Snapshot.preparation.treasuryCash, Is.EqualTo(10));
            Click(view.ExpeditionPurchaseButton);
            Assert.That(view.LastCommand.success, Is.False);
            Assert.That(RewardFingerprint(true), Is.EqualTo(afterFirst));
            Click(view.ExpeditionNextButton);
            Assert.That(view.Snapshot.preparation.treasuryCash, Is.EqualTo(25));
            Click(view.ExpeditionPurchaseButton);
            Assert.That(view.LastCommand.success, Is.True, view.LastCommand.reason);
            Assert.That(view.LastCommand.isReplay, Is.False);
            Assert.That(view.Snapshot.preparation.treasuryCash, Is.EqualTo(13));
            Assert.That(view.Snapshot.preparation.residentCash, Is.EqualTo(32));
            Assert.That(view.Snapshot.preparation.publicAuxiliary, Is.EqualTo(2));
            Click(view.ExpeditionEnhanceButton);
            Assert.That(view.LastCommand.success, Is.True, view.LastCommand.reason);
            AssertPreparedBalances(13, 32);
            Assert.That(view.LastError, Is.Null);
            yield return null;
            Canvas.ForceUpdateCanvases();
            AssertVisibleToken(view.SummaryText, view.SummaryText.rectTransform, "公库 13");
            AssertVisibleToken(view.SummaryText, view.SummaryText.rectTransform, "Lv1 / +4");
        }

        [UnityTest]
        public IEnumerator EnhancementButtonRejectsMissingAuxiliaryAtomicallyThenAcceptsThePurchase()
        {
            Click(view.ExpeditionDispatchButton);
            var before = RewardFingerprint(true);
            Click(view.ExpeditionEnhanceButton);
            Assert.That(view.LastCommand.success, Is.False);
            Assert.That(view.LastCommand.code, Is.EqualTo("InsufficientPublicAuxiliary"));
            Assert.That(RewardFingerprint(true), Is.EqualTo(before));
            Assert.That(view.FeedbackText.text, Does.Contain("InsufficientPublicAuxiliary"));
            Click(view.ExpeditionPurchaseButton);
            Assert.That(view.LastCommand.success, Is.True, view.LastCommand.reason);
            Click(view.ExpeditionEnhanceButton);
            Assert.That(view.LastCommand.success, Is.True, view.LastCommand.reason);
            Assert.That(view.LastCommand.isReplay, Is.False);
            AssertPreparedBalances(113, 32);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CampInstructionButtonsEditTheRealConfigurationsAndResetRestoresTheLoadedOpening()
        {
            Click(view.ExpeditionHealDownButton);
            foreach (var encounter in view.SliceSession.GetCombatConfiguration().encounters)
                Assert.That(encounter.scenario.units.Single(value => value.id == "mage").instructions[0].actionId, Is.EqualTo("basic_attack"));
            view.ExpeditionThresholdInput.text = "80";
            Click(view.ExpeditionApplyButton);
            foreach (var encounter in view.SliceSession.GetCombatConfiguration().encounters)
            {
                var mage = encounter.scenario.units.Single(value => value.id == "mage");
                Assert.That(mage.instructions.Single(value => value.actionId == "first_aid").condition.terms[0].threshold, Is.EqualTo(0.8));
                Assert.That(encounter.scenario.seed, Is.EqualTo(20261114));
            }
            Click(view.ExpeditionHealUpButton);
            Click(view.ExpeditionResetButton);
            foreach (var encounter in view.SliceSession.GetCombatConfiguration().encounters)
            {
                var mage = encounter.scenario.units.Single(value => value.id == "mage");
                Assert.That(mage.instructions[0].actionId, Is.EqualTo("first_aid"));
                Assert.That(mage.instructions[0].condition.terms[0].threshold, Is.EqualTo(0.35));
            }
            Assert.That(view.Snapshot.preparation.treasuryCash, Is.EqualTo(100));
            Click(view.ExpeditionNextButton);
            Assert.That(view.Snapshot.expeditionActive, Is.True);
            Assert.That(view.ExpeditionApplyButton.interactable, Is.False);
            Assert.That(view.ExpeditionThresholdInput.interactable, Is.False);
            Assert.That(view.ExpeditionHealUpButton.interactable, Is.False);
            Assert.That(view.ExpeditionHealDownButton.interactable, Is.False);
            Click(view.ExpeditionResetButton);
            Assert.That(view.Snapshot.phase, Is.EqualTo("Camp"));
            Assert.That(view.Snapshot.experience, Is.Empty);
            Assert.That(view.Snapshot.preparation.ledgerCount, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExportButtonWritesOnlyActualSessionRecordsToTheRequestedTemporaryDirectory()
        {
            var reportRoot = Path.Combine(Path.GetTempPath(), "Prototype-PlayMode-" + Guid.NewGuid().ToString("N"));
            view.ReportDirectoryOverride = reportRoot;
            Assert.That(Directory.Exists(reportRoot), Is.False, "Setting an export destination performs no IO.");
            CompleteAndPrepareThroughButtons();
            Click(view.ExpeditionCompareButton);
            Click(view.ExpeditionContinueButton);
            var accepted = view.SliceSession.GetAcceptance();
            var before = RewardFingerprint(true);
            Click(view.ExpeditionExportButton);
            Assert.That(view.LastExportError, Is.Null, view.LastExportError);
            Assert.That(view.LastExportDirectory, Is.Not.Null.And.Not.Empty);
            var directory = Path.GetFullPath(view.LastExportDirectory);
            Assert.That(directory.StartsWith(Path.GetFullPath(reportRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(directory, "battle_report.txt")), Is.EqualTo(accepted.reportText));
            Assert.That(File.ReadAllText(Path.Combine(directory, "preparation_ledger.txt")), Is.EqualTo(accepted.ledgerText));
            Assert.That(File.ReadAllText(Path.Combine(directory, "preparation_ledger.csv")), Is.EqualTo(accepted.ledgerCsv));
            var exported = JsonUtility.FromJson<ExpeditionSliceAcceptance>(File.ReadAllText(Path.Combine(directory, "session_result.json")));
            Assert.That(JsonUtility.ToJson(exported), Is.EqualTo(JsonUtility.ToJson(accepted)), "Export must serialize this actual session, including its ID and all events.");
            Assert.That(exported.expeditions.Length, Is.EqualTo(1));
            Assert.That(exported.settlements.Length, Is.EqualTo(2));
            Assert.That(exported.comparison.sourceSnapshot.time, Is.EqualTo(310));
            Assert.That(exported.verifications.Single().result.time, Is.EqualTo(430));
            var configurationDirectory = Path.Combine(directory, "configuration");
            Assert.That(File.ReadAllText(Path.Combine(configurationDirectory, "integration_loaded.json")), Is.EqualTo(ReadJson("Prototype/Expedition/default.json")));
            var integration = view.SliceSession.GetConfiguration();
            Assert.That(File.ReadAllText(Path.Combine(configurationDirectory, "combat_loaded.json")), Is.EqualTo(ReadJson(integration.combatConfigPath)));
            Assert.That(File.ReadAllText(Path.Combine(configurationDirectory, "preparation_loaded.json")), Is.EqualTo(ReadJson(integration.preparationConfigPath)));
            Assert.That(JsonUtility.ToJson(JsonUtility.FromJson<ExpeditionSliceConfig>(File.ReadAllText(Path.Combine(configurationDirectory, "integration_current.json")))),
                Is.EqualTo(JsonUtility.ToJson(view.SliceSession.GetConfiguration())));
            Assert.That(JsonUtility.ToJson(JsonUtility.FromJson<CombatExpeditionConfig>(File.ReadAllText(Path.Combine(configurationDirectory, "combat_current.json")))),
                Is.EqualTo(JsonUtility.ToJson(view.SliceSession.GetCombatConfiguration())));
            Assert.That(JsonUtility.ToJson(JsonUtility.FromJson<PreparationScenario>(File.ReadAllText(Path.Combine(configurationDirectory, "preparation_current.json")))),
                Is.EqualTo(JsonUtility.ToJson(view.SliceSession.GetPreparationConfiguration())));
            var manifest = JsonUtility.FromJson<ExportManifestDto>(File.ReadAllText(Path.Combine(directory, "export_manifest.json")));
            Assert.That(manifest.currentExpeditionId, Is.EqualTo(accepted.snapshot.currentExpeditionId));
            Assert.That(manifest.recordedComparison, Is.True);
            Assert.That(manifest.recordedVerificationCount, Is.EqualTo(1));
            Assert.That(manifest.readOnlyExport, Is.True);
            Assert.That(RewardFingerprint(true), Is.EqualTo(before));
            Click(view.ExpeditionExportButton);
            Assert.That(view.LastExportDirectory, Is.Not.EqualTo(directory));
            Assert.That(File.ReadAllText(Path.Combine(directory, "session_result.json")), Is.Not.Empty, "The prior export is preserved.");
            Assert.That(RewardFingerprint(true), Is.EqualTo(before));
            TestContext.Progress.WriteLine("Actual button export verified at " + reportRoot);
            yield return null;
        }

        private void CompleteAndPrepareThroughButtons()
        {
            Click(view.ExpeditionDispatchButton);
            Assert.That(view.LastAdvance.success, Is.True, view.LastAdvance.reason);
            Click(view.ExpeditionPurchaseButton);
            Assert.That(view.LastCommand.success, Is.True, view.LastCommand.reason);
            Click(view.ExpeditionEnhanceButton);
            Assert.That(view.LastCommand.success, Is.True, view.LastCommand.reason);
        }

        private static void Click(Button button)
        {
            Assert.That(button, Is.Not.Null);
            Assert.That(button.gameObject.activeInHierarchy, Is.True, button.name + " must be visible before clicking.");
            Assert.That(button.interactable, Is.True, button.name + " must accept a real player click.");
            button.onClick.Invoke();
        }

        private Button FindButton(string name)
        {
            return hub.GetComponentsInChildren<Button>(true).Single(value => value.name == name);
        }

        private static string ReadJson(string relativePath)
        {
            return File.ReadAllText(Path.Combine(Application.streamingAssetsPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string CanonicalReport(CombatExpeditionReport report)
        {
            return JsonUtility.ToJson(report).Replace(report.expeditionId, "<dispatch>");
        }

        private string RewardFingerprint(bool includeParty)
        {
            var snapshot = view.Snapshot;
            return JsonUtility.ToJson(snapshot.preparation) + "|" + view.SliceSession.ExportLedgerCsv() + "|" +
                string.Join(";", snapshot.experience.OrderBy(value => value.unitId).Select(value => value.unitId + ":" + value.amount)) + "|" +
                (includeParty ? JsonUtility.ToJson(snapshot.partyState) + "|" + (snapshot.report == null ? "" : CanonicalReport(snapshot.report)) : "");
        }

        private void AssertPreparedBalances(int treasury, int resident)
        {
            var snapshot = view.Snapshot;
            Assert.That(snapshot.preparation.treasuryCash, Is.EqualTo(treasury));
            Assert.That(snapshot.preparation.residentCash, Is.EqualTo(resident));
            Assert.That(snapshot.preparation.publicOre, Is.EqualTo(2));
            Assert.That(snapshot.preparation.publicAuxiliary, Is.Zero);
            Assert.That(snapshot.preparation.residentAuxiliary, Is.Zero);
            Assert.That(snapshot.preparation.weaponEnhancementLevel, Is.EqualTo(1));
            Assert.That(snapshot.preparation.weaponAttackBonus, Is.EqualTo(4));
            Assert.That(snapshot.experience.Sum(value => value.amount), Is.EqualTo(52));
        }

        private void AssertControlsInsideCanvas()
        {
            Canvas.ForceUpdateCanvases();
            var canvas = hub.GetComponentInChildren<Canvas>();
            var canvasCorners = new Vector3[4];
            ((RectTransform)canvas.transform).GetWorldCorners(canvasCorners);
            foreach (var button in hub.GetComponentsInChildren<Button>(false))
            {
                var corners = new Vector3[4];
                ((RectTransform)button.transform).GetWorldCorners(corners);
                Assert.That(corners[0].x, Is.GreaterThanOrEqualTo(canvasCorners[0].x - 2), button.name);
                Assert.That(corners[0].y, Is.GreaterThanOrEqualTo(canvasCorners[0].y - 2), button.name);
                Assert.That(corners[2].x, Is.LessThanOrEqualTo(canvasCorners[2].x + 2), button.name);
                Assert.That(corners[2].y, Is.LessThanOrEqualTo(canvasCorners[2].y + 2), button.name);
                Assert.That(corners[2].x - corners[0].x, Is.GreaterThan(20), button.name);
                Assert.That(corners[2].y - corners[0].y, Is.GreaterThan(15), button.name);
            }
        }

        private static void AssertVisibleToken(TMP_Text text, RectTransform viewport, string token)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(text.rectTransform);
            text.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();
            text.ForceMeshUpdate();
            var start = text.text.IndexOf(token, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "Missing displayed token: " + token);
            var checkedGlyphs = 0;
            for (var index = 0; index < text.textInfo.characterCount; index++)
            {
                var glyph = text.textInfo.characterInfo[index];
                if (!glyph.isVisible || glyph.index < start || glyph.index >= start + token.Length) continue;
                checkedGlyphs++;
                var bottomLeft = viewport.InverseTransformPoint(text.transform.TransformPoint(glyph.bottomLeft));
                var topRight = viewport.InverseTransformPoint(text.transform.TransformPoint(glyph.topRight));
                Assert.That(bottomLeft.x, Is.GreaterThanOrEqualTo(viewport.rect.xMin - 2), "Left-clipped token: " + token);
                Assert.That(bottomLeft.y, Is.GreaterThanOrEqualTo(viewport.rect.yMin - 2), "Bottom-clipped token: " + token);
                Assert.That(topRight.x, Is.LessThanOrEqualTo(viewport.rect.xMax + 2), "Right-clipped token: " + token);
                Assert.That(topRight.y, Is.LessThanOrEqualTo(viewport.rect.yMax + 2), "Top-clipped token: " + token);
            }
            Assert.That(checkedGlyphs, Is.GreaterThan(0), "Token did not generate visible glyphs: " + token);
        }

        private void RevealAndAssertReportToken(string token)
        {
            Assert.That(view.ReportScroll.vertical, Is.True);
            Assert.That(view.ReportText.fontSize, Is.GreaterThanOrEqualTo(16));
            Assert.That(view.ReportText.text, Does.Contain(token));
            Canvas.ForceUpdateCanvases();
            view.ReportText.ForceMeshUpdate();
            var start = view.ReportText.text.IndexOf(token, StringComparison.Ordinal);
            var first = view.ReportText.textInfo.characterInfo.First(value => value.isVisible && value.index >= start && value.index < start + token.Length);
            var top = view.ReportScroll.viewport.InverseTransformPoint(view.ReportText.transform.TransformPoint(first.topRight)).y;
            view.ReportScroll.content.anchoredPosition += new Vector2(0, view.ReportScroll.viewport.rect.yMax - 6 - top);
            Canvas.ForceUpdateCanvases();
            AssertVisibleToken(view.ReportText, view.ReportScroll.viewport, token);
        }

        [Serializable]
        private sealed class ExportManifestDto
        {
            public string currentExpeditionId;
            public bool recordedComparison;
            public int recordedVerificationCount;
            public bool readOnlyExport;
        }
    }
}
