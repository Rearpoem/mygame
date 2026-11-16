using NUnit.Framework;
using UnityEngine;
using static Prototype.Combat.Tests.CombatExpansionFixture;

namespace Prototype.Combat.Tests
{
    public sealed class CombatActionDefinitionTests
    {
        [Test]
        public void OptionalActionDefaults_AreEquivalentBeforeAndAfterUnityJsonRoundTrip()
        {
            CombatActionConfig raw = Action(); raw.targetMode = null; raw.damageType = null;
            CombatActionConfig roundTrip = JsonUtility.FromJson<CombatActionConfig>(JsonUtility.ToJson(raw));
            CombatActionConfig explicitDefaults = Action(); explicitDefaults.targetMode = "single"; explicitDefaults.damageType = "legacy";
            CombatActionConfig emptyDefaults = Action(); emptyDefaults.targetMode = ""; emptyDefaults.damageType = "";
            Assert.That(CombatSession.ActionDefinitionsEquivalent(raw, roundTrip), Is.True);
            Assert.That(CombatSession.ActionDefinitionsEquivalent(raw, explicitDefaults), Is.True);
            Assert.That(CombatSession.ActionDefinitionsEquivalent(raw, emptyDefaults), Is.True);
            Assert.That(CombatSession.ActionDefinitionsEquivalent(null, null), Is.True);
            Assert.That(CombatSession.ActionDefinitionsEquivalent(raw, null), Is.False);
        }

        [TestCase("id")]
        [TestCase("displayName")]
        [TestCase("effectKind")]
        [TestCase("magnitude")]
        [TestCase("mpCost")]
        [TestCase("baseWait")]
        [TestCase("targetMode")]
        [TestCase("damageType")]
        [TestCase("isMagic")]
        [TestCase("statusId")]
        [TestCase("statusKind")]
        [TestCase("statusMagnitude")]
        [TestCase("statusDuration")]
        public void RealActionDifferences_AreNotHiddenByNormalization(string field)
        {
            CombatActionConfig first = Action();
            CombatActionConfig changed = CombatSession.NormalizeActionDefinition(first);
            switch (field)
            {
                case "id": changed.id = "other"; break;
                case "displayName": changed.displayName = "other"; break;
                case "effectKind": changed.effectKind = "heal"; break;
                case "magnitude": changed.magnitude++; break;
                case "mpCost": changed.mpCost++; break;
                case "baseWait": changed.baseWait += .25; break;
                case "targetMode": changed.targetMode = "row"; break;
                case "damageType": changed.damageType = "physical"; break;
                case "isMagic": changed.isMagic = true; break;
                case "statusId": changed.statusId = "mark"; break;
                case "statusKind": changed.statusKind = "guard"; break;
                case "statusMagnitude": changed.statusMagnitude = 4; break;
                case "statusDuration": changed.statusDuration = 20; break;
            }
            Assert.That(CombatSession.ActionDefinitionsEquivalent(first, changed), Is.False, field);
            Assert.That(CombatSession.ActionDefinitionsEquivalent(changed, first), Is.False, field);
        }

        [Test]
        public void NormalizeAction_ReturnsAnIndependentCopyAndDoesNotValidateOrMutateItsInput()
        {
            CombatActionConfig raw = Action(); raw.targetMode = null; raw.damageType = ""; raw.statusId = null;
            string before = JsonUtility.ToJson(raw);
            CombatActionConfig copy = CombatSession.NormalizeActionDefinition(raw);
            Assert.That(copy, Is.Not.SameAs(raw)); Assert.That(copy.targetMode, Is.EqualTo("single")); Assert.That(copy.damageType, Is.EqualTo("legacy"));
            Assert.That(JsonUtility.ToJson(raw), Is.EqualTo(before));
            copy.magnitude = 999; copy.displayName = "changed";
            Assert.That(raw.magnitude, Is.EqualTo(1)); Assert.That(raw.displayName, Is.EqualTo("hit"));
            Assert.That(CombatSession.NormalizeActionDefinition(null), Is.Null);
            CombatActionConfig invalid = Action(); invalid.targetMode = "not-a-mode";
            Assert.That(CombatSession.NormalizeActionDefinition(invalid).targetMode, Is.EqualTo("not-a-mode"), "Equality is not a substitute for scenario validation.");
        }
    }
}
