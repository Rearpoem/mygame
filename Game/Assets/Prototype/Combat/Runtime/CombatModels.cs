using System;

namespace Prototype.Combat
{
    // These DTOs intentionally use fields and simple arrays for Unity JsonUtility.
    // The runtime assembly has no UnityEngine dependency.
    [Serializable]
    public sealed class CombatScenario
    {
        public string id;
        public string title;
        public string parameterNotice;
        public int seed;
        public bool randomUsed;
        public bool alliesIncludeSelf;
        public int maxUnitsPerTeam;
        public int gridRows;
        public int gridColumns;
        public CombatCapacityConfig capacity;
        public CombatUnitConfig[] units;
        public CombatActionConfig[] actions;
        public string playerTeam;
        public int maxEvents;
        public double maxWait;
    }

    [Serializable]
    public sealed class CombatCapacityConfig
    {
        public double baseSlots;
        public double slotsPerIntelligence;
        public int minimumSlots;
        public int maximumSlots;
        public int secondConditionAtIntelligence;
        public int thirdConditionAtIntelligence;
    }

    [Serializable]
    public sealed class CombatUnitConfig
    {
        public string id;
        public string name;
        public string team;
        public int row;
        public int column;
        public int maxHp;
        public int hp;
        public int maxMp;
        public int mp;
        public int intelligence;
        public double agilityFactor;
        public double initialReadyTime;
        public bool vulnerable;
        public CombatInstruction[] instructions;
        public CombatStatusConfig[] statuses;
        public int equipmentDamageBonus;
        public string equipmentSourceId;
        // Missing/empty with presence=false is legacy and inferred from original known instructions.
        // Presence=true makes null/empty an explicit empty learned-action set (JsonUtility-safe).
        public string[] availableActionIds;
        public bool availableActionIdsExplicit;
        public string[] tags;
    }

    [Serializable]
    public sealed class CombatInstruction
    {
        public string id;
        public string label;
        public CombatCondition condition;
        public string scope;
        public string selector;
        public string actionId;
    }

    [Serializable]
    public sealed class CombatCondition
    {
        public string operation;
        public CombatConditionTerm[] terms;
        // operation=mixed requires three terms, two and/or operators and explicit left/right grouping.
        public string[] operators;
        public string grouping;
    }

    [Serializable]
    public sealed class CombatConditionTerm
    {
        public string kind;
        public double threshold;
        public string subject;
        public string value;
    }

    [Serializable]
    public sealed class CombatActionConfig
    {
        public string id;
        public string displayName;
        public string effectKind;
        public int magnitude;
        public int mpCost;
        public double baseWait;
        public string statusId;
        public string statusKind;
        public int statusMagnitude;
        public double statusDuration;
        public string targetMode;
        public bool isMagic;
        public string damageType;
    }

    [Serializable]
    public sealed class CombatStatusConfig
    {
        public string id;
        public string kind;
        public int magnitude;
        public double appliedAt;
        public double expiresAt;
    }

    [Serializable]
    public sealed class CombatRunResult
    {
        public string outcome;
        public string winnerTeam;
        public string stopReason;
        public double time;
        public int executedActionCount;
        public CombatSnapshot snapshot;
        public CombatStepResult[] steps;
    }

    [Serializable]
    public sealed class CombatSnapshot
    {
        public string scenarioId;
        public string parameterNotice;
        public int seed;
        public bool randomUsed;
        public double time;
        public CombatUnitSnapshot[] units;
        public string nextActorId;
        public double nextActionTime;
        public bool pendingTimeTie;
        public bool blocked;
        public string blockReason;
        public int completedStepCount;
        public bool finished;
        public string outcome;
        public string winnerTeam;
        public string stopReason;
        public double battleStartTime;
    }

    [Serializable]
    public sealed class CombatUnitSnapshot
    {
        public string id;
        public string name;
        public string team;
        public int row;
        public int column;
        public int hp;
        public int maxHp;
        public int mp;
        public int maxMp;
        public int intelligence;
        public bool vulnerable;
        public bool baseVulnerable;
        public int instructionCapacity;
        public int conditionTermCapacity;
        public double nextActionTime;
        public CombatInstructionSnapshot[] instructions;
        public CombatStatusConfig[] statuses;
        public int equipmentDamageBonus;
        public string equipmentSourceId;
        public bool dormant;
        public string[] tags;
        public double effectiveAgilityFactor;
    }

    [Serializable]
    public sealed class CombatInstructionSnapshot
    {
        public string id;
        public string display;
        public bool capacityAllowed;
        public bool actionAvailable;
        public string validationReason;
    }

    [Serializable]
    public sealed class CombatEditResult
    {
        public bool success;
        public string message;
        public string code;
        public int instructionCapacity;
        public int conditionTermCapacity;
    }

    [Serializable]
    public sealed class CombatStepResult
    {
        public int index;
        public double time;
        public string status;
        public string actorId;
        public string actorName;
        public CombatInstructionAttempt[] attempts;
        public string actionId;
        public string actionName;
        public string targetId;
        public string targetName;
        public CombatEffect[] effects;
        public int mpCost;
        public int mpBefore;
        public int mpAfter;
        public double baseWait;
        public double agilityFactor;
        public double actualWait;
        public double nextActionTime;
        public string message;
        public string outcome;
        public string winnerTeam;
        public string stopReason;
        public bool timeTieResolved;
        public string tieBreakReason;
        public int equipmentDamageBonus;
        public string[] targetIds;
        public double baseAgilityFactor;
        public double statusWaitFactor;
        public string controlReason;
    }

    [Serializable]
    public sealed class CombatInstructionAttempt
    {
        public int instructionIndex;
        public string instructionId;
        public string display;
        public string outcome;
        public string reason;
        public string reasonCode;
        public CombatCandidateTrace[] candidates;
    }

    [Serializable]
    public sealed class CombatCandidateTrace
    {
        public string unitId;
        public string unitName;
        public bool passesCondition;
        public bool eligible;
        public string reason;
        public string[] affectedTargetIds;
    }

    [Serializable]
    public sealed class CombatEffect
    {
        public string kind;
        public string targetId;
        public string targetName;
        public int hpBefore;
        public int hpAfter;
        public int hpDelta;
        public string description;
        public int mpBefore;
        public int mpAfter;
        public int mpDelta;
        public string statusId;
        public double statusAppliedAt;
        public double statusExpiresAt;
        public string[] removedStatusIds;
        public bool baseVulnerableBefore;
        public bool baseVulnerableAfter;
    }
}
