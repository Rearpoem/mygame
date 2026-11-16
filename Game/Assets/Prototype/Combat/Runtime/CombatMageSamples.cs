using System;
using System.Globalization;
using System.Text;

namespace Prototype.Combat
{
    [Serializable]
    public sealed class CombatMageSkillProof
    {
        public string actionId;
        public string displayName;
        public string effectKind;
        public string targetMode;
        public int configuredMpCost;
        public double configuredWait;
        public CombatSnapshot before;
        public CombatStepResult firstStep;
        public CombatSnapshot after;
        public CombatStepResult[] followUpSteps;
        public CombatSnapshot followUpSnapshot;
        public bool actualChangeObserved;
        public bool controlObserved;
        public string evidence;
    }

    /// <summary>Fourth-round experiments edit one real instruction field and run the shared engine.</summary>
    public static class CombatMageSamples
    {
        public static CombatComparisonReport CompareMeditation(CombatScenario source, double newThreshold = 0.25)
        {
            return CompareInstructionThreshold(source, "mage", 0, 0, newThreshold,
                "低 MP 仍直接攻击", "低 MP 先冥想", "meditation");
        }

        public static CombatComparisonReport CompareArea(CombatScenario source, string alternativeActionId = "mage_fire_rain")
        {
            return CompareInstructionAction(source, "mage", 0, alternativeActionId,
                "单体法术", "群体法术", "area");
        }

        public static CombatComparisonReport CompareCleanse(CombatScenario source, double newThreshold = 1)
        {
            return CompareInstructionThreshold(source, "mage", 0, 0, newThreshold,
                "重伤才净化", "及时净化", "cleanse");
        }

        public static CombatComparisonReport CompareControl(CombatScenario source, double newThreshold = 1)
        {
            return CompareInstructionThreshold(source, "mage", 0, 0, newThreshold,
                "优先进攻", "先控制威胁", "control");
        }

        public static CombatComparisonReport CompareInstructionThreshold(CombatScenario source, string unitId,
            int instructionIndex, int termIndex, double value, string baselineLabel, string variantLabel, string suffix)
        {
            var baseline = new CombatSession(source);
            var variant = new CombatSession(source);
            CombatScenario before = baseline.GetScenario();
            CombatInstruction changed = Find(variant.GetScenario(), unitId).instructions[instructionIndex];
            double previous = changed.condition.terms[termIndex].threshold;
            changed.condition.terms[termIndex].threshold = value;
            CombatEditResult edit = variant.ReplaceInstruction(unitId, instructionIndex, changed);
            if (!edit.success) throw new ArgumentException(edit.code + ": " + edit.message);
            CombatScenario normalized = variant.GetScenario();
            Find(normalized, unitId).instructions[instructionIndex].condition.terms[termIndex].threshold = previous;
            return CombatSampleRuns.RunComparison(source.id + ":" + suffix, baselineLabel, variantLabel,
                "仅修改 " + unitId + ".instructions[" + instructionIndex + "].condition.terms[" + termIndex +
                "].threshold：" + N(previous) + " → " + N(value) + "；其余配置、技能、初始HP/MP、状态、种子与装备不变",
                CombatSampleRuns.Equivalent(before, normalized), baseline, variant);
        }

        public static CombatComparisonReport CompareInstructionAction(CombatScenario source, string unitId,
            int instructionIndex, string actionId, string baselineLabel, string variantLabel, string suffix)
        {
            var baseline = new CombatSession(source);
            var variant = new CombatSession(source);
            CombatScenario before = baseline.GetScenario();
            CombatInstruction changed = Find(variant.GetScenario(), unitId).instructions[instructionIndex];
            string previous = changed.actionId;
            changed.actionId = actionId;
            CombatEditResult edit = variant.ReplaceInstruction(unitId, instructionIndex, changed);
            if (!edit.success) throw new ArgumentException(edit.code + ": " + edit.message);
            CombatScenario normalized = variant.GetScenario();
            Find(normalized, unitId).instructions[instructionIndex].actionId = previous;
            return CombatSampleRuns.RunComparison(source.id + ":" + suffix, baselineLabel, variantLabel,
                "仅修改 " + unitId + ".instructions[" + instructionIndex + "].actionId：" + previous + " → " + actionId +
                "；两侧始终都拥有这两种技能，条件、对象、选择方式、初始HP/MP、状态、种子与装备不变",
                CombatSampleRuns.Equivalent(before, normalized), baseline, variant);
        }

