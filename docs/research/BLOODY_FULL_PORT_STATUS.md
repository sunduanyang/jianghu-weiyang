# Bloody Bastards 独立 Unity 恢复工程

本文件维护独立恢复工程的位置、来源、已取得证据与待验收范围；具体通过项始终以所列构建和原始 JSON 为准。

目标是用户提供的 APK 的完整战斗复现：人物、场景、操作、骨架、碰撞、武器、AI、损伤、体力、动画与反馈均须对照原作。当前已有可运行的独立 Windows 战斗工程，仍未达到完整一比一验收。

来源：`D:/BaiduNetdiskDownload/Bloody-Bastards-v5.0.3-mod-apkvision.apk`；SHA-256 为 `c9e4a52874f18c58363d976194227d68a699173d72925d22da34a75d0993f2ba`。这是用户提供的修改版，复现基准据此确定。

## 工程位置

- 可编辑工程：`unity/BloodyBastardsRecovery/`。
- 匹配编辑器：`D:/Unity/Editors/6000.4.0f1/Editor/Unity.exe`。
- 原始资源导出：`output/bloody-full-port-20261028/Recovered/ExportedProject/`。
- 可维护的恢复代码、着色器：`recovery/bloody/`。
- 原生方法与着色器程序：`output/bloody-full-port-20261028/native-full-c/`、`shader-programs/`。
- 构建输出位于 `Build/BloodyBastardsRecovery*/`。下文验收结论对应所列 JSON 与构建日志；较新目录存在不表示已经取得同等或完整验收。

原始导出与可运行恢复工程分开保存。原始 Cpp2IL 空方法体和安卓插件桩没有当作已恢复逻辑使用。此工程不替换《江湖未央》的默认启动器、战斗或存档。提取资源与构建保留在本地忽略目录中。

## 已建立的基础

恢复了原作资源与层级，包含三套战斗人物、18 组 AI 动作包、实际动画曲线、兵器预制体和物理材质。可运行工程保留原作刚体、关节、碰撞轮廓、骨骼与模型，不使用本作的简化胶囊人物替代。

导出 1289 个原生方法的 C 控制流程，保留 ARM64 指令作为核对依据。导出成功不表示逐方法语义审查或运行等价；部分反编译输出仍有控制流警告，不能直接编译为游戏代码。

着色器来自 APK 实际压缩 GLES 程序的桌面翻译。恢复了所用 URP2D 光照、TopRim、动态血迹、原地面、镜头、昼夜和天气。所有同名 shader 别名均重绑，防止运行时再次选中资源工具的白色占位程序。未使用的 GPU/XR 分支与完整画面对照尚未覆盖。

已恢复 342 条根兵器序列化记录，对应 340 个唯一资源路径；两条场景装备实例是已有资源的别名，保留来源。恢复单手、双手、盾、链枷、手炮、部位与护甲分层、体力、命中门槛、穿刺、断肢、流血、力竭、掉落和七阶段死亡。`ObscuredFloat` 使用字节反混排后 XOR；`ObscuredBool` 使用原生 `(hidden - key) XOR key`。

原始骨架恢复支撑刚体、COM、交叉命名的手部目标、脚部目标、阻尼、根移动、双击和踢击。原生脚步代码使用 `Transform.rotation.z`，保留此原生行为。使用原重力、1/60 步长和 10/10 求解迭代。

输入适配器使用原 Canvas 尺寸、150 半径、动态中心、翻转和定点量化。AI 包含原状态与协程、18 组加权动作包、单手/双手动画采样、进退、防御、踢击和胜利行为。恢复表情、语音、脚步及其源随机调用；这些数值对照不等于完整运行轨迹对照。

界面使用原 UGUI/TMP 层级、字体、动画曲线和事件。Friendly 的开场、三秒倒计时、Fight、暂停和结算已运行；计时采用原向上取整及 `05:00` 格式。较新的维护源还接入真实 Practice 教程、独立 Dummy、Campaign 生成、星级/钱包/进度及新物品提醒，必须经过下述整合构建验证，不能据独立数值检查宣布运行完成。

## 已取得的运行证据

- Build13 已修正兵器 arena/menu 域：战斗保留各原 prefab 重力/阻尼，菜单用的负重力不再污染战斗。全装备装配与两步求解1160/1160、代表真实接触/手炮10/10通过：`output/bloody-full-port-20261028/equipment-validation-13/original-equipment-validation.json`。
- Build13 损伤生命周期3/3通过：真实 InternalOrgan 触发临时关节、原计时释放；断臂剩余握持及独立流血；七阶段死亡、单次事件及实际 Host 清理。证据：`output/bloody-full-port-20261028/lifecycle-validation-13/original-lifecycle-validation.json`。
- Build13/14 完整 Friendly 输入回放通过：真实指针手控、移动/踢击、300秒超时、原结算动画及 AudioSource 活动、正常下一回合新实例/健康值。Build14 记录34次武器损伤；它以超时结束，不能称为自然击杀验收。证据：`output/bloody-full-port-20261028/match-replay-14-fast/original-match-replay.json`。
- 匹配 Unity 引擎运行 Campaign level1 的实际生成，得到原 APK 同名敌人 Willow Murphy，左武器199、右手空。独立生成器和进度数值对照通过，完整首战运行仍待新整合构建。证据：`output/bloody-full-port-20261028/campaign-engine-probe-15/campaign-first-enemy.json`。
- 实际 Windows 窗口已看到人物、装备、场景和 HUD。鼠标拖动及完整回合交互仍需端到端验收。

