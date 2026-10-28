# 武功秘籍与求师验收（2026-10-28）

当前入口调整：NPC人物卷中的求师入口及旧 `/api/hero` 的 `seek_teacher` 操作已删除；玩家统一从对话「修习 → 请教」请求秘籍，仍消耗2行动力，不占用NPC行程。下列原入口与占时说明属于此前版本记录。`ManualsSmoke` 已改为通过互动界面执行同一实体交付流程。

14派126门现有功法各对应一种同品级的实体秘籍。使用消耗一册，永久记录已学功法，立即计入人物能力和战斗；身份、品阶、门派改变后保留。同功法只计一次加成，已会时不能重复使用。

玩家入口：NPC人物卷 → 武学 → 求师学艺。选择对方已掌握的功法，双方须同地区，老师须成年、在世、身体允许并有空闲。玩家每次求师消耗2行动力，老师占用一周；对方按性格和交情接受或拒绝。接受后优先交付老师行囊现有的对应秘籍；没有时依所学抄录一册，记录来源与授业事件。收到秘籍不直接学会，仍需在自己的行囊点击「使用秘籍 · 学会功法」。

NPC从认识的同地区人物中求师，遵守双方行程、时间和每月次数；收到后可自主使用。已掌握、已持有或本月已求取同一老师同一秘籍时不可重复求取。学习、传授、拒绝均记录事实，旧修习、突破和旧秘籍仍保持停用。

旧档保留既有世界、人物、物品和已学功法；首次启用一次性登记126册货栈秘籍，记录为新增期初存货，不改写历史事件、不消耗随机数或推进月份。之后读档不重复登记。货栈也支持秘籍买卖，行囊支持赠送与继承。

## 已执行验证

- `python -m unittest tests.test_martial_manuals -v`：9项通过，覆盖126种对应关系、真实加成、消耗一册、重复操作原子性、换派/身份变动和数据库重启、现有秘籍转交、抄本来源、拒绝与冷却、无效求师、NPC求师后使用以及旧档一次性登记。
- `python -m unittest tests.test_combat_attributes.CombatAttributesTests.test_retired_training_routes_are_atomic_and_never_regenerate_arts tests.test_martial_manuals tests.test_martial_curricula -q`：22项通过。
- Unity 6000.6.7f1 Windows构建成功：`unity/Build/MartialManuals/江湖未央.exe`。在`output/manuals-20261028/BuildProject`隔离工程构建。
- 独立存档`output/manuals-20261028/save/world.sqlite`运行原生验收，11项通过，无运行错误。覆盖求师入口、实体交付、行动力、收到未直接学会、使用后的实时武学刷新、消耗、实际根基加成、全部126种货栈秘籍和月份不变。
- 原生验收结果与截图归档于`validation/martial-manuals-native-20261028.json`、`validation/screenshots/manuals-*.png`。
- 全量485项回归首轮有2项失败：一项仍断言目录不应有任何秘籍，已按新需求更新并在22项专项中重跑通过；另一项`test_player_equipment_affects_sparring_and_monthly_recovery`期望认输后扣世界体力，与工作区已有的独立切磋规则不符。临时加载Git原版`items.py`和`martial.py`后可复现同一失败，记录于`output/manuals-preexisting-combat-check.log`；本次没有改动该战斗规则或该断言。其余483项在首轮通过。

此次验收未修改玩家正式存档，也未发起真实模型调用。长期经济和大量人物互传后的平衡尚未做玩家试玩校准。

## 复现客户端验收

```powershell
python unity/prepare.py --backend-only
python scripts/validate_martial_manuals.py output/manuals-fresh/save
.\unity\Build\MartialManuals\江湖未央.exe -force-d3d11 -saveDir "D:\Desktop\新新游戏\output\manuals-fresh\save" -manualsSmoke "D:\Desktop\新新游戏\output\manuals-fresh\native"
```

必须使用新的独立验收目录；验收会求师、使用物品并保存。
