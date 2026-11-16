using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Prototype.BaseEconomy;
using Prototype.Combat;

namespace Prototype.Expedition
{
    public sealed partial class ExpeditionSliceSession
    {
        public ExpeditionSliceSnapshot GetSnapshot()
        {
            CombatExpeditionReport report = expedition == null ? null : expedition.GetReport();
            bool active = report != null && !report.finished;
            return new ExpeditionSliceSnapshot
            {
                phase = report == null ? "Camp" : active ? "Expedition" : "Completed",
                currentExpeditionId = report == null ? string.Empty : report.expeditionId,
                expeditionActive = active, canEditConfiguration = !active,
                preparation = MapPreparation(preparation.GetSnapshot()), partyState = CopySnapshot(partyState),
                experience = ExperienceTotals(), report = report, settlements = SettlementCopies(),
                lastCommand = CopyCommand(lastCommand), lastComparison = CopyComparison(lastComparison),
                lastVerification = verifications.Count == 0 ? null : CopyVerification(verifications[verifications.Count - 1])
            };
        }

        public ExpeditionSliceAcceptance GetAcceptance()
        {
            var verificationCopies = new VerificationBattleReport[verifications.Count];
            for (int i = 0; i < verificationCopies.Length; i++) verificationCopies[i] = CopyVerification(verifications[i]);
            return new ExpeditionSliceAcceptance
            {
                schemaVersion = 1, configuration = GetConfiguration(),
                combatConfiguration = CombatExpeditionSession.CopyConfig(frozenCombat ?? editableCombat),
                preparationConfiguration = GetPreparationConfiguration(), snapshot = GetSnapshot(),
                expeditions = expedition == null ? Array.Empty<CombatExpeditionReport>() : new[] { expedition.GetReport() },
                settlements = SettlementCopies(), ledger = LedgerCopies(), comparison = CopyComparison(lastComparison),
                verifications = verificationCopies, experience = ExperienceTotals(),
                reportText = ExportReportText(), ledgerText = ExportLedgerText(), ledgerCsv = ExportLedgerCsv()
            };
        }

        public string ExportLedgerText() { return preparation.ExportLedgerText(); }
        public string ExportLedgerCsv() { return preparation.ExportLedgerCsv(); }

