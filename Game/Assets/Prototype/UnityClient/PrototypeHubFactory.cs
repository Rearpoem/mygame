using Prototype.BaseEconomy;
using Prototype.Combat;
using TMPro;
using UnityEngine;

namespace Prototype.UnityClient
{
    /// <summary>The scene, player and UI tests construct the same four-entry hub, defaulting to Tactics.</summary>
    public static class PrototypeHubFactory
    {
        public static PrototypeHubController Create(TMP_FontAsset font,
            CombatScenario combatScenario = null, BaseScenario baseScenario = null)
        {
            var root = new GameObject("Prototype Hub");
            var controller = root.AddComponent<PrototypeHubController>();
            controller.Initialize(font, combatScenario, baseScenario);
            return controller;
        }
    }
}
