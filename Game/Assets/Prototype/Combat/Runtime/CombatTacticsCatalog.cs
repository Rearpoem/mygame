using System;

namespace Prototype.Combat
{
    [Serializable]
    public sealed class CombatTacticsOption
    {
        public string id;
        public string label;
        public string description;
        public bool usesThreshold;
        public double minThreshold;
        public double maxThreshold;
        public double defaultThreshold;
        public bool integerThreshold;
        public bool usesValue;
        public string valueHint;
        public string category;
        public string defaultValue;
        public string thresholdUnit;
    }

    /// <summary>Only implemented capabilities are exposed. Public DTOs are independent copies.</summary>
    public static class CombatTacticsCatalog
    {
        private static CombatTacticsOption Plain(string id, string label, string description, string category = "")
        { return new CombatTacticsOption { id = id, label = label, description = description, category = category }; }
        private static CombatTacticsOption Number(string id, string label, double initial, double max, bool integer, string category, string description)
        { return new CombatTacticsOption { id = id, label = label, description = description, category = category,
            usesThreshold = true, minThreshold = 0, maxThreshold = max, defaultThreshold = initial, integerThreshold = integer,
            thresholdUnit = category == "time" ? "wait" : category == "team" ? "count" : category == "position" ? "row" : "points" }; }
        private static CombatTacticsOption Value(string id, string label, string initial, string hint, string category)
        { return new CombatTacticsOption { id = id, label = label, description = hint, category = category,
            usesValue = true, defaultValue = initial, valueHint = hint }; }
        private static CombatTacticsOption Ratio(string id, string label, double initial)
        { CombatTacticsOption option = Number(id, label, initial, 1, false, "resource", "读取所选来源的当前值／最大值；比例0到1。"); option.thresholdUnit = "ratio"; return option; }
        private static CombatTacticsOption Row(string id, string label)
        { return Number(id, label, 0, 2, true, "position", "0=前排，1=中排，2=后排。"); }
        private static CombatTacticsOption Count(string id, string label, int initial, int max, string description)
        { return Number(id, label, initial, max, true, "team", description); }
        private static readonly CombatTacticsOption[] Conditions = {
            Plain("always", "总是", "仍需合法对象与足够资源。", "basic"),
            Ratio("hp_ratio_at_most", "HP比例≤", .35), Ratio("hp_ratio_at_least", "HP比例≥", .5),
            Ratio("mp_ratio_at_most", "MP比例≤", .3), Ratio("mp_ratio_at_least", "MP比例≥", .5),
            Plain("vulnerable", "易伤", "读取永久测试标记或有效vulnerable状态。", "status"),
            Value("has_status", "具有状态", "focus_mark", "按有效状态ID判断，例如focus_mark。", "status"),
            Value("lacks_status", "没有状态", "focus_mark", "按状态ID判断缺少该状态。", "status"),
            Row("row_is", "位于排"), Row("row_at_most", "排号≤"), Row("row_at_least", "排号≥"),
            Number("hp_at_most", "HP≤点数", 30, int.MaxValue, true, "resource", "当前HP绝对值。"),
            Number("hp_at_least", "HP≥点数", 50, int.MaxValue, true, "resource", "当前HP绝对值。"),
            Number("mp_at_most", "MP≤点数", 6, int.MaxValue, true, "resource", "当前MP绝对值。"),
            Number("mp_at_least", "MP≥点数", 12, int.MaxValue, true, "resource", "当前MP绝对值。"),
            Number("hp_missing_at_least", "HP缺口≥", 20, int.MaxValue, true, "resource", "最大HP减当前HP。"),
            Number("mp_missing_at_least", "MP缺口≥", 12, int.MaxValue, true, "resource", "最大MP减当前MP，避免恢复浪费。"),
            Plain("has_buff", "有增益", "guard/empowered/magic_guard/haste中任一有效状态。", "status"),
            Plain("has_debuff", "有减益", "有害动态状态或永久易伤标记。", "status"),
            Plain("has_control", "受控制", "slow/silenced/stunned中任一有效状态。", "status"),
            Value("has_status_kind", "具有状态类别", "guard", "填写真实状态kind；可见GetStatusKinds。", "status"),
            Value("lacks_status_kind", "没有状态类别", "guard", "填写真实状态kind；可见GetStatusKinds。", "status"),
            Value("has_tag", "具有标签", "caster", "读取角色tags，例如caster、armored。", "tag"),
            Value("lacks_tag", "没有标签", "caster", "角色tags不含指定字符串；未配置等于空集合。", "tag"),
            Count("team_alive_at_least", "同队存活数≥", 2, 6, "相对所读对象的team，包含对象自己。"),
            Count("team_alive_at_most", "同队存活数≤", 1, 6, "相对所读对象的team，包含对象自己。"),
            Count("opponents_alive_at_least", "对立队伍存活数≥", 2, int.MaxValue, "所读对象之外team的存活数；施放者敌人数请subject=actor。"),
            Count("opponents_alive_at_most", "对立队伍存活数≤", 1, int.MaxValue, "所读对象之外team的存活数。"),
            Count("team_injured_at_least", "同队伤员数≥", 2, 6, "同team存活且HP未满的人数。"),
            Count("row_alive_at_least", "同排存活数≥", 2, 3, "同team、同row的存活人数，可用于选择群攻锚点。"),
            Count("row_injured_at_least", "同排伤员数≥", 2, 3, "同team、同row的存活伤员数。"),
            new CombatTacticsOption { id="status_remaining_at_most", label="状态剩余WAIT≤", category="time", usesThreshold=true,
                minThreshold=0, maxThreshold=double.MaxValue, defaultThreshold=20, thresholdUnit="wait", usesValue=true, defaultValue="focus_mark",
                valueHint="状态ID", description="状态不存在则false，不把不存在当作剩余0。" },
            new CombatTacticsOption { id="status_remaining_at_least", label="状态剩余WAIT≥", category="time", usesThreshold=true,
                minThreshold=0, maxThreshold=double.MaxValue, defaultThreshold=20, thresholdUnit="wait", usesValue=true, defaultValue="focus_mark",
                valueHint="状态ID", description="按状态绝对到期时刻减当前时刻。" },
            Number("wait_remaining_at_most", "预约剩余WAIT≤", 20, double.MaxValue, false, "time", "max(0,readyAt-now)，不代表角色必能行动。"),
            Number("wait_remaining_at_least", "预约剩余WAIT≥", 20, double.MaxValue, false, "time", "max(0,readyAt-now)，不代表角色必能行动。"),
            Plain("is_actor", "就是施放者", "判断所读对象是否为当前施放者。", "relation"),
            Plain("is_not_actor", "不是施放者", "可排除给自己传蓝等对象。", "relation")
        };
        private static readonly CombatTacticsOption[] Selectors = {
            Plain("configured_order", "配置顺序", "同值按场景顺序。"),
            Plain("lowest_hp_ratio", "HP比例最低", "同值按场景顺序。"),
            Plain("highest_hp_ratio", "HP比例最高", "同值按场景顺序。"),
            Plain("lowest_hp", "HP最低", "当前HP绝对值。"),
            Plain("highest_hp", "HP最高", "当前HP绝对值。"),
            Plain("lowest_mp_ratio", "MP比例最低", "最大MP为0时按0比例。"),
            Plain("highest_mp_ratio", "MP比例最高", "最大MP为0时按0比例。"),
            Plain("front_first", "前排优先", "再按HP比例最低及场景顺序。"),
            Plain("back_first", "后排优先", "再按HP比例最低及场景顺序。"),
            Plain("vulnerable_first", "易伤优先", "再按HP比例最低；无偏好对象仍选择其他合法人。"),
            Plain("lowest_mp", "MP最低", "当前MP绝对值。"),
            Plain("highest_mp", "MP最高", "当前MP绝对值。"),
            Plain("most_hp_missing", "HP缺口最大", "最大HP减当前HP。"),
            Plain("most_mp_missing", "MP缺口最大", "最大MP减当前MP。"),
            Plain("buffed_first", "增益对象优先", "适合驱散；再按HP比例最低。"),
            Plain("debuffed_first", "减益对象优先", "适合净化；再按HP比例最低。"),
            Plain("controlled_first", "受控制者优先", "slow/silenced/stunned；再按HP比例最低。"),
            Plain("unprotected_first", "无防护者优先", "无guard/magic_guard状态者优先，再按HP比例最低。"),
            Plain("highest_intelligence", "智力最高", "当前配置智力；同值按场景顺序。"),
            Plain("lowest_intelligence", "智力最低", "当前配置智力；同值按场景顺序。"),
            Plain("soonest_action", "预约行动最近", "max(0,readyAt-now)最小。"),
            Plain("latest_action", "预约行动最远", "max(0,readyAt-now)最大。"),
            Plain("fastest_speed", "当前速度最快", "完整敏捷系数最小者。"),
            Plain("slowest_speed", "当前速度最慢", "完整敏捷系数最大者。")
        };
        private static readonly CombatTacticsOption[] StatusKinds = {
            Plain("guard", "防护", "增益：通用直接减伤。"),
            Plain("empowered", "增伤", "增益：直接增加输出伤害。"),
            Plain("vulnerable", "易伤标记", "减益：供条件和优先选择识别，未加伤害倍率。"),
            Plain("magic_guard", "魔法屏障", "增益：只减少magic伤害。"),
            Plain("weakened", "虚弱", "减益：直接减少输出伤害。"),
            Plain("haste", "加速", "增益：等待倍率分母增加速度点。"),
            Plain("slow", "减速", "减益／控制：等待倍率分子增加速度点。"),
            Plain("silenced", "沉默", "减益／控制：禁止isMagic动作。"),
            Plain("stunned", "定身", "减益／控制：休眠至清除或到期。")
        };
        private static readonly CombatTacticsOption[] Effects = {
            Plain("damage", "伤害", "实际扣HP。"), Plain("heal", "治疗", "恢复HP，满HP无效。"),
            Plain("apply_status", "施加状态", "固定WAIT的真实状态。"),
            Plain("restore_mp", "恢复MP", "封顶最大MP，满MP无效。"),
            Plain("drain_mp", "汲取MP", "扣目标实际MP，再向施放者返还；不可自吸。"),
            Plain("cleanse", "净化", "清除减益及永久易伤测试标记。"),
            Plain("dispel", "驱散", "清除增益。")
        };
        private static CombatTacticsOption Copy(CombatTacticsOption p)
        {
            return new CombatTacticsOption { id=p.id,label=p.label,description=p.description,usesThreshold=p.usesThreshold,
                minThreshold=p.minThreshold,maxThreshold=p.maxThreshold,defaultThreshold=p.defaultThreshold,integerThreshold=p.integerThreshold,
                usesValue=p.usesValue,valueHint=p.valueHint,category=p.category,defaultValue=p.defaultValue,thresholdUnit=p.thresholdUnit };
        }
        private static CombatTacticsOption[] Copies(CombatTacticsOption[] source)
        { var result=new CombatTacticsOption[source.Length]; for(int i=0;i<result.Length;i++) result[i]=Copy(source[i]); return result; }
        private static bool Contains(CombatTacticsOption[] options, string id)
        { foreach(CombatTacticsOption p in options) if(p.id==id) return true; return false; }
        internal static CombatTacticsOption ConditionDefinition(string id)
        { foreach(CombatTacticsOption p in Conditions) if(p.id==id) return p; return null; }
        public static CombatTacticsOption[] GetConditions() { return Copies(Conditions); }
        public static CombatTacticsOption[] GetSelectors() { return Copies(Selectors); }
        public static CombatTacticsOption[] GetStatusKinds() { return Copies(StatusKinds); }
        public static CombatTacticsOption[] GetEffectKinds() { return Copies(Effects); }
        public static CombatTacticsOption[] GetScopes()
        { return new[] { Plain("self","自身","仅施放者。"),Plain("allies","己方","是否含自身由配置决定。"),Plain("enemies","敌方","其他队伍。") }; }
        public static CombatTacticsOption[] GetOperations()
        { return new[] { Plain("all","全部满足 AND","1到3项，受智力限制。"),Plain("any","任一满足 OR","1到3项，受智力限制。"),Plain("mixed","混合 AND／OR","恰好3项，明确两个连接符和括号。") }; }
        public static CombatTacticsOption[] GetGroupings()
        { return new[] { Plain("left","(A op B) op C","先求A与B。"),Plain("right","A op (B op C)","先求B与C。") }; }
        public static CombatTacticsOption[] GetSubjects()
        { return new[] { Plain("target","对象","默认读取候选。"),Plain("actor","施放者","显式读取施放者，仍占一个条件槽。") }; }
        public static CombatTacticsOption[] GetTargetModes()
        { return new[] { Plain("single","单体","缺省口径。"),Plain("row","同排","同team且在scope的合法成员。"),Plain("column","同列","同team且在scope的合法成员。"),Plain("all","同队全体","同team且在scope的合法成员。") }; }
        public static CombatTacticsOption[] GetDamageTypes()
        { return new[] { Plain("legacy","旧伤害","缺省兼容。"),Plain("physical","物理","通用guard减伤。"),Plain("magic","魔法","通用guard与magic_guard减伤。") }; }
        public static bool IsConditionSupported(string id) { return Contains(Conditions,id); }
        public static bool IsSelectorSupported(string id) { return Contains(Selectors,id); }
        public static bool IsStatusSupported(string id) { return Contains(StatusKinds,id); }
        public static bool IsEffectSupported(string id) { return Contains(Effects,id); }
    }
}
