# UI V2 验证记录

验证日期：2026-10-13。Unity 2022.3.34f1。

主工程脚本已使用项目现有程序集引用编译通过。Unity 隔离工程的运行验证记录为 **50 项通过、0 条运行错误**，其中包括 15 张页面截图。完整记录见 [report.txt](report.txt)。

验证覆盖版本化存档与损坏恢复、旧版记录、场景和位置恢复、章节解锁、暂停及嵌套设置撤销、真实画质恢复、焦点返回、快速切页、叙事归档、建构预览及资源事务、结局确认和持久化。检查了 1600×900、1280×800、1920×820、960×540，以及 130% 字号和伪本地化。

隔离工程使用预览玩法场景和明确标注的示例内容来验证 UI 行为，未据此宣称全部正式 Map 已完成内容接入或实机验收。测试存档与测试设置使用独立目录和产品名称。

## 查看结果

- [主菜单](menu-1600x900.png)
- [有记录时的宽屏菜单](menu-continue-1920x820.png)
- [玩法 HUD](gameplay-hud.png)
- [设置](settings.png)
- [大字号与伪本地化](settings-pseudo-130percent.png)
- [对话](dialogue.png)
- [日志](journal.png)
- [建构预览界面](construction-preview.png)
- [章节完成](chapter-complete.png)

截图由专用相机仅渲染 UI。建构预览截图用于检查右侧面板布局；实际放置的线框需要在预览场景的玩法相机中查看。

## 重现

在一个复制或独立构建的测试工程中使用 Unity 的 `-batchmode -executeMethod PAO.EditorTools.UIV2Verification.RunAllCLI`。该方法创建示例测试场景并修改测试工程的构建列表及产品名，请勿在正式工程上直接执行。输出保存在测试工程根目录的 `VerificationArtifacts`。

原工程的接入与内容配置说明见 `Assets/GameMain/Scripts/UI/V2/README.md`。后续工作包括正式叙事、配方、结局与关卡出口配置，完整世界状态存档接口，以及实体手柄和目标硬件性能验收。
