# mygame

Unity 条件指令战斗、基地经济与出征联动的开发原型。规则核心使用纯 C#，场景、界面与资源位于 `Game/`；测试数值仍可替换，不是正式游戏的平衡结果。

**截至 2026-11-16：最新 A/B 试玩包在用户关闭窗口时出现 UnityPlayer 原生崩溃，尚未修复。这是开发基线，不是稳定发布版。C「刻碑指挥台」已有设计和实现草稿，正式审核因配额暂停，默认试玩入口不开放 C，也不自动恢复审核。**

## 项目入口

- [当前项目上下文](docs/context/PROJECT_CONTEXT.md)：已确认玩法、进度、后续工作和协作边界。
- [工程与模块说明](Game/README.md)：配置、核心 API、集成方式及分轮历史验证。
- [历史 Windows 试玩说明](Game/PLAY_WINDOWS.md)：历史原型的操作说明，不代表最新关闭故障已解决。
- [战斗核心](Game/Assets/Prototype/Combat/README.md)、[基地经济](Game/Assets/Prototype/BaseEconomy/README.md)、[出征联动](Game/Assets/Prototype/Expedition/README.md)。

## 源码结构

| 路径 | 内容 |
| --- | --- |
| `Game/Assets/Prototype/Combat` | WAIT 战斗、条件指令、技能、状态与测试 |
| `Game/Assets/Prototype/BaseEconomy` | 基地经济、采购与强化会话 |
| `Game/Assets/Prototype/Expedition` | 出征、结算、备战和下一场战斗联动 |
| `Game/Assets/Prototype/Tactics` | 草稿、上场配置、对照和共享战术工作台 |
| `Game/Assets/Prototype/UnityClient` | GUI、A/B 候选与保留的 C 草稿源码 |
| `Game/Assets/StreamingAssets/Prototype` | 可配置的战例与经济数据 |
| `Game/Assets/Prototype/Art` | 必要字体和临时头像，连同导入 `.meta` |
| `Game/Packages`、`Game/ProjectSettings` | 包锁定与工程设置 |
| `tools` | 顺序验证和 Windows 打包脚本 |

保留 C 源码用于后续恢复，不代表 C 审核通过或本轮恢复开发。

## 工程环境与构建

使用 **Unity 6000.3.28f1**，revision 见 [ProjectVersion.txt](Game/ProjectSettings/ProjectVersion.txt)。在 Unity Hub 中添加仓库下的 `Game` 文件夹；入口场景为 `Assets/Prototype/Scenes/PrototypeHub.unity`。依赖版本由 `Packages/manifest.json` 和 `packages-lock.json` 固定。

在仓库根目录的 PowerShell 中，最小 Windows 构建命令如下；将 Editor 路径换成本机实际安装位置，输出目录必须尚不存在：

```powershell
.\tools\Run-PrototypeChecks.ps1 -Stage Build -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe' -OutputDirectory 'outputs\manual_build'
```

该脚本也支持 `Generate`、`EditMode`、`PlayMode`、`Examples` 和 `All`。请显式提供 `-UnityPath`，不要依赖脚本中的历史安装路径默认值。`tools/Package-PrototypeWindows.ps1` 对已成功构建的 Windows 目录打包和核对解压文件，不替代运行验收。

当前先处理原生退出故障。本次仓库整理没有重新运行 Unity、Player、测试或构建，没有修改游戏代码。

## 验证边界与下一步

最新 A/B 基线此前完成 EditMode **329/329**、PlayMode **30/30**，五阶段检查退出码均为 0；双尺寸画面和普通窗口 A/B 切换、按钮与暂停交互已有验证。这些历史结果没有覆盖随后用户右键关闭窗口时触发的故障，不能作为关闭路径稳定性的证明。

下一步由工程会话在既有离线审查基础上确定修复与验证方案，重点关注退出清理、原生窗口回调及 UI Automation 相关证据。事故原始日志、dump、Windows 报告和测试原始记录留在本地 `outputs/`，不纳入本仓库。

项目上下文仅在用户明确要求时维护。主会话和专项会话负责讨论、调度与审阅，实际编码、取证、测试和打包由执行 Agent 完成；世界观候选、外部 UI 设计和研究资料不自动成为已采纳需求。

## 资源与版本管理

必要头像、中文字体和 `.meta` 随源码提交。Noto Sans CJK 字体来源与授权见 [SOURCE.txt](Game/Assets/Prototype/Art/Fonts/SOURCE.txt) 和 [OFL.txt](Game/Assets/Prototype/Art/Fonts/OFL.txt)。项目代码和其余资产尚未选定对外开源许可证，第三方资源仍按各自授权使用。

`Library/`、`Temp/`、本机设置、生成测试报告、构建 EXE/ZIP、模型通信附件、缓存、崩溃转储及个人诊断日志均被忽略。历史说明中指向 `outputs/` 的链接是本地证据入口，首次克隆不会包含这些文件；当前上下文与构建入口不依赖外部研究工作区。
