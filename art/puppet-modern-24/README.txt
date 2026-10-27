二十四人物：拆分部件骨骼绑定

游戏人物纹理只来自原始设计板右侧的独立拆分格。左侧整身最终效果只用于比例、姿态和视觉对照，不进入绑定部件图集。
split-calibration-01-08.json、09-16.json、17-24.json：拆分PNG自身的肩肘腕髋膝踝接点，以及比例、镜像、遮挡和重复衣袖裁剪参数。
assembly-calibration.json：最终效果中的目标关节位置。此文件只提供几何位置，不提供角色纹理。
每个角色根目录的 head-neck.png、torso.png 等：从右侧拆分格提取的原始透明部件。
split-bound-parts/：按校准做镜像、透明遮罩后的独立部件，不增加或重画像素。
split-binding.json：每件部件的原板来源区域、哈希、源锚点、目标骨段或挂点、比例和图层。
split-rest-composite.png：按真实骨架仿射装配的静止预览，供与 assembly-reference.png 对照。

执行 python unity/bake_paper_cast.py 生成 PaperCast 图集和绑定清单；PaperBust 是同一批拆分件的半身展示底图，PaperReference 仅供衣橱对照。
运行时每个上臂、前臂、大腿、小腿各自围绕实际端口旋转，头、腰胯、手、脚和配件挂到对应骨点。
通过 source_rect、source_extraction_mirror、source_component_sha256 和 source_mask 可反查每一个可见像素来自右侧哪一格。
旧的整身图切割蒙皮实现已退出游戏资源。

所有皮肤共用 unity/paper-rig.json 的骨架层级与动作。参考静姿保留各自绑定偏移，动作中的手臂使用公共骨架角度，并在切换时平滑重定向。手持物品全部卸下；握物手图改用同一角色的裸手拆分格。腰佩与背负装备保留。
