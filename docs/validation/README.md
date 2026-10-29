# 验证方法与已知问题

本文维护复现方法和仍有参考价值的证据入口。历史记录只说明对应源码、成品、夹具与输入，不等于当前工作区已重新通过；源码、运行成品及存档应分别确认。

## 日常规则检查

在仓库根目录执行：

```powershell
python -m pip install -r requirements-test.txt
python -X utf8 -m unittest discover -s tests -v
git diff --check
```

CI 在 Windows 与 Linux 上运行规则回归和空白检查，不调用真实模型，也不包含需要本机 Unity 许可证的完整成品构建。只改文档、链接或清理缓存时，核对链接、保留文件与启动依赖即可；规则和资源修改按实际影响运行相关测试。

皮影像素来源与结构检查：

```powershell
python -X utf8 -m unittest discover -s tests -p test_paper_cast.py -v
```

规则源文件变更后先执行 `python unity/prepare.py --backend-only`。交付成品时核对根目录、Unity 同步目录和成品内的对应 Python/JSON 文件，不能以同步命令退出成功代替逐文件核对。

## Unity 验收

所有会交易、过月、导入、交手或保存的验收都使用新的独立存档目录。以下示例从默认启动器选择成品，目录名应在每次运行时更新：

```powershell
.\启动Unity版.bat -saveDir "D:/Desktop/新新游戏/output/manual-smoke-fresh/save" -smokeDir "D:/Desktop/新新游戏/output/manual-smoke-fresh/results"
```

专门探针通过显式参数启用，具体覆盖以相应 `*Smoke.cs` 和输出 JSON 为准：

| 范围 | 参数或入口 |
| --- | --- |
| 通用规则、界面和持久化 | `-smokeDir <输出目录>` |
| 城中舆图、缩放及本地节点 | `-cityAtlasSmokeDir <输出目录>`，配合新的独立 `-saveDir`；检查聚落与城门点击区、无跨地区去路节点、长卷设施及浏览不改变世界 |
| 城中长路线与跨进程恢复 | `scripts/prepare_city_journey_acceptance.py --save-dir <output 下的新目录>`；先运行 `-cityJourneySmokeDir <首段结果>`，退出后用同一独立存档运行 `-cityJourneySmokeDir <续程结果> -cityJourneyPhase resume -cityJourneyProof <首段结果/proof.json>`；检查 35 行动力路线按月分段、只扣实际路程、途中保存和明确过月后的抵达 |
| 天下地图远行 | `-worldTravelSmokeDir <输出目录>`，配合新的独立 `-saveDir`；检查确认、取消、精力扣除、分段远行和目的地舆图 |
| 外貌模块与界面/战斗接入 | `-characterModulesSmoke <输出目录>` |
| 固定绑定与局部形变 | `-characterBindingSmoke <输出目录>`、`-characterBindingRefinementSmoke <输出目录>` |
| 锻造、医术、诗书与部位伤冷启动 | `scripts/prepare_crafts_acceptance.py --save-dir <output 下的新目录>`；同一存档依次以 `-craftsSmoke <结果目录> -craftsPhase main`、`restore`、`verify` 运行，各阶段使用不同结果目录 |
| 手动关节绑定、切图与切磋外观 | `-rigBinderSmoke <输出目录>`，配合新的独立 `-saveDir`；见[关节绑定说明](../design/JOINT_BINDING.md) |
| 原皮影绘制矩阵与关节 | `-paperRegressionDir <输出目录>` |
| 社会重构与冷启动恢复 | `-claudeSocietySmoke <输出目录>`，配合 `scripts/prepare_claude_society_acceptance.py` |
| 模型筹划、信条、口吻、人物回应与来访 | `-claudeNpcSmoke <输出目录>`，配合 `scripts/prepare_claude_npc_acceptance.py`；冷启动加 `-npcRestore 1`，可见窗口截图加 `-npcCapture 1` |
| 正式物理接触机制 | `scripts/validate_reference_mechanics.ps1`，执行前传入实际待验收的成品路径 |
| Physics Core 2D / 旧后端同输入比较 | `scripts/compare_physics_backends.ps1`，同一成品、独立存档、串行测量；见[正式战斗接入](BLOODY_SOURCE_PORT_VALIDATION.md) |
| 独立恢复工程装备与生命周期 | 见 [BLOODY_FULL_PORT_STATUS.md](../research/BLOODY_FULL_PORT_STATUS.md) |

