# 许可范围与资源来源

根目录 [MIT License](LICENSE) 适用于本项目自有源代码、构建脚本、配置和文档。下列第三方或另有说明的内容不因放入此仓库而改授 MIT；Unity Editor、引擎运行库、包依赖和生成模型也不属于本项目的 MIT 授权范围。

## Noto Sans CJK SC 字体

`Game/Assets/Prototype/Art/Fonts/NotoSansCJKsc-Regular.otf` 来自 [Noto CJK 官方仓库](https://github.com/notofonts/noto-cjk)，按 **SIL Open Font License 1.1** 提供。保留的 [OFL.txt](Game/Assets/Prototype/Art/Fonts/OFL.txt) 和 [SOURCE.txt](Game/Assets/Prototype/Art/Fonts/SOURCE.txt) 给出授权与下载来源。由该字体生成的 `PrototypeChinese.asset` 及字体数据同样保留 OFL 适用范围，不改为 MIT。

## Unity / TextMeshPro 导入资源与依赖

`Game/Assets/Prototype/Art/Fonts/TMPResources/` 是从 **com.unity.ugui 2.0.0** 的 TMP Essential Resources 导入的资源。原包采用 **Unity Companion License**；包内版权与许可声明原文保留在 [LICENSES/Unity-uGUI-LICENSE.md](LICENSES/Unity-uGUI-LICENSE.md)，完整条款见 [Unity 官方许可](https://unity.com/legal/licenses/unity-companion-license)。这些资源用于本 Unity 工程，不能把本项目的 MIT 视为脱离 Unity 许可范围的授权。

Unity 模板资源及 `Game/Packages/manifest.json`、`packages-lock.json` 声明的依赖保留各自上游许可。仓库不包含 `Library/PackageCache`、Unity Editor 或 Player 引擎二进制；使用者需按各依赖与 Unity 的适用条款取得和使用它们。

## 五张 AI 生成的临时头像

目录：`Game/Assets/Prototype/Art/UICandidates/Common/Resources/PrototypeUI/Portraits/`。

| 文件 | 项目内来源 |
| --- | --- |
| `unit_mage_256.png` | 复用本地法师试产图，seed 14703 |
| `unit_ally_256.png` | 复用本地护卫试产图，seed 14701 |
| `unit_enemy_front_256.png` | 本地生成的通用前排卫士，seed 26111531 |
| `unit_enemy_mage_256.png` | 本地生成的通用敌方法师，seed 26111532 |
| `unit_enemy_silencer_256.png` | 本地生成的通用蒙面施术者，seed 26111533 |

这五张 256×256 PNG 由本项目使用 ComfyUI 与 **Animagine XL 4.0 Opt** 生成并裁切、处理透明背景；公开前已与实际生成交付清单逐字节核对。它们是临时原型素材，不是从第三方人物素材包导入，也不是已定稿的角色设定。

模型官方来源为 [Cagliostro Lab / Animagine XL 4.0](https://huggingface.co/cagliostrolab/animagine-xl-4.0)。官方模型说明沿用 [CreativeML Open RAIL++-M](https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0/blob/main/LICENSE.md)，其中生成输出条款及适用限制仍需遵守；本仓库不分发模型权重。

在本项目能够授予的权利范围内，允许将这五张现有 PNG 随本原型使用、复制、修改和再分发。该素材使用说明不把上游模型许可改为 MIT，不转授任何第三方角色、商标或作品权利，也不承诺生成结果具有排他的版权。新增或来源未确认的素材不自动获得这里的许可。