        /// <summary>Only formats actual records. This method never runs battles or settles rewards.</summary>
        public string ExportReportText()
        {
            var text = new StringBuilder("# 出征与公共备战实跑记录\n\n");
            text.Append(initialConfiguration.parameterNotice).Append("\n\n");
            text.Append("生命周期：当前内存营地；Reset 全开局并清除去重；本轮没有跨进程存档。\n");
            if (expedition == null) text.Append("\n尚未创建真实出征，没有战斗或奖励记录。\n");
            else
            {
                CombatExpeditionReport actual = expedition.GetReport();
                text.Append('\n').Append(CombatSampleRuns.FormatExpeditionMarkdown(actual));
                foreach (CombatEncounterReport encounter in actual.encounters)
                {
                    text.Append("\n### 主线完整指令与候选原因：").Append(encounter.resultId).Append('\n');
                    AppendRun(text, new CombatRunResult
                    {
                        outcome = encounter.outcome, winnerTeam = encounter.winnerTeam, stopReason = encounter.stopReason,
                        time = encounter.battleEnd.time, executedActionCount = encounter.executedActionCount,
                        snapshot = encounter.battleEnd, steps = encounter.steps
                    });
                }
            }
            text.Append("\n## 公库与角色经验实际入账\n\n");
            foreach (ExpeditionSettlementReceipt receipt in SettlementCopies())
            {
                text.Append("settlementId=").Append(receipt.resultId).Append(" eligible=").Append(receipt.rewardsEligible)
                    .Append(" success=").Append(receipt.success).Append(" ledgerSequence=").Append(receipt.ledgerSequence)
                    .Append(" code=").Append(receipt.code).Append(" cash=").Append(receipt.cashReward).Append('\n');
                foreach (ItemQuantity item in receipt.items) text.Append("  公共物品 ").Append(item.itemId).Append(" × ").Append(item.quantity).Append('\n');
                foreach (CombatExperienceGrant grant in receipt.experience)
                    text.Append("  XP unitId=").Append(grant.unitId).Append(" +").Append(grant.amount)
                        .Append(" settlementId=").Append(receipt.resultId).Append(" posted=").Append(xpPosted.Contains(receipt.resultId)).Append('\n');
                text.Append("  ").Append(receipt.reason).Append('\n');
            }
            text.Append("\n累计角色经验：");
            foreach (CombatExperienceGrant grant in ExperienceTotals()) text.Append(grant.unitId).Append('=').Append(grant.amount).Append(' ');
            text.Append("\n\n## 采购与强化实际账本\n\n").Append(preparation.ExportLedgerText());
            if (lastComparison != null)
            {
                text.Append("\n## 已请求的同起点装备对照\n\n").Append(lastComparison.reason).Append('\n');
                AppendSnapshot(text, "捕获的主线末尾来源", lastComparison.sourceSnapshot);
                AppendSnapshot(text, "未强化分支实际开局", lastComparison.baselineStart);
                AppendSnapshot(text, "已强化分支实际开局", lastComparison.enhancedStart);
                text.Append(CombatSampleRuns.FormatComparisonMarkdown(new CombatComparisonReport
                {
                    id = "integrated-real-equipment-comparison", baselineLabel = "实际强化前", variantLabel = "实际强化后",
                    changedSetting = lastComparison.changedSetting, onlyDeclaredSettingChanged = lastComparison.onlyEquipmentChanged,
                    baselineConfiguration = lastComparison.baselineConfiguration, variantConfiguration = lastComparison.enhancedConfiguration,
                    baseline = lastComparison.baseline, variant = lastComparison.enhanced,
                    firstDivergence = lastComparison.firstDivergence,
                    observableImpact = "所有完整友方 HP/MP/状态/行动时间、指令、敌情与种子保持一致；仅真实武器攻击加成不同。"
                }));
                text.Append("\n### 未强化分支完整指令与候选原因\n");
                AppendRun(text, lastComparison.baseline);
                text.Append("\n### 已强化分支完整指令与候选原因\n");
                AppendRun(text, lastComparison.enhanced);
            }
            foreach (VerificationBattleReport verification in verifications)
            {
                text.Append("\n## 真实无奖励验证再战 ").Append(verification.battleId).Append("\n\nrewardsEligible=false\n")
                    .Append(verification.reason).Append('\n');
                AppendSnapshot(text, "前次真实队伍", verification.previousSnapshot);
                AppendSnapshot(text, "本次实际开局", verification.battleStart);
                AppendRun(text, verification.result);
            }
            AppendSnapshot(text, "当前真实队伍（只读）", partyState);
            return text.ToString();
        }

        private CombatExperienceGrant[] ExperienceTotals()
        {
            var grants = new List<CombatExperienceGrant>();
            foreach (CombatUnitConfig unit in initialCombat.encounters[0].scenario.units)
            {
                int amount;
                if (unit.team == initialCombat.carryTeam && experience.TryGetValue(unit.id, out amount))
                    grants.Add(new CombatExperienceGrant { unitId = unit.id, amount = amount });
            }
            return grants.ToArray();
        }

        private ExpeditionSettlementReceipt[] SettlementCopies()
        {
            var values = new List<ExpeditionSettlementReceipt>();
            if (expedition != null)
                foreach (CombatEncounterReport encounter in expedition.GetReport().encounters)
                {
                    ExpeditionSettlementReceipt receipt;
                    if (receipts.TryGetValue(encounter.resultId, out receipt)) values.Add(CopyReceipt(receipt));
                }
            return values.ToArray();
        }

        private PreparationLedgerDto[] LedgerCopies()
        {
            var ledger = preparation.Ledger;
            var values = new PreparationLedgerDto[ledger.Count];
            for (int i = 0; i < values.Length; i++)
            {
                PreparationLedgerEntry entry = ledger[i];
                var changes = new PreparationItemChangeDto[entry.ItemChanges.Count];
                for (int j = 0; j < changes.Length; j++)
                {
                    PreparationItemChange item = entry.ItemChanges[j];
                    changes[j] = new PreparationItemChangeDto { itemId = item.ItemId, fromOwnerId = item.FromOwnerId,
                        toOwnerId = item.ToOwnerId, quantity = item.Quantity, isConsumption = item.IsConsumption };
                }
                PreparationEquipmentChange change = entry.EquipmentChange;
                values[i] = new PreparationLedgerDto
                {
                    sequence = entry.Sequence, operationId = entry.OperationId, eventKind = entry.EventKind.ToString(),
                    eventName = entry.EventName, payerId = entry.PayerId, payeeId = entry.PayeeId, amount = entry.Amount,
                    itemChanges = changes, after = MapPreparation(entry.After),
                    equipmentChange = change == null ? null : new PreparationEquipmentChangeDto
                    {
                        weaponInstanceId = change.WeaponInstanceId, ownerId = change.OwnerId, equippedById = change.EquippedById,
                        enhancementLevelBefore = change.EnhancementLevelBefore, enhancementLevelAfter = change.EnhancementLevelAfter,
                        attackBonusBefore = change.AttackBonusBefore, attackBonusAfter = change.AttackBonusAfter
                    }
                };
            }
            return values;
        }

