# 高地邻格覆盖修复（2026-10-27）

原渲染器将2×2景观的全部侧壁放到整块的前角才绘制。同一条对角线上，右前方的低地可能先画完，随后又被高地侧壁覆盖，看起来少了一格。

现在按照全图逐格的远近顺序绘制侧壁和地面。多格景观的每个格子均先铺自身地面，再绘制完整的多格景观美术；底面取自现有 terrain-biomes-v3 素材包。剔除范围同时考虑侧壁高度，避免高侧壁进入视口时被提前略过。四处聚落、整块属性、高低起伏、地图数据和存档规则保持不变。

原生回归场景设置一块6级高的2×2梯田，其余格子为2级地面。测试模式将地面临时显示为青色，直接采样实际屏幕截图中的四个低处邻格，确认它们都显示地面颜色且能选中对应地格。此诊断颜色仅用于独立存档的原生验收，正常游戏使用原有素材。

366项原生检查全部通过，包括四个截图像素检查、49个活动地区的覆盖、属性、通路、地格选中、四处聚落、行走、进城缓存以及不同窗口尺寸。验收记录：`validation/complete-ground-native.json`。修复截图：`validation/screenshots/complete-ground-cliff.png`。诊断截图：`validation/screenshots/complete-ground-pixel-regression.png`。

成品：`unity/Build/CompleteGround/江湖未央.exe`。根目录 `启动Unity版.bat` 通过 `unity/Build/active-build.txt` 启动此版本。
