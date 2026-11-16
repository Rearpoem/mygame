using System;
using Prototype.BaseEconomy;
using Prototype.Combat;

namespace Prototype.Expedition
{
    [Serializable]
    public sealed class ExpeditionSliceConfig
    {
        public int schemaVersion;
        public string id;
        public string parameterNotice;
        public string combatConfigPath;
        public string preparationConfigPath;
        public ExpeditionItemMapping[] itemMappings;
        public ExpeditionEquipmentMapping[] equipmentMappings;
        public ExpeditionComparisonConfig comparison;
    }

    [Serializable]
    public sealed class ExpeditionItemMapping
    {
        public string combatItemId;
        public string preparationItemId;
    }

    [Serializable]
    public sealed class ExpeditionEquipmentMapping
    {
        public string preparationCombatantId;
        public string combatUnitId;
        public string weaponInstanceId;
    }

    [Serializable]
    public sealed class ExpeditionComparisonConfig
    {
        public int templateEncounterIndex;
        public double betweenWait;
        public ExpeditionEnemyHpOverride[] enemyHpOverrides;
    }

    [Serializable]
    public sealed class ExpeditionEnemyHpOverride
    {
        public string unitId;
        public int hp;
    }

    [Serializable]
    public sealed class SliceCommandResult
    {
        public bool success;
        public bool isReplay;
        public string code;
        public string reason;
        public string operationId;
        public int ledgerSequence;
    }

    [Serializable]
    public sealed class ExpeditionAdvanceResult
    {
        public bool success;
        public string code;
        public string reason;
        public CombatEncounterReport[] encounters;
        public ExpeditionSettlementReceipt[] settlements;
        public CombatExpeditionReport report;
    }

    [Serializable]
    public sealed class ExpeditionSettlementReceipt
    {
        public string resultId;
        public string expeditionId;
        public string encounterId;
        public bool rewardsEligible;
        public bool success;
        public bool isReplay;
        public string code;
        public string reason;
        public int ledgerSequence;
        public int cashReward;
        public ItemQuantity[] items;
        public CombatExperienceGrant[] experience;
    }

    // Public fields explicitly adapt the base module's immutable property snapshots for JsonUtility.
    [Serializable]
    public sealed class PreparationStateDto
    {
        public string scenarioId;
        public string publicOwnerId;
        public string residentId;
        public string combatantId;
        public string weaponInstanceId;
        public string weaponOwnerId;
        public int treasuryCash;
        public int residentCash;
        public long totalCash;
        public int publicOre;
        public int publicAuxiliary;
        public int residentAuxiliary;
        public int weaponEnhancementLevel;
        public int weaponAttackBonus;
        public int oreConsumed;
        public int auxiliaryConsumed;
        public bool hasEnhanced;
        public int ledgerCount;
    }

    [Serializable]
    public sealed class PreparationItemChangeDto
    {
        public string itemId;
        public string fromOwnerId;
        public string toOwnerId;
        public int quantity;
        public bool isConsumption;
    }

    [Serializable]
    public sealed class PreparationEquipmentChangeDto
    {
        public string weaponInstanceId;
        public string ownerId;
        public string equippedById;
        public int enhancementLevelBefore;
        public int enhancementLevelAfter;
        public int attackBonusBefore;
        public int attackBonusAfter;
    }

    [Serializable]
    public sealed class PreparationLedgerDto
    {
        public int sequence;
        public string operationId;
        public string eventKind;
        public string eventName;
        public string payerId;
        public string payeeId;
        public int amount;
        public PreparationItemChangeDto[] itemChanges;
        public PreparationEquipmentChangeDto equipmentChange;
        public PreparationStateDto after;
    }

    [Serializable]
    public sealed class EquipmentComparisonReport
    {
        public bool success;
        public string code;
        public string reason;
        public string changedSetting;
        public CombatSnapshot sourceSnapshot;
        public CombatScenario baselineConfiguration;
        public CombatScenario enhancedConfiguration;
        public CombatSnapshot baselineStart;
        public CombatSnapshot enhancedStart;
        public CombatRunResult baseline;
        public CombatRunResult enhanced;
        public CombatFirstDivergence firstDivergence;
        public int baselineEquipmentBonus;
        public int enhancedEquipmentBonus;
        public bool onlyEquipmentChanged;
        public string equipmentSourceId;
        public string combatUnitId;
    }

    [Serializable]
    public sealed class VerificationBattleReport
    {
        public bool success;
        public string code;
        public string reason;
        public bool rewardsEligible;
        public string battleId;
        public CombatScenario configuration;
        public CombatSnapshot previousSnapshot;
        public CombatSnapshot battleStart;
        public CombatRunResult result;
    }

    [Serializable]
    public sealed class ExpeditionSliceSnapshot
    {
        public string phase;
        public string currentExpeditionId;
        public bool expeditionActive;
        public bool canEditConfiguration;
        public PreparationStateDto preparation;
        public CombatSnapshot partyState;
        public CombatExperienceGrant[] experience;
        public CombatExpeditionReport report;
        public ExpeditionSettlementReceipt[] settlements;
        public SliceCommandResult lastCommand;
        public EquipmentComparisonReport lastComparison;
        public VerificationBattleReport lastVerification;
    }

    [Serializable]
    public sealed class ExpeditionSliceAcceptance
    {
        public int schemaVersion;
        public ExpeditionSliceConfig configuration;
        public CombatExpeditionConfig combatConfiguration;
        public PreparationScenario preparationConfiguration;
        public ExpeditionSliceSnapshot snapshot;
        public CombatExpeditionReport[] expeditions;
        public ExpeditionSettlementReceipt[] settlements;
        public PreparationLedgerDto[] ledger;
        public EquipmentComparisonReport comparison;
        public VerificationBattleReport[] verifications;
        public CombatExperienceGrant[] experience;
        public string reportText;
        public string ledgerText;
        public string ledgerCsv;
    }
}
