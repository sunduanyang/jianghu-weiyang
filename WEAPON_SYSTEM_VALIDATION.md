# 兵器系统重构 · 2026-10-28

16种现有兵器统一使用六项属性，行囊、货栈和战斗读取同一份配置。

| 属性 | 字段 | 用途 |
| --- | --- | --- |
| 物理质量 | mass | 战斗刚体质量、惯性、碰撞能量、挥动耗气 |
| 操控系数 | handling | 运械及预设攻击节奏 |
| 耗气系数 | cost | 预设起手及持续挥动耗气 |
| 杀伤系数 | damage | 接触能量相同时的伤害倍率 |
| 压盾系数 | pressure | 击盾时的架势冲击 |
| 耐久 | durability | 随物品实例保存，归零失效，修补恢复 |

五项固定数值与内部类型索引集中在 `jianghu/weapon_catalog.json`；`weapons.py` 负责生成六项属性和战斗配置。尺寸、伤害碰撞形状、握点、AI偏好距离与准备角度仍是兵器类型内部配置，不作为物品属性展示。

删除兵器模板的 `attack` 和旧练功加成。装备不再改变人物的伤害倍率；兵器杀伤系数只在接触伤害中乘一次。人物劲力、护体及衣甲防护继续按人物规则计算。每件现有兵器有明确配置，同类剑也可使用不同的质量、操控、耗气和杀伤数值。当前配置是首版游戏调校，尚未完成长期实战平衡。

删除所有物品的背包重量字段、行囊总重、承重上限、重量排序、重量卡片显示，以及购买、赠物、制药、NPC购买与产出的重量检查。实际实物、银两、材料、所有权、存活和战局锁定条件仍正常校验。

旧存档继续使用原物号和模板；不重发物品、不补货、不消耗随机数。旧实例的 attack/weight/capacity 字段在读取时清理。未完成的物理战局按 `weapon_version` 更新兵器及人物战斗参数，保留战局编号、修订号、耗时、身体姿态和既有伤势，不重新开战。

## 验证

以下规则测试55项通过：

```powershell
python -X utf8 -m unittest tests.test_weapons tests.test_items tests.test_physical_combat tests.test_combat_attributes tests.test_physical_impact tests.test_combat_pace -q
```

覆盖六属性目录、玩家和NPC装备传参、攻击加成失效、单次杀伤系数、同类兵器差异、损坏与修补、大量物品下的购买/赠物/制药、HTTP接口、旧战局与存档迁移，以及120个月物品归属推演。

Unity 6000.6.7f1构建成功：`unity/Build/WeaponSystem/江湖未央.exe`。原生客户端102项检查通过，包括16种兵器的真实刚体质量、操控、耗气、杀伤与压盾数值，超过旧承重限制的行囊购买与装备、实际挥动耗气、姿态存档和战后同一物品耐久结算。原生检查使用 `tmp/weapon-system-20261028/native-save/` 独立测试世界，没有访问正式存档。

原生截图在隐藏窗口中为黑屏，因此没有用它们作为界面验收依据。另行打开独立存档的可见预览，检查了六项属性、金额、耐久和取消负重后的排版；验收图为 `validation/screenshots/weapon-inventory-20261028.png` 与 `weapon-market-20261028.png`。

机器记录见 `validation/weapon-system-20261028.json` 和 `validation/weapon-system-native-20261028.json`。

复现原生检查时，先生成全新隔离目录，再向构建程序传入对应参数：

```powershell
python -X utf8 scripts/prepare_weapon_acceptance.py --output tmp/weapon-acceptance-fresh
# 使用上述目录的绝对路径传入：
# -saveDir <目录>/native-save
# -physicalCombatSmoke <目录>/native
# -physicalCombatPhase weapons
# -weaponFixture <目录>/fixture.json
```

本次兵器构建名为 `WeaponSystem`；当前默认版本以 `unity/Build/active-build.txt` 为准。已有旧版窗口需要退出后重新启动才能加载后续版本。
