# 正式战斗源图接入、保存契约与已知问题

## 2026-10-29：Physics Core 2D 战斗后端与同输入比较

已完成当前战斗的可运行 Core 后端迁移，交付包为 `unity/Build/PhysicsCoreCurrent20261029/`，Unity 为 `6000.6.7f1`。切磋、抢夺、袭击共用实现，包含当日进攻驱动、盾碰撞与当前客户端功能的源码快照。**本机同输入测量中，Core 的控制＋物理 CPU 耗时是旧后端的 5.66 倍，因此本次迁移保留既有默认成品。** 完整数据、成品指纹和原始验收路径见 [physics-core-migration-20261029.json](../../validation/physics-core-migration-20261029.json)。

通过 `开发工具.bat` →「Physics Core 2D 战斗候选（独立存档）」打开普通客户端；「同一候选的旧物理对照」使用同包的旧后端。也可运行 `unity/launch-physics-core.ps1 -Backend core` 或 `-Backend legacy`。两个入口分别使用 `output/physics-core-review/{core,legacy}/save`。已有战局沿用保存时的后端，早期无标记帧沿用旧物理；比较应在各自存档中开启新战局。

Core 路径由 `Unity.U2D.Physics` 创建并求解刚体、形状、关节、接触和触发。现有 `Rigidbody2D` 等组件作为控制、保存与显示的兼容状态，Core 战斗的旧场景模拟次数为零。踢击的传感器对传感器接触由 Core 几何查询补足，保持进入触发和防重复命中规则。当前统一使用 64 个 Core 内部求解子步，单手/徒手关节频率 1000，双手闭环 2000；外层控制仍是 60Hz、8 个统一物理步。旧端保留 50/60 迭代配置。

### 同输入测量

同一成品依次运行两个后端，十类各回放 1200 个控制帧（20 秒）；输入 JSON 的 SHA256 一致。每类前 60 帧不计时，各端合计 11400 个计时样本。CPU 指标包含控制、物理和兼容同步，排除渲染，不能直接换算为游戏帧率。夹具给双方高体力并抑制普通兵器生命伤害，以持续测量动力学；实际损伤另由机制、手动进攻和 AI 验收覆盖。

| 同输入指标 | 旧 Physics2D | Physics Core 2D |
| --- | ---: | ---: |
| 平均控制＋物理 CPU / 控制帧 | 2.812 ms | 15.905 ms |
| 最大角度限位过冲 | 9.870° | 4.742° |
| 最大活动关节间距 | 12.912 mm | 22.241 mm |
| 最大持械握持间隙 | 6.773 mm | 3.576 mm |
| 最大可见柄端偏移 | 4.803 mm | 3.579 mm |
| 活动关节间距及限位样本通过 | 9/10 | 10/10 |
| 九类持械握持及可见柄端通过 | 9/9 | 9/9 |

旧端长枪出现 9.869587° 限位过冲，原 8° 上限保持不变，其原生验收保留失败。Core 在本次样本中降低了最大角度过冲与握持间隙，活动关节间距则更大。结果反映当前兼容层及求解预算的取舍，不能据此作物理内核的普遍性能排名。

| 兵器 | 旧均值 ms | Core 均值 ms | 旧 p95 ms | Core p95 ms |
| --- | ---: | ---: | ---: | ---: |
| 徒手 | 2.56 | 15.60 | 4.11 | 19.22 |
| 匕首 | 2.62 | 15.85 | 4.50 | 18.72 |
| 刀 | 2.87 | 15.99 | 4.86 | 20.14 |
| 单手剑 | 2.91 | 16.34 | 4.85 | 19.90 |
| 单手斧 | 2.66 | 16.12 | 4.45 | 19.80 |
| 单手锤 | 2.66 | 17.05 | 4.45 | 22.49 |
| 长枪 | 3.21 | 16.60 | 5.20 | 21.14 |
| 双手剑 | 2.90 | 15.55 | 4.76 | 20.09 |
| 双手斧 | 2.94 | 15.47 | 4.80 | 23.41 |
| 双手锤 | 2.80 | 14.49 | 4.36 | 19.78 |

