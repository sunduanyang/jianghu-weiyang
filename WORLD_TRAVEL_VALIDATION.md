# 天下地图双击行旅验收（2026-10-28）

天下地图支持以下操作：

- 单击区域、地名签或城镇图标，只选中区域。
- 双击主角所在区域，直接打开该区域的舆图，即使此前选择了长卷或地格。
- 双击其他区域，弹出移动确认，显示沿途区域、全程精力（每月行动力）与本月可先抵达的区域。
- 远行不消耗盘缠，零银两也可沿陆路或航路移动。
- 确认后按真实相邻路线逐区前进，每段分别扣精力、登记事件和保存，抵达目的地区域后打开舆图。
- 若下一段精力不足，停在最后一个完整抵达的区域，提示「精力不足，点击过月」。点击「过月」只推进一个月，恢复精力后继续原目标；再次不足则再次等候手动点击，不自动跨月。
- 出发前点击取消或按 Esc 关闭弹窗，不改变位置、精力、银两、月份和事件。途中停止只取消后续路段，保留已发生的移动和消耗。
- 拖动、右键、失焦、滚轮缩放和浮层操作不会被当作区域双击。

精力由规则端的只读 `/api/hero/travel?place=<id>` 计算，选择精力最少的真实驿道／航路。报价包含全程 `points`、逐段 `legs`、本月能完成的 `travel_legs`／`travel_points`／`travel_place`，以及 `can_finish` 和 `needs_month`。兼容字段 `silver` 固定为零；不检查或扣除玩家银两，也不向沿途官府登记行旅收费。陆路与航路沿用原精力公式。可从当前区域任意位置出发，无需先走到城门或舟埠，也不额外扣除到出入口的精力。仍须暂停世界推演并结束切磋。

Unity 按报价逐段提交 `/api/hero` 的 `travel` 动作，每段携带该次报价的行程版本与月份，途中重新读取后续路线。旧报价不能修改已变化的行程。`travel_route` 动作同样按区域完成本月负担得起的路线前缀，不能支付下一整段时停止，绝不扣出负精力或停在半段中途；连第一段都无法支付时只返回过月提示，不修改状态。每段落地保存确保已发生的移动在重启后保留。点击过月使用既有 `end_turn`，保留 NPC 独立行动和新月恢复规则；客户端也等待模型启用时的异步过月作业结束后再续行。

## 检查

运行 `python -m unittest tests.test_world_travel tests.test_protagonist tests.test_economics`：43 项通过。覆盖零银两陆路／航路、单段与多段、每段事件、精力恰好足够与不足的边界、部分抵达、手动过月续行、途中存档重启、旧版本拒绝、无路可达和无效目标，并回归原有买卖、运费税与经济余额规则。玩家远行免盘缠不改变 NPC 贸易的运费和税收。

此前城内任意位置出发改动的 `tests.test_city_maps` 和 `tests.test_player_turn_order` 共 22 项通过，记录保留于上一轮验收。

Unity 6000.6.7f1 Windows Direct3D11 构建成功，19 项原生检查全部通过，无 Unity 错误或异常。原生验收使用零银两的独立测试存档，覆盖城内出发、报价、取消与 Esc、逐区抵达、精力不足停留、手动过月和原目标续行。实际行程由抚州前往建康府：剩余 16 精力时先到临安府，花费 12、剩余 4，停下显示过月提示；手动过月一次后继续抵达建康府，剩余 20 精力并打开舆图。实际渲染截图覆盖 1680×960 和 1280×720。未启用真实模型调用。

本次取消盘缠与分段行旅的机器记录：`validation/travel-by-energy-native-20261028.json`。截图：`validation/screenshots/travel-by-energy-confirmation-20261028.png`、`validation/screenshots/travel-by-energy-confirmation-small-20261028.png`、`validation/screenshots/travel-by-energy-exhausted-20261028.png`。此前出入口限制与双击行旅验收分别保留在 `validation/travel-anywhere-native-20261028.json`、`validation/world-travel-native-20261028.json`。

构建：`unity/Build/TravelByEnergy/江湖未央.exe`。启动器通过 `unity/Build/active-build.txt` 选择本地构建。共享规则源文件、Unity 内嵌副本和成品内副本的 SHA-256 相同。最终验收使用 `tmp/travel-by-energy-verified-20261028/save`，没有修改正式存档。
