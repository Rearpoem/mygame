using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Prototype.Combat;

namespace Prototype.UnityClient.Candidates.Hydrogen
{
    /// <summary>Formats actual kernel records. No effects, coverage, timing or decisions are simulated here.</summary>
    internal static class HydrogenBattleText
    {
        internal static string N(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
        private static readonly Regex Percentages = new Regex(@"(?<![\w.])([-+]?\d+(?:\.\d+)?(?:[Ee][-+]?\d+)?)%", RegexOptions.CultureInvariant);

        // Only the player-facing copy is rounded. The supplied event and raw traces stay intact.
        internal static string DisplayPercentages(string value)
        {
            return Percentages.Replace(value ?? "", match =>
            {
                double percentage;
                return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out percentage)
                    ? percentage.ToString("0.#", CultureInfo.InvariantCulture) + "%" : match.Value;
            });
        }
        internal static string[] PrimaryTargets(CombatStepResult step)
        {
            if (step == null) return Array.Empty<string>();
            if (step.targetIds != null && step.targetIds.Length > 0)
                return step.targetIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToArray();
            // Compatibility for legacy state-expiry records without the primary-coverage array.
            var ids = (step.effects ?? Array.Empty<CombatEffect>()).Select(effect => effect.targetId)
                .Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToArray();
            return ids.Length > 0 ? ids : string.IsNullOrEmpty(step.targetId) ? Array.Empty<string>() : new[] { step.targetId };
        }

        internal static string Name(CombatSnapshot snapshot, string id)
        {
            return snapshot?.units?.FirstOrDefault(unit => unit.id == id)?.name ?? id ?? "—";
        }

        internal static string StatusName(string kind)
        {
            switch (kind)
            {
                case "guard": return "护盾";
                case "magic_guard": return "魔盾";
                case "empowered": return "增幅";
                case "vulnerable": return "易伤";
                case "stunned": return "眩晕";
                case "silenced": return "沉默";
                case "weakened": return "虚弱";
                case "haste": return "加速";
                case "slow": return "减速";
                default: return kind ?? "状态";
            }
        }

        internal static string ShortStatuses(CombatStatusConfig[] statuses)
        {
            var current = statuses ?? Array.Empty<CombatStatusConfig>();
            if (current.Length == 0) return "无状态";
            return string.Join(" · ", current.Take(2).Select(status => StatusName(status.kind)))
                + (current.Length > 2 ? " +" + (current.Length - 2) : "");
        }

        internal static string Effects(CombatStepResult step, bool technical, Func<string, string> statusName = null)
        {
            if (step == null) return "尚无实际事件。应用编排后可单步或自动播放。";
            var text = new StringBuilder();
            foreach (var effect in step.effects ?? Array.Empty<CombatEffect>())
            {
                text.Append(effect.targetName).Append("：HP ").Append(effect.hpBefore).Append('→').Append(effect.hpAfter)
                    .Append("；MP ").Append(effect.mpBefore).Append('→').Append(effect.mpAfter);
                if (!string.IsNullOrEmpty(effect.statusId))
                    text.Append(effect.kind == "status_expired" ? "；状态到期：" : "；施加状态：")
                        .Append(statusName?.Invoke(effect.statusId) ?? "状态")
                        .Append(" · WAIT ").Append(N(effect.statusAppliedAt)).Append('→').Append(N(effect.statusExpiresAt));
                if (effect.removedStatusIds != null && effect.removedStatusIds.Length > 0)
                    text.Append(effect.kind == "cleanse" ? "；净化移除：" : effect.kind == "dispel" ? "；驱散移除：" : "；移除：")
                        .Append(string.Join("、", effect.removedStatusIds.Select(id => statusName?.Invoke(id) ?? "状态")));
                if (effect.baseVulnerableBefore != effect.baseVulnerableAfter)
                    text.Append("；基础易伤 ").Append(effect.baseVulnerableBefore ? "有" : "无").Append('→')
                        .Append(effect.baseVulnerableAfter ? "有" : "无");
                text.AppendLine();
                if (technical)
                {
                    text.Append("  kind=").Append(effect.kind).Append(" targetId=").Append(effect.targetId)
                        .Append(" HPΔ=").Append(effect.hpDelta).Append(" MPΔ=").Append(effect.mpDelta)
                        .Append(" statusId=").Append(effect.statusId).Append(" removed=[")
                        .Append(string.Join(",", effect.removedStatusIds ?? Array.Empty<string>())).AppendLine("]");
                    text.Append("  ").AppendLine(effect.description);
                }
            }
            return text.Length == 0 ? "本事件无数值或状态效果。" : text.ToString();
        }

