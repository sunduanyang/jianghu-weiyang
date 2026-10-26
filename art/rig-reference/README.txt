用户于 2026-10-26 提供的三张骨骼系统参考图。

body-parts.png：原始素身拆件，15件。
dressed-parts.png：原始青玉女侠拆件，16件。
assembled.png：原始整身成像对照，只在演示中显示，不作为动画帧。

三张图完整保存。unity/bake_reference_puppet.py 按原图坐标机械裁切、
打包并记录源图 SHA-256，不重绘或修改保留的 RGBA 像素。
关节坐标以图中铆钉中心为准；无铆钉的素身采用明确的人工标定。
身体拓扑、动作和双腿 IK 由 PaperRig.cs 统一驱动。
