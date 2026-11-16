using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Prototype.Combat;

namespace Prototype.Tactics
{
    internal static class TacticsComparison
    {
        internal static bool TrySingleInstructionChange(CombatScenario baseline, CombatScenario variant,
            out string kind, out string unitId, out string description, out string error)
        {
            kind = unitId = description = "";
            error = "同开局对照只允许相对加载战例插入、替换、删除或移动一条己方指令；其他配置必须相同。";
            CombatScenario normalized = CombatSession.CopyScenario(variant);
            CombatUnitConfig changed = null;
            CombatUnitConfig original = null;
            if (baseline.units.Length != normalized.units.Length) return false;
            string team = string.IsNullOrEmpty(baseline.playerTeam) ? baseline.units[0].team : baseline.playerTeam;
            for (int index = 0; index < baseline.units.Length; index++)
            {
                var left = baseline.units[index];
                var right = normalized.units[index];
                if (left.id != right.id) return false;
                if (!TacticsCopies.Equivalent(left.instructions, right.instructions))
                {
                    if (changed != null || left.team != team) return false;
                    original = left;
                    changed = right;
                }
                right.instructions = left.instructions;
            }
            if (!TacticsCopies.Equivalent(baseline, normalized)) return false;
            if (changed == null)
            {
                error = "当前草稿与加载战例没有指令变化；请只改一条规则后再对照。";
                return false;
            }
            CombatInstruction[] before = original.instructions;
            CombatInstruction[] after = variant.units.Single(value => value.id == original.id).instructions;
            if (after.Length == before.Length + 1)
            {
                for (int index = 0; index < after.Length; index++)
                    if (TacticsCopies.Equivalent(before, Without(after, index))) { kind = "Insert"; break; }
            }
            else if (after.Length + 1 == before.Length)
            {
                for (int index = 0; index < before.Length; index++)
                    if (TacticsCopies.Equivalent(Without(before, index), after)) { kind = "Remove"; break; }
            }
            else if (after.Length == before.Length)
            {
                if (Enumerable.Range(0, before.Length).Count(index => !TacticsCopies.Equivalent(before[index], after[index])) == 1)
                    kind = "Replace";
                else
                    for (int from = 0; from < before.Length && kind.Length == 0; from++)
                        for (int to = 0; to < before.Length; to++)
                        {
                            var moved = new List<CombatInstruction>(before);
                            CombatInstruction instruction = moved[from];
                            moved.RemoveAt(from);
                            moved.Insert(to, instruction);
                            if (TacticsCopies.Equivalent(moved.ToArray(), after)) { kind = "Move"; break; }
                        }
            }
            if (kind.Length == 0) return false;
            unitId = original.id;
            description = "仅对 " + unitId + " 的一条指令执行 " + kind + "；开局 HP/MP、状态、行动时间、敌情、技能与种子保持相同。";
            error = "";
            return true;
        }

        private static CombatInstruction[] Without(CombatInstruction[] values, int index)
        {
            return values.Where((value, position) => position != index).ToArray();
        }

        internal static CombatFirstDivergence FirstActualDivergence(CombatStepResult[] baseline, CombatStepResult[] variant)
        {
            // Attempts, descriptions, instruction labels and other display text are
            // excluded. Skipped is still a real queue event: structured control and
            // timing changes may matter even when no active action executes.
            CombatStepResult[] left = baseline.Where(IsActualEvent).ToArray();
            CombatStepResult[] right = variant.Where(IsActualEvent).ToArray();
            for (int index = 0; index < Math.Max(left.Length, right.Length); index++)
            {
                CombatStepResult a = index < left.Length ? left[index] : null;
                CombatStepResult b = index < right.Length ? right[index] : null;
                if (SameDecision(a, b)) continue;
                return new CombatFirstDivergence
                {
                    recordIndex = a == null ? b.index : a.index, baseline = a == null ? null : CombatSession.CopyStep(a),
                    variant = b == null ? null : CombatSession.CopyStep(b),
                    description = "首次实际行动／状态分歧：加载基准 " + Describe(a) + "；当前草稿 " + Describe(b)
                };
            }
            return null;
        }

        private static bool IsActualEvent(CombatStepResult value)
        {
            return value.status == "Executed" || value.status == "Skipped" ||
                value.status == "StateExpired" || value.status == "Finished";
        }

        private static bool SameDecision(CombatStepResult left, CombatStepResult right)
        {
            if (left == null || right == null) return left == right;
            if (left.time != right.time || left.status != right.status || left.actorId != right.actorId ||
                left.actionId != right.actionId || left.mpBefore != right.mpBefore ||
                left.mpAfter != right.mpAfter || left.mpCost != right.mpCost || left.nextActionTime != right.nextActionTime ||
                left.baseWait != right.baseWait || left.agilityFactor != right.agilityFactor || left.actualWait != right.actualWait ||
                left.baseAgilityFactor != right.baseAgilityFactor || left.statusWaitFactor != right.statusWaitFactor ||
                (left.controlReason ?? "") != (right.controlReason ?? "") || left.equipmentDamageBonus != right.equipmentDamageBonus ||
                left.outcome != right.outcome || left.winnerTeam != right.winnerTeam || left.stopReason != right.stopReason ||
                !TacticsCopies.Equivalent(left.targetIds ?? Array.Empty<string>(), right.targetIds ?? Array.Empty<string>()))
                return false;
            if (left.effects.Length != right.effects.Length) return false;
            for (int index = 0; index < left.effects.Length; index++)
            {
                CombatEffect a = left.effects[index], b = right.effects[index];
                if (a.kind != b.kind || a.targetId != b.targetId || a.hpBefore != b.hpBefore || a.hpAfter != b.hpAfter || a.hpDelta != b.hpDelta ||
                    a.mpBefore != b.mpBefore || a.mpAfter != b.mpAfter || a.mpDelta != b.mpDelta ||
                    (a.statusId ?? "") != (b.statusId ?? "") || a.statusAppliedAt != b.statusAppliedAt || a.statusExpiresAt != b.statusExpiresAt ||
                    a.baseVulnerableBefore != b.baseVulnerableBefore || a.baseVulnerableAfter != b.baseVulnerableAfter ||
                    !TacticsCopies.Equivalent(a.removedStatusIds ?? Array.Empty<string>(), b.removedStatusIds ?? Array.Empty<string>()))
                    return false;
            }
            return true;
        }