        private static PreparationStateDto MapPreparation(PreparationSnapshot state)
        {
            return new PreparationStateDto
            {
                scenarioId = state.ScenarioId, publicOwnerId = state.PublicOwnerId, residentId = state.ResidentId,
                combatantId = state.CombatantId, weaponInstanceId = state.WeaponInstanceId, weaponOwnerId = state.WeaponOwnerId,
                treasuryCash = state.TreasuryCash, residentCash = state.ResidentCash, totalCash = state.TotalCash,
                publicOre = state.PublicOre, publicAuxiliary = state.PublicAuxiliary, residentAuxiliary = state.ResidentAuxiliary,
                weaponEnhancementLevel = state.WeaponEnhancementLevel, weaponAttackBonus = state.WeaponAttackBonus,
                oreConsumed = state.OreConsumed, auxiliaryConsumed = state.AuxiliaryConsumed,
                hasEnhanced = state.HasEnhanced, ledgerCount = state.LedgerCount
            };
        }

        private static ExpeditionSliceConfig CopyConfiguration(ExpeditionSliceConfig source)
        {
            var items = new ExpeditionItemMapping[source.itemMappings.Length];
            for (int i = 0; i < items.Length; i++) items[i] = new ExpeditionItemMapping
                { combatItemId = source.itemMappings[i].combatItemId, preparationItemId = source.itemMappings[i].preparationItemId };
            var equipment = new ExpeditionEquipmentMapping[source.equipmentMappings.Length];
            for (int i = 0; i < equipment.Length; i++) equipment[i] = new ExpeditionEquipmentMapping
                { preparationCombatantId = source.equipmentMappings[i].preparationCombatantId,
                    combatUnitId = source.equipmentMappings[i].combatUnitId, weaponInstanceId = source.equipmentMappings[i].weaponInstanceId };
            var overrides = new ExpeditionEnemyHpOverride[source.comparison.enemyHpOverrides.Length];
            for (int i = 0; i < overrides.Length; i++) overrides[i] = new ExpeditionEnemyHpOverride
                { unitId = source.comparison.enemyHpOverrides[i].unitId, hp = source.comparison.enemyHpOverrides[i].hp };
            return new ExpeditionSliceConfig
            {
                schemaVersion = source.schemaVersion, id = source.id, parameterNotice = source.parameterNotice,
                combatConfigPath = source.combatConfigPath, preparationConfigPath = source.preparationConfigPath,
                itemMappings = items, equipmentMappings = equipment,
                comparison = new ExpeditionComparisonConfig { templateEncounterIndex = source.comparison.templateEncounterIndex,
                    betweenWait = source.comparison.betweenWait, enemyHpOverrides = overrides }
            };
        }

        private static SliceCommandResult CopyCommand(SliceCommandResult value)
        {
            return value == null ? null : Command(value.success, value.isReplay, value.code, value.reason, value.operationId, value.ledgerSequence);
        }

        private static ExpeditionSettlementReceipt CopyReceipt(ExpeditionSettlementReceipt source)
        {
            var items = new ItemQuantity[source.items.Length];
            for (int i = 0; i < items.Length; i++) items[i] = source.items[i].Clone();
            return new ExpeditionSettlementReceipt
            {
                resultId = source.resultId, expeditionId = source.expeditionId, encounterId = source.encounterId,
                rewardsEligible = source.rewardsEligible, success = source.success, isReplay = source.isReplay,
                code = source.code, reason = source.reason, ledgerSequence = source.ledgerSequence,
                cashReward = source.cashReward, items = items, experience = CopyExperience(source.experience)
            };
        }

        private static CombatSnapshot CopySnapshot(CombatSnapshot source) { return source == null ? null : CombatSession.CopySnapshot(source); }
        private static CombatScenario CopyScenario(CombatScenario source) { return source == null ? null : CombatSession.CopyScenario(source); }
        private static CombatStepResult CopyStep(CombatStepResult source) { return source == null ? null : CombatSession.CopyStep(source); }
        private static CombatExperienceGrant[] CopyExperience(CombatExperienceGrant[] source)
        {
            if (source == null) return Array.Empty<CombatExperienceGrant>();
            var values = new CombatExperienceGrant[source.Length];
            for (int i = 0; i < values.Length; i++) values[i] = new CombatExperienceGrant { unitId = source[i].unitId, amount = source[i].amount };
            return values;
        }

