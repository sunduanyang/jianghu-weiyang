# 用户红莲素材试装

使用本次上传的透明 PNG 右侧编号分件绑定正式人物骨架。原文件备份在 `art/red-lotus/source.png`，运行时源纹理在 `Assets/Resources/Characters/RedLotus/sheet.png`，两者 SHA256 均为 `ba8a451bbdafc64d307338f0790094c7d4b2122b81ab2a5568a3031e8c7e3e52`。没有重新生成、重绘或缩放原图。

## 绑定范围

- 01 头颈，03 躯干，04/05 前后衣摆，06—11 两侧上臂、前臂、手，12/15 大腿，13/16 小腿，14/17 独立靴，共 16 个可见分件。
- 02 是备用过渡片，留在原图中，本次没有强行绑入人体。
- 12/15 和 13/16 中重复出现的下肢与靴子，通过 UV 显示范围分开使用，避免将整条腿压缩到一根大腿骨。
- 原画已经包含五官。本次保留原脸、原发色、原衣色，关闭额外眉眼唇覆盖及换色遮罩。
- 头颈层级 35，衣领 40。腰腹和衣摆有限蒙皮，四肢跟随真实骨段。头颈锚点针对这份原素材单独校准。

图集中的参考整人、编号和相邻分件可能落在同一个矩形取图范围内。`part-map.png` 是独立的像素归属数据，着色器只显示当前分件所属的原图像素；它不替换原画。数据图按非 sRGB、点采样导入；原图按 1536×1024 原尺寸、无压缩导入。

## 运行方式

`试穿红莲皮影.bat` → `unity/launch-red-lotus.ps1` → `unity/Build/RedLotusTrial/江湖未央.exe`。

独立存档为 `output/red-lotus/showcase`。试装界面可暂停/播放行走、切换朝向、显示骨架，并进入正式人物交互或正式切磋；界面下方的“返回素材试装”可回到预览。试装参数 `-redLotusTrial 1` 仅改变本进程成年男性的显示外观，不写入人物外貌档案，也不修改默认构建标记。

重建：`python unity/bake_red_lotus.py`，再通过 Unity `Jianghu.Editor.CharacterModulesBuild.Build` 构建。所有锚点、UV 和源分件编号在 `unity/bake_red_lotus.py` 与 `art/red-lotus/binding.json`。

## 验证

最终验收结果见 `validation/red-lotus-native-20261028.json`；实机截图见 `validation/screenshots/red-lotus-workshop.png`、`red-lotus-interaction.png` 和 `red-lotus-battle.png`。

第二轮构建 `output/red-lotus/build-v2.log` 成功。12 项素材试装检查通过，原有人物系统的 14 项检查在同一构建、不带试装参数时通过，结果保存在 `validation/red-lotus-normal-regression-20261028.json`。实际试装网格为 576 个顶点、16 个可见网格，运行错误为 0。源图尺寸仍为 1536×1024；正式交互窗口中约 570×520 像素的立绘由 768×704 缓存提供。

原生检查覆盖源纹理尺寸、原脸/原色路径、左右朝向共 48 帧行走网格有限性、16 分件与领口层级、大窗口交互采样、月份保持、正式战斗接入及缓存预算。检查不替代所有动作和极限姿态的美术验收；本次首先交付可实际试穿、行走和交手的原素材绑定版。
