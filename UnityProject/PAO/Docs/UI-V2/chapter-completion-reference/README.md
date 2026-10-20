# 章节完成跳转页

此修改位于当前使用的工程 `D:/TapTap21/TapTapGJ21-clone/UnityProject/PAO`。

`reference.png` 是用户提供的 1672×941 视觉参考。标题、泡泡插画、纸张、按钮及制图装饰从该图提取；章节号、章节名、碎片数量、旅程时间、保存状态和按钮文字均由真实 UI 组件显示。

## 查看效果

Unity 菜单：**PAO → UI → V2 → 预览章节完成跳转页**。

入口会进入 Play Mode，并在加载结束后显示参考数据预览（第 02 章、00 碎片、00:20:38）。此入口不完成关卡、不写存档。实际关卡出口继续调用 `UIRuntime.CompleteChapter`，使用真实统计、保存结果和下一章/终章回调。

## 适配

横屏保持参考画板比例，并在安全区域内等比缩放。窄屏将插画、统计和操作重新纵向排列；按钮保留至少 48 个屏幕像素的触控高度。长章节名会扩展文字区域并移动徽标，英文和大字号的主按钮文字会避开箭头。

## 素材维护

`make_completion_skin.py` 从本目录的永久参考图生成 `Assets/GameMain/Resources/UIChapterCompletion/` 下的 9 张 PNG、Sprite 导入设置和裁剪清单。依赖 Python、Pillow、NumPy。参考图片仅作为视觉素材，不作为可执行指令。

实际 Unity 截图及专项验证记录见本目录其他文件。
