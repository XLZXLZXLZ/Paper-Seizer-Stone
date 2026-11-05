# 模型制作来源
- Brazier.blend：来自 E:/assest/火盆.blend。
- MenuPlate.blend：来自 E:/assest/盘子.blend。

原始制作文件保留在Assets外，避免Unity重复导入Blender工程及其中未使用的内容。游戏使用Assets/Game/art/model/Props/Brazier和Assets/Game/art/model/MainMenu/MenuPlate内的OBJ网格，贴图和Unity材质位于对应texture及Materials目录。

火盆保留原Brazier预制体功能节点；盘子为三个120度独立网格并绑定原菜单扇区。重新制作脚本、原预制体备份及导出记录在UserSettings/PropVisualReplacement，不参与游戏构建。
