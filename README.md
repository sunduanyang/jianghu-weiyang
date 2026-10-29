# 江湖未央

《江湖未央》是以人物、门派、城镇与因果事件持续演化为核心的武侠沙盒。Unity 提供原生界面、地图、皮影人物与物理战斗，Python 执行共享规则并保存 SQLite 世界。

## 开始游玩

Windows 本地已有成品时，双击根目录的 **启动Unity版.bat**。公开源码需要按[开发指南](CONTRIBUTING.md)构建；成品可携带私有 Python，没有携带时需要 Python 3.12 或以上版本。

启动器读取 `unity/Build/active-build.txt`，选择对应目录中的游戏；没有有效标记时才回退到 `unity/Build/Current/`。查看本机当前版本：

```powershell
Get-Content unity/Build/active-build.txt
```

源码与已构建成品是两份内容。修改规则后要同步后端，修改客户端后要重新构建；不能仅凭工作区新文件判断正在运行的成品已经包含改动。

首次启动按当前规则建立新世界。通过「系统 → 存档」查看实际数据库路径、另起世界或导入旧档。导入会创建副本；测试和试玩使用独立的 `-saveDir`。

## 主要玩法

- **人物与时间**：创建主角后在本月行动，结束本月时由 NPC 执行各自行动，再进入下月。人物资料中的身份、家计、人情、筹划与年谱来自真实世界记录。
- **交互**：点击人物进入交互界面，查看条件与行动力消耗后办理赠礼、交易、请教、亲近或敌对事项。查看 NPC 行囊只展示对方物品；赠物和装备操作明确使用主角自己的行囊。
- **地图**：天下地图选择地区并确认真实相邻路线；精力不足时由玩家决定是否过月继续。城中有舆图和长卷两种视图：舆图支持缩放与拖动，点选聚落、城门或去路后按实际步道前往，Esc 停止行走；双击聚落展开长卷查看设施与在场之人。过河采用自然浅滩。
- **行囊与装备**：类别、名称搜索和重置用于筛选，物品自动排列。装备只有主手、副手、护甲三槽；双手兵器暂时停用盾牌，停用或损坏装备不继续提供防护或产生战斗磨损。
- **门派与社会**：门派九品和社会九品表示不同身份。升降依真实任职、履职、经营与判决，保留事件前因。具体制度见[人物品阶升降](RANK_PROGRESSION.md)。
- **武学与秘籍**：14 派共 126 门功法，目录见[九品武学设计](MARTIAL_NINE_GRADES_DESIGN.md)。从人物交互「修习 → 请教」取得实体秘籍后，仍须在自己的行囊使用；学习消耗一册，已经学会的功法不重复叠加，身份或门派改变后保留。
- **物理交手**：切磋、抢夺、袭击共用正式战斗实现。新切磋按独立对局重置本场状态；暂离与恢复保留已有战局，其他敌对交手按世界规则处理。操作以客户端 H 帮助为准。
- **社会模拟**：家户、婚姻、生育死亡、经营与互济按月推进。NPC 的长期筹划通过实际合法行动执行，知晓程度与声望按城镇传播；研究实验中的统计数字不作为每个存档的保证。

人物头像、交谈立像与战斗共用保存的外貌身份。原创皮影以指定拆分原件绑定，整身效果图用于视觉对照；技术原则见[贴图与骨骼绑定研究](BLOODY_BINDING_STUDY.md)。

## 可选本地模型

规则模式可以独立游玩。启用本地模型后，NPC 可结合自身处境、记忆与玩家互动生成决定和答话；实际财物、行动力和关系仍由规则校验与结算。 NPC 还能选定长期筹划，形成固定口吻与人生信条，回应玩家行为，并为实际处境主动来访；重大人生转折优先进入后台思考。

```powershell
python scripts/setup_model.py
```

安装后可运行 **启动本地模型.bat**，或在「系统 → 本地模型」中启用。模型和运行时统一由 `.runtime/manifest.json` 指定；当前安装清单使用 Qwen3.5 4B Q4_K_M 与 llama.cpp，服务名称为 `qwen3.5:4b`。非 NVIDIA 设备可按安装脚本选择 Vulkan：

```powershell
python scripts/setup_model.py --backend vulkan
```

默认本地 llama.cpp 地址为 `http://127.0.0.1:11435`，上下文 8192、一个推理槽。自定义端点与已有 Ollama 配置保留。没有模型文件时，先完成安装再启用；历史少量样例的耗时比较不能代表全部任务速度或长期决策质量。

## 开发与资料

| 文档 | 用途 |
| --- | --- |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 源码位置、后端同步、资源重建、构建与提交 |
| [unity/README.md](unity/README.md) | Unity 工程入口、启动选择和独立存档 |
| [VALIDATION.md](VALIDATION.md) | 日常验证方法、已有证据入口与已知失败 |
| [MARTIAL_NINE_GRADES_DESIGN.md](MARTIAL_NINE_GRADES_DESIGN.md) | 武学目录、能力加成与制度设计 |
| [RANK_PROGRESSION.md](RANK_PROGRESSION.md) | 门派与社会身份升降 |
| [BLOODY_BINDING_STUDY.md](BLOODY_BINDING_STUDY.md) | 固定 UV、刚柔分区、关节覆盖与绑定矩阵 |
| [BLOODY_SOURCE_PORT_VALIDATION.md](BLOODY_SOURCE_PORT_VALIDATION.md) | 正式战斗源图接入、存档契约与未通过项目 |
| [BLOODY_FULL_PORT_STATUS.md](BLOODY_FULL_PORT_STATUS.md) | 独立恢复工程的位置、来源和复现 |
| [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) | 第三方工具、字体、地图与美术来源 |

`jianghu/` 是规则源文件，Unity 的 `Assets/StreamingAssets/Backend/jianghu/` 是同步副本。原画在 `art/`，原生与规则测试在 `tests/`，原始验收数据在 `validation/`；本地构建、工具、模型、存档与运行输出不随公开源码发布。

`output/` 还包含恢复工程直接引用的本地依赖、原始导出和试玩存档。清理时按具体内容核对；旧临时工程的 Library 与已解压下载包可再生成。代码采用 [MIT 许可证](LICENSE)。