        private static CombatStepResult[] CopySteps(CombatStepResult[] source)
        {
            var values = new CombatStepResult[source.Length];
            for (int i = 0; i < values.Length; i++) values[i] = CombatSession.CopyStep(source[i]);
            return values;
        }

        private static CombatRunResult CopyRun(CombatRunResult source)
        {
            return source == null ? null : new CombatRunResult
            {
                outcome = source.outcome, winnerTeam = source.winnerTeam, stopReason = source.stopReason,
                time = source.time, executedActionCount = source.executedActionCount,
                snapshot = CopySnapshot(source.snapshot), steps = CopySteps(source.steps)
            };
        }

        private static CombatEncounterReport CopyEncounter(CombatEncounterReport source)
        {
            var loot = new CombatLootStack[source.publicDrops.Length];
            for (int i = 0; i < loot.Length; i++) loot[i] = new CombatLootStack { itemId = source.publicDrops[i].itemId, quantity = source.publicDrops[i].quantity };
            return new CombatEncounterReport
            {
                resultId = source.resultId, expeditionId = source.expeditionId, encounterId = source.encounterId,
                scenarioId = source.scenarioId, encounterIndex = source.encounterIndex, betweenBattleWait = source.betweenBattleWait,
                previousBattleEnd = CopySnapshot(source.previousBattleEnd), afterIntermission = CopySnapshot(source.afterIntermission),
                battleStart = CopySnapshot(source.battleStart), battleEnd = CopySnapshot(source.battleEnd),
                outcome = source.outcome, winnerTeam = source.winnerTeam, stopReason = source.stopReason,
                executedActionCount = source.executedActionCount, steps = CopySteps(source.steps),
                rewardsEligible = source.rewardsEligible, experience = CopyExperience(source.experience), publicCash = source.publicCash, publicDrops = loot
            };
        }

        private static CombatFirstDivergence CopyDivergence(CombatFirstDivergence source)
        {
            return source == null ? null : new CombatFirstDivergence { recordIndex = source.recordIndex,
                baseline = CopyStep(source.baseline), variant = CopyStep(source.variant), description = source.description };
        }

        private static EquipmentComparisonReport CopyComparison(EquipmentComparisonReport source)
        {
            return source == null ? null : new EquipmentComparisonReport
            {
                success = source.success, code = source.code, reason = source.reason, changedSetting = source.changedSetting,
                sourceSnapshot = CopySnapshot(source.sourceSnapshot), baselineConfiguration = CopyScenario(source.baselineConfiguration),
                enhancedConfiguration = CopyScenario(source.enhancedConfiguration), baselineStart = CopySnapshot(source.baselineStart),
                enhancedStart = CopySnapshot(source.enhancedStart), baseline = CopyRun(source.baseline), enhanced = CopyRun(source.enhanced),
                firstDivergence = CopyDivergence(source.firstDivergence), baselineEquipmentBonus = source.baselineEquipmentBonus,
                enhancedEquipmentBonus = source.enhancedEquipmentBonus, onlyEquipmentChanged = source.onlyEquipmentChanged,
                equipmentSourceId = source.equipmentSourceId, combatUnitId = source.combatUnitId
            };
        }

        private static VerificationBattleReport CopyVerification(VerificationBattleReport source)
        {
            return source == null ? null : new VerificationBattleReport
            {
                success = source.success, code = source.code, reason = source.reason, rewardsEligible = false,
                battleId = source.battleId, configuration = CopyScenario(source.configuration),
                previousSnapshot = CopySnapshot(source.previousSnapshot), battleStart = CopySnapshot(source.battleStart), result = CopyRun(source.result)
            };
        }

        private static CombatFirstDivergence FindDivergence(CombatRunResult baseline, CombatRunResult enhanced)
        {
            for (int i = 0; i < Math.Max(baseline.steps.Length, enhanced.steps.Length); i++)
            {
                CombatStepResult left = i < baseline.steps.Length ? baseline.steps[i] : null;
                CombatStepResult right = i < enhanced.steps.Length ? enhanced.steps[i] : null;
                if (DecisionsEquivalent(left, right)) continue;
                return new CombatFirstDivergence { recordIndex = i + 1, baseline = CopyStep(left), variant = CopyStep(right),
                    description = "第一处实际行动/效果分歧：WAIT " + Number(left == null ? right.time : left.time) +
                        "；未强化 " + (left == null ? "无事件" : left.actorId + "→" + left.actionId + "→" + left.targetId) +
                        "；已强化 " + (right == null ? "无事件" : right.actorId + "→" + right.actionId + "→" + right.targetId) };
            }
            return null;
        }