        /// <summary>
        /// Purpose-built legal openings prove every learned Mage action against the actual Step.
        /// These are isolated capability probes, not single-variable balance comparisons.
        /// </summary>
        public static CombatMageSkillProof[] RunSkillProofs(CombatScenario library)
        {
            CombatActionConfig[] actions = new CombatSession(library).GetAvailableActions("mage");
            var proofs = new CombatMageSkillProof[actions.Length];
            for (int i = 0; i < actions.Length; i++)
            {
                CombatActionConfig action = actions[i];
                CombatScenario probe = SkillOpening(library, action);
                var session = new CombatSession(probe);
                CombatSnapshot before = session.GetSnapshot();
                CombatStepResult first = session.Step();
                if (first.status != "Executed" || first.actionId != action.id)
                    throw new InvalidOperationException("法师技能未实际执行：" + action.id + "；" + first.message);
                bool changed = false;
                foreach (CombatEffect effect in first.effects)
                    changed |= effect.hpDelta != 0 || effect.mpDelta != 0 || !string.IsNullOrEmpty(effect.statusId) ||
                        (effect.removedStatusIds != null && effect.removedStatusIds.Length > 0) ||
                        effect.baseVulnerableBefore != effect.baseVulnerableAfter;
                if (!changed) throw new InvalidOperationException("法师技能没有真实资源／状态变化：" + action.id);
                CombatSnapshot after = session.GetSnapshot();
                // Remove only the already-executed probe rule. Continue this same session
                // with its staff fallback, so status persistence/WAIT/control can be observed.
                CombatEditResult removed = session.RemoveInstruction("mage", 0);
                if (!removed.success) throw new InvalidOperationException(removed.message);
                var follow = new System.Collections.Generic.List<CombatStepResult>();
                bool controlled = false;
                for (int step = 0; step < 8 && !session.GetSnapshot().finished; step++)
                {
                    CombatStepResult next = session.Step();
                    follow.Add(next);
                    controlled |= !string.IsNullOrEmpty(next.controlReason);
                    foreach (CombatInstructionAttempt attempt in next.attempts ?? Array.Empty<CombatInstructionAttempt>())
                        controlled |= attempt.reasonCode == "Silenced";
                }
                var note = new StringBuilder();
                foreach (CombatEffect effect in first.effects) note.Append(effect.description).Append("；");
                if (controlled) note.Append("后续真实事件观察到主动受控／魔法被沉默阻止；");
                bool actorExample = false, targetExample = false, protectionExample = false;
                foreach (CombatStepResult next in follow)
                {
                    if (next.status != "Executed") continue;
                    if (!actorExample && next.actorId == "mage" && (action.statusKind == "haste" || action.statusKind == "empowered"))
                    {
                        note.Append("后续法师 ").Append(next.actionName).Append("：WAIT ").Append(N(next.actualWait))
                            .Append("（基础 ").Append(N(next.baseWait)).Append("）；");
                        foreach (CombatEffect effect in next.effects) note.Append(effect.description).Append("；");
                        actorExample = true;
                    }
                    if (!targetExample && next.actorId == first.targetId && (action.statusKind == "slow" || action.statusKind == "weakened"))
                    {
                        note.Append("后续受影响者 ").Append(next.actionName).Append("：WAIT ").Append(N(next.actualWait))
                            .Append("（基础 ").Append(N(next.baseWait)).Append("）；");
                        foreach (CombatEffect effect in next.effects) note.Append(effect.description).Append("；");
                        targetExample = true;
                    }
                    if (!protectionExample && (action.statusKind == "guard" || action.statusKind == "magic_guard"))
                        foreach (CombatEffect effect in next.effects)
                            if (effect.targetId == "mage" && effect.hpDelta < 0)
                            {
                                note.Append("后续实际承伤：").Append(effect.description).Append("；");
                                protectionExample = true; break;
                            }
                }
                proofs[i] = new CombatMageSkillProof
                {
                    actionId = action.id, displayName = action.displayName, effectKind = action.effectKind,
                    targetMode = string.IsNullOrEmpty(action.targetMode) ? "single" : action.targetMode,
                    configuredMpCost = action.mpCost, configuredWait = action.baseWait,
                    before = before, firstStep = first, after = after, followUpSteps = follow.ToArray(),
                    followUpSnapshot = session.GetSnapshot(), actualChangeObserved = changed,
                    controlObserved = controlled, evidence = note.ToString()
                };
            }
            return proofs;
        }

