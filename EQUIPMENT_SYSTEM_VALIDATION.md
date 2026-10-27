# 装备系统 · 2026-10-28

装备仅保留主手、副手、护甲三个槽位。

| 槽位 | 当前可装备物品 | 生效方式 |
| --- | --- | --- |
| 主手 | 现有16种兵器 | 物理质量、操控、耗气、杀伤、压盾和耐久六项属性直接进入战斗 |
| 副手 | 盾牌 | 提供有效防护并生成实际盾牌；主手双手兵器完好时暂时停用 |
| 护甲 | 粗布衣、熟皮护衣、云纹锦袍等衣甲 | 增加实际受伤减免，耐久归零后失效 |

为兼容已有物号和存档，内部继续使用 `weapon`、`shield`、`armor` 三个键。旧 `accessory`、`tool` 绑定读取时解除，物品、归属与来源记录全部保留；普通佩饰和器具不再提供装备增益。

## 使用与规则

从行囊入口默认打开装备页。左侧显示三个槽位与实际生效效果，中间列出当前槽位可选物品，右侧提供属性、耐久、穿卸、修补与替换对比。护甲和副手显示防护变化，主手保持六项兵器属性。NPC物品页也显示相同的三槽装备状态。

每个槽位区分未装备、生效中、已损坏、暂时停用。双手兵器暂时停用副手盾牌，盾牌保留原物号和装备绑定；换回单手兵器或主手损坏后自动恢复。停用与损坏装备不提供防护，也不计入战斗磨损。玩家和NPC共用 `jianghu/equipment.py` 的判定，战斗不再另行扣一次盾牌防护。

替换预览不修改世界、银两、随机数或事件。穿卸不推进时间、不收取银两；被替换物品继续保留在行囊。修补沿用真实材料与工费。切磋进行中只允许查看装备，穿卸与修补均被服务器拒绝。所有装备操作沿用原物品事件与存档流程。

护甲接入防护数值，服饰外观沿用现有皮影衣橱。兵器独立物理模型、双手握点与碰撞逻辑继续由内部类型配置提供。

不增加兵器 attack、背包重量、承重或携带上限。

## 接口与验证

`GET /api/equipment` 返回三个槽位、有效数值、候选装备、替换预览和战斗锁定状态。`POST /api/equipment` 仅接受 `operation` 与 `item_id`，操作为 `equip`、`unequip`、`repair`；复用物品交易校验、事件和持久化。旧 `/api/items` 保持兼容。

67项相关规则测试通过，含120个月物品归属与读档推演：

```powershell
python -X utf8 -m unittest tests.test_equipment tests.test_weapons tests.test_physical_combat tests.test_combat_attributes tests.test_physical_impact tests.test_combat_pace -q
python -X utf8 -m unittest tests.test_items -q
```

Unity 6000.6.7f1构建成功：`unity/Build/EquipmentSystem/江湖未央.exe`。

独立客户端流程36项检查通过，覆盖三槽穿卸、购买与材料修补、替换预览、实际刚体主手和副手状态、护甲防护、战斗换装锁定、停用盾牌不磨损、NPC装备状态。关闭后重新启动同一测试世界，另外4项检查通过，确认装备实例、耐久、绑定及生效数值保持一致。

原生记录：`validation/equipment-system-native-20261028.json`、`validation/equipment-system-resume-20261028.json`。汇总：`validation/equipment-system-20261028.json`。

在可见的独立测试窗口中检查了三槽排版、物品列表、六项兵器属性、替换对比，并实际点击装备双手战斧，确认副手变为暂时停用、装备防护由6降到4、对应减伤减少3.6个百分点。验收截图：

- `validation/screenshots/equipment-three-slots-20261028.png`
- `validation/screenshots/equipment-comparison-20261028.png`
- `validation/screenshots/equipment-offhand-suppressed-20261028.png`

所有程序验证均使用 `tmp/equipment-system-20261028/` 下新建或复制的测试世界，没有读取或写入正式存档。交互预览窗口使用其中的 `visual-save`，正常启动器继续使用原来的正式存档路径。

默认启动版本为 `EquipmentSystem`，重新正常启动游戏后加载。具体版本以 `unity/Build/active-build.txt` 为准。

可使用 `scripts/prepare_weapon_acceptance.py --output <全新目录>` 创建验收世界，向构建程序传入 `-saveDir <目录>/native-save -equipmentSmoke <结果目录>`；第二次运行增加 `-equipmentPhase resume` 验证重启持久化。
