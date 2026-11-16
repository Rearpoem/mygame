using System;
using System.Collections.Generic;
using Prototype.BaseEconomy;
using Prototype.Combat;

namespace Prototype.Expedition
{
    /// <summary>
    /// One camp experiment, one reward-bearing expedition, and repeatable reward-free verification battles.
    /// No Unity, file IO, save persistence or second combat/economy implementation.
    /// </summary>
    public sealed partial class ExpeditionSliceSession
    {
        private readonly ExpeditionSliceConfig initialConfiguration;
        private readonly CombatExpeditionConfig initialCombat;
        private readonly PreparationScenario initialPreparation;
        private readonly PreparationSession preparation;
        private CombatExpeditionConfig editableCombat;
        private CombatExpeditionConfig frozenCombat;
        private CombatExpeditionSession expedition;
        private CombatSnapshot partyState;
        private CombatSnapshot comparisonOrigin;
        private CombatScenario comparisonTemplate;
        private PreparationSnapshot beforeEnhancement;
        private EquipmentComparisonReport lastComparison;
        private SliceCommandResult lastCommand;
        private readonly List<VerificationBattleReport> verifications = new List<VerificationBattleReport>();
        private readonly Dictionary<string, CombatEncounterReport> knownResults = new Dictionary<string, CombatEncounterReport>(StringComparer.Ordinal);
        private readonly Dictionary<string, ExpeditionSettlementReceipt> receipts = new Dictionary<string, ExpeditionSettlementReceipt>(StringComparer.Ordinal);
        private readonly HashSet<string> xpPosted = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> experience = new Dictionary<string, int>(StringComparer.Ordinal);

        public ExpeditionSliceSession(ExpeditionSliceConfig configuration, CombatExpeditionConfig combatConfiguration,
            PreparationScenario preparationConfiguration)
        {
            Validate(configuration, combatConfiguration, preparationConfiguration);
            initialConfiguration = CopyConfiguration(configuration);
            initialCombat = CombatExpeditionSession.CopyConfig(combatConfiguration);
            initialPreparation = preparationConfiguration.Clone();
            preparation = new PreparationSession(initialPreparation);
            Reset();
        }

        public ExpeditionSliceConfig GetConfiguration() { return CopyConfiguration(initialConfiguration); }
        public CombatExpeditionConfig GetCombatConfiguration() { return CombatExpeditionSession.CopyConfig(editableCombat); }
        public PreparationScenario GetPreparationConfiguration() { return initialPreparation.Clone(); }

        /// <summary>Explicit full experiment reset, including edited rules, XP and all idempotency records.</summary>
        public void Reset()
        {
            preparation.Reset();
            editableCombat = CombatExpeditionSession.CopyConfig(initialCombat);
            frozenCombat = null;
            expedition = null;
            comparisonOrigin = null;
            comparisonTemplate = null;
            beforeEnhancement = null;
            lastComparison = null;
            verifications.Clear();
            knownResults.Clear();
            receipts.Clear();
            xpPosted.Clear();
            experience.Clear();
            CombatScenario preview = CombatSession.CopyScenario(editableCombat.encounters[0].scenario);
            ApplyEquipment(preview, preparation.GetSnapshot());
            partyState = new CombatSession(preview).GetSnapshot();
            lastCommand = Command(true, false, "Reset", "已恢复全部加载开局，清空指令编辑、报告、账本、经验与去重记录。", string.Empty, 0);
        }

        public SliceCommandResult StartExpedition()
        {
            if (expedition != null)
                return Remember(Command(false, false, "ExistingExpedition", "本轮营地已有出征；请继续推进或用当前队伍进行无奖励验证再战。全开局 Reset 后才能新出征。", string.Empty, 0));
            CombatExpeditionConfig candidate = CombatExpeditionSession.CopyConfig(editableCombat);
            candidate.id = Guid.NewGuid().ToString("D");
            PreparationSnapshot equipment = preparation.GetSnapshot();
            foreach (CombatEncounterConfig encounter in candidate.encounters) ApplyEquipment(encounter.scenario, equipment);
            try
            {
                var started = new CombatExpeditionSession(candidate);
                frozenCombat = candidate;
                expedition = started;
                return Remember(Command(true, false, "Started", "已创建唯一出征 ID 并冻结本次全部场景、指令、种子与装备。", candidate.id, 0));
            }
            catch (ArgumentException exception)
            {
                return Remember(Command(false, false, "InvalidExpedition", exception.Message, candidate.id, 0));
            }
        }

