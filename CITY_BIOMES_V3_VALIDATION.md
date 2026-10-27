> 历史交付记录：以下描述地图版本2。当前30／20／10地区、台地和1／4／9格聚落请见 [CITY_REGIONS_VALIDATION.md](CITY_REGIONS_VALIDATION.md)。

# 城中地图重建 · terrain-biomes-v3 · 2026-10-26

全部 168 个地点使用 `terrain-biomes-v3-20261026.zip` 的 32 种地貌重建。当前世界的 49 处聚落沿用同一套地图数据。项目仅保留 Unity 客户端，不再加载旧城中地形、建筑或景物贴图。

| 档位 | 规则 | 尺寸 | 全部地点数量 |
| --- | --- | --- | --- |
| 大城 | 都城，或地点权重 ≥ 8 的区域中心 | 40×40 | 34 |
| 中城 | 其余城市、港口 | 30×30 | 93 |
| 小城 | 镇、村落、营地、山地据点 | 20×20 | 41 |

保留各城命名街道、山水方向、设施和组织驻地，按新尺寸重新铺设街巷和跨水桥梁。素材编号 01—16 占 1×1，17—32 占完整 2×2；每格只由一个地貌实例覆盖，大地貌不覆盖设施、街道和桥。设施以名称和字牌标示，保留交易、修习、官府及出城等功能；用户指定的包不含建筑立绘，因此没有借用旧建筑图补齐。

地图版本升级为 2。加载版本 1 存档时，设施上的主角仍留在同一设施，其他位置投影到相邻可达地格；迁移不扣行动力、不推进月份、不新增人物活动、不消耗世界随机数。Unity 继续使用自己的独立存档。

## 资源与生成

- 原图：`art/terrain-biomes-v3/`，32 张原图逐项校验 ZIP 清单中的 SHA-256。
- Unity：`unity/JianghuUnity/Assets/Resources/CityBiomes/`。
- 导入器：`python -X utf8 unity/import_city_biomes.py`。完整原图保留，运行图统一为 512×512 透明 PNG，按地面两侧角和底点校准到 2:1 地面与共同锚点。
- 共享后端：`python -X utf8 unity/prepare.py --backend-only`。
- 生成清单：`validation/city-biomes-v3-inventory.json`，含全部地点档位、布局指纹、设施数和地貌使用次数。32 种地貌均已用到，导出清单记录原图与 Unity 运行图的摘要。

大地图支持全图、缩放、坐标和移动。Unity 可用滚轮或工具条缩放至 800%，拖动查看。

## 验证与启动

城中地图、主角和聚落专项 35 项测试覆盖 168 个地点的多随机种子设施可达性、占地、道路、存档迁移和本地接口。移除网页后，接口测试同时检查网页路径均返回404；Unity 原生验收 300 项全部通过，覆盖当前 49 处聚落的寻路费用、坐标命中、地貌占地、渲染和实际行走，新结果见 `validation/unity-only-native.json`。

完整回归运行 244 项，通过 243 项。唯一失败为本次未改动的人物素材检查：`test_paper_cast.PaperCastTests.test_resting_assembly_matches_the_supplied_final_silhouette_and_pigment` 的 `m-daoist` 子项，源参考图尺寸 565×888，静态拼合图尺寸 602×913。该检查不涉及城中地图；未改动人物图或放宽检查。完整日志保存在 `output/city-biomes-v3-regression-final.log`。

画面：`validation/screenshots/city-biomes-v3-overview.png` 和 `validation/screenshots/city-biomes-v3-detail.png`。验收使用独立测试存档；截图中的年月和人物属于测试副本。

Windows 版本：`unity/Build/BiomesV3/江湖未央.exe`。`unity/Build/active-build.txt` 指向 `BiomesV3`，双击根目录 `启动Unity版.bat` 即可打开；已经运行的旧进程需退出后重新启动。
