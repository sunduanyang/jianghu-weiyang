# 正式 PhysicsDuel 原生 AI 地址映射（2026-10-28）

范围：来自用户提供 APK 5.0.3（c9e4a52874f18c58363d976194227d68a699173d72925d22da34a75d0993f2ba），不是官方 Unity 工程。主证据是 recovered-combat / native-full-c；关键分支额外核对 bloody-code-audit/native ARM64。表内“实现”表示有对应代码，不等于已经完成 APK 同输入逐帧对照。

正式入口：DuelAI.Fighter.ReadAI 在 Reference 时直接调用 ReadSourceAI。切磋/抢夺/袭击共用 PhysicsDuel；非 Reference 历史路径保留旧 AI，不将其冒称原作状态机。

| 原生函数（VA） | 实现 | 核对内容 |
| --- | --- | --- |
| Start 342a154 | InitializeSourceAI / ReadSourceAI | 0.5秒配置装备、1秒开始主协程 |
| Update 342a4ac、cig 342a9bc | ReadSourceAI / AIReadActivity | 输入活动幅度+差分、上升.7/下降.25平滑、抑制/程序化/盾追踪优先级；真实point读回 |
| SetAIWeaponAndShieldProperties 342b488 | SetSourceAIWeaponProperties | 空槽/单手/盾/双手标记，长度+.1、minSpacing/preferredSpacing乘法；本游戏仅主手+盾槽 |
| cik 342bc14、cil 342bdcc | AIRollAggression / AIReactionDelay | 浮点Range截断为1至5，relentless强度、原反应区间 |
| cim 342be5c、cin 342be98、cio 342bec8 | AIStaminaThreshold / AIAbortChance / AIVulnerable | 35-20a、.35-.34min(a²,1)、玩家力竭/低于15/手部失能 |
| cip 342c010、cir 342c120 | AIChoosePersonality / AIDecideMovement | Bully/Matador、活动偏置、2.5至6秒性格切换、.94至1.08间距扰动 |
| cih 342acc0、cii 342ba74 | AIRefreshThreat | 排除掉落物与盾的武器威胁；有源挂点覆盖接口，缺省回退本游戏item刚体 |
| cjc 342add0、cjd 342af00、cje 342c484 | AISetShieldDefending / AITrackShield / AISetWeaponDefending | 状态mask 0x52、16度瞄准、.842强度、.2半径、10跟踪速度，持盾时不进武器防御分支 |
| cjf 342c52c、cjg 342c7cc | AIDecideMovement | 踢击阈值95/90/80/70/40，AIState mask 0x16、边界3、移动再开放.3至.1秒 |
| ciw 342c250、cix 342c350、cji 342c2e4、cjj 342c278、ca 342d798、cc 342d850 | AICiw / AICix / AIWalk | 原生符号链保留，不用Towards/Away猜测替代。cit=selfX-playerX>0时ciw→cji→ca→Walk(-1)，cix→cjj→cc→Walk(+1)；<=0时反向。ELF RELA+metadata39类型与ARM64联合确认 |
| cd 342d908 | AIWalk | 同侧两tap、.05至.15实时等待、交叉方向最短.25、该侧输入抑制额外.05 |
| bz 342d4b8 | AIKick | 左、右、左、右四tap，.0至.15侧jitter、.05至.15 pair等待、.25交叉gap、1.2冷却；均为实时等待 |
| cl 342fcb8 | AIStateLoop | 七枚举：Idling/Attacking/Withdrawing/Defending/Cowering/GoingBerserk/EnjoyingTheVictory；自然状态分支保留，原作未自动赋值的枚举不补猜测触发 |
| ce 342dad4 | AIActionLoop | Cowering五次、Berserk round-to-even(3+3a)、原动作等待、胜利切换 |
| cj 342f458、cg 342ea20 | AIReactiveLoop / AIDodgeCounter | 活动上升/下降沿、Matador闪避、反击、空闲踢击、a²主动攻击机会与脆弱加权 |
| cf 342df28 | AIAttack | 开口反转等待、异步左手延迟/右手立即/双手、.95/1.25等待、追击机会、收招。特别修正：342e0c8 / 342e104低体力或abort直接跳342e400→PullBackward，不进入完成出招后的repeat |
| ci 342f278 | AIPullBackward | .8至1.2乘(1-.75a)，边界外固定方向保留cjj(+1)/cji(-1) |
| ck 342fa5c | AIVictory | 清除strike/guard/suppress，反应.15至.35，.5至1等待，cjj固定Walk(+1)，停止动画/复位。保留手relay独立协程scope |
| AIHandsInputRelayer.Attack 341884c | SourceHandRelay.Attack | 先抽动画time offset，后按保留重复项的bundle索引选clip；不是播放速度 |
| AIHandsInputRelayer.Start 3417d28、bs 3418e20 | SourceHandRelay.Defend / Update | 动画父容器初始位置+.15，OutQuad归位、OutCirc局部X/世界Y防御tween、反应等待与weaponCOM缺失重试；独立于child nub |
| ch 342eccc、cv.Evaluate 35142c8、Evaluate2H 3515520、AimDegrees 35156f0、Profile.Default 3515e48、Varied 3515f40、cctor 3514ee4 | AIProceduralStrike / DuelSourceAIStrikeCurves | 可选程序出招路径、类型刺击概率、原profile表、随机扰动顺序、单双手rest、.55支撑半径/23度偏移；默认敌预制体该开关为0 |
| CharacterMover.clu 3435e18 | SourceAIMoveTapHandler / 缺省SourceAIMoveTap | 源mover优先接收原tap；缺省适配器按60Hz将.35/.175双击与踢击窗口变为21/10ticks，.25移动持续 |

