# 第三方与资源来源

本仓库自有程序、界面和皮影矢量资源采用根目录的 MIT 许可证。外部工具、引擎和数据继续适用各自的条款。

| 内容 | 来源与许可 |
| --- | --- |
| 海岸线及陆地轮廓 | [Natural Earth](https://www.naturalearthdata.com/about/terms-of-use/)，公共领域。具体来源和原始摘要保存在 `static/geography*.json`，重建工具是 `scripts/build_map.py`。 |
| 山水与人物小像 | 本项目通过 imagegen 生成并提供，原始提示词与接入记录见 `static/assets/ART.md`。没有提取其他游戏的美术文件。 |
| Unity | 用户自行安装，适用 [Unity 软件条款](https://unity.com/legal)。仓库不分发 Unity 编辑器或许可证。 |
| Python | 用户自行安装；可选 Windows 打包工具复制本机 CPython，并把其 `LICENSE.txt` 一同放入成品运行时。参见 [Python 许可](https://docs.python.org/3/license.html)。 |
| Pillow | 仅用于开发时生成地图资源，参见 [Pillow LICENSE](https://github.com/python-pillow/Pillow/blob/main/LICENSE)。 |
| 思源宋体 / Noto Serif CJK SC | © Adobe，[SIL Open Font License 1.1](https://openfontlicense.org/)。Unity 客户端内置其子集（`unity/JianghuUnity/Assets/Resources/Fonts/`，许可全文同目录 `LICENSE-OFL.txt`），由 `unity/build_fonts.py` 生成。 |
| NumPy、SciPy、fontTools | 仅用于开发时生成界面纹理、舆图与字体子集，各自适用 BSD / MIT 许可。 |
| Sharp | 仅用于开发时生成皮影纹理，参见 [Sharp license](https://github.com/lovell/sharp/blob/main/LICENSE)。安装的依赖保留各自许可，依赖不打进源码仓库。 |

本地模型及 llama.cpp 由使用者按需下载，均不包含在仓库中；相关模型和运行时使用其各自许可。没有模型也可完整游玩规则模式。
