# 第三方与资源来源

本仓库自有程序、界面和皮影矢量资源采用根目录的 MIT 许可证。外部工具、引擎和数据继续适用各自的条款。

| 内容 | 来源与许可 |
| --- | --- |
| 海岸线及陆地轮廓 | [Natural Earth](https://www.naturalearthdata.com/about/terms-of-use/)，公共领域。具体来源和原始摘要保存在 `art/world-map/geography*.json`，重建工具是 `scripts/build_map.py`。 |
| 山水与人物小像 | 本项目通过 imagegen 生成并提供，原始提示词与接入记录见 `art/unity-shared/ART.md`。没有提取其他游戏的美术文件。 |
| 战斗山石背景与参考界面 | 按用户提供的《太吾绘卷》战斗截图参考重制；背景通过内置 image_gen 生成，提示词和记录见 `art/battle-ui/`，运行图位于 `Assets/Resources/BattleUI/stone-stage.png`。式轮、太极盘、状态和功法图标由本项目代码绘制，未提取原游戏文件。 |
| 城中自然景观与聚落 | 用户提供的 `terrain-biomes-v3-20261026.zip`、`settlement-tiles-1-4-9-20261026.zip` 以及 `D:\Desktop\美术` 的31张PNG。自然原图、校验与分类见 `art/terrain-biomes-v3/`、`art/landscape-library-20261027/`；聚落原图见 `art/settlement-tiles-1-4-9/`。运行图只进行透明边缘和投影标定，未提取其他游戏文件。 |
| 24 套现代皮影形象及透明部件 | 用户指定的 `puppet-cast24-modern-flat-20261026.zip`（内置 imagegen 生成）；原图、提示词、来源摘要、透明部件与骨骼绑定清单见 `art/puppet-modern-24/`。图集机械打包去底后的部件，动画由项目骨骼代码实现。 |
| Unity | 用户自行安装，适用 [Unity 软件条款](https://unity.com/legal)。仓库不分发 Unity 编辑器或许可证。 |
| Python | 用户自行安装；可选 Windows 打包工具复制本机 CPython，并把其 `LICENSE.txt` 一同放入成品运行时。参见 [Python 许可](https://docs.python.org/3/license.html)。 |
| Pillow | 仅用于开发时生成地图资源，参见 [Pillow LICENSE](https://github.com/python-pillow/Pillow/blob/main/LICENSE)。 |
| 思源宋体 / Noto Serif CJK SC | © Adobe，[SIL Open Font License 1.1](https://openfontlicense.org/)。Unity 客户端内置其子集（`unity/JianghuUnity/Assets/Resources/Fonts/`，许可全文同目录 `LICENSE-OFL.txt`），由 `unity/build_fonts.py` 生成。 |
| NumPy、SciPy、fontTools | 仅用于开发时生成界面纹理、舆图与字体子集，各自适用 BSD / MIT 许可。 |
| Sharp | 仅用于开发时生成皮影纹理，参见 [Sharp license](https://github.com/lovell/sharp/blob/main/LICENSE)。安装的依赖保留各自许可，依赖不打进源码仓库。 |

本地模型及 llama.cpp 由使用者按需下载，均不包含在仓库中；相关模型和运行时使用其各自许可。没有模型也可完整游玩规则模式。
