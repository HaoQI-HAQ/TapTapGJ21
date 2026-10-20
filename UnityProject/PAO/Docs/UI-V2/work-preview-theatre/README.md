# 作品预览 · Creation Theatre

工程：`D:\TapTap21\TapTapGJ21-clone\UnityProject\PAO`。

入口：进入「泡泡造物场」后按 **F6**，或点击「作品预览」。

预览采用深色暖金舞台、宋体大标题、取景框与细线图标。造物数量和尺寸来自当前场景，模型保留原本颜色。保存作品、作品存档、返回建造、四个视角、地面开关与鼠标操作继续使用原有逻辑。窄窗口和放大字号下工具栏自动改为两行。

主要实现：

- `Assets/GameMain/Scripts/UI/V2/UIWorkPreviewTheatre.cs`：界面、矢量装饰、响应布局与控件状态。
- `Assets/GameMain/Scripts/UI/V2/UIRuntime.SandboxPreview.cs`：接入实际作品预览入口。
- `Assets/GameMain/Scripts/Gameplay/Sandbox/SandboxPreviewView.cs`、`Assets/GameMain/Resources/SandboxPreviewSurface.shader`：透明预览背景、暖金地面及边缘渐隐。
- `Assets/GameMain/Scripts/UI/V2/UICreatStyle.cs`：补齐存档页边框等绘制组件的 `CanvasRenderer` 依赖。

截图使用程序生成的 1490 个真实 `SandboxPiece` 泡泡造物示例，经过实际 `ShowSandboxPreview` 页面与 URP 渲染；未读取或覆盖玩家存档。

| 检查场景 | 截图 |
| --- | --- |
| 常规窗口 | [1600×900](preview-1600x900.png) |
| 小窗口 | [934×534](preview-934x534.png) |
| 竖向窗口 | [800×900](preview-800x900.png) |
| 宽窗口 | [2100×900](preview-2100x900.png) |
| 字号 1.3、界面缩放 1.2 | [放大显示](preview-large-text.png) |
| 空场景 | [首次创作提示](preview-empty.png) |
| 命名保存 | [保存弹窗](preview-save-dialog.png) |
| 保存成功 | [提示消息](preview-saved-toast.png) |
| 作品存档 | [存档页面](preview-archives.png) |
| 保存后查看 | [新作品条目](preview-saved-archive.png) |

验证工具为 `Assets/GameMain/Scripts/Editor/SandboxPreviewTheatreReview.cs`，仅允许在带 `PAO_UI_VERIFICATION_PROJECT` 标记的隔离工程中执行 `PAO.EditorTools.SandboxPreviewTheatreReview.RunCLI`。检查涵盖四视角实际渲染变化、地面切换、旋转／平移／缩放／取景、保存和存档返回、世界 JSON 与修订号不变、暂停恢复、预览场景与渲染纹理释放。保存测试写入独立临时目录。

Unity 2022.3.34f1 隔离工程实际运行结果：**170 项通过，0 失败，0 错误**。最终主图、空状态、窄窗口和弹窗已人工验图；正确工程运行时代码编译通过。

完整结果见 [report.txt](report.txt)。
