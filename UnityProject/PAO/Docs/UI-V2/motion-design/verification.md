# 海洋珠光 UI 与 DOTween 动效验收

2026-10-13，Unity 2022.3.34f1。最新视觉参考优先于规划原来的暖白纸感配色。保留功能流程与之前去除的重复文字提示。

| 验证组 | 通过记录 | 运行错误 | 截图 |
| --- | ---: | ---: | ---: |
| menu | 186 | 0 | 13 |
| regression | 200 | 0 | 35 |

通过记录包含行为断言、布局检查和截图记录。运行脚本编译通过，仅保留原有 LevelPathPanel 未使用字段警告。

- [主菜单](menu/menu-new-1600x900.png)：深海蓝底、青蓝与淡紫渐变按钮、珠光膜与漂浮水母。角色图片从已有 takoyaki 玩家视觉网格烘焙，沿用游戏真实角色。
- [能力切换动态预览](ability-motion-preview.gif)：来自 Unity 实际运行的 60 帧，展示三能力切换、转动光弧、选中涟漪和跨节点移动的光珠。GIF 做了局部裁切与循环，未覆盖所有输入情境。
- [蓄力状态](regression/motion-charging.png)：选中泡泡外的蓄力弧跟随真实 BubbleLauncher 状态；详细提示模式下能力卡与蓄力条同步使用对应能力色。
- [减少动态](regression/motion-reduced.png)：关闭漂移、旋转、缩放和扩散，只保留短透明度及色彩反馈。
- [完整回归](regression/report.txt) 与 [真实菜单检查](menu/report.txt)。真实菜单继续游戏进入实际 Map0 并恢复记录；完整回归通过示例玩法场景覆盖设置、叙事、建构、结算和结局。

能力切换的 Sequence 在 260 ms 内完成。连续 60 次切换后只保留一个选中态和一个旋转循环；HUD 的定期刷新只更新状态，不重设动画中的缩放。暂停时按钮动画使用独立时间，隐藏 HUD 终止旋转，恢复后仅重新启动一次。实际蓄力状态同步、减少动态以及 12 次反复开关页面后的 Tween 数量检查均通过。

通用按钮使用青蓝到淡紫的泡膜色变、按压和涟漪。菜单导航焦点联动水母姿态与右侧说明卡；文字和导航布局保持稳定。页面内容与底部操作使用错开的淡入。所有自绘装饰关闭 raycastTarget，支持 uGUI 的遮罩与常规材质批处理。

动效参数统一在 UIConfig.motion / UIMotionProfile；配色在 PAOUIConfig。UIAbilityMotion 负责能力节点与能力卡联动，UIButtonFeedback 负责通用按钮，UIMenuResonance 负责角色与说明卡响应，UIOceanStyle 负责渐变与泡膜网格。组件隐藏、销毁或替换动画时 Kill 并清空引用，减少动态在运行时即时应用。

验证覆盖常规、超宽与窄窗口，以及字号、UI 缩放、伪本地化、高对比和减少动态组合。最终关卡 URP 光照、美术及实体手柄手感仍需在原工程中体验；这里的世界截图使用诊断材质。此目录保留被验证的独立工程源码快照与 SHA-256，不覆盖之前的视觉验收基线。

重现：先运行 Tools/UIV2/prepare_validation.py，使用工程外的路径。随后依次在独立工程执行 PAO.EditorTools.UIMenuVerification.RunCLI 和 PAO.EditorTools.UIV2Verification.RunAllCLI，分别等待 Unity 退出；确认两个报告 Errors: 0 后运行 Tools/UIV2/collect_motion_results.py 收集。测试使用隔离的存档目录。
