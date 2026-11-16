using System;
using System.Collections.Generic;
using System.Linq;
using Prototype.Combat;

namespace Prototype.Expedition
{
    public sealed partial class ExpeditionSliceSession
    {
        /// <summary>
        /// Applies friendly instructions, their role whitelist and missing referenced
        /// action definitions to every encounter of this unstarted camp. Staging uses
        /// real CombatSession editing APIs. Existing finances and rewards are untouched.
        /// </summary>
        public SliceCommandResult ApplyTactics(CombatScenario source)
        {
            if (expedition != null)
                return Remember(Command(false, false, "CampRequired", "本营地已创建出征；请全开局重置后再导入战术及所需动作。当前队伍、奖励与账本未改变。", "", 0));
            if (source == null)
                return Remember(Command(false, false, "InvalidTactics", "没有可导入的战术配置。", "", 0));
            try
            {
                var sourceSession = new CombatSession(source);
                CombatScenario tactics = sourceSession.GetScenario();
                string sourceTeam = string.IsNullOrEmpty(tactics.playerTeam) ? tactics.units[0].team : tactics.playerTeam;
                if (sourceTeam != editableCombat.carryTeam)
                    return RejectTactics("FriendlyRosterMismatch", "战术己方标识与当前出征 carryTeam 不一致。");
                CombatUnitConfig[] friendly = tactics.units.Where(unit => unit.team == sourceTeam).ToArray();
                var sourceIds = new HashSet<string>(friendly.Select(unit => unit.id), StringComparer.Ordinal);
                CombatExpeditionConfig staged = CombatExpeditionSession.CopyConfig(editableCombat);
                foreach (CombatEncounterConfig encounter in staged.encounters)
                {
                    CombatScenario destination = encounter.scenario;
                    CombatUnitConfig[] receivers = destination.units.Where(unit => unit.team == staged.carryTeam).ToArray();
                    if (receivers.Length != friendly.Length || receivers.Any(unit => !sourceIds.Contains(unit.id)))
                        return RejectTactics("FriendlyRosterMismatch", encounter.id + "：全部友方 ID 必须与工作台一致；不能替换阵容或只导入一部分角色。");
                    var actions = new List<CombatActionConfig>(destination.actions);
                    foreach (CombatUnitConfig member in friendly)
                    {
                        CombatActionConfig[] available = sourceSession.GetAvailableActions(member.id);
                        var availableIds = new HashSet<string>(available.Select(action => action.id), StringComparer.Ordinal);
                        CombatUnitConfig receiver = receivers.Single(unit => unit.id == member.id);
                        // Explicitly project the real source role whitelist. Legacy R2
                        // inferred skills must not reject the newly imported tactics.
                        receiver.availableActionIds = available.Select(action => action.id).ToArray();
                        receiver.availableActionIdsExplicit = member.availableActionIdsExplicit;
                        // The real engine requires every whitelist id to have a
                        // definition. Keep this learned-skill catalog closed, including
                        // unused learned actions, without importing any enemy skills.
                        foreach (CombatActionConfig definition in available)
                        {
                            CombatActionConfig existing = actions.FirstOrDefault(action => action.id == definition.id);
                            if (existing != null && !CombatSession.ActionDefinitionsEquivalent(existing, definition))
                                return RejectTactics("ActionDefinitionConflict", encounter.id + "：动作 " + definition.id + " 与既有定义不一致；拒绝覆盖共享动作及敌情。");
                            if (existing == null) actions.Add(definition);
                        }
                        foreach (CombatInstruction instruction in member.instructions)
                        {
                            if (instruction == null || !availableIds.Contains(instruction.actionId))
                                return RejectTactics("ActionUnavailable", encounter.id + " / " + member.id + "：指令引用了该角色实际技能目录以外的行动。");
                        }
                    }
                    destination.actions = actions.ToArray();
                    var validator = new CombatSession(destination);
                    foreach (CombatUnitConfig member in friendly)
                    {
                        int count = validator.GetScenario().units.Single(unit => unit.id == member.id).instructions.Length;
                        for (int index = count - 1; index >= 0; index--)
                        {
                            CombatEditResult removed = validator.RemoveInstruction(member.id, index);
                            if (!removed.success) return RejectTactics(removed.code, encounter.id + " / " + member.id + "：" + removed.message);
                        }
                        for (int index = 0; index < member.instructions.Length; index++)
                        {
                            CombatEditResult inserted = validator.InsertInstruction(member.id, index, member.instructions[index]);
                            if (!inserted.success)
                                return RejectTactics(inserted.code, encounter.id + " / " + member.id + " / 第 " + (index + 1) + " 条：" + inserted.message);
                        }
                    }
                    CombatScenario validated = validator.GetScenario();
                    foreach (CombatUnitConfig receiver in receivers)
                    {
                        CombatUnitConfig checkedMember = validated.units.Single(unit => unit.id == receiver.id);
                        receiver.instructions = checkedMember.instructions;
                        receiver.availableActionIds = checkedMember.availableActionIds;
                        receiver.availableActionIdsExplicit = checkedMember.availableActionIdsExplicit;
                    }
                    // No unit stats, enemy data, timing, seed, rewards or mappings are copied.
                }
                CombatScenario preview = CombatSession.CopyScenario(staged.encounters[0].scenario);
                ApplyEquipment(preview, preparation.GetSnapshot());
                CombatSnapshot stagedParty = new CombatSession(preview).GetSnapshot();
                editableCombat = staged;
                partyState = stagedParty;
                return Remember(Command(true, false, "TacticsApplied", "已在未出征营地应用全部友方战术及引用的新动作；按每场实际容量校验，保留敌情、属性、种子、收益及现有公私账本。", "", 0));
            }
            catch (ArgumentException exception)
            {
                return RejectTactics("InvalidTactics", exception.Message);
            }
        }

        private SliceCommandResult RejectTactics(string code, string reason)
        {
            return Remember(Command(false, false, string.IsNullOrEmpty(code) ? "InvalidTactics" : code, reason, "", 0));
        }

    }
}
