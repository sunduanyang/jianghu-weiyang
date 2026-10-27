# 城中地图全屏显示验收

日期：2026-10-27（Asia/Taipei）。Unity 6000.6.7f1，Windows / Direct3D11。

城中地图覆盖整个游戏客户区，左右名册、城中详情、工具条、小地图和底部操作栏在地图之上绘制。地图不再依赖中间矩形的大小；收起名册不改变地图比例和中心。窗口比例与1680×960界面基准不同时，地图继续覆盖多出的区域。首次进入使用240%视角，「全图」仍可缩小查看完整城镇。

界面控件拦截地图输入，滚轮以鼠标为中心缩放，空白地图区域支持拖动、点选和双击移动。缩放窗口时，线条在局部GUI空间变换，修正小地图线条偏移。

## 构建和验收

- 成品：`unity/Build/CityCleanHud/江湖未央.exe`。启动器的`active-build.txt`已选择`CityCleanHud`。
- 构建日志：`unity/city-fullscreen-build.log`，记录`JIANGHU_BUILD_OK`，退出码0。
- 原生验收：`validation/city-fullscreen-native.json`，313项全部通过。
- 三种窗口尺寸：1280×800、1680×960、1920×1080。地图像素边界分别精确覆盖客户区；每种尺寸检查10处UI遮挡、收起名册后的相机稳定，以及鼠标缩放锚点。
- 继续通过地图资源、各城镇寻路、台地地格点选、独立坐标格、行走消耗、月份保持和无模型调用检查；没有Unity错误或异常。
- 验收使用SQLite在线备份生成的独立测试存档，未使用正式存档目录执行移动测试。
- 截图：`validation/screenshots/city-fullscreen-1920x1080.png`、`validation/screenshots/city-fullscreen-1280x800.png`。
## 左下角半身像透明背景修正（2026-10-27）

- 补齐`Resources/PaperBust/`中24套透明半身像，直接使用已有拆分角色静立装配的原始PNG，不修改人物像素。图片四角均透明，Unity导入保留Alpha。
- 半身像缺失时的备用头像绘制关闭矩形底色，名单头像仍保留原来的底色。正常半身像绘制显式启用Alpha混合。
- 资源检查：`validation/transparent-hero-bust-assets.json`。24套资源与装配源图逐字节一致；既有`test_composite_bust_and_visual_metrics_describe_the_split_assembly`通过。
- 最终Windows构建：`unity/transparent-bust-final-build.log`，构建成功、退出码0。默认启动版本为`CityFullscreenBust`。
- 最终原生验收：`validation/transparent-hero-bust-native.json`，313项全部通过，使用独立测试存档。已逐图复核三种窗口尺寸下的半身像轮廓和地图背景。
- 实际客户端截图：`validation/screenshots/hero-bust-transparent-1920x1080.png`。
## 界面精简与小地图位置（2026-10-27）

删除地图底部的地形行走消耗／坐标行动力横条，移除地图页上的行旅手记；世界记录数据继续保留。城中小地图移到右上角原手记位置（虚拟坐标1420,70，252×176），图面随面板放大，保留点击定位、主角位置和收览全图。

原小地图及横条区域恢复地图输入，右上角新小地图拦截地图点击。最终成品为`unity/Build/CityCleanHud/江湖未央.exe`，启动器已选择该版本。`unity/city-clean-hud-build.log`记录构建成功及退出码0。

使用独立测试存档运行原生验收，`validation/city-clean-hud-native.json`的313项检查全部通过，覆盖1280×800、1680×960、1920×1080。已复核客户端截图：`validation/screenshots/city-clean-hud-1920x1080.png`。旧版日志和截图保留为历史验证记录。