        public ExpeditionAdvanceResult RunNextEncounter()
        {
            if (expedition == null) return AdvanceFailure("StartRequired", "请先创建本次出征。");
            if (expedition.GetReport().finished) return AdvanceFailure("ExpeditionFinished", "出征已经结束；查看报告不会重打或重新结算。");
            CombatEncounterReport encounter = expedition.RunNextEncounter();
            if (encounter == null) return AdvanceFailure("ExpeditionFinished", "没有待推进的场次。");
            knownResults.Add(encounter.resultId, CopyEncounter(encounter));
            partyState = CombatSession.CopySnapshot(encounter.battleEnd);
            ExpeditionSettlementReceipt receipt = Settle(encounter);
            CaptureComparisonOrigin();
            CombatExpeditionReport report = expedition.GetReport();
            string code = receipt.success ? (report.finished ? "Finished" : "EncounterCompleted") : "SettlementPending";
            string reason = receipt.success ? "已使用战斗出征核心推进并处理本场收益；" + report.stopReason : receipt.reason;
            Remember(Command(receipt.success, false, code, reason, encounter.resultId, receipt.ledgerSequence));
            return new ExpeditionAdvanceResult
            {
                success = receipt.success, code = code, reason = reason,
                encounters = new[] { CopyEncounter(encounter) }, settlements = new[] { CopyReceipt(receipt) }, report = report
            };
        }

        public ExpeditionAdvanceResult RunToEnd()
        {
            if (expedition == null) return AdvanceFailure("StartRequired", "请先创建本次出征。");
            if (expedition.GetReport().finished)
            {
                bool pending = HasPendingSettlement();
                return new ExpeditionAdvanceResult { success = !pending,
                    code = pending ? "SettlementPending" : "AlreadyFinished",
                    reason = pending ? "已有获奖场次尚未入账；请按 resultId 重试结算。没有再次战斗或发奖。" : "只返回已有结果，没有再次战斗或发奖。",
                    encounters = Array.Empty<CombatEncounterReport>(), settlements = Array.Empty<ExpeditionSettlementReceipt>(), report = expedition.GetReport() };
            }
            var advanced = new List<CombatEncounterReport>();
            var posted = new List<ExpeditionSettlementReceipt>();
            bool success = true;
            while (!expedition.GetReport().finished)
            {
                ExpeditionAdvanceResult step = RunNextEncounter();
                advanced.AddRange(step.encounters);
                posted.AddRange(step.settlements);
                success &= step.success;
            }
            string code = success ? "Finished" : "SettlementPending";
            string reason = success ? "一键派遣与逐场推进共用同一个 CombatExpeditionSession 及结算路径。" : "战斗已完成，部分收益尚未入账；可按 resultId 重试。";
            Remember(Command(success, false, code, reason, frozenCombat.id, 0));
            return new ExpeditionAdvanceResult { success = success, code = code, reason = reason,
                encounters = advanced.ToArray(), settlements = posted.ToArray(), report = expedition.GetReport() };
        }

        public SliceCommandResult RetrySettlement(string resultId)
        {
            CombatEncounterReport encounter;
            if (resultId == null || !knownResults.TryGetValue(resultId, out encounter))
                return Remember(Command(false, false, "UnknownResult", "找不到当前营地实际执行的战斗结果；不能提交外部伪造战报。", resultId, 0));
            if (!encounter.rewardsEligible)
                return Remember(Command(false, false, "RewardsIneligible", "该结局没有奖励资格。", resultId, 0));
            ExpeditionSettlementReceipt receipt = Settle(encounter);
            return Remember(Command(receipt.success, receipt.isReplay, receipt.code, receipt.reason, resultId, receipt.ledgerSequence));
        }

        public SliceCommandResult PurchaseAuxiliary(string commandId, int quantity)
        {
            return Remember(Adapt(preparation.PurchaseAuxiliary(commandId, quantity), commandId));
        }

        public SliceCommandResult EnhanceEquippedWeapon(string commandId)
        {
            PreparationSnapshot before = preparation.GetSnapshot();
            PreparationCommandResult result = preparation.EnhanceEquippedWeapon(commandId);
            if (result.Success && !result.IsReplay) beforeEnhancement = before;
            return Remember(Adapt(result, commandId));
        }

