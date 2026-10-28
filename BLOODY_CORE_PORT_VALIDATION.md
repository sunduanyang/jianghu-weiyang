# Bloody Bastards 战斗核心恢复与正式游戏接入

2026-10-28。第一批从原生控制流程恢复的战斗代码已接入正式切磋、抢夺和袭击。当前完成的是部分核心的恢复与适配，尚未完成原作整套战斗程序的移植。

## 恢复位置

`DuelReferenceRules.cs` 保存不依赖本作人物、美术或存档的判定与数值规则；`DuelReferenceState.cs` 将损伤、整体伤害、体力恢复、流血、命中预算和快照接入正式 `PhysicsDuel.Fighter`。现有 `DuelReference.cs` 调用这两部分；`DuelReferenceKick.cs` 去掉了此前误加的阻尼。

原 APK 的结构 DLL 方法体为空，本次依据实际 ARM64 方法和序列化配置恢复逻辑。以下地址为 ELF 虚拟地址，本库代码段的文件偏移比它小 `0x4000`。

| 原作入口 | 已恢复行为 | 本项目入口 |
| --- | --- | --- |
| CharacterDamageCalculator.RegisterImpact `0x34281b8` | 接受命中后延长 .1 秒重置时刻，最多 3 次；各部位另有 .15 秒冷却 | Rules.RegisterImpact、AcceptReferenceImpact |
| OnHandItemImpact_Authoritative `0x3428224`，cgg `0x3428820` | 方向加权、D=.1 精确分支、防御平减、木柄 .2/金属柄 .4、材质/护甲修正与截断；击退使用截断前值 | Rules.CalculateDamage、ReferenceContact |
| BodyPart.cdt/cdu `0x341a7bc`/`0x3419cb8` | None/Bashed/Pierced/Severed 状态；头部致命门槛用绝对血量 20 | Rules.PartMode、SetReferencePartMode |
| BodyPart.cdx/cdy `0x341a914`/`0x341aa54` | 钝伤失去控制和握持；刺伤、切断启动流血；只切断受击根关节，子关节仍连接 | SetReferencePartMode、SeverReference |
| cdy 末段 `0x341b24c` | 伤部根刚体额外覆盖质量 9、线性阻尼 0、角阻尼 5 | SetReferenceLimp |
| SetPhysicsProps_PassedOut/Dead `0x341b710`/`0x3419b0c` | 力竭与死亡按部位使用不同质量/阻尼；正常恢复使用初始化参数 | Rules.RelaxedPhysics、SetReferenceLimp |
| CharacterVitality.FixedUpdateNetwork `0x350d424` | 倒地 3 秒，起身体力固定 10，保护 1 秒，回气 20/s；力竭时仍处理流血 | TickReferenceVitality、TickReferenceBleeding |
| StartBleeding `0x350d72c` | 首次延迟 .65 秒，随后每 .25 秒扣整体血 .5 | StartReferenceBleeding |
| CharacterMover.clr `0x34354d8` | 60 Hz 组件时钟在积分前限速，受踢与力竭不豁免 | ClampReferenceRoot |
| BodyPart.cdx/cdy 降低移动参数 | 腿部损坏后上限 1.75、移动力 10、骨盆辅助质量 5 | Rules.DownedSpeed 与现有降低支撑逻辑 |
| KickReceiver/ct.MoveNext `0x3512670` | 仅维持 .35+.15 秒受踢标记，不改阻尼 | UpdateReferenceKick、RestoreReferenceKick |

继续读指令确认 `0x3fe00000` 对应断腿上限 **1.75**，此前审计中的 1 已更正。限幅后仍会积分施力，帧末速度检查允许一次积分增量。

普通盾击恢复使用伤害属性 1，撤销固定 .1 的特例。具体兵器材质、特殊盾和多层护甲尚需逐件映射；规则函数支持分支，不等于全部正式装备已经映射齐全。

## 正式战局保存

新增可升级的 `core_state` v1，保存 17 个部位的损伤类型、冷却、整体命中计数/窗口、流血标记与计时。姿态仍用现有身体与配重快照。

- 旧快照可升级，按原有离体/局部健康恢复；旧伤不追补此前未保存的流血。
- 新状态不能静默降级丢弃；损伤类型不能回退或替换，流血状态不能无故消失。
- 拒绝非法类型、NaN、越界计时、错误计数、健康部位标记损坏、切断类型缺少离体状态等数据。
- 根目录后端已通过 `python unity/prepare.py --backend-only` 同步到 Unity；没有打开或改写真实用户存档。

## 验证范围

结果与源码摘要见 `validation/bloody-core-port-20261028.json`；原始记录在 `output/bloody-core-port-20261028/`。

最终七组原生检查合计 **238/238** 通过，相关 Python 测试 **104** 项通过。最终正式默认版的三模式检查为 **51/51**。十种长战组合的最大活跃关节限位误差 6.200241°，最大连接间隙约 4.43 mm；双手大剑轨迹未产生命中，只计稳定性证据。

新增原生检查覆盖固定原生分支算例、真实剑/盾碰撞、命中预算、部位冷却、受踢/力竭限速、阻尼、140 体力上限时固定 10 点起身、钝伤掉武器、刺伤保留关节、断肢子关节、流血时序、绝对血量 20 门槛、保存与跨进程恢复。

固定算例使用字面预期值，没有通过被测公式反算。例如普通盾在相对速度 (24,0)、无甲时局部伤害 8.6；真实碰撞后躯干整体伤害 3.01。后端检查覆盖状态往返、旧快照升级、防降级、不可逆损伤及异常输入拒绝。

正式人物交互验收从切磋、抢夺、袭击各自入口进入，检查普通 Campaign、共享核心、保存、暂离返回和结算。断腿检查继续验证地面接触、倒地支撑、踢击禁用和恢复。长战覆盖徒手及九种兵器组合；没有产生接触的轨迹只计稳定性证据。

首个构建因验收脚本变量重名失败，已修正。首次核心保存验收的脚本伤害漏记命中次数，后端正确拒绝；已补齐隔离夹具记录，没有放松正式校验。失败记录保留，最终结果单独汇总。

## 当前正式版本

本次构建产物为 `CombatCorePort`。验证期间默认整合版先后更新为 `IntegratedConversation`、`PersonScrollClean`，均已包含本次代码；最终版本以验证汇总记录为准。两个整合版与本次构建的 444 个战斗方法规范化 IL 一致，战斗后端文件也与当前源码一致，因此保留默认整合版并复验正式三模式，不回退其他已整合功能。比对记录为 `current-default-combat-il.json`、`release-combat-il.json`。

双击「启动Unity版.bat」继续通过现有默认启动器进入游戏。

## 尚未完成

- AI 决策协程、动作包、盾牌特殊碰撞事件与完整攻击轨迹。
- 器官触发、穿刺处决、武器临时固定关节；本次 Pierced 是部位刺伤状态。
- 身形、碰撞轮廓、步态、踢击旋转/曲线的坐标校准；当前保留已验证的断腿支撑适配。
- 死亡协程分阶段的对象/碰撞体切换、关节软化；本次只恢复了上表中的物理参数和损伤行为。
- 正式角色属性、装备质量/伤害标尺、多层护甲与离屏 NPC 战斗的统一适配。
- 联机预测/重模拟，以及完整音效、伤口、表情和镜头反馈。

这些项目仍待恢复，不能用本次通过的检查替代完整代码恢复或原作动态轨迹对照。