        public static string FormatSkillProofsMarkdown(CombatMageSkillProof[] proofs)
        {
            var text = new StringBuilder("# 法师技能逐项真实执行证据\n\n每项在独立合法开局中调用同一个 CombatSession.Step；净化、驱散、传蓝等按需预置真实状态或资源缺口。首动作后移除探针规则，继续同一会话若干步，观察护盾、增伤、WAIT及控制；不另写效果模拟器。这些能力探针不是同开局平衡对照。\n\n");
            text.Append("|技能|真实效果／范围|施放者MP|费用／WAIT|实际主覆盖ID|真实变化与后续|\n|---|---|---|---|---|---|\n");
            foreach (CombatMageSkillProof proof in proofs)
            {
                text.Append('|').Append(proof.actionId).Append("（").Append(proof.displayName).Append("）|")
                    .Append(proof.effectKind).Append('／').Append(proof.targetMode)
                    .Append('|').Append(proof.firstStep.mpBefore).Append("→").Append(proof.firstStep.mpAfter)
                    .Append('|').Append(proof.firstStep.mpCost).Append("／").Append(N(proof.firstStep.actualWait))
                    .Append('|').Append(string.Join(", ", proof.firstStep.targetIds ?? Array.Empty<string>()))
                    .Append('|').Append(Cell(proof.evidence)).Append("|\n");
            }
            text.Append("\n每项完整前后快照和后续事件见同名 JSON。haste/slow 只改变后续预约WAIT；stunned/silenced的阻止原因来自实际事件，而非仅看到一个状态名称。\n");
            return text.ToString();
        }

        private static CombatScenario SkillOpening(CombatScenario library, CombatActionConfig action)
        {
            CombatScenario probe = CombatSession.CopyScenario(library);
            probe.id = library.id + ":proof:" + action.id;
            CombatUnitConfig mage = Find(probe, "mage");
            CombatUnitConfig ally = Find(probe, "ally");
            foreach (CombatUnitConfig unit in probe.units)
            {
                unit.hp = unit.maxHp = 120;
                unit.mp = unit.maxMp = 20;
                unit.vulnerable = false;
                unit.statuses = Array.Empty<CombatStatusConfig>();
                unit.equipmentDamageBonus = 0;
                if (unit.team != mage.team)
                {
                    unit.instructions = new[] { ProbeRule("enemy_response", "enemies", "highest_intelligence", "round4_enemy_magic") };
                    unit.availableActionIds = new[] { "round4_enemy_magic" };
                    unit.availableActionIdsExplicit = true;
                }
            }
            mage.mp = mage.maxMp = 30;
            mage.initialReadyTime = 0;
            ally.instructions = new[] { ProbeRule("ally_response", "enemies", "configured_order", "round4_ally_attack") };
            ally.initialReadyTime = 15;
            int enemy = 0;
            foreach (CombatUnitConfig unit in probe.units)
                if (unit.team != mage.team)
                {
                    unit.row = enemy < 2 ? 0 : 1;
                    unit.column = enemy == 1 ? 1 : 0;
                    unit.initialReadyTime = enemy == 0 ? 10 : enemy == 1 ? 30 : 40;
                    enemy++;
                }
            string scope = "enemies", selector = "configured_order";
            if (action.effectKind == "restore_mp")
            {
                if (action.id == "mage_mana_transfer") { scope = "allies"; selector = "lowest_mp_ratio"; ally.mp = 0; }
                else { scope = "self"; mage.mp = 0; }
            }
            else if (action.effectKind == "drain_mp") { mage.mp = 5; selector = "highest_mp"; }
            else if (action.effectKind == "heal") { scope = "allies"; selector = "lowest_hp_ratio"; mage.hp = 50; ally.hp = 40; }
            else if (action.effectKind == "cleanse")
            {
                scope = "allies"; selector = "controlled_first"; mage.vulnerable = true;
                mage.statuses = new[] { InitialStatus("probe_vulnerable", "vulnerable", 0) };
                ally.statuses = new[] { InitialStatus("probe_stun", "stunned", 0), InitialStatus("probe_slow", "slow", 25) };
            }
            else if (action.effectKind == "dispel")
            {
                foreach (CombatUnitConfig unit in probe.units) if (unit.team != mage.team)
                { unit.statuses = new[] { InitialStatus("probe_guard", "guard", 5), InitialStatus("probe_empower", "empowered", 6) }; break; }
            }
            else if (action.effectKind == "apply_status" && IsHelpful(action.statusKind)) scope = "allies";
            mage.instructions = new[] { ProbeRule("probe_action", scope, selector, action.id),
                ProbeRule("probe_staff_fallback", "enemies", "configured_order", "mage_staff") };
            return probe;
        }

