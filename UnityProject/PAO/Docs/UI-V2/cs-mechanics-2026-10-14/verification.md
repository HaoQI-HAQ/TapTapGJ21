# cs 场景修复与机制 UI

目标工程：`D:/TapTap21/TapTapGJ21-clone/UnityProject/PAO`。Unity 2022.3.34f1。

## 使用

在 clone 中重新打开 `Assets/Scenes/cs.unity` 并进入 Play。按 Esc 打开暂停菜单，顶部的“机制测试”提供补满泡泡值、设置 10% 资源、回到出生点三个操作。回到出生点会结束乘坐或远程操控，并清除角色的下落和冲刺速度。

HUD 始终显示当前/最大泡泡值、所选类型的最小/最大消耗、蓄力已用值、类型锁定、资源不足、待发射和恢复反馈。瞄准 35 米内的泡泡地形会显示实际填充量；暂停时统一隐藏 HUD。操作提示跟随当前按键设置，支持中英文及 130% 字号。“操作与教学”补充泡泡值、回收、地形填充、重生与表情机制。

## 修改

- `cs` 的已删除 takoyaki 预制体引用改为当前预制体，同时映射根对象、Transform 和 BubbleLauncher 的 fileID，保留出生位置 `(0.55, 0.59, -3.37)` 及三种场景泡泡模板。
- 清理重复的 EventSystem，保留测试地形与环境。
- 当前 takoyaki 预制体加入 BubbleResourceManager 和 JellyFaceDirector。Map5 原有的这两个附加组件配置与预制体默认值相同，移除其重复附加记录，使场景继承单一组件；旧测试 IMGUI 显示在 V2 UI 中停用。
- `MechanicsTestScene` 仅标记测试场景，普通关卡不显示测试按钮。
- 增加独立工程的机制验证入口，并让准备脚本包含 cs 的资源依赖和新增脚本。

## 验证

109 个运行脚本、25 个编辑器脚本编译通过。唯一编译警告是原有的 `LevelPathPanel.m_DragStartedByTouch` 未使用。

机制验证检查场景无缺失脚本、单一角色与 EventSystem、三种模板和独立消耗参数、预算扣除、类型锁定、资源不足、滞留发射不重复扣值、地形填充与奖励、返还不重复、暂停与测试按钮、场景重载、Map5 组件及 UI 兼容。覆盖 1600×900、960×540、中英文和 130% 字号。

机制验证 61 项通过、0 错误；完整 UI 回归 204 项通过、0 错误。结果见同目录 `mechanics-report.txt` 和 `regression-report.txt`。PNG 为 UI 专用相机截图，用于检查文字及布局；不用于评估场景的光照、模型与材质。

独立验证工程：`D:/TapTap21/PAO-UI-MergeValidation-20261014`。使用编译后的依赖 DLL，映射包脚本 GUID；`-noUpm` 避开该副本的包管理器初始化问题。

复现准备：

```powershell
python Tools/UIV2/prepare_validation.py D:/TapTap21/PAO-UI-MergeValidation-20261014
```

Unity 命令参数：

```text
-batchmode -noUpm --burst-disable-compilation
-projectPath D:/TapTap21/PAO-UI-MergeValidation-20261014
-executeMethod PAO.EditorTools.UIV2Verification.RunMechanicsCLI
-logFile D:/TapTap21/PAO-UI-MergeValidation-20261014/mechanics.log
-screen-width 1600 -screen-height 900
```

完整 UI 回归入口为 `PAO.EditorTools.UIV2Verification.RunAllCLI`。验证入口仅允许带 `PAO_UI_VERIFICATION_PROJECT` 标记的独立副本运行，以免覆盖正式场景或测试设置。
