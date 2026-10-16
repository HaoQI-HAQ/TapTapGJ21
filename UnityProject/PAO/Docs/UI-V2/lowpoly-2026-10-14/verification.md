# Lowpoly UI 改版

此目录记录 `acd37c4` 的历史版本。clone 后续恢复了原 UI 样式，最新左上进度条、右下能力信息和底部消息布局见 [HUD 验证记录](../hud-layout-2026-10-14/verification.md)。本目录截图及报告不代表当前布局。

共用 V2 UI 采用切角面板、平面三角分面、几何图标、冷浅绿与深海蓝配色，并以暖橙突出主要操作。主菜单背景替换为程序绘制的 lowpoly 海底和水母。菜单、章节、记录、设置、图鉴、日志、对话、建造、暂停、结算和提示共用同一套组件，不再叠加纸纹、水彩或手绘贴图。选中轮廓、按下反馈、键盘与手柄导航保留。

泡泡值按用户选择保留在左下角。默认仅显示数值和能量条；蓄力、滞留、资源不足、回收、待引爆时显示短状态。消耗范围放在详细提示模式中。蓄力圈缩小并收进资源面板，地形填充提示移到上方且按角色屏幕边界避让。中英文、130% 字号、重映射、颜色辅助、高对比和减少动画均可继续使用。

cs 与 Map0–Map5 共用 CSBubbleLoadout，尺寸、消耗、浮力、发射和滞留参数保持 cs 基准。clone 原有玩家引用修复与 Map5 场景模板清理在合并中保留。

## 验证

Unity 2022.3.34f1，独立测试工程 `D:/TapTap21/PAO-CSBubbleValidation-20261014`。

| 检查 | 通过 | 错误 | 报告 |
| --- | ---: | ---: | --- |
| 真实主菜单、焦点、导航、存档与响应布局 | 337 | 0 | [menu-report.txt](menu-report.txt) |
| 完整 UI 回归 | 204 | 0 | [ui-report.txt](ui-report.txt) |
| cs 与 Map0–Map5 泡泡配置、扣值及布局 | 145 | 0 | [bubbles-report.txt](bubbles-report.txt) |
| cs 新机制、测试工具与角色避让 | 58 | 0 | [mechanics-report.txt](mechanics-report.txt) |
| Map0–Map5 正式关卡、保存、恢复与结算 | 96 | 0 | [formal-report.txt](formal-report.txt) |

共 840 项检查通过。脚本编译通过，唯一警告为原有 LevelPathPanel 未使用字段。视觉复查包含 1600×900、1280×800、1920×820、960×540、中英文及大字号。

[UI 总览](overview.png) · [主菜单](menu.png) · [设置](settings.png) · [章节](chapters.png) · [对话](dialogue.png) · [结算](completion.png) · [精简 HUD](hud-idle.png) · [蓄力 HUD](hud.png) · [暂停测试工具](pause.png)

UI 截图使用独立相机验证布局；world-hud.png 使用诊断材质确认角色附近的提示位置，不用于评估正式 URP 场景材质与光照。测试副本使用已解析依赖 DLL、原 glTF Built-In 着色器和包脚本 GUID 映射，未修改正式项目的渲染管线。
