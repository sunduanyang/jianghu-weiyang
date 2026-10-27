皮影演武背景

当前版本（2026-10-26，按用户修正）：
墨绿色战斗 UI 与外层界面共用 paper、paperText、muted、hairline、jade 和 brass 配色。
按用户提供的参考图，战斗与皮影衣橱共用暖黄透光幕布及朴素细边框。
中央呈柔和灯光，边缘过渡为赭黄暗角，并带有细微旧布纹理。
无帷幔、雕花、山水或文字牌匾。

背景由 unity/JianghuUnity/Assets/Scripts/ShadowTheater.cs 调用现有 StagePanel 绘制。
共用贴图：Assets/Resources/UI/screen.png、frame_stage.png，由 unity/bake_ui.py 生成。
人物仍由透明部件与共用骨骼实时绘制。

初版花纹戏台留作历史参考，已移出 Unity Resources，不进入运行版本：
ornate-stage-v1.png：OpenAI 内置 imagegen 工具生成的旧背景。
prompt.txt：该旧背景的原始生成提示词。