### 当前成品验收

Core 共 **419 项原生检查通过，Python 物理与后端状态测试 66 项通过**。旧后端对照的失败不算作 Core 通过项。原始证据根目录为 `output/physics-core-migration-20261029/`：

| 范围 | 结果 | 原始证据 |
| --- | --- | --- |
| 真实碰撞阈值、盾、断肢/死亡与踢击 | 93/93 | `distribution-native/reference-mechanics/` |
| 源图、装备、保存；独立进程恢复 | 97/97、17/17 | `distribution-native/source-rig/`、`source-rig-resume/` |
| 普通客户端切磋、抢夺、袭击、保存与返回结算 | 81/81 | `distribution-formal/`，使用包内 Python |
| 手动剑刃进攻与盾碰撞 | Core 11/11，旧端 11/11 | `distribution-native/guard/`、`distribution-legacy-guard/guard/` |
| 十类真实 AI 回放 | 43/43 | `distribution-ai/reference-stress/` |
| 掉落兵器边界保存、独立进程恢复 | 10/10、15/15 | `distribution-native/save-bounds/`、`save-bounds-resume/` |
| Core 有符号几何距离和只读查询 | 9/9 | `distribution-native/core-query/` |
| 十类同输入动力学 | Core 43/43；旧端 32/33 | `distribution-comparison/` |

当前 AI 回放每类上限 60 秒，死亡后提前结束，十类均观察到实际命中。最大活动关节间距 41.893 mm、限位过冲 5.984°、持械握持间隙 5.880 mm、可见柄端偏移 5.730 mm，分别符合原 .15 米、8°、.03 米、.03 米上限。正式流程验收包含受控损伤和认输结算，用于核对保存、返回与世界状态，不作为平衡性证明。

恢复测试还用 `-combatPhysics legacy` 打开已保存 Core 战局，实际仍使用 Core，保存后端不会在一局内切换。几何查询确认 Core 的分离/穿透距离为 +.5/-.5 米，旧端为 +.49/-.51 米，差值对应旧端 .01 米接触皮层；查询不推进模拟。

源码与包内 70 个 Python 文件指纹分别在 `current-source-hashes.json`、`release-backend-hashes.json`，Windows 全量构建日志为 `distribution-build.log`。早期较低预算的失败原始文件继续保留；早期最长 180 秒数据对应旧逻辑快照，未计入本节当前成品的通过数。此节限定为后端迁移与上述样本验收；完整原作资源和逐帧动态等价仍保留本页既有范围限制。

复现同输入比较：`scripts/compare_physics_backends.ps1 -ShowGame`，选择新的输出目录；汇总使用 `scripts/summarize_physics_comparison.py`。可见对照图片在 `distribution-comparison/{legacy,core}/comparison-sword.png`。

## 2026-10-29：恢复原作进攻驱动与盾碰撞处理（当前交付）

当前交付为 `unity/Build/SourceGuardRelease20261029/江湖未央.exe`，日常 `启动Unity版.bat` 已选择该成品。切磋、抢夺、袭击共用实现。完整指纹、八组同成品对照和启动器进程记录见 `validation/source-guard-restoration-20261029.json`。以下记录取代下一节的 .05/Minimum 与 12/24 米每秒手部限制；历史证据保留。

此前防卡调整过度限制了进攻。我添加的 12/24 米每秒手部限速并非原作行为。实际反编译 `CharacterMover__cln_3435bfc.c`（0x3435bfc）直接调用 `Rigidbody2D.MovePosition`，没有该限制；原提取 HandItem 材质是摩擦 .7、Maximum 合并。现已恢复，原有 Discrete 握持、头部颈铰点绑定，以及本作 60Hz、8 个统一物理子步、50/60 迭代继续保留。后两项仍是混合几何的适配，不能说全部数值与原作相同。