普通窗口截图需要实际绘制。隐藏窗口黑屏不能作为视觉通过；直接 Camera/RenderTexture 诊断也不能代替交互窗口验收。人物图形检查须区分硬片比例、柔性区形变、关节覆盖、透明度与层序，不能只凭 UV 固定、alpha 正确或权重和为 1 宣布绑定完成。

## 保留的历史证据

原始 JSON、截图与运行输出继续保存在原位置。部分本机文件未入库，公开检出缺少这些文件时须重新运行，不能补写通过结果。历史证据对应的成品可能已按构建保留规则清理；需要复跑时，依据记录的源码版本和构建条件重新生成，当前入口始终读取 `unity/Build/active-build.txt`。

| 对应记录 | 证据与边界 |
| --- | --- |
| 2026-10-30 地格移除与分月行走 | `validation/map-no-tiles-20261030.json`。合入 Claude PR #30，保留本机舆图不显示跨地区去路的行为；仅移除地格界面和专用运行资源，保留位置数据、寻路和存档格式。长路线按本月行动力走可负担的路段，过月由玩家明确发起；35 点路线首月扣 30，退出重启后过月，再扣 5 抵达。736 项规则、828 项原生检查通过，覆盖停止与改道、舆图长卷、三种聚落、NPC 活动、天下远行和正式交互战斗。成品 `MapNoTiles20261030Final`，回退为先前默认的 `Current`；只使用独立存档，已复核实际窗口截图。 |
| 2026-10-30 舆图去路入口移除 | `validation/atlas-local-only-20261030.json`。删除城中舆图的跨地区路牌、驿道/航路虚线、对应图例及远行列表，保留本地城门、步道和长卷码头设施。23 项地图/远行规则、48 项舆图原生检查、19 项天下远行检查通过，包含有海路区域且无残留去路点击区；已复核实际窗口截图。默认成品为 `Current`，回退为 `CraftsIntegrated20261029Final`；以原默认版本的独立源码快照构建，仅替换两个舆图脚本，75 个后端文件与原成品一致，个人存档未参与验收。 |
| 2026-10-29 Claude PR #29 技艺 | `validation/claude-pr29-integration-20261029.json`。合入至 `77cc4dc`，新增锻造、医术、诗书及部位伤诊治，保留最新 SourceGuard 战斗修复。737 项规则回归、59 项技艺及两次冷启动、81 项正式玩法、17 项绑定、11 项格挡检查通过；真实默认启动器再通过 8 项，旧验收存档兼容 6 项。默认成品 `CraftsIntegrated20261029Final`，回退 `SourceGuardRelease20261029`；验收只使用独立存档，受控伤害注入不代表战斗平衡。 |
| 2026-10-29 Claude PR #28 关节绑定 | `validation/claude-pr28-integration-20261029.json`。合入至 `415e9ca`，包含自动猜关节、切磋预览、接缝补偿、原画朝向和双方独立外观。715 项规则回归、17 项绑定及正式保存、81 项正式玩法检查通过；真实默认启动器再通过 17 项。成品 `RigBinderLatest20261029` 使用已验收的战斗基线独立构建，正在进行的 Physics Core 改造保留在原工程；示例比例与有界补偿限制仍保留。 |
| 2026-10-29 武器接触卡滞修复 | `validation/combat-contact-release-20261029.json`。成品 `CombatContactFinal20261029`：统一 8 子步、滑动接触材质、手部驱动限速。两轮各 36 个实际回抽检查通过；完整接触探针保留斧头对身体未实际接触的覆盖失败。三正式入口 81、网格 122、源图/冷启动/碰撞机制 97/17/93 通过；同正式实现的十类 AI 最长 60 秒验收 42/42，未重跑 180 秒上限。 |
| 2026-10-29 Claude PR #19–#26 融合 | `validation/claude-pr19-26-integration-20261029.json`。成品 `ClaudeNpc20261029`：712 项规则回归、NPC 新功能及冷启动验收；本机 Qwen3.5 的信条、口吻、人物回应及来访各有通过样本。测试使用独立存档；样本不代表长期模型质量或推演速度。 |
| 2026-10-29 正式战斗握持与头部修复 | `validation/combat-hands-head-fixed-20261029.json`。对应成品 `CombatHandsHeadFixed20261029`：正式绑定 122、三入口保存结算 81、源图 97、冷启动 17 全通过；经日常启动器再次通过 122。九类持械握持全通过，整体长回放仍有九类角度限位失败（34/43）。 |
| 2026-10-29 社会功能整合 | `validation/claude-pr13-18-integration-20261029.json`。对应成品 `ClaudeSociety20261029Final`：规则回归 670 项，原生功能/冷启动 28 项，旧档兼容 5 项，账目 5 项。账目原始残差含测试人物带入的 200 银，扣除测试资本后为 0；未复跑旧文档的 36/60 月对照。 |
| 正式源图候选 | [BLOODY_SOURCE_PORT_VALIDATION.md](BLOODY_SOURCE_PORT_VALIDATION.md) 与 `validation/bloody-source-rig-integration-20261029.json`。保留流程通过及长回放失败，不能用其中一类覆盖另一类。 |
| 人物绑定修订 | `validation/character-binding-refinement-native-20261029.json`、`validation/character-binding-refinement-comparison-20261029.json`、`validation/character-binding-refinement-regression-20261029.json`。记录对应旧构建的同输入测量；旧成品清理后应重建并记录新指纹。 |
| 比例与原绑定 | `validation/character-proportions-native-20261029.json`、`validation/character-binding-native-20261029.json`。视觉修复与物理握持分别记录。 |
| 原始绑定研究 | [BLOODY_BINDING_STUDY.md](../research/BLOODY_BINDING_STUDY.md) 和 `scripts/inspect_bloody_binding.py`，属于指定 APK 的静态与数学分析。 |

