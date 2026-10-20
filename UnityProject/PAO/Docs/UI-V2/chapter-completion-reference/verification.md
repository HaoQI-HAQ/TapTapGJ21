验证日期：2026-10-20。章节完成页专项最终结果为 **286 项通过，0 项失败**，详见 `report.txt`。Unity Editor 实际编译及独立 C# 编译均通过；仅保留原有 `LevelPathPanel.m_DragStartedByTouch` 未使用字段警告，详见 `compile.log`。

源工程为 `D:/TapTap21/TapTapGJ21-clone/UnityProject/PAO`，运行于独立副本 `C:/Users/Huya/AppData/Local/Temp/PAO-ChapterCompletion-20261020`。未操作用户已打开的 Unity，也未修改源工程场景或构建场景列表。`source-hashes.json` 记录了 5 个相关脚本及 9 张 PNG：源工程与验证副本的 SHA256 全部一致。

实际 Unity uGUI 渲染截图已保存于本目录：

| 场景 | 截图 |
| --- | --- |
| 与参考图相同的 1672×941 | `reference-1672x941.png` |
| 1600×900 / 2560×1080 / 1024×768 / 800×900 | `reference-对应尺寸.png` |
| 手机 390×844 / 320×568 | `reference-phone-对应尺寸.png` |
| 英文、130% 文字，横屏 / 竖屏 | `reference-english-large-1600x900.png`、`reference-english-large-800x900.png` |
| 配置中的真实终章长标题、终章操作和保存失败反馈 | `actual-long-title-390x844.png` |
| Map3_5 真实完成流程、3 个碎片、20 分 38 秒 | `actual-map3_5-completed.png` |

验证覆盖四个可用按钮、默认焦点、方向导航、控件在视口内，以及手机四个按钮至少 48px 的点击高度。英文大字和真实长标题均经目检。预览入口不会写存档或标记章节完成。

实际调用 `CompleteChapter()` 验证了 Map3_5 仍为第六章，动态统计与实际保存结果一致；自动档保留完成章节、3 个碎片和玩家位置 `(4, 2, 7)`，重复调用不会重复结算。自动档证据为 `verification-save-slot3.json`。回看碎片和章节路径打开真实页面，关闭后恢复原按钮焦点；前往下一章和主菜单通过真实加载协程进入 Map4/Menu；终章主操作打开真实结局页。无玩家可保存时显示保存失败。

运行使用隔离存档目录，切场目标使用验证副本内的空场景；Map3_5 的真实配置、运行时逻辑、页面和存档系统参与验证，复杂地图几何与关卡通关操作未在本专项中重演。

复现脚本位于 `Assets/GameMain/Scripts/Editor/UIChapterCompletionVerification.cs`，本目录保存了同名副本。该入口要求验证副本根目录存在 `PAO_UI_VERIFICATION_PROJECT` 标记，防止直接在源工程运行。使用 Unity 2022.3.34f1 对副本执行：

```text
Unity.exe -batchmode -noUpm --burst-disable-compilation -projectPath <验证副本> -executeMethod PAO.EditorTools.UIChapterCompletionVerification.RunCLI -logFile <日志路径> -screen-width 1600 -screen-height 900
```

运行结果输出至副本的 `ChapterCompletionArtifacts`。`unity-verification.log` 是最终运行日志，已去除许可证会话和机器标识信息。
