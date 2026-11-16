using System;
using System.Globalization;
using System.Text;

namespace Prototype.Combat
{
    /// <summary>Third-round experiments use the same validated edits and battle engine as the GUI.</summary>
    public static class CombatTacticsSamples
    {
        public static CombatComparisonReport CompareMpReserve(CombatScenario source, string unitId = "mage",
            int instructionIndex = 1, int termIndex = 0, double reserveThreshold = 0.75)
        {
            var baseline = new CombatSession(source);
            var variant = new CombatSession(source);
            CombatScenario before = baseline.GetScenario();
            CombatInstruction changed = FindUnit(variant.GetScenario(), unitId).instructions[instructionIndex];
            CombatConditionTerm condition = changed.condition.terms[termIndex];
            if (condition.kind != "mp_ratio_at_least" || condition.subject != "actor")
                throw new ArgumentException("预留 MP 对照需要显式读取 actor 的 mp_ratio_at_least 条件。");
            double oldThreshold = condition.threshold;
            condition.threshold = reserveThreshold;
            CombatEditResult edit = variant.ReplaceInstruction(unitId, instructionIndex, changed);
            if (!edit.success) throw new ArgumentException(edit.message);
            CombatScenario normalized = variant.GetScenario();
            FindUnit(normalized, unitId).instructions[instructionIndex].condition.terms[termIndex].threshold = oldThreshold;
            return CombatSampleRuns.RunComparison(source.id + ":mp-reserve", "不预留 MP", "预留 MP 急救",
                "仅修改 " + unitId + ".instructions[" + instructionIndex + "].condition.terms[" + termIndex +
                "].threshold：" + Number(oldThreshold) + " → " + Number(reserveThreshold) +
                "；subject 始终为 actor，急救规则、费用、行动顺序、技能、敌情与初始状态不变",
                CombatSampleRuns.Equivalent(before, normalized), baseline, variant);
        }

        public static CombatComparisonReport CompareFocusMark(CombatScenario source, string unitId = "ally",
            int instructionIndex = 0, string focusSelector = "vulnerable_first")
        {
            var baseline = new CombatSession(source);
            var variant = new CombatSession(source);
            CombatScenario before = baseline.GetScenario();
            CombatInstruction changed = FindUnit(variant.GetScenario(), unitId).instructions[instructionIndex];
            string oldSelector = changed.selector;
            changed.selector = focusSelector;
            CombatEditResult edit = variant.ReplaceInstruction(unitId, instructionIndex, changed);
            if (!edit.success) throw new ArgumentException(edit.message);
            CombatScenario normalized = variant.GetScenario();
            FindUnit(normalized, unitId).instructions[instructionIndex].selector = oldSelector;
            return CombatSampleRuns.RunComparison(source.id + ":focus-mark", "最低 HP 比例目标", "标记目标优先",
                "仅修改 " + unitId + ".instructions[" + instructionIndex + "].selector：" + oldSelector + " → " +
                focusSelector + "；法师标记规则、状态、费用、技能、敌情与初始状态不变",
                CombatSampleRuns.Equivalent(before, normalized), baseline, variant);
        }

        public static CombatComparisonReport CompareEmergencyThreshold(CombatScenario source, string unitId = "mage",
            int instructionIndex = 0, int termIndex = 0, double newThreshold = 0.10)
        {
            var baseline = new CombatSession(source);
            var variant = new CombatSession(source);
            CombatScenario before = baseline.GetScenario();
            CombatInstruction changed = FindUnit(variant.GetScenario(), unitId).instructions[instructionIndex];
            CombatConditionTerm condition = changed.condition.terms[termIndex];
            if (condition.kind != "hp_ratio_at_most" || condition.subject == "actor")
                throw new ArgumentException("默认急救对照需要读取候选目标的 hp_ratio_at_most 条件。");
            double oldThreshold = condition.threshold;
            condition.threshold = newThreshold;
            CombatEditResult edit = variant.ReplaceInstruction(unitId, instructionIndex, changed);
            if (!edit.success) throw new ArgumentException(edit.message);
            CombatScenario normalized = variant.GetScenario();
            FindUnit(normalized, unitId).instructions[instructionIndex].condition.terms[termIndex].threshold = oldThreshold;
            return CombatSampleRuns.RunComparison(source.id + ":emergency-threshold",
                "急救阈值 " + Number(oldThreshold * 100) + "%", "急救阈值 " + Number(newThreshold * 100) + "%",
                "仅修改 " + unitId + ".instructions[" + instructionIndex + "].condition.terms[" + termIndex +
                "].threshold：" + Number(oldThreshold) + " → " + Number(newThreshold),
                CombatSampleRuns.Equivalent(before, normalized), baseline, variant);
        }

