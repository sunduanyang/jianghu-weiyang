# 美术资源与接入

这版借鉴太吾绘卷的纸本卷册、水墨山河、朱砂点印与克制设色。场景和人物均为新生成资源，没有提取或打包太吾绘卷游戏文件。界面使用系统自带楷体/宋体，无网络字体或 CDN。

- `river-valley.png`：Unity 使用的共用山水原画。画中聚落坐标只用于显示，不改动模拟地理。
- `portraits.png`：四列两行的小像图集，依次为成年男子、成年女子、老年男子、老年女子、男童、女童、书生、女医者。八类小像会复用，按性别、年龄和固定编号选取；不表示每个 NPC 都有独立面貌或对应职业。16 岁前使用童年小像，60 岁起使用老年小像；亡者小像去色。

图片使用内置 imagegen 生成。以下保留实际生成提示词，方便后续替换与追溯。Unity 资源准备程序从本目录复制图片到 `Assets/Resources/Art/`。

## 山水提示词

Use case: stylized-concept. Asset type: original environment illustration for the map panel of a Chinese wuxia social simulation game. Create an exquisite hand-painted Chinese shan shui ink and very restrained mineral pigment landscape on warm aged mulberry paper, 3:2 landscape composition. Bird's-eye oblique map view of a peaceful river valley: layered craggy mist mountains at upper left and top edge, a small traditional Chinese grey tile roof county town at center, a humble farming hamlet at lower left, a mountain temple at upper left hillside, a roadside inn at upper right, a tiny ferry dock by a winding pale jade river at lower right. Delicate dry brush rock textures, fine etched architectural linework, pale sage foliage and warm ochre washes, expressive but muted black ink, large mist and paper negative spaces between landmarks. Organic old-world Chinese game art with refined scroll painting composition. This is illustration only; the software overlays all location labels and interactive markers. No letters, text, numbers, logos, frames, UI, people portraits, grids, watermarks. Original scene.

## 小像图集提示词

Use case: stylized-concept. Asset type: original 4 by 2 portrait atlas for a Chinese wuxia game. One image divided into exactly 4 equal width columns and 2 equal height rows, no margins or gutters. Each of the eight cells contains one centered head-and-shoulders Chinese person painted in a consistent traditional Chinese ink outline and restrained muted watercolor style on a flat warm ivory paper background (#e8ddc2). All faces fully inside their cells, shoulders at bottom, generous space above hair, consistently sized human heads. Top row left to right: adult male itinerant swordsman with topknot in dusty sage robe; adult female traveler in muted terracotta robe with tied back hair; elderly man with grey beard and topknot in taupe robe; elderly woman with grey bun in slate sage robe. Bottom row left to right: young boy in ochre robe with tied hair; young girl in dusty rose robe with paired buns; adult male scholar in faded indigo with simple traditional cap; adult female healer in muted sage with pinned up hair. Grounded ordinary people, distinct gentle faces, elegant intricate line drawing, expressive ink eyebrows, hand painted brush texture, ancient Chinese clothes without modern or fantasy accessories. Consistent bust compositions, front or slight 3/4 view. No text, borders, logos, watermarks or UI. This is one uniform game sprite atlas of original fictional people.

## Unity 接入

在根目录执行 `python unity/prepare.py` 重建资源。通过 `启动Unity版.bat` 或 Unity 编辑器查看；项目不再提供浏览器游戏预览。
