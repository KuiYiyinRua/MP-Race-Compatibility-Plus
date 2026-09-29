# MP-Race-Compatibility-Plus 封面

## 成品
- ../../About/Preview.png：640×360，RGB PNG，16:9，游戏与创意工坊预览。
- 根目录 Preview.png / Preview.jpg：同尺寸上传副本。
- cover-1280x720.png：高清版本。
- cover.svg / cover.html：完整可编辑矢量源稿，无嵌入位图。
- build_cover.py：生成 SVG 和内嵌 SVG 的 HTML。
- export_cover.py：将 render-v2.png 导出为各尺寸并检查文件。
- validation.json：最终尺寸、格式与文件大小检查结果。

重建流程：运行 build_cover.py，以 Chrome headless、1280×720 窗口、device scale factor 1、隐藏滚动条渲染 cover.html 到 render-v2.png，然后运行 export_cover.py。

## 角色依据
原封面副本和本机模组路径仅保存在本地验证目录，不随公共源码发布。最终图中的人物均为 SVG 重绘，没有直接拼贴原封面。
- Milira / Ancot.MiliraRace：浅金双马尾、尖耳、光环、白翼、浅灰披风与浅蓝内装。
- Wolfein / MelonDove.WolfeinRace：银白长发、狼耳、琥珀眼、黑红制服、领带和腰扣。
- Raven / ZuoYao.RavenRace：黑色侧分遮眼发型、金眼、头侧羽簇、金边黑翼、黑白服装与金饰。
- MoeLotl / HenTaiLoliTeam.Axolotl：白色双马尾、粉色外鳃、深色角、花饰珠链、深梅色中式服装。
- Kiiro / Ancot.KiiroRace：沙色遮眼发型、绿眼、猫耳和棕色兜帽。
- Ratkin / Solaris.RatkinRaceMod：圆形粉色鼠耳、深灰辫子、酒红披肩和灰紫衣服。

reference-v2.png 是根据原模组封面生成的新构图参考。v1-square 保留首版方图及源稿；backup 为任务开始前的旧封面。

## 尺寸依据
RimWorld Wiki 模组结构指南建议使用 640×360，或高清 1280×720，并保持小于 1MB：
https://rimworldwiki.com/wiki/Modding_Tutorials/Mod_Folder_Structure

已核查本地 RimWorld 1.6 的 RimWorld.Page_ModsConfig 显示代码：按原图比例缩放，将显示高度限制为信息区高度的 35%，最后以 ScaleToFit 绘制。因此 16:9 是推荐比例，其他比例并非完全无法加载。本次采用标准横版 640×360。
