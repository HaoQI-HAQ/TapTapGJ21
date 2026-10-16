# 主菜单补充优化验收

2026-10-13，Unity 2022.3.34f1。运行脚本编译通过，仅有原有 `LevelPathPanel.m_DragStartedByTouch` 未使用警告。

| 验证组 | 通过记录 | 运行错误 | 截图 |
| --- | ---: | ---: | ---: |
| menu | 337 | 0 | 15 |
| regression | 204 | 0 | 36 |

通过记录包括行为断言、布局及焦点检查和截图记录。原始报告：[主菜单专项](menu/report.txt)、[完整 UI 回归](regression/report.txt)。专项从实际 Menu 场景启动，验证继续进入实际 Map0 并恢复位置与意识碎片；完整回归覆盖示例场景中的设置、叙事、建构、结算、玩法提示和 DOTween 交互。

## 补充优化

- 延续当前左侧菜单、右侧水母插画的构图。限制菜单列宽，统一标题、副标题、菜单边界；窄窗口改为居中列，英文和长标题按空间缩放并换行。
- 主入口使用更大的点击区域和字号，底部图鉴、制作人员与离开保持轻量外观，并按文字大小自动换行布局。
- 菜单图标使用 140 ms DOTween 反馈，在预留范围内移动并随焦点变色；减少动态立即复位，快速悬停替换旧 Tween，隐藏或销毁时终止。
- 复用共用滚动条，仅在内容超出视口时显示，不额外添加说明文字。滚动条不进入默认焦点与方向导航，修复危险确认可能先选中滚动条的问题。
- 保留精简的信息结构、有效记录继续入口、异常恢复、取消确认与返回焦点。高对比主按钮文字对比度至少 7:1。

专项还验证悬停图标不触碰文字、连续快速悬停回到正确终态、动态设置立即生效、标题完整可读与滚动条按内容显隐。原有主菜单布局验收保留在 `../menu-layout/`。

## 实际画面与无重叠检查

覆盖 1600×900、1280×800、1920×820、960×540、用户截图的 934×534，以及 800×900 窄窗口。130% 字号、120% UI 缩放、伪本地化、高对比和减少动态组合均检查文字可读性与聚焦可见性。

每张主菜单截图及逐项聚焦后，使用控件实际四角计算可见矩形，先裁切滚动区域，再检查标题、副标题、所有入口、页面提示和帮助入口之间是否交叠，避免把已裁切的滚动内容算作遮挡。

- [用户窗口大小：934×534](menu/menu-returning-934x534.png)
- [首次进入](menu/menu-new-1600x900.png)
- [超宽窗口](menu/menu-returning-1920x820.png)
- [窄窗口](menu/menu-returning-800x900.png)
- [大字号与伪本地化](menu/menu-accessible-pseudo.png)
- [高对比模式](menu/menu-high-contrast-934x534.png)

截图来自隔离 Unity 工程的实际 uGUI 渲染。代码和配置快照见 [verified-source.zip](verified-source.zip)，校验值见 [verified-source-sha256.txt](verified-source-sha256.txt)。报告对应该快照；不将其他会话后续改动自动视作已验收。导航测试使用 Unity 事件注入，未进行实体手柄测试。

## 重现

在工程外准备独立目录，依次运行两项 Unity 验证，等待每次进程退出且报告为 `Errors: 0` 后收集。验证使用独立存档目录。

```powershell
python Tools/UIV2/prepare_validation.py C:/PAOMenuLayoutValidation
& 'D:/Unity 2022 3 34f1/Unity 2022.3.34f1/Editor/Unity.exe' -batchmode -noUpm --burst-disable-compilation -projectPath C:/PAOMenuLayoutValidation -executeMethod PAO.EditorTools.UIMenuVerification.RunCLI -logFile C:/PAOMenuLayoutValidation/menu.log -screen-width 1600 -screen-height 900
# 等待上述 Unity 退出后再运行完整回归。
& 'D:/Unity 2022 3 34f1/Unity 2022.3.34f1/Editor/Unity.exe' -batchmode -noUpm --burst-disable-compilation -projectPath C:/PAOMenuLayoutValidation -executeMethod PAO.EditorTools.UIV2Verification.RunAllCLI -logFile C:/PAOMenuLayoutValidation/regression.log -screen-width 1600 -screen-height 900
# 等待第二次 Unity 退出后收集。
python Tools/UIV2/collect_menu_layout_results.py C:/PAOMenuLayoutValidation polish
```
