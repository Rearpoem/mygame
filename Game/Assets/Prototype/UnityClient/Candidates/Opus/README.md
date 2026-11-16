# C · 刻碑指挥台

依据完整 `outputs/ui_candidates_20261115/designs/opus/DESIGN.md`（611 行，53,803 字节）、公共简报与 `UI-RESUMPTION-20261115-v2`。五张临时公共头像源为真实 256×256，不宣称交付了稿中建议的 512 源图。边框、徽记、范围/状态文字和交锋线采用原稿允许的原生 uGUI/TMP；可选装饰与动效未启用。

注册 `OpusCandidateView` / `[TacticsCandidate("opus", "C · 刻碑指挥台")]`，命名空间 `Prototype.UnityClient.Candidates.Opus`。仅视图：一个共享 workbench、raw 表单、已提交战斗与时钟。初始化/刷新不选择规则、不写表单、不推进。`RefreshView` 和尺寸回调只置 dirty，安全 Update 才投影；输入/弹窗键盘没有单步或排序快捷键。

编排是队员栏、中央规则句栈及四段同屏句式条、右常驻技能目录；编辑详情只切换当前段，raw 修改保持。技能行只切换预览，点击「设为行动」才填第④段。ratio 输入是百分数，10 对应 raw `0.1`，非比例保留真实单位，非法文本保留。替换/新增由共享核心实际验证并返回拒绝原因；视图不复制另一套规则验证器。超载项保留并标示，满槽仍可替换或删除。

战斗是左纵向 WAIT、中敌上我下双阵与交锋线、右事件流及渐进因果/角色抽屉。敌方只将显示行翻为 R2→R0，己方 R0→R2；原 row/column 与实际 targetIds 不变。历史前后值与当前棋盘明确区分，覆盖与附加效果分组；跟随最新使用共享 SetFollowLatest。无独立模拟或自动推进。角色完整详情含当场已提交规则、状态/到期、tags、有效敏捷。头像真实 Sprite 明确 alpha=1。

共享壳保留原 40px 四入口导航，本视图加 44px 品牌/页签带，不重复建立实验菜单。仅 C 的按高度缩放由 Hub owner 实现，视图不改全局 CanvasScaler。52px 规则卡采用 24/22px 两行盒；阵位卡 92px 内姓名22px、13号资源各20px、12号状态20px（Noto face92.672/point64），没有通过统一缩字塞内容。内部字段折叠；长句 tooltip 和局部滚动可达。

真实操作与 QA 句柄：

- `EditorTab` / `BattleTab`；`RuleButtons[0]`（`Opus_Rule_0`）。点规则后等待呈现帧，再取 `Terms[0].ThresholdInput` / `KindButton`。
- `SegmentButtons[0..3]` 切换当前段。条件/选择器按钮打开 `CatalogPopup`，搜索后点击真实 `Opus_Catalog_<optionId>`；键盘上下/Enter 仅用于该目录。
- `ActionSearchInput` → `SkillButtons[actionId]`（仅预览）→ `SetActionButton` → `ReplaceButton` → `ApplyButton`（实际共享提交并自动播放）。单步前先 `PauseButton`，之后 `StepButton` / `ReplayButton`。
- `FriendlyBoard/EnemyBoard.CellButtons` 按核心 row*3+column 索引，点击打开真实抽屉，`CloseInspectorButton`/Esc 关闭。`EventButtons` 为倒序事件列表；`FollowLatestButton`、`DiagnosticsButton` 与实际候选展开按钮可读且可点。
- `SendButton` / `ResetButton` 必须点击 `ConfirmButton`（`Opus_ConfirmDialog`）才执行。Runtime QA 不能绕过确认直接调用共享命令来声称 UI 路径通过。`CompareButton` / `ExportButton` 打开实际结果，`DismissModalButton` 关闭；`ExportPathInput` 为可复制只读路径。

编码自检采用本机实际 Unity DLL 的静态编译；它不代替 Unity PlayMode、严格射线、原生 EXE 双尺寸渲染或模型审核。九份源先在 work 草拟，经 base 释放窗口后才同步到 Opus 独占目录。本模块未运行 Unity/原生 GUI，未发送 round2 审核。
