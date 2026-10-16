# 私有仓库 UI 合并验证

日期：2026-10-14（Asia/Shanghai）。Unity 2022.3.34f1。

目标：`D:/TapTap21/TapTapGJ21-clone` 的 `main`。
来源：`D:/TapTapGJ21/TapTapGJ21` 的 `main`，提交 `a56d94a`。
合并前目标：`78dd43d`；共同祖先：`50e85fc`。

合并了私有仓库的四次 UI 更新：`4d3dd67`、`e046253`、`28be85e`、`a56d94a`。

## 合并处理

- 保留 clone 的 Resources 文件夹 GUID。
- 保留 clone 字体缓存：它包含私有 UI 使用的全部字符，以及 clone 新增的一、二、三。
- BubbleTypeSwitcher 同时检查蓄力锁定和 V2 UI 输入屏蔽；保留 clone 的泡泡值扣除、回收和类型锁定逻辑。
- PAOUIConfig 的默认玩家改用 clone 当前的低面水母预制体：GUID `533472653752a5e42b3a814b5ef9cbac`、根对象 `4001224688086030769`。旧 UI 配置引用的 GUID 已在 clone 更换，直接合并会导致 Map0 无法生成玩家。
- clone 独有的 195 个文件改动均保留，包括玩法、表情、存读档和美术更新。

合并前的备份分支：`codex/backup-before-private-ui-20261014-78dd43d`。

## 验证

运行脚本 106 个与编辑器脚本 25 个均编译通过。运行编译只有原有 `LevelPathPanel.m_DragStartedByTouch` 的 CS0414 警告；编辑器编译无错误。

| 检查 | 通过记录 | 错误 | 结果 |
| --- | ---: | ---: | --- |
| 实际主菜单 | 337 | 0 | 通过；包括继续进入 Map0、恢复坐标与记忆 |
| 完整 UI 回归 | 204 | 0 | 通过；包括设置、绑定、暂停、叙事与建构 |
| Map0–Map5 行为检查 | 96 | 12 | 行为断言通过；场景资源错误使检查程序返回失败 |

报告为本次合并结果的实际运行记录，见同目录 `menu-report.txt`、`regression-report.txt`、`formal-report.txt`。

## 关卡检查限制

- Map1 在合并前的 `78dd43d` 就引用旧 takoyaki GUID `c8c72f6b23ce2334dae9db64d6e83b08`，clone 中已无该 GUID。本次两次加载共报告 4 条场景/预制体错误；Map1 场景内容保持 clone 原状。
- Map4 的三个 glTF 模型文件及 GUID 均存在于 clone。独立验证工程使用编译后的依赖 DLL，未配置 glTF 导入器，因此两次加载共报告 8 条导入相关错误。此结果不能用来判断正式工程的 glTF 渲染效果。

独立测试工程：`D:/TapTap21/PAO-UI-MergeValidation-20261014`。测试副本复用来源工程的已解析依赖缓存，并将包脚本 GUID 映射到 DLL；完整 UI 入口补充了与正式关卡入口相同的 TMP 脚本映射。正式仓库的测试脚本与包配置未因此改动。
