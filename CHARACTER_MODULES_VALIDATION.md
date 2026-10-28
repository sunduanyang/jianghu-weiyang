# 正式人物模块化重构

主角和 NPC 的名册头像、地图头像、人物详情、对话立绘及正式战斗，现在共用版本 2 外貌编号与模块化美术。头型、眉、眼、唇、肤色、发色、衣色和脸宽由同一份已保存参数决定，切换界面不会重新随机。

## 资源与渲染

- 10 个头型覆盖成年男女、儿童、老人和僧人；3 种眉、3 种眼、3 种唇分别组合。
- 当前有 2 套服装 UV 模板、8 组衣色，职业类别选择相应服装模板。旧版 24 套原画仍可从衣橱查看。
- 14 个身体分件共用固定 UV；头部使用校准后的独立 UV。衣摆、腰腹、颈根采用有限蒙皮，四肢保持硬片。
- 头颈层级 35，衣领层级 40，五官层级 65 至 67；领口遮住颈根，透明背景和镂空保留。
- `CharacterRenderer` 同时用于界面和战斗。界面通过一个共用离屏模型生成缓存图；战斗为当前交手双方创建模型，直接读取真实骨段，不注入自动摆手或行走输入。
- 旧物理战斗比例通过骨段矩阵缩放适配；战斗离屏取景会按网格范围扩展，避免移动或脱离躯体的部件被固定画框裁去。

缓存图像预算为 128 MiB，按使用顺序淘汰；这一预算不包括共用源纹理、离屏相机和当前战斗角色的渲染目标。大量 NPC 不会各自持有一套网格和相机。

## 存档迁移

`jianghu/appearance.py` 将版本 1 外貌升级到版本 2，保留原种子、头部身份字段、肤色、发色、衣色、脸宽和来源。新增 `eyes`、`brows`、`lips`、`outfit`、`head_variant` 由原外貌种子确定，重复迁移得到相同结果。

迁移不消耗世界随机流、不推进月份、不增加历史事件。真实出生仍可从实际父母继承部分外貌特征。年龄决定儿童、成年与老年头型及灰发程度，职业变化保留面部身份。

## 验证

- 22 项 Python 外貌与主角测试通过，包含版本 1 迁移、随机流隔离、SQLite 往返、出生继承和组合分布。
- 正式 Unity 客户端通过真实本地后端加载独立验收存档，1,084 名 NPC 全部使用版本 2 外貌。
- 原有 12 项原生检查通过，运行错误为 0：名册与详情一致、职业换装不换脸、27 种五官组合产生不同画面、儿童老人僧人头型、换色透明度、领口层级、缓存上限、世界时间与模型调用保持、正式切磋创建，以及战斗与头像外貌一致。后续清晰度修复加入两项大窗口采样检查，见 [CHARACTER_CLARITY_VALIDATION.md](CHARACTER_CLARITY_VALIDATION.md)。
- 换色前后的最大 alpha 差为 0。缓存压力检查后仍低于 134217728 字节，使用 1 个共用界面模型。
- 结果在 `validation/character-modules-native-20261028.json`；最终整合版实机画面在 `output/character-modules/native-integrated/`。

这些检查覆盖外貌与渲染接入，不作为全部实战动作自然程度或所有极限姿势的验收。正式战斗继续采用原有物理规则。

## 使用和重建

清晰度修复后的发布构建位于 `unity/Build/CharactersSharp/江湖未央.exe`，默认启动器通过 `unity/Build/active-build.txt` 选择此版本。`查看重构人物.bat` 使用独立展示存档打开正式客户端的“皮影衣橱”，可切换年龄、性别、职业、朝向与行走；默认“千人千面”入口显示重构后的人物。

先运行 `python unity/bake_character_modules.py`，再使用 Unity 批处理方法 `Jianghu.Editor.CharacterModulesBuild.Build` 构建。`-characterModulesSmoke <输出目录> -saveDir <独立存档目录>` 运行正式人物验收；旧 `-appearanceSmoke` 参数转入同一套验收。

新头型由内置 image_gen 工具生成，原件在 `art/character-modular/heads-life-stages.png`、`heads-roles.png`，完整提示词在同目录 `generation-prompts.txt`。衣服与五官沿用本项目皮影样例；没有使用 Bloody Bastards 的美术贴图。10 个头型的锚点、五官区域和 UV 校准写在 `unity/bake_character_modules.py` 与 `Assets/Resources/Characters/index.json`。