原作双方兵器层 15/16 发生碰撞，并不会无条件互相穿透。`SourcePrefab_HandItem__OnCollisionEnter2D_363f08c.c`（0x363f08c）在接触点速度达到物件提取门槛 5 时处理碰撞：盾事件的体力消耗为 `min(2.5, 碰撞相对线速度 × 对方物件 D / 50)`；持盾方在相对线速度大于 .5 时继承对方物件线速度。`OriginalRig.Combat.cs` 将 `ShieldWasHit` 与 `WeaponHitShield` 均连接到体力消耗。正式桥原先遗漏了这段，现在补回。空手 Nothing 的 Type 7 不等于盾的 category 8，没有把拳头误当盾处理。

兵器碰撞使用独立的正数原物件 D。木剑身体损伤仍保留现有映射，不能把其中可能为负的健康伤害 effectiveD 拿去算盾体力。提取资源现在带有 impact speed threshold 和 clash D；运行时仍按江湖物件属性比例适配，健康伤害公式未改。

| 当前成品验收 | 结果 | 原始证据（相对 `output/source-guard-release-20261029/`） |
| --- | --- | --- |
| 手动输入、真实两方完整源图的进攻/盾碰撞 | 10/10 | `guard/guard/acceptance.json` |
| 同成品旧配置对照 | 预期失败，八组主手剑刃命中均为 0 | `baseline/guard/` |
| 源图、独立进程恢复、真实损伤机制 | 97/97、17/17、93/93 | `source/` |
| 正式切磋/抢夺/袭击、保存、返回与结算 | 81/81 | `formal/acceptance.json` |
| 正式人物网格、头部及握持 | 122/122 | `mesh/character-binding-native.json` |
| 通过日常 BAT 启动后的正式绑定 | 122/122 | `launcher/character-binding-native.json` |
| 装备桥 C# 数值与 Python 物理/源状态 | 117、63、5 通过 | 完整日志索引见交付 JSON |

进攻对照包含真实木剑（身体速度门槛 20）与铁剑（15）、有盾/无盾、两档手动挥击。测试以实际主手剑刃命中为依据，剑柄和副手不算剑刃成功；没有瞬移刚体、关闭敌方碰撞或解除握持。快速无盾样本在实际兵器交锋后分别有木剑 2 次、铁剑 3 次剑刃身体命中；旧配置均为 0。当前八组最大握持间隙 .003429 米。慢挥的剑刃零命中和盾成功防守照实保留，不要求每次挥动都打穿防守。此控制试验给进攻者 1000 体力、守方 100、每例最长 12 秒，用于隔离动力学问题，不代表普通对局耐力平衡。

**早期 4 子步 Core 观察（后续状态见上节）：** 当时的 Core 后端在同类手动进攻样本中尚未产生兵器接触/身体命中，并有约 4–6 厘米握持间隙。因此新对局默认使用已验收 Physics2D；显式 `-combatPhysics core` 及既有 Core 保存帧的选择继续保留。未删除该迁移代码，也未宣称该后端已通过进攻验收。

**仍有长枪抽离边界失败，不能称为全部防卡问题已解决。** 原作材质与驱动配置下，长枪对身体的快速回抽伴随后退为 .3 秒，未达到严格小于 .3 秒；站定仅收手的样本前三秒没有完全脱开。原始证据在 `output/source-original-combat-20261029/contact/binds/`。本次不会再用限制全部兵器进攻来掩盖该问题；完整兵器图替换和原 APK 同输入动态等价也仍未完成。

## 2026-10-29：武器接触卡滞修复正式交付

交付成品为 `unity/Build/CombatContactFinal20261029/江湖未央.exe`，日常启动器选择此构建，切磋、抢夺、袭击共用修复。完整索引与成品指纹见 `validation/combat-contact-release-20261029.json`。

正式配置保持 60Hz 外层控制和 50/60 求解迭代，以 8 个统一物理子步处理整套骨骼与持械关节。刀刃、杆身与护手摩擦为 .05、Minimum 合并，盾为 .25、Minimum 合并。薄刃主手和双手握持驱动限速 12 米/秒，徒手和独立副手限速 24 米/秒，保留原 15/20 米/秒伤害门槛的可达性。这样可防止瞬间跨越数米的 MovePosition 驱动把兵器推过接触面，再被身体或护手夹住。实际旋转、碰撞反力、握持关节与损伤公式继续参与求解。