        internal static string Causality(CombatStepResult step, bool technical, Func<string, string> statusName = null)
        {
            if (step == null) return "选择左侧真实事件，逐条查看规则尝试和候选原因。";
            var text = new StringBuilder();
            var attempts = step.attempts ?? Array.Empty<CombatInstructionAttempt>();
            var selected = attempts.FirstOrDefault(attempt => attempt.outcome == "Selected");
            // Lead with the actual selected rule and results, before any earlier skipped rule.
            // This is a projection of the kernel record, never another eligibility calculation.
            if (selected != null)
                text.Append("✓ 命中 #").Append(selected.instructionIndex + 1).Append(" · ");
            else text.Append("事件 #").Append(step.index).Append(" · ");
            text.Append(string.IsNullOrEmpty(step.actionName) ? "未执行行动" : step.actionName)
                .Append(" · WAIT ").AppendLine(N(step.time));
            bool actorMpInEffects = (step.effects ?? Array.Empty<CombatEffect>()).Any(effect => effect.targetId == step.actorId
                && effect.mpBefore == step.mpBefore && effect.mpAfter == step.mpAfter && effect.mpBefore != effect.mpAfter);
            if (!actorMpInEffects)
                text.Append(step.actorName).Append(" MP ").Append(step.mpBefore).Append('→').Append(step.mpAfter).Append("；");
            text.Append("一次费用 ").Append(step.mpCost).Append(" MP / ").Append(N(step.actualWait)).AppendLine(" WAIT");
            text.AppendLine(EffectSummary(step, statusName));
            if (selected != null)
                text.Append("命中规则：").AppendLine(selected.display);
            if (!string.IsNullOrEmpty(step.controlReason))
                text.Append("控制原因：").AppendLine(step.controlReason == "Stunned" ? "眩晕阻止行动" : step.controlReason == "Silenced" ? "沉默限制法术" : step.controlReason);
            if (step.timeTieResolved) text.Append("同刻次序：").AppendLine(step.tieBreakReason);
            foreach (var attempt in attempts.Where(attempt => attempt.outcome != "Selected"))
            {
                var candidate = (attempt.candidates ?? Array.Empty<CombatCandidateTrace>()).FirstOrDefault(value => !value.eligible);
                string reason = candidate == null ? attempt.reason : candidate.reason;
                text.Append("× 前序 #").Append(attempt.instructionIndex + 1).Append("：").AppendLine(FirstClause(reason));
            }
            text.AppendLine().AppendLine("完整规则与候选（可滚动）");
            foreach (var attempt in attempts)
            {
                text.AppendLine().Append(attempt.outcome == "Selected" ? "✓ 命中 " : "× 跳过 ")
                    .Append('#').Append(attempt.instructionIndex + 1).Append(" · ").AppendLine(attempt.display);
                text.AppendLine(attempt.reason);
                foreach (var candidate in attempt.candidates ?? Array.Empty<CombatCandidateTrace>())
                    text.Append("  ").Append(candidate.unitName).Append(candidate.eligible ? " ✓ " : " × ").AppendLine(candidate.reason);
                if (technical) text.Append("  instructionId=").Append(attempt.instructionId).Append(" code=").AppendLine(attempt.reasonCode);
            }
            text.AppendLine().AppendLine("实际效果").Append(Effects(step, technical, statusName));
            if (!string.IsNullOrEmpty(step.message)) text.AppendLine().AppendLine(step.message);
            if (technical)
                text.Append("\n当时 WAIT 因子：基础 ").Append(N(step.baseWait)).Append(" × 敏捷 ").Append(N(step.baseAgilityFactor))
                    .Append(" × 状态 ").Append(N(step.statusWaitFactor)).Append(" = ").Append(N(step.actualWait))
                    .Append("；下次 WAIT ").Append(N(step.nextActionTime)).Append("\nstatus=").Append(step.status)
                    .Append(" actionId=").Append(step.actionId).Append(" anchor=").Append(step.targetId)
                    .Append(" coverage=[").Append(string.Join(",", PrimaryTargets(step))).Append(']');
            return technical ? text.ToString() : DisplayPercentages(text.ToString());
        }