## 默认数据与物理边界

source-combat-data.json 的敌人 c0b154b0ebaf72f4fa139b4e3cf363e9 / CharacterEnemyAI 894 提供默认数值。AIHands relays挂在parent transform289（.3230000138,.5640000105）、446（.0480000079,.5640000105），不是child nubs427/372。默认useProceduralStrikes=0。

SourceAIHandCommand输出Near=源LeftParent+rightInput+leftActive，Far=RightParent+leftInput+rightActive。SourceAI决策不含.435m适配；SourceAIWorldHandTarget只是当前物理坐标桥。原mover先MovePosition旧point，再用input/100+.15/.25写新point。不得把动画parent米值当摇杆，不得把nub直接米值再重复偏移。

空武器挂点不会虚构Stick物品。SourceAIWeaponLengthMetres、SourceAIWeaponMount、SourceAIShieldCOM可接已恢复物品/挂点；没有原始物品绑定时有明确本游戏兼容回退。音效/Charge/Arrow/TMP调试文本未在本AI任务中伪造。

## 存档

source_ai v1保留七状态、性格、活动/冷却/抑制/移动计时、三条relay动画与并行tween、协程栈及依赖、已知routine的编译器状态数值字段、确定性seed+draw随机流。恢复仅从显式白名单工厂创建本地routine，不从存档加载类型/程序集/方法；schema形状不匹配会拒绝。后端physical_source_ai.validate校验结构、routine、finite数值、范围、队列环、大小，并复制数据。source_motion v1继续输出兼容投影，旧在途clip可升级。

## 当前验证与未证实项

- 全工程使用Unity随带Roslyn、原Bee引用响应文件直接编译通过；未启动Unity、未改active-build.txt。
- tests.test_physical_source_ai：10项后端检查通过，含并行队列、缺失/未知routine、非finite、依赖环、大小、布尔/数字类型、禁止降级等。
- ProbeSourceAI已提供七状态动作入口、并发左右relay、缺失绑定保持、四tap踢击、完整checkpoint后120帧续跑对比；这些原生probe待主线程实际客户端执行，不能以存在probe宣布通过。
- PE元数据读取确认实际Roslyn迭代器数值字段符合后端pattern；没有依赖猜出的私有类型名。
- Unity随机全局流被本游戏确定性每人流替代，以支持冷恢复；相同范围/分布及该路抽取顺序不证明原APK随机序列相等。
- scaled等待使用战局Clock，realtime等待使用unscaledTime（fixture统一Clock）；暂停时由正式客户端冻结调度，不能据此声称原应用暂停协程逐帧相同。
- 尚无原APK与本工程的同输入逐帧trace；Animation/DOTween与当前采样调度的帧阶段等价性尚待实测。不能称整个原作或所有物理/AI行为一比一移植完成。

## 真实肢体flag交叉核对

不是按属性名字推测：敌prefab ArmL872/ForearmL1019/HandL994的bodyPartType=2，ArmR958/ForearmR915/HandR1052的type=3。BodyPart.cdx 0x341a914在ARM64 341a96c将type2写至CharacterMover+0x11a(anyHandPartRSevered)，341a980将type3写至+0x119(anyHandPartLSevered)；cdw/cdy相同。因此Near实际L损伤对应Rflag，Far实际R损伤对应Lflag。SourceAILeftArmDisabled读取Far、Right读取Near保留源含义；ObservedNub(left)对应leftPad/Far，main右Pad对应rightInput/Near。

## 首轮原生续演问题定位

source-rig-native-1的ProbeSourceAI实际七状态动作入口、左右并行relay、缺失绑定保持、四tap踢击均成功；continuation_equal失败只有两处：一帧move（1/0）与停止动画time约1.49e-8差。前者是探针未恢复mover kickUntil，首轮未来踢击计时污染回拨后的tap；现与生产一样配套恢复kick_left/kick_state。后者已用原float算术重现（output/source-ai-continuation-diagnosis.json）：只保存elapsed后重基准改变原clipOffset/clipAt操作数。source_ai未发布v1 relay新增offset/started_at保存原操作数，time投影强制float boxing再升double。修复后Python19项与Roslyn通过，第二轮原生重测由root执行，尚未以代码修复代替重测通过结论。