上一轮的 Discrete 握持修复与头部基点继续保留。子步、接触材质和驱动限速是本作混合几何的运行适配，原提取资源未改写，不能称为原作全部数值同值。

| 范围 | 结果 | 原始证据 |
| --- | --- | --- |
| 源图与保存、独立进程冷启动、真实碰撞机制 | 97/97、17/17、93/93 | `output/combat-contact-final-20261029/source/` |
| 可见正式三入口、保存、返回、结算 | 81/81 | 同验收根目录 `formal/acceptance.json` |
| 共享实际网格与正式战斗握持 | 122/122 | 同验收根目录 `mesh/character-binding-native.json` |
| 两个独立进程的手动回抽 | 各 36/36 个实际接触行为检查通过 | 同验收根目录 `{contact,contact-repeat}/binds/` |
| 当前共享后端回归 | 712 通过 | `output/combat-contact-sliding-python-20261029.log` |

两轮手动回抽各尝试 42 组，实际接触 36 组，其中 31 组在回抽前末 12 帧仍有接触；最慢约 .0667 秒开始持续脱开，最大握持/骨骼间隙 .009896 米。要求保持为握持间隙小于 .03 米、直接回抽小于 .3 秒，未把两秒后的特殊扭转计为通过。**完整接触探针仍有一项覆盖失败：此固定动作下斧头对身体没有实际接触，未计为回抽通过。** 原生报告保留该失败，不能把 42 个尝试全部称为通过。

回抽测量使用真实源身体碰撞体，替代旧 layer 9 空目标集；显式手动输入关闭探针中的源 AI 手部覆盖。兵器对兵器夹具隔离副手拳，匕首对抗长剑，以实际交叉兵器为目标；隔离仅存在于受 IsSmoke 保护的探针，正式游戏保留全部碰撞。

空手及九类兵器各最长 60 秒的真实 AI 交手在 `output/combat-contact-release-20261029/stress/reference-stress/` 完成，42/42 检查通过，十类均产生实际命中，九类持械握持检查均通过。最大持械关节间隙 .008373 米、可见剑柄偏移 .004320 米、限位过冲 4.602 度，均满足既有上限。该验收包与交付包的 822 个 PhysicsDuel 方法已逐条比较，仅两个隔离探针不同，正式物理实现一致；见交付验收根目录 `production-il-comparison.json`。本轮未重跑 180 秒上限，也未进行原 APK 同输入动态等价验收。

可见画面复核为 `output/combat-contact-final-20261029/mesh/03-motion-47.png`。全部验收使用独立存档；正式绑定范围仍为 `-characterBindingScope formal`，不把旧非源图试玩夹具计为通过。

## 2026-10-29：正式战斗握持与头部绑定修复

交付构建为 `unity/Build/CombatHandsHeadFixed20261029/江湖未央.exe`，正式切磋、抢夺、袭击共用本次修复。以下为新构建的原生验收，原始结果保留在 `output/combat-hands-head-fixed-20261029/`，汇总见 `validation/combat-hands-head-fixed-20261029.json`。

- 武器离手的直接原因是本作 `Make()` 创建的武器使用 Continuous，而源手部使用 Discrete。逐物体的 CCD 推进使刚性握持脱节。源图装备桥现统一使用与原选定物件相同的 Discrete，继续使用真实握持关节，没有用绘制瞬移遮盖间隙。
- 原 `Bone_Head` 原点是颈部铰点；旧桥接把它当中心，向下偏移 .16 米。现按公共 `CharacterPose.Bind` 的颈部起点到头顶 .30 米定义接线，保留共享人物渲染和其他界面的绑定。
- 源图和本作武器几何混用时，沿用本作已有的 50 次速度、60 次位置迭代预算；源图保持 60Hz、1 子步。原资源中的 10/10 未改写；50/60 是本作适配，不能称为原作完全同值。

