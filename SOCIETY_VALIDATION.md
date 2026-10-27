# 社交、国家与门派完善验证

## 家计与乡里补完（2026-10-26）

本次在当前统一版补入亲友扶助、各城独立义仓、自主捐助、双方占时的邻里照护、成年手足与父母义亲接续监护，以及根据已有见闻沿驿道投亲。Unity「势力簿 → 乡里」与人物关系页、网页势力簿与人物关系页均接入实际账簿。

复现：`python -m unittest discover -s tests -v`；`python scripts/validate_social_livelihood.py`。规则同步到 Unity 使用 `python unity/prepare.py --backend-only`。

- 新增19项专项测试，覆盖扶助资金守恒、出资人生活与抚养余量、每月结算幂等、同地和双向结义条件、交恶与自主意愿、真实捐款和救济优先级、空仓与异地隔离、过期行动重验、照护双方周行程与接收方月上限、照护不代替治疗、困顿恢复、已知地点与实际行路费用、受监护者随行、查询不改状态、旧档不改随机数/财产/旧史、合并城镇保留账目、手足与义亲接续监护。
- 最终全量规则测试240项全部通过，原始本地日志 `output/social-final-tests.txt`。网页脚本通过 `node --check static/society.js`，Python 源码通过编译检查，源码、Unity 后端副本与本次构建的27份规则模块逐字节一致。
- 自然演化：种子42、初始100人推进120个月，终态118人在世，产生10次亲友扶助与157次邻里照护；种子20261026、初始800人推进24个月，终态860人在世，产生1次亲友扶助、2次自主捐助、1283次邻里照护。两组逐月检查资金非负、义仓账目平衡、四周行程无重叠，非法行动均为0；保存重读后的完整世界状态和随机数一致，读档不新增事件。
- 两组自然演化未发生义仓救济或新增接续监护；这些分支由明确构造困境的专项测试覆盖，不把未触发的机制声称为自然演化成果。
- 自然演化使用全新内存世界，未打开玩家存档，也未调用真实模型。原始摘要见 [validation/social-livelihood.json](validation/social-livelihood.json)。这些检查不代表长期经济平衡、全人口模型决策或所有灾荒情境已经验收。
- Unity 6000.6.7f1、Windows x64 / Direct3D11 构建成功。独立存档运行 `-smokePhase society`，18项原生客户端检查全部通过，涵盖49份独立乡里账户、零期初资金、人物家计、三处画面渲染、月结、账目平衡、保存、零模型调用与零运行时异常，见 [validation/social-native.json](validation/social-native.json)。已查看[乡里页](validation/screenshots/social-towns-20261026.png)与[人物关系页](validation/screenshots/social-household-20261026.png)截图。隐藏窗口首次截图全黑，未计为通过；改用正常游戏窗口完成有效画面检查，失败诊断保留在本地 `output/social-unity/`。

以下为此前独立社会分支的历史验证，版本、人口和检查范围与本次不同。

以下保留独立分支合并前的验证。与168地点、城市占领及城中地图的整合验证见 [MERGE_VALIDATION.md](MERGE_VALIDATION.md)。

基线：`v0.2.0-unified`，提交 `3ef4fe3289bf5cfaa0046dfd830d7a04a9108539`。开发位于独立 worktree、分支 `codex/social-nations-sects`，原主目录与正式存档未修改。

## 复现

```powershell
python -m unittest discover -s tests -v
python scripts/validate.py --populations 100 500 1000 --months 12 --durable --output society-populations.json
python run.py --port 8777 --population 100 --prehistory 3 --db reports/social-preview.sqlite
```

运行时仍仅依赖 Python 标准库。规则测试和预览不调用真实模型。

## 覆盖与证据

- 120项完整测试，其中新增18项社交与组织测试：独立回应、结义拒绝、和解不抹去深仇、双向冷却、双方周行程、过期提案重验、定向关系统计、门派资金守恒、驻地及库银限制、师资转账、贡献与本领双重晋升、离门贡献隔离、主持死亡接替前因、本国施政、外交实际往来、旧档补齐、存档确定性、HTTP只读查询与拜访持久化。
- 固定种子42，初始100 / 500 / 1000人，各推进12个月、每月持久化。逐月执行模拟，终态检查因果引用、秘密记忆、婚姻、国库、官职、物品归属、周行程、门派库银、门派主持、外交对称性与双向结义。各项错误均为0。原始数据库和结果保留在 `reports/`。
- 使用原标签源码生成100人、12个月的真实统一版存档，另复制数据库后升级。原有4012条事件的原始JSON、所有既有状态字段和随机数状态保持一致，续跑两个月成功。门派期初数使用原余额。原始数据库不改写，升级副本单独保留。
- 本地浏览器实查：势力簿目录、国家施政与账房、组织名册与门中账房、人物门中页、关系分类，以及拜访后情分4 / 信任2与本月按钮禁用；浏览器无错误或警告日志。修复了较矮桌面窗口中人物介入区不可见的问题。截图在 `reports/screenshots/`。

机器可读摘要：`validation/society-systems.json`。完整测试日志：`reports/society-tests.log`；人口验证：`reports/society-populations.json`；升级验证：`reports/society-upgrade.json`。

## 验证边界

这些检查确认上述规则、账户、时间和证据能共同工作；不等同于长期经济平衡、历史准确性或全人口AI质量验收。未发起真实模型调用。国家依然采用既有固定疆域，未新增战争和政权更替；请益使用既有武学目录，未声称各门派已有完整独占功法树。
