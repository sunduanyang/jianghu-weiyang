# Unity 客户端

主工程是本目录的 `JianghuUnity/`，使用 Unity 6000.6.7f1 和 Direct3D 11。场景为 `Assets/Scenes/Jianghu.unity`。Python 3.12+ 作为本机规则进程随客户端启停；自带 Python 的成品直接使用随包运行时。

从仓库根目录双击 **用Unity打开.bat** 打开工程，双击 **启动Unity版.bat** 运行成品。完整玩法入口见[根目录说明](../README.md)，源码约定与资源生成见[开发指南](../CONTRIBUTING.md)。

## 源码与构建

- 共享规则的源文件为根目录 `jianghu/`。修改后执行 `python unity/prepare.py --backend-only`，同步到 `Assets/StreamingAssets/Backend/jianghu/`。
- 界面、地图、外貌与战斗位于 `Assets/Scripts/`；`GameClient.cs` 管理本机规则进程、请求及存档位置。
- 编辑器菜单「江湖 / 构建 Windows 游戏」生成 Windows 成品。需要私有 Python 时，先在 Windows CPython 环境执行 `python unity/prepare.py --bundle-python`。
- 也可使用本目录的 `构建Unity版.ps1`；编辑器路径与运行时选项以该脚本参数为准。
- 启动器优先读取 `Build/active-build.txt`；没有有效标记时才回退到 `Build/Current/`。源码更新后须重新构建，成品名称和目录时间不能证明验收通过。

## 存档和验证

默认存档使用 Unity 的 `Application.persistentDataPath/Saves`，实际路径可在「系统 → 存档」查看。旧档导入建立新副本；个人存档不入库。

验收使用显式 `-saveDir <新的独立目录>`。通用 `-smokeDir` 会进行实际交易、移动、交手、月结和保存，不能传入个人世界。探针、已有结果和已知失败统一见[验证指南](../VALIDATION.md)。

人物头像、交谈立像与正式战斗共用外貌身份和渲染代码。原创拆分原件、锚点、遮挡层和固定 UV 是绑定依据；整身原画用于视觉对照。原画路径、烘焙命令及绑定要求见开发指南和[绑定研究](../BLOODY_BINDING_STUDY.md)。

## 关节绑定

「系统 → 关节绑定」把拆件图手动绑成可动的皮影：自动切块、分配部位、点关节（起点、终点、肩、髋、手的握点）、调转动范围、拖动布娃娃检查，再「设为切磋外观」。切磋时每块按物理骨骼摆放、只旋转不拉伸，碰撞与伤害不变。绑定在编辑器里存入 `Assets/StreamingAssets/Rigs/`，成品存入 `persistentDataPath/Rigs`。用法与验收见[关节绑定说明](../JOINT_BINDING.md)。

## 两个战斗工程

本工程的切磋、抢夺、袭击共用 `Assets/Scripts/PhysicsCombat/`；从正式人物交互进入战斗验收。根目录 **启动物理格斗Demo.bat** 使用同一默认成品并增加试玩参数。源图候选、保存契约与压力测试失败见[正式战斗接入](../BLOODY_SOURCE_PORT_VALIDATION.md)。

相邻 `BloodyBastardsRecovery/` 是独立恢复工程，使用 Unity 6000.4.0f1，源码位于根目录 `recovery/bloody/`。它有单独的来源、构建和验收，详见[恢复工程状态](../BLOODY_FULL_PORT_STATUS.md)。

## 可再生成的内容

主工程 `Library/`、`Temp/` 和 `Logs/` 由 Unity 生成；关闭使用该工程的编辑器和构建进程后再清理。删除 Library 会触发重新导入。

根目录 `output/` 中还保存本地包依赖、原始资源导出和试玩存档，恢复工程的 Packages 清单会直接引用其中部分目录；清理以具体目录与引用为准。
