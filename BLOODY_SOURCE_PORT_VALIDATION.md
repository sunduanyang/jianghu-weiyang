# 正式战斗源图接入、保存契约与已知问题

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

`output/bloody-full-port-20261028/extraction-audit.json` 保存提取范围、哈希、排除项及反编译警告。反编译成功和局部数值同值不证明全部方法已审读或游戏动态等价。本作正式交手的公共实现位于 `unity/JianghuUnity/Assets/Scripts/PhysicsCombat/`；相邻独立恢复工程见 [BLOODY_FULL_PORT_STATUS.md](BLOODY_FULL_PORT_STATUS.md)。

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

这些数值/资源检查不替代 Unity 真实碰撞、长回放或原 APK 同输入动态对照。全部原兵器及链条、火器、护甲视觉、碰撞图和物件生命周期仍需逐项核验。具体复现和记录要求见 [VALIDATION.md](VALIDATION.md)。
