using System.Linq;

namespace Prototype.Combat.Tests
{
    // Scenario construction only: every assertion exercises the production CombatSession.
    internal static class CombatExpansionFixture
    {
        internal static CombatUnitConfig Unit(string id, string team, int row = 0, int column = 0)
        {
            return new CombatUnitConfig { id = id, name = id, team = team, row = row, column = column,
                hp = 100, maxHp = 100, mp = 30, maxMp = 30, intelligence = 40,
                agilityFactor = 1, initialReadyTime = 100, instructions = new CombatInstruction[0],
                statuses = new CombatStatusConfig[0], tags = new string[0] };
        }
        internal static CombatActionConfig Action(string id = "hit", string kind = "damage", int magnitude = 1,
            int cost = 0, double wait = 40, string mode = "single")
        {
            return new CombatActionConfig { id = id, displayName = id, effectKind = kind, magnitude = magnitude,
                mpCost = cost, baseWait = wait, targetMode = mode };
        }
        internal static CombatConditionTerm Term(string kind = "always", double threshold = 0,
            string value = null, string subject = null)
        { return new CombatConditionTerm { kind = kind, threshold = threshold, value = value, subject = subject }; }
        internal static CombatInstruction Rule(string id = "rule", string action = "hit", string scope = "enemies",
            string selector = "configured_order", params CombatConditionTerm[] terms)
        {
            return new CombatInstruction { id = id, label = id, actionId = action, scope = scope, selector = selector,
                condition = new CombatCondition { operation = "all", terms = terms.Length == 0 ? new[] { Term() } : terms } };
        }
        internal static CombatStatusConfig Status(string id, string kind, int magnitude = 1, double expires = 90)
        { return new CombatStatusConfig { id = id, kind = kind, magnitude = magnitude, appliedAt = 0, expiresAt = expires }; }
        internal static CombatScenario Scenario(params CombatUnitConfig[] units)
        {
            return new CombatScenario { id = "round4-test", title = "Round4 actual engine test", parameterNotice = "Test values only",
                seed = 42, alliesIncludeSelf = true, gridRows = 3, gridColumns = 3, maxUnitsPerTeam = 6,
                playerTeam = units[0].team,
                capacity = new CombatCapacityConfig { baseSlots = 1, slotsPerIntelligence = .1, minimumSlots = 1,
                    maximumSlots = 6, secondConditionAtIntelligence = 20, thirdConditionAtIntelligence = 40 },
                units = units, actions = new[] { Action() } };
        }
        internal static CombatScenario Pair()
        {
            CombatUnitConfig actor = Unit("actor", "blue"); actor.initialReadyTime = 0;
            CombatUnitConfig a = Unit("a", "red"); a.hp = 80; a.mp = 5; a.intelligence = 20; a.initialReadyTime = 20;
            CombatUnitConfig b = Unit("b", "red", 1); b.hp = 40; b.mp = 20; b.initialReadyTime = 60;
            return Scenario(actor, a, b);
        }
        internal static CombatUnitConfig Get(CombatScenario config, string id)
        { return config.units.Single(unit => unit.id == id); }
        internal static CombatUnitSnapshot Get(CombatSnapshot snapshot, string id)
        { return snapshot.units.Single(unit => unit.id == id); }
        internal static void Configure(CombatScenario config, CombatInstruction[] rules, params CombatActionConfig[] actions)
        {
            config.actions = config.actions.Concat(actions).ToArray();
            config.units[0].instructions = rules;
            config.units[0].availableActionIds = config.actions.Select(action => action.id).ToArray();
            config.units[0].availableActionIdsExplicit = true;
        }
    }
}