        public CombatEditResult MoveInstruction(string unitId, int fromIndex, int toIndex)
        {
            return EditConfiguration(unitId, scenario => new CombatSession(scenario).MoveInstruction(unitId, fromIndex, toIndex),
                (scenario, session) => session.MoveInstruction(unitId, fromIndex, toIndex));
        }

        public CombatEditResult SetConditionThreshold(string unitId, int instructionIndex, int termIndex, double value)
        {
            return EditConfiguration(unitId, scenario => new CombatSession(scenario).SetConditionThreshold(unitId, instructionIndex, termIndex, value),
                (scenario, session) => session.SetConditionThreshold(unitId, instructionIndex, termIndex, value));
        }

        private CombatEditResult EditConfiguration(string unitId, Func<CombatScenario, CombatEditResult> preflight,
            Func<CombatScenario, CombatSession, CombatEditResult> edit)
        {
            if (IsActive()) return new CombatEditResult { success = false, message = "当前出征配置已冻结；结束后可修改后续验证战指令。" };
            foreach (CombatEncounterConfig encounter in editableCombat.encounters)
            {
                CombatEditResult result = preflight(encounter.scenario);
                if (!result.success) return result;
            }
            foreach (CombatEncounterConfig encounter in editableCombat.encounters)
            {
                var session = new CombatSession(encounter.scenario);
                edit(encounter.scenario, session);
                encounter.scenario = session.GetScenario();
            }
            if (expedition == null)
            {
                CombatScenario preview = CombatSession.CopyScenario(editableCombat.encounters[0].scenario);
                ApplyEquipment(preview, preparation.GetSnapshot());
                partyState = new CombatSession(preview).GetSnapshot();
            }
            return new CombatEditResult { success = true, message = "已对所有场景及后续真实验证战应用同一指令编辑；已有出征与捕获对照基准保持原配置。" };
        }

        public EquipmentComparisonReport CompareEquipmentFromCurrentState()
        {
            if (comparisonOrigin == null) return ComparisonFailure("CompletedExpeditionRequired", "请先完成主线全部胜利场次，捕获真实战后对照起点。");
            if (beforeEnhancement == null) return ComparisonFailure("EnhancementRequired", "请先实际采购并成功强化；对照不会自行制造 +4 装备。" );
            CombatScenario baselineConfig = CombatSession.CopyScenario(comparisonTemplate);
            CombatScenario enhancedConfig = CombatSession.CopyScenario(comparisonTemplate);
            ApplyEquipment(baselineConfig, beforeEnhancement);
            PreparationSnapshot currentEquipment = preparation.GetSnapshot();
            ApplyEquipment(enhancedConfig, currentEquipment);
            var baselineSession = new CombatSession(baselineConfig, comparisonOrigin, frozenCombat.carryTeam, initialConfiguration.comparison.betweenWait);
            var enhancedSession = new CombatSession(enhancedConfig, comparisonOrigin, frozenCombat.carryTeam, initialConfiguration.comparison.betweenWait);
            CombatSnapshot baselineStart = baselineSession.GetSnapshot();
            CombatSnapshot enhancedStart = enhancedSession.GetSnapshot();
            CombatRunResult baseline = baselineSession.RunToCompletion(frozenCombat.maxStepsPerBattle, frozenCombat.maxWaitPerBattle);
            CombatRunResult enhanced = enhancedSession.RunToCompletion(frozenCombat.maxStepsPerBattle, frozenCombat.maxWaitPerBattle);
            ExpeditionEquipmentMapping mapping = initialConfiguration.equipmentMappings[0];
            lastComparison = new EquipmentComparisonReport
            {
                success = true, code = "Compared", reason = "从主线结束时捕获的同一完整队伍起点比较；没有修改当前真实队伍、库存或经验。",
                changedSetting = "仅 " + mapping.combatUnitId + ".equipmentDamageBonus 从真实强化前值变为强化后值；装备实例、全部指令、敌情和种子相同。",
                sourceSnapshot = CombatSession.CopySnapshot(comparisonOrigin), baselineConfiguration = baselineConfig,
                enhancedConfiguration = enhancedConfig, baselineStart = baselineStart, enhancedStart = enhancedStart,
                baseline = baseline, enhanced = enhanced, firstDivergence = FindDivergence(baseline, enhanced),
                baselineEquipmentBonus = beforeEnhancement.WeaponAttackBonus, enhancedEquipmentBonus = currentEquipment.WeaponAttackBonus,
                onlyEquipmentChanged = true, equipmentSourceId = mapping.weaponInstanceId, combatUnitId = mapping.combatUnitId
            };
            Remember(Command(true, false, "Compared", lastComparison.reason, string.Empty, 0));
            return CopyComparison(lastComparison);
        }