        private static bool IsHelpful(string kind)
        { return kind == "guard" || kind == "magic_guard" || kind == "empowered" || kind == "haste"; }
        private static CombatStatusConfig InitialStatus(string id, string kind, int magnitude)
        { return new CombatStatusConfig { id = id, kind = kind, magnitude = magnitude, appliedAt = 0, expiresAt = 150 }; }
        private static CombatInstruction ProbeRule(string id, string scope, string selector, string action)
        {
            return new CombatInstruction { id = id, label = id, scope = scope, selector = selector, actionId = action,
                condition = new CombatCondition { operation = "all", terms = new[] { new CombatConditionTerm { kind = "always", subject = "target" } } } };
        }

        public static string FormatComparisonMarkdown(CombatComparisonReport report)
        {
            var text = new StringBuilder();
            text.Append("# ").Append(report.baselineConfiguration.title).Append("\n\n")
                .Append("本报告由实际 CombatSession 生成；全部数值属于第四轮可替换试玩参数。\n\n")
                .Append("- 配置：`").Append(report.baselineConfiguration.id).Append("`\n")
                .Append("- 唯一改变：").Append(report.changedSetting).Append("。\n")
                .Append("- 声明字段恢复后，其他配置逐字段相同：**").Append(report.onlyDeclaredSettingChanged ? "是" : "否").Append("**。\n")
                .Append("- 固定种子：").Append(report.baselineConfiguration.seed).Append("；本轮没有随机抽样。\n\n")
                .Append("## 第一次真实行动分歧\n\n");
            if (report.firstDivergence == null) text.Append("无真实行动分歧。\n");
            else text.Append("记录 #").Append(report.firstDivergence.recordIndex).Append("：\n\n- ")
                .Append(report.baselineLabel).Append("：").Append(Event(report.firstDivergence.baseline)).Append("\n- ")
                .Append(report.variantLabel).Append("：").Append(Event(report.firstDivergence.variant)).Append("\n");
            text.Append("\n## 战果与真实资源\n\n|分支|结局|停止原因|结束WAIT|行动次数|角色HP／MP|\n|---|---|---|---:|---:|---|\n");
            Outcome(text, report.baselineLabel, report.baseline);
            Outcome(text, report.variantLabel, report.variant);
            text.Append("\n").Append(report.observableImpact).Append("\n")
                .Append("\n## 四段配置\n\n|角色／序号|基线|变体|\n|---|---|---|\n");
            CombatSnapshot before = new CombatSession(report.baselineConfiguration).GetSnapshot();
            CombatSnapshot after = new CombatSession(report.variantConfiguration).GetSnapshot();
            foreach (CombatUnitSnapshot unit in before.units)
            {
                if (unit.team != Team(report.baselineConfiguration)) continue;
                CombatUnitSnapshot other = Find(after, unit.id);
                for (int i = 0; i < unit.instructions.Length; i++)
                    text.Append('|').Append(unit.id).Append('[').Append(i).Append("]|").Append(Cell(unit.instructions[i].display))
                        .Append('|').Append(Cell(other.instructions[i].display)).Append("|\n");
            }
            Trajectory(text, report.baselineLabel, report.baseline.steps);
            Trajectory(text, report.variantLabel, report.variant.steps);
            return text.ToString();
        }

