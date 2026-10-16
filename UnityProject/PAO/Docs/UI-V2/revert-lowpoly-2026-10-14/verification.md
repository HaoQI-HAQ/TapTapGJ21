# 撤回 lowpoly UI 风格（2026-10-14）

clone 恢复至 ab2bb2d 的视觉样式：纸感暖色、水母插画、圆润按钮、手绘图标及泡泡。移除 UILowPolyImage、UILowPolyRingImage 与 UILowPolyOcean 绘制实现，恢复共用 UI 组件，因此菜单、设置、暂停、章节及 HUD 一并恢复。

保留 acd37c4 中独立的功能优化：左下角精简资源提示、蓄力标识收进资源面板、地形提示按角色投影避让、详细提示显示消耗范围。cs 与 Map0–Map5 的泡泡配置、场景修复，以及既有未提交的 Map5 临时模板清理均保留。没有重置分支或修改提交历史。

## 验证

Unity 2022.3.34f1，独立工程 D:/TapTap21/PAO-CSBubbleValidation-20261014，入口 PAO.EditorTools.UIV2Verification.RunAllCLI。完整 UI 回归 204 项通过、0 错误。菜单截图人工确认恢复水母插画、原配色和圆润按钮；覆盖四种窗口尺寸与设置、导航、暂停、HUD。测试副本的旧 UIRuntime.HudLayout.cs 残留曾导致首次编译失败，清理该副本残留后重新运行通过。正式工程不存在该残留脚本。

git diff --check 通过；运行代码中无 UILowPoly 引用。此次未重跑正式场景渲染验证。

[主菜单](menu.png) · [小窗口菜单](menu-small.png) · [UI 回归报告](ui-report.txt)
