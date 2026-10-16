# UI V2 正式关卡接入验证

2026-10-13，Unity 2022.3.34f1。当前运行脚本使用主工程的现有程序集引用编译通过；仅有原有 `LevelPathPanel.m_DragStartedByTouch` 未使用警告。

| 验证组 | 通过记录 | 运行错误 | 截图 |
| --- | ---: | ---: | ---: |
| UI 回归 | 67 | 0 | 16 |
| 实际 Map0 至 Map5 | 96 | 0 | 7 |

通过记录包含行为检查和截图输出记录。完整原始报告分别为 [UI 回归](regression/report.txt) 和 [实际关卡](formal/report.txt)。

验收版本的源码快照见 [verified-source.zip](verified-source.zip)，文件校验见 [源码 SHA-256](verified-source-sha256.txt)。设置扩展任务“开发用户界面 (2)”随后在同一工程修改了键鼠、手柄和画面设置；本页结果对应快照中的版本，后续设置改动应使用该任务的新报告验收。

## 已验证行为

- 原有菜单、章节、存档、设置、叙事、建构、结算和结局流程继续通过。
- 六项旧键位迁移为十五项操作，保留自定义键；修复重复、非法键位，允许鼠标键，保留界面导航键。
- 通过设置页实际按钮验证冲突交换和取消撤销；重建后的按钮保持选中，并完整显示在滚动区域内。
- HUD、日志入口及教学显示当前键位；预览目录包含失败和结局入口，重复打开不会叠加页面。
- 读档旅程深拷贝，清理空条目、重复 ID、负成本和无效计时；已收集碎片避免重复收集，尚未完成选择的对话可以重开。
- 每个实际 Map 中只有一个玩家，包含移动、发射器、相机和 HUD；场景副本没有缺失脚本。
- 实际 Map 的暂停阻止组件输入和位置变化；关闭页面的当帧继续屏蔽玩法输入，随后恢复。
- 各 Map 的实际玩家位置可以保存并在重载后恢复；完成章节写入自动槽位。
- 五个新增终点验证交互确认、取消继续探索及确认完成；Map1 使用原有 `tiaozhuan` 进入结算。

## 截图

- [六关入口与 HUD 总览](maps-contact-sheet.png)
- [Map0 入口](formal/Map0-entry.png)
- [移动键冲突交换与焦点](regression/controls-all-actions.png)
- [正式终点结算](formal/formal-goal-complete.png)
- [大字号与伪本地化](regression/settings-pseudo-130percent.png)
- [建构界面](regression/construction-preview.png)

UI 回归覆盖 1600×900、1280×800、1920×820、960×540，以及 130% 字号和伪本地化。实际关卡截图显示原场景几何、玩家和 HUD；隔离工程使用诊断材质与内置管线，不能用这些截图判定正式 URP 材质、灯光和后处理效果。

实际终点验证将玩家移动到出口并触发回调，验证交互与持久化。它没有模拟从入口走完所有解谜路线，白盒路线可达性、镜头遮挡及出口位置仍需人工通关检查。正式叙事、建构配方和结局仍由内容资产提供；完整世界状态存档、实体手柄及硬件性能也尚未验收。

## 重现

在主工程目录运行 `Tools/UIV2/prepare_validation.py`，参数是工程外的独立测试路径。脚本复制当前源码、实际关卡依赖及已有运行时依赖程序集，不覆盖正式场景。测试目录不要放在 Unity 的 Temp 中，编辑器会清理该目录。

示例 PowerShell：

```powershell
python Tools/UIV2/prepare_validation.py C:/PAOUIValidation
& 'D:/Unity 2022 3 34f1/Unity 2022.3.34f1/Editor/Unity.exe' -batchmode -noUpm --burst-disable-compilation -projectPath C:/PAOUIValidation -executeMethod PAO.EditorTools.UIV2Verification.RunAllCLI -logFile C:/PAOUIValidation/regression.log -screen-width 1600 -screen-height 900
```

等待该 Unity 进程退出，并确认 `VerificationArtifacts/report.txt` 为 `Errors: 0`。将这组产物复制到测试目录的 `RegressionArtifacts`，然后用相同命令执行 `RunFormalCLI`。等待完成后运行 `python Tools/UIV2/collect_results.py C:/PAOUIValidation`，收集两组报告及报告引用的截图。

两个 Unity 验证入口都要求 `prepare_validation.py` 创建的 `PAO_UI_VERIFICATION_PROJECT` 标记；直接在正式工程执行会报错停止。测试存档使用独立目录，测试设置使用独立产品名称。
