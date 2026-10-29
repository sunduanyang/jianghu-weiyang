# 参与开发

欢迎通过 Issue 讨论问题，或 Fork 本仓库后提交 Pull Request。公开仓库允许任何人查看和 Fork；主仓库的合并由维护者审核。提交的贡献按本仓库的 MIT 许可证提供。

`main` 是统一主版本。开始开发前先更新 `main`，再为改动创建独立分支；测试通过后通过 Pull Request 合回 `main`。Codex 与 Claude 协作时均以最新 `main` 为起点，避免从旧功能分支继续开发而遗漏已合并内容。

## 从源码运行

本项目仅维护 Unity 原生客户端。安装 Python 3.12 或以上版本作为本地规则引擎；游戏玩法界面不使用浏览器。公开仓库不包含作者存档，首次运行按当前规则与配置创建新世界。

Unity 开发目前针对 Windows。安装 Unity 6000.6.7f1，在 Unity Hub 添加 `unity/JianghuUnity` 文件夹，打开 `Assets/Scenes/Jianghu.unity` 并播放。还需要 Python 3.12+；如果 Python 不在 PATH，可在启动编辑器前设置环境变量 `JIANGHU_PYTHON` 为 `python.exe` 的完整路径。

## 修改位置

| 要修改的内容 | 源码位置 |
| --- | --- |
| 社会模拟、地图、物品、战斗、模型与存档规则 | 根目录 `jianghu/` |
| Unity 界面和皮影动画 | `unity/JianghuUnity/Assets/Scripts/` |
| 共用皮影骨骼与动作关键帧 | `unity/paper-rig.json`、`PaperRig.cs` 与 `PaperCast.cs` |
| 24 套皮影皮肤、原始部件、提示词与来源摘要 | `art/puppet-modern-24/` |
| Unity 场景、资源和设置 | `unity/JianghuUnity/Assets/`、`Packages/`、`ProjectSettings/` |
| 地图源数据与共用原画 | `art/world-map/`、`art/unity-shared/` |

`jianghu/` 是共享规则的源文件。Unity 工程中的 `Assets/StreamingAssets/Backend/jianghu/` 是同步副本。修改规则后运行：

```powershell
python unity/prepare.py --backend-only
```

提交共享规则修改时一并提交更新后的副本。请保留 Unity 的 `.meta` 文件，避免其他人的资源引用失效。

## 重新生成资源

现有资源已入库，直接打开工程不需要重生成。更改原始美术或生成程序时，安装开发依赖：

```powershell
python -m pip install -r requirements-dev.txt
python unity/prepare.py
python unity/bake_paper_cast.py
python unity/bake_appearance_masks.py
```

修改地区、城池、驿道或地图投影（`jianghu/atlas.py`、`jianghu/historical_map.py`）后，依次运行：

```powershell
python scripts/build_map.py --reuse-coast   # 沿用海岸线，投影变化时自动换算
python scripts/build_compact_map.py         # 地区划分与驿道
python unity/prepare.py --map-only          # 同步后端并重绘天下地图与地区底色
```

城中舆图与长卷的建筑、绢底和节点标记由 `python unity/bake_scroll.py` 生成。只调整界面纹理或舆图画法时，运行 `python unity/bake_ui.py` 即可（`--ui-only` 只重做界面纹理）；重建内置字体子集用 `python unity/build_fonts.py`。

战斗改进必须应用于正式游戏的切磋、抢夺和袭击，共用 `unity/JianghuUnity/Assets/Scripts/PhysicsCombat/`，不得只交付独立试玩。发布前使用普通游戏客户端和独立验收存档，从人物交互实际进入上述战斗，验证新机制、保存、返回和结算，再更新 `unity/Build/active-build.txt`。独立物理试玩通过正式启动器传入 `-referenceCombat 1` 打开，与正式游戏使用同一成品；`demos/PhysicsDuelUnity/` 仅保留历史原型。

修改皮影皮肤或通用动画后，运行 `python unity/bake_paper_cast.py`，提交更新后的 `Assets/Resources/PaperCast/` 与 `.meta`。原始设计板及独立拆分件保存在 `art/puppet-modern-24/`。拆分件关节和遮挡见 `split-calibration-*.json`，最终效果的目标关节见 `assembly-calibration.json`。所有绑定像素必须来自右侧拆分格；最终效果只供对照。重建后同时提交 `PaperCast/`、`PaperBust/`、`PaperReference/` 及 `.meta`；在衣橱以「最终效果对照」复核绑定姿态，再检查活动时的接缝。请在 Unity 的皮影衣橱检查左右朝向、各动作、关节与配饰，再检查人物头像、对话和演武中的形象一致性。

