# GitHub默认版本发布验收（2026-10-28）

本次发布将当前Unity游戏源码、美术、规则副本及验收记录整合到 `main`。源快照从Git待提交文件导出到独立目录，测试与Windows构建均在该导出副本进行，未依赖个人存档、模型或本地Python运行时包。发布期间其他工作继续产生的改动留在原工作目录。

## 行为与兼容

- 玩家入门统一从该门派三品及以上人物的对话「互动」栏办理，移除门派页旧入门和全部门派差事。
- NPC人物卷不再提供求师，互动「修习 → 请教」与秘籍使用保留。
- 整合当前连续对话、按行动力行旅、行囊界面、人物外貌与物理战斗及相应素材。
- 保留旧贡献、账目、年谱和晋升考核，新的履职来源尚未加入。
- 修复AI初始化首帧的存档问题：源动作初始化设置有效等级，调度器实际启动后才覆盖该等级，避免未初始化的零值使战斗保存被后端拒绝。
- 补全随包规则模块和Unity新资源的 `.meta` 文件；根目录规则与随包副本保持一致。

## 验证

- 干净源码副本：`python -X utf8 -m unittest discover -s tests -q`，553项通过，用时271.098秒。
- Windows成品：`GitHubDefault20261028`，Unity 6000.6.7f1构建成功。
- 门派原生检查14项通过；秘籍原生检查11项通过。
- 源动作、部件与正式战斗保存检查40项通过；冷重启恢复及结算检查8项通过。
- 原生合计73项通过，使用独立测试存档；没有Unity运行异常。
- GitHub的Windows/Linux规则和空白检查，以发布PR #9最终提交关联的Actions结果为准。

## 证据

- [门派原生检查](validation/github-default-sect-20261028.json)
- [秘籍原生检查](validation/github-default-manuals-20261028.json)
- [动作与正式保存](validation/github-default-combat-20261028.json)
- [冷重启恢复与结算](validation/github-default-combat-resume-20261028.json)
- [门派入口调整的详细规则](SECT_INTERACTIONS_VALIDATION.md)

原生检查不调用真实本地模型，也不证明长期经济、全部战斗情境或未完成的恢复工程达到一比一效果。源码仓库不包含Windows成品、个人存档、模型、导出缓存和机器专用运行时；新克隆须按 `CONTRIBUTING.md` 安装Python与Unity后构建。