        private static string FirstClause(string value)
        {
            if (string.IsNullOrEmpty(value)) return "参见完整判定。";
            int end = value.IndexOfAny(new[] { '；', ';', '\n', '\r' });
            return end < 0 ? value : value.Substring(0, end);
        }

        private static string EffectSummary(CombatStepResult step, Func<string, string> statusName)
        {
            var results = (step.effects ?? Array.Empty<CombatEffect>()).Select(effect =>
            {
                var change = new StringBuilder(effect.targetName).Append(' ');
                if (effect.hpBefore != effect.hpAfter)
                    change.Append("HP ").Append(effect.hpBefore).Append('→').Append(effect.hpAfter).Append(' ');
                if (effect.mpBefore != effect.mpAfter)
                    change.Append("MP ").Append(effect.mpBefore).Append('→').Append(effect.mpAfter).Append(' ');
                if (!string.IsNullOrEmpty(effect.statusId))
                    change.Append(effect.kind == "status_expired" ? "到期：" : "施加：")
                        .Append(statusName?.Invoke(effect.statusId) ?? "状态").Append(' ');
                if (effect.removedStatusIds != null && effect.removedStatusIds.Length > 0)
                    change.Append("移除：").Append(string.Join("、", effect.removedStatusIds.Select(id => statusName?.Invoke(id) ?? "状态"))).Append(' ');
                if (effect.baseVulnerableBefore != effect.baseVulnerableAfter)
                    change.Append("基础易伤 ").Append(effect.baseVulnerableBefore ? "有" : "无").Append('→')
                        .Append(effect.baseVulnerableAfter ? "有" : "无");
                return change.ToString().TrimEnd();
            }).ToArray();
            return results.Length == 0 ? "本事件无数值或状态效果。" : string.Join("；", results);
        }

        internal static string UnitDetail(CombatUnitSnapshot unit, bool technical)
        {
            if (unit == null) return "点击棋盘上的角色，查看当前完整状态与只读指令。";
            var text = new StringBuilder();
            text.Append(unit.name).Append(" · (").Append(unit.row).Append(',').Append(unit.column).AppendLine(")")
                .Append("HP ").Append(unit.hp).Append('/').Append(unit.maxHp).Append(" · MP ").Append(unit.mp).Append('/').Append(unit.maxMp)
                .Append(unit.hp <= 0 ? " · 已倒下" : "").AppendLine()
                .Append("下次行动 WAIT ").Append(N(unit.nextActionTime)).Append(" · 有效敏捷系数 ").AppendLine(N(unit.effectiveAgilityFactor))
                .Append("智力 ").Append(unit.intelligence).Append(" · 指令容量 ").Append(unit.instructionCapacity)
                .Append(" · 每条条件≤").AppendLine(unit.conditionTermCapacity.ToString())
                .Append("标签：").AppendLine(string.Join("、", unit.tags ?? Array.Empty<string>()));
            foreach (var status in unit.statuses ?? Array.Empty<CombatStatusConfig>())
            {
                text.Append(StatusName(status.kind)).Append(" · 强度 ").Append(status.magnitude).Append(" · WAIT ")
                    .Append(N(status.appliedAt)).Append('→').AppendLine(N(status.expiresAt));
                if (technical) text.Append("  statusId=").AppendLine(status.id);
            }
            text.AppendLine("\n当前指令（只读）");
            foreach (var rule in unit.instructions ?? Array.Empty<CombatInstructionSnapshot>())
                text.Append(rule.capacityAllowed && rule.actionAvailable ? "· " : "! ").AppendLine(rule.display)
                    .AppendLine(string.IsNullOrEmpty(rule.validationReason) ? "" : rule.validationReason);
            if (technical) text.Append("\nunitId=").Append(unit.id).Append(" team=").Append(unit.team)
                .Append(" equipmentSource=").Append(unit.equipmentSourceId).Append(" bonus=").Append(unit.equipmentDamageBonus);
            return text.ToString();
        }
    }
}
