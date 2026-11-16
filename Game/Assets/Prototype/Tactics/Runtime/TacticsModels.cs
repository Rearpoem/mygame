using System;
using Prototype.Combat;

namespace Prototype.Tactics
{
    [Serializable]
    public sealed class TacticsCatalogSnapshot
    {
        public CombatTacticsOption[] conditions;
        public CombatTacticsOption[] operations;
        public CombatTacticsOption[] scopes;
        public CombatTacticsOption[] selectors;
        public CombatTacticsOption[] groupings;
        public CombatTacticsOption[] subjects;
        public CombatTacticsOption[] statusKinds;
        public CombatTacticsOption[] targetModes;
        public CombatTacticsOption[] damageTypes;
        public CombatTacticsOption[] effectKinds;
        public CombatActionConfig[] actions;
        public int instructionCapacity;
        public int conditionCapacity;
    }

    [Serializable]
    public sealed class TacticsComparisonResult
    {
        public bool success;
        public string code;
        public string message;
        public string changeKind;
        public string changedUnitId;
        public CombatComparisonReport comparison;
        public string baselineLogText;
        public string variantLogText;
    }

    [Serializable]
    public sealed class TacticsWorkbenchSnapshot
    {
        public CombatScenario draftScenario;
        public CombatSnapshot draftPreview;
        public CombatScenario committedScenario;
        public CombatSnapshot battleSnapshot;
        public CombatStepResult[] history;
        public int draftRevision;
        public int committedRevision;
        public bool hasCommittedBattle;
        public bool hasUncommittedChanges;
        public CombatEditResult lastEditResult;
        public TacticsComparisonResult lastComparison;
    }

    [Serializable]
    public sealed class TacticsWorkbenchAcceptance
    {
        public int schemaVersion;
        public CombatScenario loadedScenario;
        public CombatScenario draftScenario;
        public CombatScenario committedScenario;
        public CombatSnapshot battleSnapshot;
        public CombatStepResult[] battleHistory;
        public int draftRevision;
        public int committedRevision;
        public bool hasUncommittedChanges;
        public CombatEditResult lastEditResult;
        public TacticsComparisonResult lastComparison;
        public string reportText;
    }
}