| 本构建范围 | 结果 | 原始证据 |
| --- | --- | --- |
| 共享实际网格与正式源图战斗，包括可见剑柄和手掌 | 122/122 | `mesh/character-binding-native.json` |
| 切磋、抢夺、袭击，保存、暂离、返回与结算，自带 Python | 81/81 | `formal/acceptance.json` |
| 源图、两方头部基点和装备碰撞模式、保存恢复 | 97/97 | `source/source-rig/acceptance.json` |
| 独立进程冷启动恢复及结算 | 17/17 | `source/source-rig-resume/acceptance.json` |
| 九类持械兵器长回放的实际握持与可见剑柄 | 9/9 | `stress/reference-stress/acceptance.json` 中 `held weapon and visible handle remain attached` |

正式网格回放中最大握持关节间隙为 .001950 米，可见剑柄至手部为 .002337 米。最长 180 秒真实 AI 交手的十类兵器回放中，九类持械各有至少 10017 次仍握持状态采样，最大实际握持间隙 .011873 米、可见剑柄偏移 .011476 米，均小于 .03 米要求；合法掉落和死亡后解除的握持不计为仍持械。

验收显式使用 `-characterBindingScope formal`。早期完整范围跑测还包含旧非源图试玩夹具；该夹具在当前 Cast24 颈片上出现零长颈骨异常，未在本次修复中变更共享渲染器或旧试玩适配，也未宣称全范围试玩验收通过。验收的美术测量现从与渲染器一致的目录读取，避免用旧通用手部顶点测量 Cast24 网格而产生假偏移。正式窗口截图为 `mesh/03-motion-47.png`，人物来自隔离验收存档。

**整体压力验收仍为 34/43，通过的握持检查不能覆盖九类持械的角度限位过冲失败。** 原 .15 米间隙、8 度限位标准未放宽，失败原始记录仍保留。此前米级握持失败属于下文历史构建，本构建已消除该现象；完整移植与动态等价仍不能据此宣告完成。

该成品于 16:40 构建，保留当时已集成的 Cast24 和社会功能；构建后 16:50 共享工作区另有 AI/社会功能改动。本轮未覆盖那些源码，也未把它们直接注入已验收成品；对应差异与包内后端指纹记在汇总 JSON 中。

## 2026-10-29：正式人物图替换候选

本阶段将人物数值物理图接入正式 `PhysicsDuel`，新对局使用原 Player1 / Enemy 图；切磋、抢夺、袭击共用同一实现。**整体移植仍未完成，持械长回放未通过，不能称为一比一完成或整体稳定。** 该记录对应源图候选；当前默认成品以 `unity/Build/active-build.txt` 为准。

可复查成品为 `unity/Build/SourceFormalReplacementVerified/江湖未央.exe`；目录名中的 Verified 只对应下列局部与流程验收，不能替代失败的长回放结论。

本阶段替换包括：

- 玩家266、敌方269个原Transform，34/31个原刚体、32条原关节槽、源碰撞轮廓与14种物理材质。Create 后原生逐字段核对为玩家5267、敌方5240项，均无差异；手部自连接占位在装备阶段显式接线。
- 原构造体型的物理配置、七状态AI与并行手部调度、器官穿刺时序、部位损伤/断肢、七阶段死亡、真实踢击及其两段等待。体型目前使用原始中性男性默认，尚未完整映射江湖人物的外观、性别和身高。
- 新对局的空手 `Nothing` 圆形物件、本体与配重、盾槽手COM质量与原握持关节；原 `isBlunt=false` 的15速度门槛、Dagger速度加权与D=.1特例。AI仍不把Nothing当武器或盾。现有其他兵器仍混合使用江湖几何与源公式/属性适配；342件原物件与链条尚未全部替换。
- `source_rig`、`source_shape`、`source_ai`、`source_impalement`、`source_equipment` 与踢击接收器的后端验证和保存。源图Single转Double写入JSON，修复Mono默认七位格式造成的冷启动角度舍入偏移。源图与旧图按既有帧选择，不强行把新图套到旧帧上。

### 实际通过

