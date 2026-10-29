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
| 外貌模块与界面/战斗接入 | `-characterModulesSmoke <输出目录>` |
| 固定绑定与局部形变 | `-characterBindingSmoke <输出目录>`、`-characterBindingRefinementSmoke <输出目录>` |
| 原皮影绘制矩阵与关节 | `-paperRegressionDir <输出目录>` |
| 社会重构与冷启动恢复 | `-claudeSocietySmoke <输出目录>`，配合 `scripts/prepare_claude_society_acceptance.py` |
| 模型筹划、信条、口吻、人物回应与来访 | `-claudeNpcSmoke <输出目录>`，配合 `scripts/prepare_claude_npc_acceptance.py`；冷启动加 `-npcRestore 1`，可见窗口截图加 `-npcCapture 1` |
| 正式物理接触机制 | `scripts/validate_reference_mechanics.ps1`，执行前传入实际待验收的成品路径 |
| 独立恢复工程装备与生命周期 | 见 [BLOODY_FULL_PORT_STATUS.md](BLOODY_FULL_PORT_STATUS.md) |

普通窗口截图需要实际绘制。隐藏窗口黑屏不能作为视觉通过；直接 Camera/RenderTexture 诊断也不能代替交互窗口验收。人物图形检查须区分硬片比例、柔性区形变、关节覆盖、透明度与层序，不能只凭 UV 固定、alpha 正确或权重和为 1 宣布绑定完成。

## 保留的历史证据

原始 JSON、截图与运行输出继续保存在原位置。部分本机文件未入库，公开检出缺少这些文件时须重新运行，不能补写通过结果。

| 对应记录 | 证据与边界 |
| --- | --- |
| 2026-10-29 Claude PR #19–#26 融合 | `validation/claude-pr19-26-integration-20261029.json`。成品 `ClaudeNpc20261029`：712 项规则回归、NPC 新功能及冷启动验收；本机 Qwen3.5 的信条、口吻、人物回应及来访各有通过样本。测试使用独立存档；样本不代表长期模型质量或推演速度。 |
| 2026-10-29 正式战斗握持与头部修复 | `validation/combat-hands-head-fixed-20261029.json`。对应成品 `CombatHandsHeadFixed20261029`：正式绑定 122、三入口保存结算 81、源图 97、冷启动 17 全通过；经日常启动器再次通过 122。九类持械握持全通过，整体长回放仍有九类角度限位失败（34/43）。 |
| 2026-10-29 社会功能整合 | `validation/claude-pr13-18-integration-20261029.json`。对应成品 `ClaudeSociety20261029Final`：规则回归 670 项，原生功能/冷启动 28 项，旧档兼容 5 项，账目 5 项。账目原始残差含测试人物带入的 200 银，扣除测试资本后为 0；未复跑旧文档的 36/60 月对照。 |
| 正式源图候选 | [BLOODY_SOURCE_PORT_VALIDATION.md](BLOODY_SOURCE_PORT_VALIDATION.md) 与 `validation/bloody-source-rig-integration-20261029.json`。保留流程通过及长回放失败，不能用其中一类覆盖另一类。 |
| 人物绑定修订 | `validation/character-binding-refinement-native-20261029.json`、`validation/character-binding-refinement-comparison-20261029.json`、`validation/character-binding-refinement-regression-20261029.json`。记录对应旧构建的同输入测量；旧成品清理后应重建并记录新指纹。 |
| 比例与原绑定 | `validation/character-proportions-native-20261029.json`、`validation/character-binding-native-20261029.json`。视觉修复与物理握持分别记录。 |
| 原始绑定研究 | [BLOODY_BINDING_STUDY.md](BLOODY_BINDING_STUDY.md) 和 `scripts/inspect_bloody_binding.py`，属于指定 APK 的静态与数学分析。 |

## 仍需追踪的失败

1. **正式源图的持械长回放未通过**：10 类兵器、最长 180 秒的 34 项压力验收整体失败，部分存在米级关节分离和多圈角度异常。验收仍使用 0.15 米间隙、8 度过冲上限。原始证据和下一步见正式源图文档；不放宽上限，也不用贴图拉伸掩盖物理分离。
2. **绑定回归曾有 1 项握持失败**：对应 `BindingRefined20261029` 的原绑定回归为 128/129；单手剑实际握持分离峰值约 0.715604 世界单位，绘制手尖仍与物理手尖吻合。局部形变限制 0.65～1.35 是本项目工程选择，不是源游戏参数；性能量度只覆盖同步 Render 的受控范围。
3. **完整恢复尚未证明与原作动态等价**：独立工程的完整回合交互、原作同输入轨迹、随机调用顺序及全部依赖仍需持续核验。以[独立恢复工程状态](BLOODY_FULL_PORT_STATUS.md)及各具体构建的机器证据为准。
4. **长期社会与模型质量需要独立实验**：同种子前后对照要保留人口、月份、模型、配置与统计分母。少量决策样例、一次短时原生检查或历史通过项数量不能推出长期平衡或普遍性能改善。

## 记录要求

每次有意义的功能验收记录源码/成品指纹、Unity 与 Python 版本、独立存档来源、命令、输入规模、结果、失败与跳过项。持久化检查包含退出后的独立进程恢复；玩法检查验证真实财物、日历、事件与状态变化。

日常维护更新本文和对应专题文档，原始结果写入 `validation/` 或本地 `output/`，避免为每个中间构建新增一份根目录说明。

部分历史 JSON 的 report 字段或 Git 状态快照保留当时的旧文档名称。这些字段属于原始快照，不改写为新的验收结论；当前开发入口使用本文，战斗旧审计与核心恢复的后续状态统一见正式战斗接入文档。
