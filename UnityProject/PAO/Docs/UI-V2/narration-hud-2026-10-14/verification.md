# 旁白弹窗与精简 HUD

右下角移除类型、消耗、地形填充信息卡片，只保留三种能力按钮和选中／蓄力圆环。左上角仍显示泡泡剩余量与进度条。资源不足、回收和蓄力不再生成底部常驻文字面板。

底部中央是独立的旁白弹窗，使用圆润浅色底板、旁白名称和正文，随文本高度及字号自适应。正式关卡没有内容时隐藏；cs 的 `MechanicsTestScene.NarrationPreview` 已开启，进入测试场景后常驻示例。暂停或关闭字幕时隐藏，恢复后常驻示例继续显示。

后续旁白系统可调用 `UIRuntime.Instance.ShowNarration(speaker, text, duration)`：默认时长根据文本长度计算，正数指定秒数，负数持续显示直至调用 `HideNarration()`。原 `ShowSubtitle` 入口继续使用这套弹窗。切换场景会清理内容，正式关卡不会继承测试旁白。

## 验证

Unity 2022.3.34f1，独立工程 `D:/TapTap21/PAO-NarrationHUDValidation-20261014`。检查 cs 常驻预览、触发显示、结束隐藏、主动关闭、暂停恢复、字幕设置、正式关卡默认隐藏，以及小窗口和大字号下的遮挡与文本裁切。

| 检查 | 通过 | 错误 | 报告 |
| --- | ---: | ---: | --- |
| cs 机制与旁白显示生命周期 | 78 | 0 | [mechanics-report.txt](mechanics-report.txt) |
| cs、Map0–Map5 泡泡与默认隐藏行为 | 170 | 0 | [bubbles-report.txt](bubbles-report.txt) |
| 完整 UI 回归 | 204 | 0 | [ui-report.txt](ui-report.txt) |

共 452 项检查通过，0 错误。源码编译通过，仅有原有 `LevelPathPanel.m_DragStartedByTouch` 未使用字段警告。测试覆盖 1600×900、960×540、英文和 130% 字号。

[cs 常驻旁白预览](cs-preview.png) · [小窗口](small-window.png) · [正式关卡默认隐藏](Map0-idle.png) · [诊断场景](world-preview.png)

UI 与诊断世界截图用于验证布局，不用于评估正式 URP 材质与光照。
