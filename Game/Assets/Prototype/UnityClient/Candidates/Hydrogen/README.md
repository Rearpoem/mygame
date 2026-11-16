# B · 星象秘库

实现依据 `outputs/ui_candidates_20261115/designs/hydrogen/DESIGN.md` 与 `UI-RESUMPTION-20261115-v1`。

`HydrogenCandidateView` 注册为 `hydrogen`，挂载合同为 `Initialize(font, TacticsSharedController)` / `RefreshView()`。Host 提供扣除 40px 公共导航后的全幅空间；本视图仅建立 56px 子页状态带。页面一是队员/可见优先级栈/完整四段与技能检索三栏，页面二是 WAIT 轨、双 3×3 与中央行动桥、底部事件流及因果/单位详情双标签。

所有编辑、提交、排序、搜索、单步、自动、暂停、重放、对照、导出与发送出征都直接调用唯一共享控制器。没有独立 workbench、CombatSession、计时器或模拟。初始化、刷新及候选切换不选择规则、不写表单、不推进战斗。切离本视图/播放页会暂停。

比例条件输入显示百分数（25% 对应共享 raw `0.25`）；其他数值保留目录真实单位。无效输入字符串完整保留给共享验证。满槽可选已有规则替换或删除，未提交切换有保存/丢弃/取消提示。搜索不会写入草稿；无匹配时明确说明，已选技能保留并单独标记。

棋盘始终读取当前实际快照，R0/R1/R2 自上而下。边框使用共享 `highlightedTargetIds`，不把未覆盖锚点或汲魔返还者加入主目标。旧历史事件显示真实前后值、规则尝试及逐候选原因；横幅明确棋盘保持最新时刻。技术字段默认折叠，点击角色查看完整名字、状态、标签和有效速度。

五张头像通过共享 `GetPortrait(unitId)` 使用已批准的临时公共原图。边框、资源条、状态简称、行动箭头均为原生 uGUI/TMP；可选纹理/图标未生成，缺失头像显示原生职业首字占位，不代表正式美术定案。

主控件：`AssemblyTabButton`、`BattleTabButton`、`InstructionButtons`、`Terms`、`ReplaceButton`、`ApplyBattleButton`、`PauseButton`、`StepButton`、`ReplayButton`、`ActionSearchInput`、`SkillButtons`、`FriendlyBoard`、`EnemyBoard`、`EventButtons`、`ReasonText/ReasonScroll`。所有句柄均指向真实画面控件。

静态编译、Unity 回归和双分辨率原生图由集成 owner 统一执行；本视图不自行启动 Unity 或原生 GUI。旧第四轮测试属于历史，当前视图在拿到本轮真实验证前不宣称通过。正式模型审核尚未发送，需 Harness 真实修复、本地检查及截图就绪后由 root 明确授权。