| 范围 | 结果 | 证据 |
| --- | --- | --- |
| 源图、体型、AI续播、空手、损伤/死亡、器官、真实踢击 | 93/93 | `output/source-formal-port-final-native-20261029/source-rig/acceptance.json` |
| 独立进程冷启动、源图/装备/AI状态与结算 | 17/17 | 同目录 `source-rig-resume/acceptance.json` |
| 实际碰撞门槛、护具、连续接触/再接触、踢击保存 | 93/93 | 同目录 `reference-mechanics/acceptance.json` |
| 普通客户端切磋/抢夺/袭击、保存/暂离/返回/结算，自带Python | 81/81 | `output/source-formal-port-final-20261029/native/acceptance.json` |
| 旧图生成帧，再不带强制旧图参数冷启动 | 40/40 + 8/8 | `output/source-port-legacy-compat-20261029/` |
| Python全量回归 | 636通过 | `output/source-port-python-latest-20261029.log` |
| 装备桥接C#数值检查 | 117通过 | `output/source-nothing-equipment-csharp-20261029.log` |

三正式入口的实际保存帧在 `output/source-formal-port-final-20261029/native/combat-{spar,rob,assault}-frame.json`，均含两图、体型、空手装备版本与两个配重；普通游戏窗口截图也在该目录。全部使用独立验收存档。另有1026个源受保护float与60个HeldProps重检，不能把它们加总为原生游戏检查。

### 未通过，保留为下一阶段问题

`output/source-nothing-stress-20261029/reference-stress/` 的10类兵器、最长180秒真实回放中，全部产生实际命中、根节点均留在场内，但没有一类同时满足原有最大间隙小于.15米、最大限位过冲小于8度；**34项压力验收整体失败**。空手真实结构使该例最大间隙从旧拳头代理约.314米降至约.025米，但开场踢击仍有约23.25度胸部过冲；持械仍出现米级间隙与多圈角度异常。流程通过和局部改善不能覆盖这个失败。

原始限位端点排序、4/8/16接触子步、50/60和100/100迭代、Baumgarte .2等对照，均未解决整体失稳，未变成默认数值。证据分别在 `output/source-ordered-limits-4/`、`source-convergence-8-5060/`、`source-convergence-16-100/`、`source-baumgarte-4-5060/`。测量采用signed lossyScale的原生2D锚点坐标，区分仍连躯干的约束与已脱离肢体；没有放宽.15米/8度上限。

旧压力夹具在ResetRound后才把Clock归零，会让第二类起的AI等待约180秒，产生零兵器命中的错误夹具结果；已修正。因此 `source-length-baseline-1` 中九类兵器零命中的旧记录不作为当前AI或物理质量证据。

下一项具体替换对象为原兵器完整物理图、真实Handle_1/Handle_2、配重和惯量。例如原双手剑两握点差约-.1367米、长枪约-.483米；现桥接按初始双手世界点建立第二连接，不能宣称保留了原握距。只移植挂点也不能代替原碰撞轮廓、层级、全部物件生命周期及原APK同输入动态对照。

后续匹配对照 `output/source-mount-experiment-8-5060/` 与 `output/source-nothing-control-8-5060/` 使用同一成品、相同8子步与50/60迭代，区别为是否用10个命名原物件的握距/配重数据。两组均只有空手满足约束（约.011米、3.71度）；其余兵器仍失败，部分更差。该试验只在隔离smoke模式显式传入 `-referenceSourceMounts 1` 时启用，普通游戏不会启用；未把它当成成功的完整物件替换。全部对照行与汇总见 `validation/bloody-source-rig-integration-20261029.json`。

`unity/launch-source-combat-candidate.ps1` 运行普通游戏候选，使用 `output/source-combat-candidate-play/save` 独立存档，不打开独立物理试玩，也不改变默认构建指向。

## 来源与代码边界

基准是用户提供的 `Bloody-Bastards-v5.0.3-mod-apkvision.apk`，SHA-256 为 `c9e4a52874f18c58363d976194227d68a699173d72925d22da34a75d0993f2ba`，使用 Unity 6000.4.0f1 / ARM64 IL2CPP。dummy DLL 只提供类型、字段和地址，方法体为空；恢复逻辑需结合 `libil2cpp.so` 的 C/ARM64、序列化对象和动画核对。地址按 ELF PT_LOAD 映射，不能把 Cpp2IL 的文件偏移当作虚拟地址。