        public static string FormatComparisonMarkdown(CombatComparisonReport report)
        {
            string content = CombatSampleRuns.FormatComparisonMarkdown(report)
                .Replace("全部数值均为第二轮试玩参数", "全部数值均为第三轮试玩参数");
            var rules = new StringBuilder("## 实际四段指令\n\n|角色与序号|基线|变体|\n|---|---|---|\n");
            CombatSnapshot baseline = new CombatSession(report.baselineConfiguration).GetSnapshot();
            CombatSnapshot variant = new CombatSession(report.variantConfiguration).GetSnapshot();
            foreach (CombatUnitSnapshot unit in baseline.units)
            {
                if (unit.team != PlayerTeam(report.baselineConfiguration)) continue;
                CombatUnitSnapshot other = FindUnit(variant, unit.id);
                for (int i = 0; i < unit.instructions.Length; i++)
                    rules.Append('|').Append(unit.id).Append('[').Append(i).Append("]|")
                        .Append(Cell(unit.instructions[i].display)).Append('|')
                        .Append(Cell(other.instructions[i].display)).Append("|\n");
            }
            rules.Append("\n默认条件读取当前候选；只有显式 subject=actor 的 MP 门槛读取施放者。标记由实际 apply_status 施加，vulnerable_first 根据当时仍有效的易伤状态选目标。所有编辑通过 ReplaceInstruction 校验，没有另一套演示结算。\n\n");
            content = content.Replace("## 第一次行动分歧", rules + "## 第一次行动分歧");
            content += "\n## 实际决策后果\n\n" + Consequence(report) + "\n";
            return content;
        }

        private static string Consequence(CombatComparisonReport report)
        {
            string team = PlayerTeam(report.baselineConfiguration);
            int leftHeals = 0, rightHeals = 0;
            double leftFirstHeal = -1, rightFirstHeal = -1;
            foreach (CombatStepResult step in report.baseline.steps)
                if (step.status == "Executed" && step.actionId == "tactics_first_aid")
                { leftHeals++; if (leftFirstHeal < 0) leftFirstHeal = step.time; }
            foreach (CombatStepResult step in report.variant.steps)
                if (step.status == "Executed" && step.actionId == "tactics_first_aid")
                { rightHeals++; if (rightFirstHeal < 0) rightFirstHeal = step.time; }
            var text = new StringBuilder();
            text.Append("基线实际急救 ").Append(leftHeals).Append(" 次");
            if (leftFirstHeal >= 0) text.Append("（首次 WAIT ").Append(Number(leftFirstHeal)).Append('）');
            text.Append("；变体实际急救 ").Append(rightHeals).Append(" 次");
            if (rightFirstHeal >= 0) text.Append("（首次 WAIT ").Append(Number(rightFirstHeal)).Append('）');
            text.Append("。\n\n");
            if (report.firstDivergence != null && report.firstDivergence.baseline != null && report.firstDivergence.variant != null &&
                report.firstDivergence.baseline.actorId == report.firstDivergence.variant.actorId)
                text.Append("首次分歧行动后，").Append(report.firstDivergence.baseline.actorId).Append(" 的 MP 为 ")
                    .Append(report.firstDivergence.baseline.mpAfter).Append("／").Append(report.firstDivergence.variant.mpAfter)
                    .Append("（基线／变体）。\n\n");
            AppendStatusEvents(text, report.baselineLabel, report.baseline.steps);
            AppendStatusEvents(text, report.variantLabel, report.variant.steps);
            string markedTarget = null;
            foreach (CombatStepResult step in report.baseline.steps)
                if (step.status == "Executed" && step.actionId == "tactics_focus_mark") { markedTarget = step.targetId; break; }
            if (markedTarget != null)
                text.Append("被标记的 ").Append(markedTarget).Append(" 实际行动次数：")
                    .Append(ActorActions(report.baseline.steps, markedTarget)).Append("／")
                    .Append(ActorActions(report.variant.steps, markedTarget)).Append("（基线／变体）。\n\n");
            foreach (CombatUnitSnapshot unit in report.baseline.snapshot.units)
            {
                if (unit.team != team) continue;
                CombatUnitSnapshot other = FindUnit(report.variant.snapshot, unit.id);
                text.Append(unit.id).Append(" 战后 HP：").Append(unit.hp).Append("／").Append(other.hp)
                    .Append("；MP：").Append(unit.mp).Append("／").Append(other.mp).Append("（基线／变体）。\n\n");
            }
            text.Append("每次只改一个配置字段；首次分歧及后续伤害、急救和死亡均来自上面的实际事件。战果不是预先写入报告的预期值。");
            return text.ToString();
        }

        private static void AppendStatusEvents(StringBuilder text, string label, CombatStepResult[] steps)
        {
            foreach (CombatStepResult step in steps)
                foreach (CombatEffect effect in step.effects ?? Array.Empty<CombatEffect>())
                    if (effect.kind == "apply_status")
                        text.Append(label).Append("，WAIT ").Append(Number(step.time)).Append("：")
                            .Append(effect.description).Append("。\n\n");
        }

        private static int ActorActions(CombatStepResult[] steps, string actorId)
        {
            int count = 0;
            foreach (CombatStepResult step in steps) if (step.status == "Executed" && step.actorId == actorId) count++;
            return count;
        }

        private static CombatUnitConfig FindUnit(CombatScenario scenario, string id)
        {
            foreach (CombatUnitConfig unit in scenario.units) if (unit.id == id) return unit;
            throw new ArgumentException("找不到角色：" + id);
        }

        private static CombatUnitSnapshot FindUnit(CombatSnapshot snapshot, string id)
        {
            foreach (CombatUnitSnapshot unit in snapshot.units) if (unit.id == id) return unit;
            throw new ArgumentException("找不到快照角色：" + id);
        }

        private static string PlayerTeam(CombatScenario scenario)
        {
            return string.IsNullOrEmpty(scenario.playerTeam) ? scenario.units[0].team : scenario.playerTeam;
        }

        private static string Cell(string text) { return (text ?? string.Empty).Replace("|", "\\|").Replace("\n", " "); }
        private static string Number(double value) { return value.ToString("0.###############", CultureInfo.InvariantCulture); }
    }
}