        public VerificationBattleReport RunVerificationBattle()
        {
            if (expedition == null || !expedition.GetReport().finished || partyState == null)
                return VerificationFailure("CompletedExpeditionRequired", "请先推进本轮主线到结束，再用真实队伍验证再战。");
            CombatScenario scenario = VerificationTemplate(editableCombat);
            ApplyEquipment(scenario, preparation.GetSnapshot());
            CombatSnapshot previous = CombatSession.CopySnapshot(partyState);
            var session = new CombatSession(scenario, previous, editableCombat.carryTeam, initialConfiguration.comparison.betweenWait);
            var report = new VerificationBattleReport
            {
                success = true, code = "VerificationCompleted", reason = "真实无奖励验证再战：承接当前全队 HP/MP、状态与行动时间，使用当前装备，结果更新当前队伍。",
                rewardsEligible = false, battleId = Guid.NewGuid().ToString("D"), configuration = scenario,
                previousSnapshot = previous, battleStart = session.GetSnapshot(),
                result = session.RunToCompletion(editableCombat.maxStepsPerBattle, editableCombat.maxWaitPerBattle)
            };
            partyState = CombatSession.CopySnapshot(report.result.snapshot);
            verifications.Add(report);
            Remember(Command(true, false, report.code, report.reason, report.battleId, 0));
            return CopyVerification(report);
        }

        private ExpeditionSettlementReceipt Settle(CombatEncounterReport encounter)
        {
            var receipt = new ExpeditionSettlementReceipt
            {
                resultId = encounter.resultId, expeditionId = encounter.expeditionId, encounterId = encounter.encounterId,
                rewardsEligible = encounter.rewardsEligible, cashReward = encounter.publicCash,
                items = Array.Empty<ItemQuantity>(), experience = CopyExperience(encounter.experience)
            };
            if (!encounter.rewardsEligible)
            {
                receipt.success = true; receipt.code = "RewardsIneligible"; receipt.reason = "本场没有获奖资格；未入公库或经验。";
                receipts[encounter.resultId] = receipt;
                return receipt;
            }
            try
            {
                receipt.items = MapItems(encounter.publicDrops);
                // Compute every XP addition before mutating the public ledger. A failed reward can be retried.
                var nextExperience = new Dictionary<string, int>(experience, StringComparer.Ordinal);
                if (!xpPosted.Contains(encounter.resultId))
                    foreach (CombatExperienceGrant grant in encounter.experience)
                    {
                        if (grant == null || grant.amount < 0 || !IsPartyMember(grant.unitId))
                            throw new ArgumentException("经验应收必须属于本次出场队员且为非负值。");
                        int current;
                        nextExperience.TryGetValue(grant.unitId, out current);
                        nextExperience[grant.unitId] = checked(current + grant.amount);
                    }
                PreparationCommandResult result = preparation.ApplyExpeditionDrop(new ExpeditionDrop
                {
                    settlementId = encounter.resultId, expeditionId = encounter.expeditionId,
                    encounterId = encounter.encounterId, cashReward = encounter.publicCash, items = receipt.items
                });
                receipt.success = result.Success; receipt.isReplay = result.IsReplay;
                receipt.code = result.Code.ToString(); receipt.reason = result.Reason; receipt.ledgerSequence = result.LedgerSequence;
                if (result.Success && !xpPosted.Contains(encounter.resultId))
                {
                    experience.Clear();
                    foreach (KeyValuePair<string, int> value in nextExperience) experience.Add(value.Key, value.Value);
                    xpPosted.Add(encounter.resultId);
                }
            }
            catch (OverflowException)
            {
                receipt.success = false; receipt.code = "NumericOverflow"; receipt.reason = "经验或映射数量将溢出；公库与经验均未修改。";
            }
            catch (ArgumentException exception)
            {
                receipt.success = false; receipt.code = "InvalidRewardMapping"; receipt.reason = exception.Message;
            }
            ExpeditionSettlementReceipt previous;
            if (!receipts.TryGetValue(encounter.resultId, out previous) || !previous.success) receipts[encounter.resultId] = CopyReceipt(receipt);
            return receipt;
        }