`output/bloody-full-port-20261028/extraction-audit.json` 保存提取范围、哈希、排除项及反编译警告。反编译成功和局部数值同值不证明全部方法已审读或游戏动态等价。本作正式交手的公共实现位于 `unity/JianghuUnity/Assets/Scripts/PhysicsCombat/`；相邻独立恢复工程见 [BLOODY_FULL_PORT_STATUS.md](../research/BLOODY_FULL_PORT_STATUS.md)。

## 导入和保存契约

- `python scripts/build_duel_source_rig.py` 从 `output/bloody-full-port-20261028/source-combat-data.json` 及恢复工程的层/碰撞设置生成 `Assets/Resources/CombatSource/rig.json`。`DuelSourceRig` 负责数字图加载，`DuelSourceRigBridge` 负责正式 Fighter 接线。
- 原图创建后先执行逐字段 `ValidateSource()`，再应用 `ConfigureSourceBodyShape()` 与装备。原构造形体包含骨盆 container localY 从 .5 到 .42、肩髋锚点等初始化；模板未初始化值不能直接与形体配置后的值比较。当前中性男性配置不等于完整外貌到体型映射。
- 原图包含 3D 镜像和负尺度，关节间隙采用原生 2D 锚点坐标：`rb.GetRelativePoint(Vector2.Scale(anchor, (Vector2)rb.transform.lossyScale))`。TransformPoint 或忽略 signed scale 的换算可制造假间隙。修正测量不能顺带改真实锚点、限位或验收上限。
- 快照保存实际刚体、关节、动态目标、体型、装备、AI、穿刺、伤肢、死亡与踢击状态。恢复按层级写位置与完整 quaternion，同步两图后恢复 2D 角度及速度；q 与 -q 表示同一旋转。Single 提升为 Double 后进入 JSON，避免 Mono 七位格式舍入。
- 源图与旧图按已有存档帧选择，不强制迁移为另一套物理图；新版本状态标签不能被降级丢弃，死亡阶段不能倒退。断肢、护具耗尽及敌对交手结算必须做保存后独立进程恢复。

## 装备桥接的必要约定

`DuelSourceEquipment` 与后端对切磋、抢夺、袭击共用同一份适配。源原值与本作设计分别保存在资源的 `source_observations`、`adaptation` 中。

- 江湖武器系数 c 转入源伤害式时使用 `effectiveD = 16*c - 15`，以保留相同速度、材质、减甲前的比例。负有效 D 是代数适配量，不能直接夹成 0，也不能宣称为原作物品 stats；源 D=.1 特例必须与普通适配区分。
- 材质依实际碰撞部位与模板确定，不从品级、低伤害或名称子串猜测。商店 obs_Weight 不直接等于 Rigidbody.mass；非 arena 的 OnEnable 阻尼、重力也不能直接用于正式持械。
- 实际空手占位 `None`（GO 70947、组件 273502）与商店空格 `None_HandItem`（GO 71131、组件 273686）是不同对象。空手有真实圆碰撞体、本体/配重各质量 1、D=.1；它不设置 AI 武器/盾标记。Type7 的 COM .25、手臂阻尼及源握持连接仍须应用，不能据“没有盾装备”改为武器 COM .55。
- `source_equipment={version:1,nothing_template:"sharedassets0.assets:273502"}` 表示新空手结构。新结构单手/空手保存 Near、Far 配重，双手兵器保存 Near 配重；后端拒绝丢标记或丢配重。已有旧标记/旧图对局保留兼容路径。
- 受保护 float 的提取先交换隐藏值字节 1/2，再 XOR key；bool 采用独立恢复的算法。不能把两种解码合并。

```powershell
python scripts/build_duel_source_equipment.py --check
python scripts/validate_duel_source_equipment.py
python scripts/verify_duel_source_shape.py
```

这些数值/资源检查不替代 Unity 真实碰撞、长回放或原 APK 同输入动态对照。全部原兵器及链条、火器、护甲视觉、碰撞图和物件生命周期仍需逐项核验。具体复现和记录要求见 [VALIDATION.md](README.md)。
