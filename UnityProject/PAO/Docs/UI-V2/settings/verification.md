# 设置与设备绑定验收

日期：2026-10-13。实现工程：`D:/TapTapGJ21/TapTapGJ21/UnityProject/PAO`。

对应规划第 13、18、19 部分的设置、设备输入和辅助功能要求。主菜单与暂停共用六页设置，键鼠和手柄使用各自的绑定界面与独立配置。

## 已实现

- 键鼠 17 项玩法绑定；手柄 13 项按钮绑定，支持按钮、十字键、LT/RT 扳机，独立摇杆输入参数及左右摇杆交换。
- 捕获新输入、释放后开始监听、冲突确认交换、取消、12 秒超时、单项重置、本设备重置、全部默认、应用保存、返回撤销。必需操作不会因冲突交换失去绑定。
- 固定菜单确认/返回/暂停，捕获期间关闭菜单导航；绑定修改后恢复编辑控件焦点并滚动到可见位置。
- HUD 与情境提示读取实际映射；设备切换保留焦点，手柄断开时暂停并提供键鼠恢复路径。
- 独立鼠标/手柄灵敏度、视角反转、死区、扳机阈值、单击蓄力替代长按。
- 音量即时预览、音效试听、后台静音；显示模式、分辨率、画质、垂直同步和帧率限制。显示模式及分辨率必须确认，15 秒无输入自动还原。
- 文字/UI 缩放、高对比、色觉辅助、字幕背景透明度和预览、减少动态、教学开关/重置、语言预览。大字号与长文本按钮自动增高。

## 验证结果

Unity 2022.3.34f1，独立测试工程，使用当前源代码和正式地图资产副本。验证不会打开或修改原工程场景，也不会读写玩家存档。

| 验证 | 结果 | 报告 |
| --- | --- | --- |
| 全部运行源码编译 | 通过；仅现有 `LevelPathPanel.m_DragStartedByTouch` 未使用字段警告 | Roslyn 编译 |
| `UIV2Verification.RunAllCLI` | 158 项检查通过，0 条错误 | [settings-report.txt](settings-report.txt) |
| `UIV2Verification.RunFormalCLI` | Map0–Map5 共 96 项检查通过，0 条错误 | [formal-report.txt](formal-report.txt) |

检查包括扳机改键后旧按钮失效、冲突取消/确认、单项/设备重置、必需操作保护、保存/撤销、改键超时、设备切换焦点、断连暂停恢复，以及画面手动还原和真实等待 15 秒自动还原。实际关卡检查覆盖输入暂停/恢复、HUD、玩家组件、位置保存与读取、章节终点确认/取消。

渲染检查包含 1920×1080、960×540、文字 130%、UI 120% 与伪本地化。保留的截图：

- [键鼠绑定](settings-keyboard-bindings.png)
- [手柄页](settings-controller-1080p.png) / [手柄绑定](settings-controller-bindings.png)
- [手柄输入参数](settings-controller-options.png) / [手柄示意](settings-controller-layout.png)
- [冲突确认](settings-binding-conflict.png) / [画面确认](settings-display-confirmation.png)
- [大字号、UI 放大与伪本地化](settings-controller-large-pseudo.png)

## 尚需实机验收

手柄模拟输入验证了输入链路，实体手柄尚未测试。当前使用 Windows/XInput 轴与按钮布局；其他设备需要按驱动布局核对。音效字幕需为正式音源填写 `soundCaption`；章节、剧情与配方内容仍由原有内容资产配置。独立工程截图验证 UI 排版，不代表正式 URP 场景的最终光照。