        private ItemQuantity[] MapItems(CombatLootStack[] drops)
        {
            var amounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (CombatLootStack drop in drops ?? Array.Empty<CombatLootStack>())
            {
                ExpeditionItemMapping mapping = Array.Find(initialConfiguration.itemMappings, value => value.combatItemId == drop.itemId);
                if (mapping == null) throw new ArgumentException("缺少明确物品映射：" + drop.itemId);
                int amount;
                amounts.TryGetValue(mapping.preparationItemId, out amount);
                amounts[mapping.preparationItemId] = checked(amount + drop.quantity);
            }
            var mapped = new List<ItemQuantity>();
            foreach (string id in new[] { initialPreparation.oreItemId, initialPreparation.auxiliaryItemId })
            {
                int amount;
                if (amounts.TryGetValue(id, out amount)) mapped.Add(new ItemQuantity { itemId = id, quantity = amount });
            }
            return mapped.ToArray();
        }

        private void CaptureComparisonOrigin()
        {
            CombatExpeditionReport report = expedition.GetReport();
            if (comparisonOrigin != null || !report.finished || report.stopReason != "CompletedAllEncounters") return;
            comparisonOrigin = CombatSession.CopySnapshot(report.finalSnapshot);
            comparisonTemplate = VerificationTemplate(frozenCombat);
        }

        private CombatScenario VerificationTemplate(CombatExpeditionConfig source)
        {
            CombatScenario scenario = CombatSession.CopyScenario(source.encounters[initialConfiguration.comparison.templateEncounterIndex].scenario);
            foreach (ExpeditionEnemyHpOverride value in initialConfiguration.comparison.enemyHpOverrides)
                Array.Find(scenario.units, unit => unit.id == value.unitId).hp = value.hp;
            return scenario;
        }

        private void ApplyEquipment(CombatScenario scenario, PreparationSnapshot equipment)
        {
            foreach (ExpeditionEquipmentMapping mapping in initialConfiguration.equipmentMappings)
            {
                if (mapping.preparationCombatantId != equipment.CombatantId || mapping.weaponInstanceId != equipment.WeaponInstanceId)
                    throw new ArgumentException("装备实例或装备者不匹配，拒绝凭空解析加成。");
                CombatUnitConfig unit = Array.Find(scenario.units, value => value.id == mapping.combatUnitId);
                if (unit == null || unit.team != editableCombat.carryTeam) throw new ArgumentException("装备映射缺少真实友方角色：" + mapping.combatUnitId);
                unit.equipmentSourceId = equipment.WeaponInstanceId;
                unit.equipmentDamageBonus = equipment.WeaponAttackBonus;
            }
        }

        private bool IsPartyMember(string id)
        {
            return Array.Exists(initialCombat.encounters[0].scenario.units, unit => unit.id == id && unit.team == initialCombat.carryTeam);
        }
        private bool IsActive() { return expedition != null && !expedition.GetReport().finished; }
        private bool HasPendingSettlement()
        {
            foreach (ExpeditionSettlementReceipt receipt in receipts.Values)
                if (receipt.rewardsEligible && !receipt.success) return true;
            return false;
        }
        private SliceCommandResult Remember(SliceCommandResult result) { lastCommand = CopyCommand(result); return CopyCommand(result); }
        private static SliceCommandResult Command(bool success, bool replay, string code, string reason, string id, int sequence)
        { return new SliceCommandResult { success = success, isReplay = replay, code = code, reason = reason, operationId = id, ledgerSequence = sequence }; }
        private static SliceCommandResult Adapt(PreparationCommandResult result, string id)
        { return Command(result.Success, result.IsReplay, result.Code.ToString(), result.Reason, id, result.LedgerSequence); }
        private ExpeditionAdvanceResult AdvanceFailure(string code, string reason)
        {
            Remember(Command(false, false, code, reason, string.Empty, 0));
            return new ExpeditionAdvanceResult { success = false, code = code, reason = reason,
                encounters = Array.Empty<CombatEncounterReport>(), settlements = Array.Empty<ExpeditionSettlementReceipt>(),
                report = expedition == null ? null : expedition.GetReport() };
        }
        private EquipmentComparisonReport ComparisonFailure(string code, string reason)
        { Remember(Command(false, false, code, reason, string.Empty, 0)); return new EquipmentComparisonReport { success = false, code = code, reason = reason }; }
        private VerificationBattleReport VerificationFailure(string code, string reason)
        { Remember(Command(false, false, code, reason, string.Empty, 0)); return new VerificationBattleReport { success = false, code = code, reason = reason, rewardsEligible = false }; }