第二轮native source-rig-native-2确认offset/started_at已进入成品，动画时间差消失，完整续演仅剩同一帧move差。进一步核对RestoreReferenceKick的范围发现它只恢复paid/receiver状态，生产RestoreReference在外层设置kickUntil；探针第一次修复遗漏这个外层赋值。现探针也先设置kickUntil=Clock+kick_left，才恢复helper与AI。第三轮原生验证待root执行；没有放宽比较阈值。

## 移动方向纠正与所有factory类型指针审计

第三轮源图真实AI回放暴露人物远离对手至两侧墙边。此前仅按方法地址/名称猜定cji/cjj分别对应cc/ca是错误的，虽然cit减法、clu布尔符号与两条MoveNext各自的参数都读对了，中间的typeinfo实际分配类型接反。现通过ELF.rela.dyn R_AARCH64_RELATIVE追踪全局指针，再按metadata39编码解出真实类型：

- cjj读取PTR07212738→073c49e0→0x200183cb→typeIndex0xc1e5→CharacterEnemyAI/cc→AIWalk(+1)。
- cji读取PTR07212740→073c49d8→0x200183c9→typeIndex0xc1e4→CharacterEnemyAI/ca→AIWalk(-1)。
- cit在342c21c确为selfX-playerX；ciw正分支到cji，所以靠近对手；cix正分支到cjj，所以后退。
- clu true在343604c置MoveDirection=-1，false在3436058置+1；FixedUpdateNetwork直接以positive movementSpeed乘MoveDirection作世界X AddForce。角色flip不反转这一力。

最小代码纠正只涉及AICiw/AICix符号、PullBack墙外方向及Victory固定方向；没有改数值、攻击调度或比较条件。可复现脚本unity/verify_bloody_ai_factory_types.py只读ELF/metadata，完整13个factory（含relay cdm）的类型核对均通过，结果output/source-ai-factory-type-audit.json。其余state/action/reactive/counter/attack/kick/walk/procedural/pull/victory/defense factory先前归类正确。

source-rig-native-3 / source-ai.json现已实测continuation_equal=true，七状态/并行relay/缺失绑定/tap踢击均通过。移动方向typeinfo纠正发生在此之后，下一次完整源图原生回放仍须另证实靠近、攻击与命中，不能把续演相等当成交战有效。

## 原生第四轮冷启动计时修正

source-rig-native-4已实际通过49项源图检查及完整AI续演，但13项冷重启检查发现AI状态两点差异：aiLastSourceFrame从6.983369827变7.000036716，及敌人实时job due在固定Clock下从-6.218986988漂到-6.831521511。前者来自恢复末尾强制对齐Clock，现保留已按Clock shift恢复的原字段，保存最后一帧相位。后者来自fixture Clock直接切至进程Time.unscaledTime以及暂停期间用后者重算remaining。

现使用可暂停AI逻辑实时钟：记录sourceRealtimeValue，原始机器时钟anchor不保存；域切换先结算旧域末尾delta，再换raw anchor；暂停冻结逻辑值，恢复时从已保存逻辑值建立当前进程anchor。Capture先同步一次再保存fields和所有job remaining，Restore所有job用同一anchor，不清除队列。Root接SourceAIClockPaused/Run/Rebase hooks。新增探针pause_realtime_stable与fixture_realtime_stable；Python19项、Roslyn通过，原生重测仍由root执行。

## 长回放暴露的兵器距离适配错误

source-rig-final-native/reference-stress/solver-measurements.json实测徒手180秒22命中，9类兵器180秒均0命中，不能据其测试流程完成声称实战有效。AuditFighter实际传送了distance，不是缺字段回退；Bridge将RPG root间策略distance误当原SourcePrefab_HandItem.Length。例Sword：strategy=1.95→攻击range2.05/min1.845；本游戏实体reach1.18+tail.16=Length1.34。AI fallback已改几何Length，Bridge由root对应改显式override，原生实战需重测。

原字段是普通float Length，不是obs_Length，也不是已证明的Collider bbox公式：原Just a Dagger组件154声明Length=.65，而原blade+handle局部Y跨度约.51076，Handle1.y=-.181。当前geometry Length桥接是明确本游戏适配，不能假称逐件原装备参数恢复。新增stress每秒双方AI状态/flags/出招数/区间/实际root距离与武器端点速度，另返回non_kick_impact_observed以区分只有踢击的命中；不更改源攻击概率/数值来掩盖0-hit。
