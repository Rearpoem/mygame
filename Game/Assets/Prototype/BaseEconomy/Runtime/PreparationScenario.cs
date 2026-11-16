using System;
using System.Collections.Generic;
using System.Globalization;

namespace Prototype.BaseEconomy
{
    [Serializable]
    public sealed class ItemQuantity
    {
        public string itemId;
        public int quantity;

        public ItemQuantity Clone() { return (ItemQuantity)MemberwiseClone(); }
    }

    /// <summary>Local input DTO. The caller determines reward eligibility and stable identifiers.</summary>
    [Serializable]
    public sealed class ExpeditionDrop
    {
        public string settlementId;
        public string expeditionId;
        public string encounterId;
        public int cashReward;
        public ItemQuantity[] items;

        public ExpeditionDrop Clone()
        {
            ExpeditionDrop copy = (ExpeditionDrop)MemberwiseClone();
            if (items != null)
            {
                copy.items = new ItemQuantity[items.Length];
                for (int i = 0; i < items.Length; i++)
                    copy.items[i] = items[i] == null ? null : items[i].Clone();
            }
            return copy;
        }
    }

    /// <summary>Temporary second-slice values; JSON is loaded by the caller, without a Unity dependency.</summary>
    [Serializable]
    public sealed class PreparationScenario
    {
        public int schemaVersion;
        public string scenarioId;
        public string testValueNotice;
        public string publicOwnerId;
        public string publicOwnerName;
        public string residentId;
        public string residentName;
        public string combatantId;
        public string combatantName;
        public string weaponInstanceId;
        public string weaponName;
        public string oreItemId;
        public string oreItemName;
        public string auxiliaryItemId;
        public string auxiliaryItemName;
        public int initialTreasuryCash;
        public int initialResidentCash;
        public int initialPublicOre;
        public int initialPublicAuxiliary;
        public int initialResidentAuxiliary;
        public int initialWeaponEnhancementLevel;
        public int initialWeaponAttackBonus;
        public int auxiliaryUnitPrice;
        public int purchaseQuantity;
        public int oreRequired;
        public int auxiliaryRequired;
        public int enhancementAttackBonus;
        public ExpeditionDrop presetExpeditionDrop;

        public PreparationScenario Clone()
        {
            PreparationScenario copy = (PreparationScenario)MemberwiseClone();
            copy.presetExpeditionDrop = presetExpeditionDrop == null ? null : presetExpeditionDrop.Clone();
            return copy;
        }

        public void Validate()
        {
            if (schemaVersion != 1) throw new ArgumentException("Unsupported preparation schemaVersion.");
            RequireText(scenarioId, "scenarioId");
            RequireText(testValueNotice, "testValueNotice");
            RequireText(publicOwnerId, "publicOwnerId");
            RequireText(publicOwnerName, "publicOwnerName");
            RequireText(residentId, "residentId");
            RequireText(residentName, "residentName");
            RequireText(combatantId, "combatantId");
            RequireText(combatantName, "combatantName");
            RequireText(weaponInstanceId, "weaponInstanceId");
            RequireText(weaponName, "weaponName");
            RequireText(oreItemId, "oreItemId");
            RequireText(oreItemName, "oreItemName");
            RequireText(auxiliaryItemId, "auxiliaryItemId");
            RequireText(auxiliaryItemName, "auxiliaryItemName");
            if (publicOwnerId == residentId || publicOwnerId == combatantId || residentId == combatantId)
                throw new ArgumentException("Public owner, private supplier and public combatant must have different identifiers.");
            if (oreItemId == auxiliaryItemId)
                throw new ArgumentException("Ore and auxiliary item identifiers must differ.");
            RequireNonnegative(initialTreasuryCash, "initialTreasuryCash");
            RequireNonnegative(initialResidentCash, "initialResidentCash");
            RequireNonnegative(initialPublicOre, "initialPublicOre");
            RequireNonnegative(initialPublicAuxiliary, "initialPublicAuxiliary");
            RequireNonnegative(initialResidentAuxiliary, "initialResidentAuxiliary");
            RequireNonnegative(initialWeaponEnhancementLevel, "initialWeaponEnhancementLevel");
            RequireNonnegative(initialWeaponAttackBonus, "initialWeaponAttackBonus");
            RequireNonnegative(auxiliaryUnitPrice, "auxiliaryUnitPrice");
            RequirePositive(purchaseQuantity, "purchaseQuantity");
            RequirePositive(oreRequired, "oreRequired");
            RequirePositive(auxiliaryRequired, "auxiliaryRequired");
            RequirePositive(enhancementAttackBonus, "enhancementAttackBonus");
            int ore, auxiliary;
            string fingerprint, reason;
            if (!PreparationDropValidation.TryNormalize(this, presetExpeditionDrop,
                out ore, out auxiliary, out fingerprint, out reason))
                throw new ArgumentException("Invalid presetExpeditionDrop: " + reason);
        }

        private static void RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(name + " must not be empty.");
        }
        private static void RequireNonnegative(int value, string name)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(name, "Value must be nonnegative.");
        }
        private static void RequirePositive(int value, string name)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(name, "Value must be positive.");
        }
    }

    internal static class PreparationDropValidation
    {
        public static bool TryNormalize(PreparationScenario scenario, ExpeditionDrop drop,
            out int ore, out int auxiliary, out string fingerprint, out string reason)
        {
            ore = 0;
            auxiliary = 0;
            fingerprint = null;
            reason = null;
            if (drop == null) { reason = "Drop input must not be null."; return false; }
            if (string.IsNullOrWhiteSpace(drop.settlementId) || string.IsNullOrWhiteSpace(drop.expeditionId) ||
                string.IsNullOrWhiteSpace(drop.encounterId))
            { reason = "Drop needs nonempty settlementId, expeditionId and encounterId."; return false; }
            if (drop.cashReward < 0) { reason = "Drop cashReward must be nonnegative."; return false; }
            if (drop.items == null) { reason = "Drop items must be an explicit array (empty is allowed)."; return false; }
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemQuantity item in drop.items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.itemId) || item.quantity <= 0)
                { reason = "Each drop item needs a known identifier and positive quantity."; return false; }
                if (!seen.Add(item.itemId)) { reason = "Duplicate drop item identifier: " + item.itemId; return false; }
                if (item.itemId == scenario.oreItemId) ore = item.quantity;
                else if (item.itemId == scenario.auxiliaryItemId) auxiliary = item.quantity;
                else { reason = "Unsupported drop item identifier: " + item.itemId; return false; }
            }
            fingerprint = Encode(drop.expeditionId) + Encode(drop.encounterId) +
                drop.cashReward.ToString(CultureInfo.InvariantCulture) + "|" +
                ore.ToString(CultureInfo.InvariantCulture) + "|" + auxiliary.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private static string Encode(string value)
        {
            return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
        }
    }
}