        private static void Validate(ExpeditionSliceConfig config, CombatExpeditionConfig combat, PreparationScenario prep)
        {
            if (config == null || combat == null || prep == null) throw new ArgumentNullException("Integration, combat and preparation configurations are required.");
            if (config.schemaVersion != 1 || string.IsNullOrWhiteSpace(config.id) || string.IsNullOrWhiteSpace(config.parameterNotice) ||
                string.IsNullOrWhiteSpace(config.combatConfigPath) || string.IsNullOrWhiteSpace(config.preparationConfigPath))
                throw new ArgumentException("集成配置版本、标识、参数说明或配置路径无效。");
            prep.Validate();
            new CombatExpeditionSession(combat);
            ValidateAggregateBudgets(combat);
            if (config.itemMappings == null || config.equipmentMappings == null || config.equipmentMappings.Length != 1)
                throw new ArgumentException("本轮需要显式物品映射和唯一已装备武器映射。");
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ExpeditionItemMapping mapping in config.itemMappings)
                if (mapping == null || string.IsNullOrEmpty(mapping.combatItemId) || !sourceIds.Add(mapping.combatItemId) ||
                    (mapping.preparationItemId != prep.oreItemId && mapping.preparationItemId != prep.auxiliaryItemId))
                    throw new ArgumentException("物品映射必须唯一且指向基地接受的明确物品标识。");
            foreach (CombatEncounterConfig encounter in combat.encounters)
                foreach (CombatLootStack drop in encounter.publicDrops ?? Array.Empty<CombatLootStack>())
                    if (!sourceIds.Contains(drop.itemId)) throw new ArgumentException("缺少战斗掉落物品映射：" + drop.itemId);
            ExpeditionEquipmentMapping equipment = config.equipmentMappings[0];
            if (equipment == null || equipment.preparationCombatantId != prep.combatantId || equipment.weaponInstanceId != prep.weaponInstanceId)
                throw new ArgumentException("装备映射必须匹配基地的实际装备者和武器实例。");
            foreach (CombatEncounterConfig encounter in combat.encounters)
                if (!Array.Exists(encounter.scenario.units, unit => unit.id == equipment.combatUnitId && unit.team == combat.carryTeam))
                    throw new ArgumentException("装备映射必须在每场对应同一个真实友方角色。");
            ExpeditionComparisonConfig comparison = config.comparison;
            if (comparison == null || comparison.templateEncounterIndex < 0 || comparison.templateEncounterIndex >= combat.encounters.Length ||
                double.IsNaN(comparison.betweenWait) || double.IsInfinity(comparison.betweenWait) || comparison.betweenWait < 0 || comparison.enemyHpOverrides == null)
                throw new ArgumentException("验证战模板或显式场间 WAIT 无效。");
            sourceIds.Clear();
            foreach (ExpeditionEnemyHpOverride value in comparison.enemyHpOverrides)
            {
                if (value == null || !sourceIds.Add(value.unitId)) throw new ArgumentException("验证战敌方 HP 覆盖必须唯一。");
                CombatUnitConfig enemy = Array.Find(combat.encounters[comparison.templateEncounterIndex].scenario.units, unit => unit.id == value.unitId);
                if (enemy == null || enemy.team == combat.carryTeam || value.hp <= 0 || value.hp > enemy.maxHp)
                    throw new ArgumentException("验证战只能覆盖已有敌人的合法起始 HP，保留其余敌情。");
            }
        }

        private static void ValidateAggregateBudgets(CombatExpeditionConfig combat)
        {
            // The native report uses int totals. Reject unrepresentable input before any camp mutation.
            int xpPool = 0;
            int cash = 0;
            var loot = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                foreach (CombatEncounterConfig encounter in combat.encounters)
                {
                    xpPool = checked(xpPool + encounter.experiencePool);
                    cash = checked(cash + encounter.publicCash);
                    foreach (CombatLootStack drop in encounter.publicDrops ?? Array.Empty<CombatLootStack>())
                    {
                        int amount;
                        loot.TryGetValue(drop.itemId, out amount);
                        loot[drop.itemId] = checked(amount + drop.quantity);
                    }
                }
            }
            catch (OverflowException)
            {
                throw new ArgumentException("配置的经验池、现金或同物品掉落汇总超过本轮报告 DTO 的 int 范围；在创建营地前拒绝。");
            }
        }
    }
}
