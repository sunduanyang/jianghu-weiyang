# 参与开发

欢迎通过 Issue 讨论问题，或 Fork 本仓库后提交 Pull Request。公开仓库允许任何人查看和 Fork；主仓库的合并由维护者审核。提交的贡献按本仓库的 MIT 许可证提供。

## 从源码运行

安装 Python 3.12 或以上版本后，执行 `python run.py`，浏览器打开 <http://127.0.0.1:8782>。不需要模型或第三方 Python 运行库。公开仓库不包含作者存档，首次运行会创建新的800人世界。

Unity 开发目前针对 Windows。安装 Unity 6000.6.7f1，在 Unity Hub 添加 `unity/JianghuUnity` 文件夹，打开 `Assets/Scenes/Jianghu.unity` 并播放。还需要 Python 3.12+；如果 Python 不在 PATH，可在启动编辑器前设置环境变量 `JIANGHU_PYTHON` 为 `python.exe` 的完整路径。

## 修改位置

| 要修改的内容 | 源码位置 |
| --- | --- |
| 社会模拟、地图、物品、战斗、模型与存档规则 | 根目录 `jianghu/` |
| Unity 界面和皮影动画 | `unity/JianghuUnity/Assets/Scripts/` |
| Unity 场景、资源和设置 | `unity/JianghuUnity/Assets/`、`Packages/`、`ProjectSettings/` |
| 网页版本界面 | `static/` |
| 皮影原始矢量轮廓 | `prototypes/shadow-puppets/puppet.js` |

`jianghu/` 是共享规则的源文件。Unity 工程中的 `Assets/StreamingAssets/Backend/jianghu/` 是同步副本。修改规则后运行：

```powershell
python unity/prepare.py --backend-only
```

提交共享规则修改时一并提交更新后的副本。请保留 Unity 的 `.meta` 文件，避免其他人的资源引用失效。

## 重新生成资源

现有资源已入库，直接打开工程不需要重生成。更改原始美术或生成程序时，安装开发依赖：

```powershell
python -m pip install -r requirements-dev.txt
npm ci
python unity/prepare.py
node unity/bake_puppets.cjs
```

如果需要让 Windows 成品自带 Python，在 Windows 的 CPython 安装环境运行 `python unity/prepare.py --bundle-python`，再使用 Unity 菜单「江湖 / 构建 Windows 游戏」。打包运行时和游戏成品不提交到源码仓库，CPython 的许可证随打包运行时保留。其他 Unity 安装路径可以通过 Unity Hub 打开，或给构建脚本传入 `-EditorPath`。

## 提交前验证

```powershell
python -m unittest discover -s tests -v
node prototypes/shadow-puppets/verify.cjs
git diff --check
```

Pull Request 请写清楚问题、修改后的行为和实际执行的验证。界面修改附上截图；数值或规则修改说明对已有世界的影响。CI 会运行规则测试和皮影结构检查；CI 不调用真实模型，也不包含需要 Unity 许可证的完整客户端构建。

个人存档、推理账本、模型、运行日志、Unity 的 Library/Temp 缓存及凭据文件均保留在本地。历史验收文档描述特定版本的检查范围，不能替代对新修改的验证。