        private static void Outcome(StringBuilder text, string label, CombatRunResult run)
        {
            text.Append('|').Append(label).Append('|').Append(run.outcome).Append('|').Append(run.stopReason)
                .Append('|').Append(N(run.time)).Append('|').Append(run.executedActionCount).Append('|');
            foreach (CombatUnitSnapshot unit in run.snapshot.units)
                text.Append(unit.id).Append(" HP ").Append(unit.hp).Append('/').Append(unit.maxHp)
                    .Append(" MP ").Append(unit.mp).Append('/').Append(unit.maxMp).Append("；");
            text.Append("|\n");
        }

        private static void Trajectory(StringBuilder text, string label, CombatStepResult[] steps)
        {
            text.Append("\n## ").Append(label).Append("实际事件\n\n|记录|WAIT|状态|行动者|行动／对象|施放者MP|本次WAIT／下次时刻|所有效果|\n|---:|---:|---|---|---|---|---|---|\n");
            foreach (CombatStepResult step in steps)
            {
                text.Append('|').Append(step.index).Append('|').Append(N(step.time)).Append('|').Append(step.status)
                    .Append('|').Append(step.actorId).Append('|').Append(step.actionId).Append(" → ").Append(step.targetId)
                    .Append('|').Append(step.mpBefore).Append("→").Append(step.mpAfter)
                    .Append('|').Append(N(step.actualWait)).Append("／").Append(N(step.nextActionTime)).Append('|');
                foreach (CombatEffect effect in step.effects ?? Array.Empty<CombatEffect>()) text.Append(Cell(effect.description)).Append("；");
                if (step.effects == null || step.effects.Length == 0) text.Append(Cell(step.message));
                text.Append("|\n");
            }
        }

        private static string Event(CombatStepResult step)
        {
            if (step == null) return "该分支已经结束";
            var text = new StringBuilder("WAIT ").Append(N(step.time)).Append("，").Append(step.status).Append("，")
                .Append(step.actorId).Append(" 使用 ").Append(step.actionId).Append(" → ").Append(step.targetId)
                .Append("；施放者 MP ").Append(step.mpBefore).Append("→").Append(step.mpAfter)
                .Append("；本次 WAIT ").Append(N(step.actualWait)).Append("，下次时刻 ").Append(N(step.nextActionTime));
            foreach (CombatEffect effect in step.effects ?? Array.Empty<CombatEffect>()) text.Append("；").Append(effect.description);
            if (step.status != "Executed") text.Append("；").Append(step.message);
            return text.ToString();
        }

        private static CombatUnitConfig Find(CombatScenario scenario, string id)
        {
            foreach (CombatUnitConfig unit in scenario.units) if (unit.id == id) return unit;
            throw new ArgumentException("找不到角色：" + id);
        }
        private static CombatUnitSnapshot Find(CombatSnapshot snapshot, string id)
        {
            foreach (CombatUnitSnapshot unit in snapshot.units) if (unit.id == id) return unit;
            throw new ArgumentException("找不到角色快照：" + id);
        }
        private static string Team(CombatScenario scenario) { return string.IsNullOrEmpty(scenario.playerTeam) ? scenario.units[0].team : scenario.playerTeam; }
        private static string Cell(string value) { return (value ?? string.Empty).Replace("|", "\\|").Replace("\n", " "); }
        private static string N(double value) { return value.ToString("0.###############", CultureInfo.InvariantCulture); }
    }
}
