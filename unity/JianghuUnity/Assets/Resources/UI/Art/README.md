# 手绘美术覆盖目录

把手绘（或 GPT 生成后修整过的）贴图放在这里，文件名与 `Resources/UI/` 中的烘焙贴图相同，游戏会优先使用这里的版本。`unity/bake_ui.py` 不会写入这个目录，重新烘焙不会覆盖你的画。

可选图标（透明背景 PNG，正方形，256×256 以上）：

| 文件名 | 用途 |
| --- | --- |
| icon_person.png | 底栏「人物」 |
| icon_bag.png | 底栏「行囊」 |
| icon_martial.png | 底栏「功法」、顶栏「武学」 |
| icon_faction.png | 底栏「势力」 |
| icon_chronicle.png | 底栏「年谱」 |
| icon_puppet.png | 底栏「皮影」 |
| icon_advance.png | 底栏「推演」 |
| icon_silver.png | 顶栏「银两」 |
| icon_action.png | 顶栏「行动力」 |
| icon_fame.png | 顶栏「声望」 |
| icon_health.png | 顶栏「体力」 |
| icon_population.png | 顶栏「在世」 |
| icon_events.png | 顶栏「事件」 |
| icon_bonds.png | 顶栏「结义」 |

也可以整张替换 `bottombar.png`、`topbar.png`、`panel.png`、`plaque.png`、`moon.png` 等，尺寸和九宫格边距见 `unity/ART_PROMPTS.md`。