## 仍需追踪的失败

1. **正式源图 180 秒压力验收仍待复核**：历史构建的最长 180 秒验收存在米级关节分离和多圈角度异常。握持与接触修复后，本轮十类兵器各最长 60 秒验收 42/42，通过既有 0.15 米间隙与 8 度过冲标准；本轮未重跑 180 秒上限。历史失败及本轮证据均见正式源图文档。
2. **绑定回归曾有 1 项握持失败**：对应 `BindingRefined20261029` 的原绑定回归为 128/129；单手剑实际握持分离峰值约 0.715604 世界单位，绘制手尖仍与物理手尖吻合。局部形变限制 0.65～1.35 是本项目工程选择，不是源游戏参数；性能量度只覆盖同步 Render 的受控范围。
3. **完整恢复尚未证明与原作动态等价**：独立工程的完整回合交互、原作同输入轨迹、随机调用顺序及全部依赖仍需持续核验。以[独立恢复工程状态](../research/BLOODY_FULL_PORT_STATUS.md)及各具体构建的机器证据为准。
4. **长期社会与模型质量需要独立实验**：同种子前后对照要保留人口、月份、模型、配置与统计分母。少量决策样例、一次短时原生检查或历史通过项数量不能推出长期平衡或普遍性能改善。

## 记录要求

每次有意义的功能验收记录源码/成品指纹、Unity 与 Python 版本、独立存档来源、命令、输入规模、结果、失败与跳过项。持久化检查包含退出后的独立进程恢复；玩法检查验证真实财物、日历、事件与状态变化。

日常维护更新本文和对应专题文档，原始结果写入 `validation/` 或本地 `output/`，避免为每个中间构建新增一份根目录说明。

部分历史 JSON 的 report 字段或 Git 状态快照保留当时的旧文档名称。这些字段属于原始快照，不改写为新的验收结论；当前开发入口使用本文，战斗旧审计与核心恢复的后续状态统一见正式战斗接入文档。

## 专题验证说明

以下文档保留原功能开发时的条件、检查与限制；当前整合验收见上文的机器证据。文中的源码和命令路径均以仓库根目录为基准。

| 专题 | 原始说明 |
| --- | --- |
| 二十四人物拆件与绑定 | [CAST24_BINDING_VALIDATION.md](CAST24_BINDING_VALIDATION.md) |
| 人生信条 | [CREED_VALIDATION.md](CREED_VALIDATION.md) |
| 后台思考与人生转折 | [LEAN_THINKING_VALIDATION.md](LEAN_THINKING_VALIDATION.md) |
| 模型参与度 | [LLM_PARTICIPATION_VALIDATION.md](LLM_PARTICIPATION_VALIDATION.md) |
| 人物回应 | [LLM_REACTIONS_VALIDATION.md](LLM_REACTIONS_VALIDATION.md) |
| 模型长期筹划 | [LLM_UNDERTAKINGS_VALIDATION.md](LLM_UNDERTAKINGS_VALIDATION.md) |
| 人物印象 | [NPC_IMPRESSION_VALIDATION.md](NPC_IMPRESSION_VALIDATION.md) |
| 人物来访 | [NPC_VISITS_VALIDATION.md](NPC_VISITS_VALIDATION.md) |
| 人物口吻 | [NPC_VOICE_VALIDATION.md](NPC_VOICE_VALIDATION.md) |
