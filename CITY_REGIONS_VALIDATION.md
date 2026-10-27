# Unity 城中地区：30 / 20 / 10 与高低地格

当前城中地图版本为3，仅维护 Unity 客户端。

| 重要程度 | 地区尺寸 | 地点数 |
| --- | --- | --- |
| 都城、权重≥8的区域中心 | 30×30 | 34 |
| 其余城市、港口 | 20×20 | 93 |
| 镇、村落、营地与山地据点 | 10×10 | 41 |

地形保留各地的山水方位与命名地标，在独立随机种子下进行平滑扭曲。树林、田地形成连片分布，大型地貌不再只对齐偶数坐标；街路按地形代价连接设施，替代固定的三横三纵棋盘。不同世界种子的地貌和台地不同，同一存档重启保持一致，不消耗世界推演的随机数。

地格恢复实际高度、土石侧面和道路台阶。水面为零，高地与山岭形成不同层次；相邻可行走地格最多相差一级。完整的2×2地貌和1×1、2×2、3×3聚落各自统一高度，避免图块撕裂和斜地基。人物、路径、定位和点选均跟随抬升地表。默认视角放大，工具条「全图」可查看完整地区。

## 聚落素材

建筑只使用用户提供的 `output/imagegen/settlement-tiles-1-4-9-20261026.zip` 中三张选定图：

- `01-village-1cell`：1×1，普通院落与设施。
- `02-town-4cell`：2×2，重要设施与街区。
- `03-city-9cell`：3×3，大区核心宫府或官署。

原图保留于 `art/settlement-tiles-1-4-9/`，按原清单逐张校验 SHA-256。`python unity/import_city_settlements.py` 校准地面菱形与锚点，生成1024×1024透明运行图，保存到 `Assets/Resources/CitySettlements/`。不导入包内参考图，渲染器不回退到其他建筑素材。自然地貌继续使用此前指定的32种 `terrain-biomes-v3` 素材。

旧版本1和2存档在加载时迁移到新地图：主角若位于设施入口，继续留在相同设施；其他位置缩放到相邻可达地格。迁移不扣行动力、不推进年月、不新增事件。版本2的168个原布局指纹已与上次交付清单逐项比对一致，保留旧坐标解释。

## 检查与运行

相关规则测试为 `tests/test_city_maps.py`、`tests/test_city_landscape.py`、`tests/test_protagonist.py`、`tests/test_settlements.py`；检查三种尺寸、多随机种子的设施通路、占地、地貌覆盖、地格高程和旧存档迁移。

原生检查与截图见 `validation/living-regions-native.json`、`validation/screenshots/living-regions-*.png`；各地点尺寸和地格摘要见 `validation/living-regions.json`。这些验收使用独立测试存档。

Windows 成品为 `unity/Build/LivingRegions/江湖未央.exe`。根目录 `启动Unity版.bat` 按 `unity/Build/active-build.txt` 选择当前版本。
