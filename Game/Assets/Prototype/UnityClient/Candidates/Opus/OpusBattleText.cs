using System;
using System.Linq;
using System.Text;
using Prototype.Combat;

namespace Prototype.UnityClient.Candidates.Opus
{
    internal static class OpusBattleText
    {
        internal static string StatusName(string kind)
        {
            switch (kind) { case "guard": return "护盾"; case "magic_guard": return "魔盾"; case "empowered": return "增幅"; case "vulnerable": return "易伤";
                case "stunned": return "眩晕"; case "silenced": return "沉默"; case "weakened": return "虚弱"; case "haste": return "加速"; case "slow": return "减速"; default: return kind ?? "状态"; }
        }
        internal static string ShortStatuses(CombatStatusConfig[] statuses)
        {
            var actual = statuses ?? Array.Empty<CombatStatusConfig>();
            return actual.Length == 0 ? "无状态" : string.Join(" ", actual.Take(2).Select(status => StatusName(status.kind) + "→" + OpusUi.N(status.expiresAt))) + (actual.Length > 2 ? " +" + (actual.Length - 2) : "");
        }
        internal static string Name(CombatSnapshot snapshot, string id) { return snapshot?.units.FirstOrDefault(unit => unit.id == id)?.name ?? id ?? "—"; }
        internal static string Effect(CombatEffect effect, Func<string, string> statusName, bool technical)
        {
            string text = effect.targetName + "  HP " + effect.hpBefore + "→" + effect.hpAfter + "  MP " + effect.mpBefore + "→" + effect.mpAfter;
            if (!string.IsNullOrEmpty(effect.statusId)) text += (effect.kind == "status_expired" ? "\n状态到期：" : "\n获得状态：") + statusName(effect.statusId)
                + " · WAIT " + OpusUi.N(effect.statusAppliedAt) + "→" + OpusUi.N(effect.statusExpiresAt);
            if (effect.removedStatusIds != null && effect.removedStatusIds.Length > 0) text += (effect.kind == "cleanse" ? "\n净化：" : effect.kind == "dispel" ? "\n驱散：" : "\n移除：") + string.Join("、", effect.removedStatusIds.Select(statusName));
            if (effect.baseVulnerableBefore != effect.baseVulnerableAfter) text += "\n基础易伤 " + effect.baseVulnerableBefore + "→" + effect.baseVulnerableAfter;
            if (technical) text += "\nkind=" + effect.kind + " targetId=" + effect.targetId + " statusId=" + effect.statusId + " removed=[" + string.Join(",", effect.removedStatusIds ?? Array.Empty<string>()) + "]\n" + effect.description;
            return text;
        }
        internal static string Unit(CombatUnitSnapshot unit, int committed)
        {
            if (unit == null) return "无当前角色。";
            var text = new StringBuilder(unit.name + "\n" + unit.team + " · (" + unit.row + "," + unit.column + ")\nHP " + unit.hp + "/" + unit.maxHp + " · MP " + unit.mp + "/" + unit.maxMp);
            text.Append("\n智力 ").Append(unit.intelligence).Append(" · 下次 WAIT ").Append(OpusUi.N(unit.nextActionTime))
                .Append("\n有效敏捷系数 ").Append(OpusUi.N(unit.effectiveAgilityFactor)).Append("\n标签：").Append(string.Join("、", unit.tags ?? Array.Empty<string>()));
            foreach (var status in unit.statuses ?? Array.Empty<CombatStatusConfig>()) text.Append("\n").Append(StatusName(status.kind)).Append(" · 强度 ").Append(status.magnitude).Append(" · WAIT ").Append(OpusUi.N(status.appliedAt)).Append('→').Append(OpusUi.N(status.expiresAt));
            text.Append("\n\n上场 v").Append(committed).Append(" 指令（只读）");
            foreach (var rule in unit.instructions ?? Array.Empty<CombatInstructionSnapshot>()) text.Append("\n\n").Append(rule.display).Append(rule.capacityAllowed && rule.actionAvailable ? "" : "\n" + rule.validationReason);
            return text.ToString();
        }
    }
}
