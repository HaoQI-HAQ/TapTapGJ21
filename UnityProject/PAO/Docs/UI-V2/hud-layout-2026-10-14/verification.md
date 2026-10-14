# 按截图调整游戏 HUD

此目录记录 `820eebd` 的验证快照。右下信息卡片后续已移除，底部改为平时隐藏、cs 常驻示例的旁白弹窗；最新实现与验证见 [旁白 HUD](../narration-hud-2026-10-14/verification.md)。

泡泡值改为左上角大数值、泡泡图标和加粗进度条；当前类型、消耗、地形填充与蓄力放到右下角三个能力按钮之间；字幕和通知统一排在底部。默认关闭重复的蓄力、已用值、松开按键和回收文字，低资源使用警示色，回收短暂提亮量条，耗尽时填充完全隐藏。详细操作反馈仍可主动开启。消息区为右下能力区留出空间，小窗口时略向左移；长文本自动增加高度，顶部能力按钮也随中间卡片高度避让。

沿用 clone 已恢复的纸感配色、圆润控件和水母插画。此前 lowpoly 改版记录保留为历史版本，本次不重新启用其绘制组件。cs 与 Map0–Map5 的共享泡泡配置及场景修复保留，Map5 的三个废弃泡泡模板及旧参数覆盖已清理。

修复了 HUD 创建前已生成的通知没有进入底部消息区、文本被裁切的问题。滞留泡泡继续显示原类型，发射不重复扣值；蓄力由选中能力环显示，没有资源组件的预览场景保留备用标识。暂停隐藏整组 HUD，模态页面反馈使用页面自己的通知区。

## 验证

Unity 2022.3.34f1，独立工程 `D:/TapTap21/PAO-HudLayoutValidation-20261014`。验证入口仅允许带 `PAO_UI_VERIFICATION_PROJECT` 标记的测试副本，正式工程没有诊断渲染辅助脚本。

| 检查 | 通过 | 错误 | 报告 |
| --- | ---: | ---: | --- |
| cs、Map0–Map5 共享配置、真实扣值、量条和布局 | 179 | 0 | [bubbles-report.txt](bubbles-report.txt) |
| cs 机制、测试工具、字幕与通知位置 | 78 | 0 | [mechanics-report.txt](mechanics-report.txt) |
| 完整 UI 回归 | 204 | 0 | [ui-report.txt](ui-report.txt) |
| Map0–Map5 加载、保存、恢复和结算 | 96 | 0 | [formal-report.txt](formal-report.txt) |

共 557 项检查通过，0 错误。报告均来自最终源码和恢复后的原 UI 样式。

脚本编译通过，仅有原有 `LevelPathPanel.m_DragStartedByTouch` 未使用字段警告。验证包含 1600×900、960×540、中英文、130% 字号、字幕与通知并存、耗尽与恢复、蓄力与滞留、暂停、实际关卡加载和保存。

UI 截图用于确认布局、文本和控件样式；世界截图使用诊断材质确认位置，不用于评估正式 URP 材质与光照。

[剩余量与能力布局](hud.png) · [满值](hud-full.png) · [底部消息及大字号](hud-bottom-messages.png) · [蓄力](hud-charging.png) · [英文大字号](hud-english-large.png) · [Map5](Map5-hud.png) · [诊断场景](world-hud.png)

[主菜单](menu.png) · [小窗口菜单](menu-small.png) · [设置](settings.png) · [章节](chapters.png) · [对话](dialogue.png) · [结算](completion.png) · [测试工具](test-tools.png)