最新已通过上述完整回合的可执行文件是 Build14，入口为原场景 Player_1 与 Enemy 配置的本地 Friendly。维护源的新入口按真实新档进入教程 Practice，完成后进入 Campaign；它尚在整合验证。PC 操作为 A/D 或方向键移动、拖动屏幕一侧控制手、右键捕获另一侧、空格踢击、Esc 暂停、Enter 下一回合或重赛；正常界面不覆盖额外工程文字，可用显式 `-recoveryControlsOverlay` 开启操作说明。

Build15 编译成功但运行启动失败：环境读取器把嵌套数据路径当成字面字段，误读空 Sprite 数组，在角色生成前抛错。两个失败日志与 `integration-startup-15.json` 保留；此构建不作为可玩交付。维护源已修复路径解析，Build16 正在重新构建和验证，未取得运行通过前不替代 Build14 的证据。

隐藏窗口的普通截图可能不执行相机渲染；诊断采用 URP SingleCameraRequest。带 HUD 诊断临时调整 Canvas 渲染方式和相机层后全部恢复。它不能替代实际窗口交互验收。

## 距离完整移植仍缺少的内容

1. 新整合入口的教程、真实 Dummy 接触/音效、Campaign 自然胜负、实际奖励/Next/Retry 与重新启动验证；桌面硬件鼠标拖动验收。
2. 原作与恢复工程在相同输入轨迹下的对照，包括兵器路径、身体姿态、接触、损伤、随机调用顺序和计时。目前 `runtime_parity_verified=false`。
3. 完整主菜单、装备选择及服务整合。Campaign/钱包/进度/新物品提醒已恢复维护源，但不等于主菜单、购买或联网已完成。Practice 死亡按原规则只发 HP 重置请求并返回 None，不计分/进入结算；真实 HP 请求订阅者尚未确定，没有编造复活。提供的 MOD APK 实际显示99M，当前真实 release 钱包默认0；其 MOD 加载/授予来源尚未解释，没有靠硬填余额匹配图片。
4. Fusion 联机、移动硬件触觉、节日及未使用的 GPU/XR 分支。

原 APK 已在独立官方 Android API36 模拟器实际运行。安装后的 APK 哈希与用户文件一致，已进入教程、Campaign 首战 Smeg Pisdarion 对 Willow Murphy，记录真实触控、掉血、血迹和自然死亡结算；资料在 `output/bloody-full-port-20261028/apk-reference36-live/`。只读核得首战 region1/type1 Monastery/seed0，以及 Hour16/Day29。录制有效帧率约3.8–7.2fps，不能作为逐帧运动或速度一致性证明。旧 API30 的转译失败仍保留原日志，已经不是当前对照阻断。

## 复现准备

`scripts/prepare_bloody_recovery_project.py` 从原始导出准备工程并解析控制器的原始对象引用；`scripts/export_bloody_shader_programs.py` 导出实际 GPU 程序；`scripts/recover_bloody_base_shaders.py` 翻译所选基础着色器；`scripts/build_bloody_item_catalog.py` 导出原始兵器参数与引用。

`scripts/sync_bloody_recovery_runtime.py` 同步稳定源码快照，同时重绑引擎动画脚本及所有同名 shader 别名。用匹配编辑器执行 `OriginalRecoveryBuild.Build`，可传 `-recoveryBuildOutput <绝对exe路径>` 输出新目录。

启动参数 `-recoveryCapture <绝对目录> -captureSeconds 8` 输出结构与渲染记录；`-validateOriginalEquipment <绝对目录>` 验证装配和真实接触；`-validateOriginalLifecycle <绝对目录>` 验证损伤生命周期；`-validateOriginalOnboarding <绝对目录>` 验证原按钮、教程存档、实际 Dummy 与首关切换；`-validateOriginalMatch <绝对目录>` 运行真实输入回放。可显式选择 `-originalGameMode 0|2|5`、`-originalCampaignLevel`、`-originalCampaignStage`、`-originalProgressPath` 和对照 Hour/Day。测试使用各自独立存档，不修改原 APK。Unity Button 回调探针不代表操作系统硬件输入已经验收。