        private static bool DecisionsEquivalent(CombatStepResult left, CombatStepResult right)
        {
            if (left == null || right == null) return left == right;
            if (left.time != right.time || left.status != right.status || left.actorId != right.actorId || left.actionId != right.actionId ||
                left.targetId != right.targetId || left.mpAfter != right.mpAfter || left.effects.Length != right.effects.Length) return false;
            for (int i = 0; i < left.effects.Length; i++)
            {
                CombatEffect a = left.effects[i]; CombatEffect b = right.effects[i];
                if (a.kind != b.kind || a.targetId != b.targetId || a.hpBefore != b.hpBefore || a.hpAfter != b.hpAfter || a.hpDelta != b.hpDelta) return false;
            }
            return true;
        }

        private static string Number(double value) { return value.ToString("0.###############", CultureInfo.InvariantCulture); }
        private static void AppendSnapshot(StringBuilder text, string label, CombatSnapshot snapshot)
        {
            if (snapshot == null) return;
            text.Append("\n").Append(label).Append(" WAIT=").Append(Number(snapshot.time)).Append('\n');
            foreach (CombatUnitSnapshot unit in snapshot.units)
            {
                text.Append("  ").Append(unit.id).Append(" HP=").Append(unit.hp).Append('/').Append(unit.maxHp)
                    .Append(" MP=").Append(unit.mp).Append('/').Append(unit.maxMp).Append(" next=").Append(Number(unit.nextActionTime))
                    .Append(" pos=(").Append(unit.row).Append(',').Append(unit.column).Append(") equipment=")
                    .Append(unit.equipmentSourceId).Append(" bonus=").Append(unit.equipmentDamageBonus).Append('\n');
                foreach (CombatStatusConfig status in unit.statuses ?? Array.Empty<CombatStatusConfig>())
                    text.Append("    status=").Append(status.id).Append('/').Append(status.kind).Append(" applied=")
                        .Append(Number(status.appliedAt)).Append(" expires=").Append(Number(status.expiresAt)).Append('\n');
            }
        }

        private static void AppendRun(StringBuilder text, CombatRunResult run)
        {
            text.Append("\noutcome=").Append(run.outcome).Append(" stopReason=").Append(run.stopReason)
                .Append(" time=").Append(Number(run.time)).Append('\n');
            foreach (CombatStepResult step in run.steps)
            {
                text.Append("#").Append(step.index).Append(" t=").Append(Number(step.time)).Append(' ').Append(step.status)
                    .Append(" actor=").Append(step.actorId).Append(" action=").Append(step.actionId).Append(" target=").Append(step.targetId)
                    .Append(" equipmentBonus=").Append(step.equipmentDamageBonus).Append(" MP=").Append(step.mpBefore).Append("->").Append(step.mpAfter)
                    .Append(" next=").Append(Number(step.nextActionTime)).Append(" | ").Append(step.message).Append('\n');
                if (!string.IsNullOrEmpty(step.tieBreakReason))
                    text.Append("  同刻裁决 timeTieResolved=").Append(step.timeTieResolved).Append(" | ").Append(step.tieBreakReason).Append('\n');
                if (step.status == "Executed")
                    text.Append("  WAIT=").Append(Number(step.baseWait)).Append('*').Append(Number(step.agilityFactor))
                        .Append('=').Append(Number(step.actualWait)).Append('\n');
                foreach (CombatInstructionAttempt attempt in step.attempts)
                {
                    text.Append("  rule[").Append(attempt.instructionIndex).Append("] ").Append(attempt.display).Append(" | ")
                        .Append(attempt.outcome).Append(" | ").Append(attempt.reason).Append('\n');
                    foreach (CombatCandidateTrace candidate in attempt.candidates)
                        text.Append("    ").Append(candidate.unitId).Append(" condition=").Append(candidate.passesCondition)
                            .Append(" eligible=").Append(candidate.eligible).Append(" | ").Append(candidate.reason).Append('\n');
                }
                foreach (CombatEffect effect in step.effects) text.Append("  effect: ").Append(effect.description).Append('\n');
            }
            AppendSnapshot(text, "真实战后", run.snapshot);
        }
    }
}
