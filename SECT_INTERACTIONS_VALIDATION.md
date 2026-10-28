# 门派互动入口调整验收（2026-10-28）

玩家入门改为与该派当前一、二、三品人物对话，在「互动」栏选择「拜入门派」。四品及以下、非门派和已故人物不显示该选项；提交时重新校验对方实际门派和当前品阶。玩家仍须同地区、未入其他门派、离门满一年、战斗能力至少12，并消耗4行动力，不要求站在山门。入门登记为九品、贡献归零，记录引收人物与事件，并同步功法传承。

门派页的旧入门操作、玩家差事与NPC自动差事均已删除，旧接口不能继续结算。捐助与辞门保留原条件。NPC人物卷不再显示求师入口，旧主角求师接口已移除；对话「修习 → 请教」继续交付真实秘籍，收到后须在自己的行囊使用。

旧存档贡献、账目、差事历史和履职证据保留。晋升考核未重设，捐助仍不能代替履职；差事删除后，新的玩家履职来源尚未加入。

## 验证结果

- `python -X utf8 -m unittest discover -s tests -q`：548项通过，300.343秒。
- 门派、交互、秘籍、考核及社会系统定向回归：105项通过。
- `tests.test_sect_interactions`：4项，覆盖一至三品权限、四品及以下拒绝、当前门派归属、异地相遇、原子性、重复请求、重载，以及旧入口/差事无法执行。
- Unity Windows构建成功，标记 `SectInteractions20261028`。`unity/Build/active-build.txt` 已指向该成品。
- 根目录规则、Unity StreamingAssets副本和成品随包规则的8个改动文件哈希一致。
- 原生门派互动验收：14项通过，无Unity运行异常。
- 原生秘籍回归：11项通过，无Unity运行异常；覆盖互动中请求、实体交付、2行动力消耗、使用后学习、物品消耗与加成刷新。
- 已检查渲染截图：三品人物的互动栏显示并可确认拜入；四品人物没有该操作；NPC人物卷武学页没有求师模块。

验收均使用从空世界生成的独立SQLite数据库，未读取或修改玩家存档。首轮入门测试夹具漏设NPC目标，造成摘要读取失败；修正夹具后从新的 `save-v2` 数据库重新执行，正式结果为下列v2记录。

## 证据与复现

- 入门原生结果：`validation/sect-interactions-native-20261028-v2/acceptance.json`
- 秘籍原生结果：`validation/sect-interactions-manuals-native-20261028/acceptance.json`
- 三品人物入口：`validation/sect-interactions-native-20261028-v2/02-third-grade-recruitment.png`
- 四品人物入口：`validation/sect-interactions-native-20261028-v2/01-fourth-grade-no-entry.png`
- NPC人物卷：`validation/sect-interactions-manuals-native-20261028/01-npc-martial-view.png`
- 构建日志：`unity/sect-interactions-build.log`

重新运行须选用尚不存在的验收存档目录：

```powershell
python -X utf8 unity/prepare.py --backend-only
python -X utf8 scripts/prepare_sect_interaction_acceptance.py --save-dir output/sect-interactions-fresh/save
& 'unity/Build/SectInteractions20261028/江湖未央.exe' -force-d3d11 -screen-fullscreen 0 -saveDir 'output/sect-interactions-fresh/save' -sectInteractionSmoke 'validation/sect-interactions-fresh'
```

原生检查不调用真实本地模型；新入门为规则交互，不依赖启用AI交谈。没有新增门派委托、履职玩法或调整晋升节奏。
