using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Prototype.UnityClient.Tests
{
    public sealed class PrototypeHubPlayModeTests
    {
        private PrototypeHubController hub;

        [UnitySetUp]
        public IEnumerator CreateRealClient()
        {
            Assert.That(TMP_Settings.defaultFontAsset, Is.Not.Null,
                "Run Prototype.Editor.PrototypeProjectBuilder.GenerateHub before running client tests.");
            hub = PrototypeHubFactory.Create(TMP_Settings.defaultFontAsset);
            hub.ShowCombat();
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator DestroyClient()
        {
            if (hub != null) UnityEngine.Object.Destroy(hub.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PriorityButtonsChangeFirstActionAndResetKeepsChoice()
        {
            hub.CombatReplayButton.onClick.Invoke();
            Assert.That(hub.LastCombatStep.actionId, Is.EqualTo("first_aid"));
            Assert.That(hub.LastCombatStep.targetId, Is.EqualTo("ally"));
            Assert.That(hub.CombatHealDownButton.interactable, Is.True);
            hub.CombatHealDownButton.onClick.Invoke();
            Assert.That(hub.CombatSession.GetSnapshot().completedStepCount, Is.Zero);
            hub.CombatReplayButton.onClick.Invoke();
            Assert.That(hub.LastCombatStep.actionId, Is.EqualTo("basic_attack"));
            Assert.That(hub.LastCombatStep.targetId, Is.EqualTo("enemy_low"));
            Assert.That(hub.CombatLogText.text, Does.Contain(hub.LastCombatStep.targetName));
            hub.CombatResetButton.onClick.Invoke();
            Assert.That(hub.CombatSession.GetSnapshot().completedStepCount, Is.Zero);
            hub.CombatStepButton.onClick.Invoke();
            Assert.That(hub.LastCombatStep.actionId, Is.EqualTo("basic_attack"));
            hub.CombatDefaultsButton.onClick.Invoke();
            hub.CombatReplayButton.onClick.Invoke();
            Assert.That(hub.LastCombatStep.actionId, Is.EqualTo("first_aid"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator InputsReachDomainAndFullHpFallsBackWithTrace()
        {
            var ally = hub.CombatSession.GetScenario().units.First(unit => unit.id == "ally");
            var injuredHp = ally.hp;
            hub.AllyHpInput.text = ally.maxHp.ToString();
            hub.CombatApplyButton.onClick.Invoke();
            hub.CombatReplayButton.onClick.Invoke();
            Assert.That(hub.LastCombatStep.actionId, Is.EqualTo("basic_attack"));
            Assert.That(hub.LastCombatStep.attempts.Length, Is.GreaterThan(1));
            Assert.That(hub.LastCombatStep.attempts[0].reason, Is.Not.Empty);
            hub.AllyHpInput.text = injuredHp.ToString();
            hub.HealingThresholdInput.text = "0";
            hub.CombatApplyButton.onClick.Invoke();
            hub.CombatReplayButton.onClick.Invoke();
            Assert.That(hub.LastCombatStep.actionId, Is.EqualTo("basic_attack"));
            hub.HealingThresholdInput.text = "100";
            hub.CombatApplyButton.onClick.Invoke();
            hub.CombatReplayButton.onClick.Invoke();
            Assert.That(hub.LastCombatStep.actionId, Is.EqualTo("first_aid"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RecruitmentToMealAndResetUseRealButtonListeners()
        {
            hub.ShowBase();
            var initial = hub.BaseSession.GetSnapshot();
            Assert.That(hub.BaseRunToMealButton.interactable, Is.False);
            hub.BaseRecruitButton.onClick.Invoke();
            Assert.That(hub.BaseSession.GetSnapshot().IsCookRecruited, Is.True);
            Assert.That(hub.BaseSession.GetSnapshot().TotalCash, Is.EqualTo(initial.TotalCash));
            hub.BaseRunToMealButton.onClick.Invoke();
            var completed = hub.BaseSession.GetSnapshot();
            Assert.That(completed.IsComplete, Is.True);
            Assert.That(completed.MealsConsumed, Is.EqualTo(1));
            Assert.That(completed.TotalCash, Is.EqualTo(initial.TotalCash));
            Assert.That(hub.BaseSession.Ledger.All(entry => entry.TotalCash == initial.TotalCash), Is.True);
            Assert.That(hub.BaseLogText.text, Does.Contain("厨师"));
            hub.BaseResetButton.onClick.Invoke();
            var reset = hub.BaseSession.GetSnapshot();
            Assert.That(reset.Tick, Is.Zero);
            Assert.That(reset.IsCookRecruited, Is.False);
            Assert.That(reset.KitchenPosition, Is.EqualTo(initial.KitchenPosition));
            Assert.That(reset.TreasuryCash, Is.EqualTo(initial.TreasuryCash));
            Assert.That(reset.SupplierGrain, Is.EqualTo(initial.SupplierGrain));
            yield return null;
        }

        [UnityTest]
        public IEnumerator KitchenChoicesChangeTravelTimeAndMapButtonStartsNewOpening()
        {
            hub.ShowBase();
            hub.BaseNearButton.onClick.Invoke();
            hub.BaseRecruitButton.onClick.Invoke();
            hub.BaseRunToMealButton.onClick.Invoke();
            var near = hub.BaseSession.GetSnapshot();
            Assert.That(near.IsComplete, Is.True);
            hub.BaseFarButton.onClick.Invoke();
            Assert.That(hub.BaseSession.GetSnapshot().Tick, Is.Zero);
            hub.BaseRecruitButton.onClick.Invoke();
            hub.BaseRunToMealButton.onClick.Invoke();
            var far = hub.BaseSession.GetSnapshot();
            Assert.That(far.IsComplete, Is.True);
            Assert.That(far.FirstMealTick, Is.GreaterThan(near.FirstMealTick));
            var mapButton = hub.GetComponentsInChildren<Button>(true)
                .First(button => button.name.StartsWith("Base_Map_") && button.interactable);
            var coordinates = mapButton.name.Split('_');
            var x = int.Parse(coordinates[2]);
            var y = int.Parse(coordinates[3]);
            mapButton.onClick.Invoke();
            var selected = hub.BaseSession.GetSnapshot();
            Assert.That(selected.Tick, Is.Zero);
            Assert.That(selected.IsCookRecruited, Is.False);
            Assert.That(selected.KitchenPosition.x, Is.EqualTo(x));
            Assert.That(selected.KitchenPosition.y, Is.EqualTo(y));
            hub.BaseResetButton.onClick.Invoke();
            Assert.That(hub.BaseSession.GetSnapshot().KitchenPosition, Is.EqualTo(selected.KitchenPosition));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ModuleNavigationKeepsIndependentSessionsAndChineseControlsFitCanvas()
        {
            var font = hub.UiFont;
            Assert.That(font.HasCharacters("战斗基地法师急救厨房供粮顾客公库钱包账本重置", out List<char> missing), Is.True,
                "Missing glyphs: " + new string(missing.ToArray()));
            hub.CombatReplayButton.onClick.Invoke();
            var combatSteps = hub.CombatSession.GetSnapshot().completedStepCount;
            var combatClock = hub.CombatSession.GetSnapshot().time;
            AssertControlsInsideCanvas();
            AssertCombatRosterVisible();
            hub.ShowBase();
            hub.BaseRecruitButton.onClick.Invoke();
            hub.BaseStepButton.onClick.Invoke();
            var baseClock = hub.BaseSession.GetSnapshot().Tick;
            Assert.That(hub.CombatSession.GetSnapshot().completedStepCount, Is.EqualTo(combatSteps));
            Assert.That(hub.CombatSession.GetSnapshot().time, Is.EqualTo(combatClock));
            yield return null;
            Canvas.ForceUpdateCanvases();
            AssertControlsInsideCanvas();
            hub.ShowCombat();
            hub.CombatResetButton.onClick.Invoke();
            Assert.That(hub.BaseSession.GetSnapshot().Tick, Is.EqualTo(baseClock));
            yield return null;
            AssertCombatRosterVisible();
        }

        private void AssertCombatRosterVisible()
        {
            Canvas.ForceUpdateCanvases();
            var text = hub.CombatUnitsText;
            text.ForceMeshUpdate();
            LayoutRebuilder.ForceRebuildLayoutImmediate(text.rectTransform);
            Canvas.ForceUpdateCanvases();
            text.ForceMeshUpdate();
            var viewport = hub.CombatUnitsScroll.viewport;
            var units = hub.CombatSession.GetSnapshot().units;
            Assert.That(text.fontSize, Is.GreaterThanOrEqualTo(16), "Roster must remain readable.");
            Assert.That(text.enableAutoSizing, Is.False, "Do not hide clipping by shrinking roster type.");
            Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(viewport.rect.height + 1),
                "The entire four-person roster must fit the visible viewport without scrolling.");
            Assert.That(text.preferredWidth, Is.LessThanOrEqualTo(viewport.rect.width + 1),
                "Roster rows must not be clipped horizontally.");
            Assert.That(text.textInfo.lineCount, Is.EqualTo(units.Length), "Each person occupies one complete row.");
            var rows = text.text.Split(new[] { '\n' }, StringSplitOptions.None);
            Assert.That(rows.Length, Is.EqualTo(units.Length));
            for (var i = 0; i < units.Length; i++)
            {
                var unit = units[i];
                Assert.That(rows[i], Does.Contain(unit.name), "Complete roster: " + unit.id);
                Assert.That(rows[i], Does.Contain("HP " + unit.hp + "/" + unit.maxHp));
                Assert.That(rows[i], Does.Contain("MP " + unit.mp + "/" + unit.maxMp));
                Assert.That(rows[i], Does.Contain("WAIT→" + unit.nextActionTime.ToString("0.##", CultureInfo.InvariantCulture)));
                Assert.That(rows[i], Does.Contain("容量" + unit.instructionCapacity));
            }
            // The old two-line-per-person layout had all names in the string but masked
            // away the last resident. Check actual generated glyphs against the mask.
            for (var i = 0; i < text.textInfo.characterCount; i++)
            {
                var glyph = text.textInfo.characterInfo[i];
                if (!glyph.isVisible) continue;
                var bottomLeft = viewport.InverseTransformPoint(text.transform.TransformPoint(glyph.bottomLeft));
                var topRight = viewport.InverseTransformPoint(text.transform.TransformPoint(glyph.topRight));
                Assert.That(bottomLeft.x, Is.GreaterThanOrEqualTo(viewport.rect.xMin - 1), "Left-clipped roster glyph " + i);
                Assert.That(bottomLeft.y, Is.GreaterThanOrEqualTo(viewport.rect.yMin - 1), "Bottom-clipped roster glyph " + i);
                Assert.That(topRight.x, Is.LessThanOrEqualTo(viewport.rect.xMax + 1), "Right-clipped roster glyph " + i);
                Assert.That(topRight.y, Is.LessThanOrEqualTo(viewport.rect.yMax + 1), "Top-clipped roster glyph " + i);
            }
        }

        private void AssertControlsInsideCanvas()
        {
            Canvas.ForceUpdateCanvases();
            var canvas = hub.GetComponentInChildren<Canvas>();
            var bounds = new Vector3[4];
            ((RectTransform)canvas.transform).GetWorldCorners(bounds);
            foreach (var button in hub.GetComponentsInChildren<Button>(false))
            {
                var corners = new Vector3[4];
                ((RectTransform)button.transform).GetWorldCorners(corners);
                Assert.That(corners[0].x, Is.GreaterThanOrEqualTo(bounds[0].x - 2), button.name);
                Assert.That(corners[0].y, Is.GreaterThanOrEqualTo(bounds[0].y - 2), button.name);
                Assert.That(corners[2].x, Is.LessThanOrEqualTo(bounds[2].x + 2), button.name);
                Assert.That(corners[2].y, Is.LessThanOrEqualTo(bounds[2].y + 2), button.name);
            }
        }
    }
}