        private static string Describe(CombatStepResult step)
        {
            if (step == null) return "没有对应行动（该分支已结束）";
            return "WAIT " + step.time.ToString("0.##", CultureInfo.InvariantCulture) + " " + step.status + " " + step.actorId + " " + step.actionId
                + "；锚点 " + step.targetId + "；实际覆盖 [" + string.Join(",", step.targetIds ?? Array.Empty<string>()) + "]"
                + "；MP " + step.mpBefore + "→" + step.mpAfter + "；下次 WAIT " + step.nextActionTime.ToString("0.##", CultureInfo.InvariantCulture)
                + (string.IsNullOrEmpty(step.controlReason) ? "" : "；控制 " + step.controlReason)
                + "；" + string.Join("；", step.effects.Select(DescribeEffect));
        }

        private static string DescribeEffect(CombatEffect effect)
        {
            return effect.targetId + " " + effect.kind + " HP " + effect.hpBefore + "→" + effect.hpAfter
                + " MP " + effect.mpBefore + "→" + effect.mpAfter
                + (string.IsNullOrEmpty(effect.statusId) ? "" : " 状态 " + effect.statusId + " 施加 "
                    + effect.statusAppliedAt.ToString("0.##", CultureInfo.InvariantCulture) + " 到期 " + effect.statusExpiresAt.ToString("0.##", CultureInfo.InvariantCulture))
                + (effect.removedStatusIds == null || effect.removedStatusIds.Length == 0 ? "" : " 移除 [" + string.Join(",", effect.removedStatusIds) + "]")
                + (effect.baseVulnerableBefore == effect.baseVulnerableAfter ? "" : " 永久易伤 " + effect.baseVulnerableBefore + "→" + effect.baseVulnerableAfter);
        }
    }

    internal static class TacticsCopies
    {
        internal static bool Equivalent(object left, object right)
        {
            if (left == null || right == null) return left == right;
            Type type = left.GetType();
            if (type != right.GetType()) return false;
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)) return left.Equals(right);
            Array a = left as Array;
            if (a != null)
            {
                Array b = (Array)right;
                if (a.Length != b.Length) return false;
                for (int index = 0; index < a.Length; index++) if (!Equivalent(a.GetValue(index), b.GetValue(index))) return false;
                return true;
            }
            return type.GetFields(BindingFlags.Instance | BindingFlags.Public).All(field => Equivalent(field.GetValue(left), field.GetValue(right)));
        }

        internal static CombatEditResult Edit(CombatEditResult source)
        {
            return source == null ? null : new CombatEditResult { success = source.success, code = source.code, message = source.message,
                instructionCapacity = source.instructionCapacity, conditionTermCapacity = source.conditionTermCapacity };
        }

        internal static TacticsComparisonResult Comparison(TacticsComparisonResult source)
        {
            if (source == null) return null;
            CombatComparisonReport report = source.comparison;
            return new TacticsComparisonResult
            {
                success = source.success, code = source.code, message = source.message, changeKind = source.changeKind,
                changedUnitId = source.changedUnitId, baselineLogText = source.baselineLogText, variantLogText = source.variantLogText,
                comparison = report == null ? null : new CombatComparisonReport
                {
                    id = report.id, baselineLabel = report.baselineLabel, variantLabel = report.variantLabel, changedSetting = report.changedSetting,
                    onlyDeclaredSettingChanged = report.onlyDeclaredSettingChanged, baselineConfiguration = CombatSession.CopyScenario(report.baselineConfiguration),
                    variantConfiguration = CombatSession.CopyScenario(report.variantConfiguration), baseline = Run(report.baseline), variant = Run(report.variant),
                    firstDivergence = report.firstDivergence == null ? null : new CombatFirstDivergence
                    {
                        recordIndex = report.firstDivergence.recordIndex, description = report.firstDivergence.description,
                        baseline = report.firstDivergence.baseline == null ? null : CombatSession.CopyStep(report.firstDivergence.baseline),
                        variant = report.firstDivergence.variant == null ? null : CombatSession.CopyStep(report.firstDivergence.variant)
                    }, observableImpact = report.observableImpact
                }
            };
        }

        private static CombatRunResult Run(CombatRunResult source)
        {
            return source == null ? null : new CombatRunResult
            {
                outcome = source.outcome, winnerTeam = source.winnerTeam, stopReason = source.stopReason, time = source.time,
                executedActionCount = source.executedActionCount, snapshot = CombatSession.CopySnapshot(source.snapshot),
                steps = source.steps.Select(CombatSession.CopyStep).ToArray()
            };
        }
    }
}
