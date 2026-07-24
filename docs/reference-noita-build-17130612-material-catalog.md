# Noita Build 17130612 材料与反应参考基线

本文件记录 Demo 完整材料生态接入所依赖的本机 Steam Noita 参考身份。正式构建只读取仓库内生成的 `demo/PixelEngine.Demo/content/noita-material-catalog.json`，不会访问 Noita 安装目录、进程或外部二进制。

| 字段 | 值 |
| --- | --- |
| Steam build | `17130612` |
| version hash | `9dbd52ced019a643169a2db02f46c77f8766c6e5` |
| source path | `data/materials.xml` |
| source SHA256 | `122df34514edaf312e1a15a619b3d6a44d49ce605c929d5950c9051a57429d04` |
| catalog output SHA256 | `c59b5e591fa7bd7d71dfb46c36dbb6b32ddcffb5a672f98b05f6282f125b86ce` |

## 清点

`tools/extract-noita-material-catalog.ps1` 结构化读取 XML DOM，并保留声明顺序、`CellData`/`CellDataChild` 继承关系、全部属性、嵌套节点 XML 和反应属性。当前来源清点为：

- 468 条材料声明；去重后 466 个稳定材料名。
- 325 条 `Reaction`；5 条 `ReqReaction`。
- 重复声明名为 `meat_pumpkin` 与 `rock_box2d`，两者的声明顺序保留，不能在转换时静默覆盖。

该目录是完整映射的输入，不是 Demo 运行时的简化材料表。后续运行时接入必须对每条声明给出 CellType、运动/温度/破坏/视觉语义映射；未映射字段必须在转换校验中报错，不能静默丢弃。