修改皮影原件后还需运行 `python unity/bake_appearance_masks.py` 重建 `Assets/Resources/Appearance/Masks/`。遮罩是线性 RGBA 数据，四个通道分别代表肤色、主衣色、发色、次衣色，不可按透明图片预处理。`Jianghu.Editor.AppearanceBuild.Build` 配置遮罩导入后调用原 Windows 构建；人物组合逻辑位于 `jianghu/appearance.py` 与 `PaperAppearanceArt.cs`。用独立 `-saveDir` 和 `-appearanceSmoke <输出目录>` 运行组合渲染验收；测试窗口必须实际绘制，隐藏窗口会得到空白截图并使关节测量失败。

如果需要让 Windows 成品自带 Python，在 Windows 的 CPython 安装环境运行 `python unity/prepare.py --bundle-python`，再使用 Unity 菜单「江湖 / 构建 Windows 游戏」。打包运行时和游戏成品不提交到源码仓库，CPython 的许可证随打包运行时保留。其他 Unity 安装路径可以通过 Unity Hub 打开，或给构建脚本传入 `-EditorPath`。

## 提交前验证

皮影源图与运行图集的逐像素校验需要 Pillow 和 NumPy。以下测试依赖仅用于开发验证，游戏规则运行仍只依赖 Python 标准库；资源重生成使用的 `requirements-dev.txt` 已包含这些依赖。

```powershell
python -m pip install -r requirements-test.txt
python -m unittest discover -s tests -v
python -m unittest discover -s tests -p test_paper_cast.py -v
git diff --check
```

Pull Request 请写清楚问题、修改后的行为和实际执行的验证。界面修改附上截图；数值或规则修改说明对已有世界的影响。CI 会运行规则测试和皮影结构检查；CI 不调用真实模型，也不包含需要 Unity 许可证的完整客户端构建。

个人存档、推理账本、模型、运行日志、Unity 的 Library/Temp 缓存及凭据文件均保留在本地。历史验收文档描述特定版本的检查范围，不能替代对新修改的验证。

## 文档和清理

根目录的 Markdown 只保留 README、本文和第三方声明，bat 只保留三个常用入口，源配置与验证脚本保留原位置。玩法与入口写入 README，开发流程写入本文；设计进入 `docs/design/`，研究与恢复状态进入 `docs/research/`，验证说明进入 `docs/validation/`。[验证指南](docs/validation/README.md)维护复现方法、证据索引和已知失败；品阶、武学、绑定与战斗的独有设计保存在对应专题。功能更新先修改这些现有文档，原始验收数据写入 `validation/` 或本地 `output/`，不为每个临时构建增加根目录报告。

外貌版本迁移由人物原有种子派生新增字段，保留既有身份与颜色，不消耗世界随机流、不推进月份。跨界面切换、职业变化、换装和重新载入都应保持人物面部身份。硬片保持比例，柔性区单独量测；固定 UV 或透明度检查不能替代实际动作、遮挡与物理握持验收。技术依据见 [BLOODY_BINDING_STUDY.md](docs/research/BLOODY_BINDING_STUDY.md)。

清理仅移除已明确不用的构建、关闭的临时工程缓存与已解压下载包。原画及其来源清单、Unity `.meta`、源配置、存档和直接引用的本地包依赖保留。正在运行或构建的工程使用的 Library 不参与清理。

启动入口统一为 `启动Unity版.bat`、`用Unity打开.bat` 和 `开发工具.bat`。新增预览或工具应加入 `scripts/dev-tools.ps1`，不再增加根目录 bat。人物图鉴与骨骼贴图预览共用当前成品；标注「历史」的试玩保留原专用构建。

主游戏构建按 `unity/build-retention.json` 保留当前成品、一个已验收的回退成品和注明用途的专用构建。发布并验收新版本后更新回退项，再运行 `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/cleanup-builds.ps1` 查看清单；加 `-Apply` 才执行删除并写入本地清理记录。脚本会检查实际进程、启动标记、路径和文件变化，遇到存档、模型或目录链接即停止。默认构建目标仍为 `Current`，日常中间构建不长期新增保留项。

独立恢复工程的根目录 `Build/`、安卓对照环境和 Unity 当前工程缓存单独维护，不在主游戏构建清理范围内。清理不会自动遍历删除 `output/`。
