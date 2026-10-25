# 社交、国家与门派完善验证

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
