# 地形植被空引用导致打包失败

检查日期：2026-10-21（Asia/Hong_Kong）。

## 原因

Unity 原生地形构建报错 `Tree prefab at index 4 is missing.`。
Map4 引用的 `Assets/New Terrain.asset` 有 6 个 TreePrototype，其中索引 4 的 prefab 为 null。
没有任何绘制的植被实例使用此槽位，但 Unity 构建仍会校验整个原型数组。
海草 `Nature/Soft Occlusion` 提示是另一组警告，并非此空引用错误。

## 修复

- 修改前将 TerrainData 源文件和 .meta 备份到 `backups/20261020_183746_537/`。
- 移除未使用的空槽位 4，将原索引 5 映射到 4。
- 保留全部 594 个植被实例，其中 566 个实例仅更新 prototypeIndex。
- 在内存中逐项比较完整 TreeInstance 结构，确认映射后的实例与预期完全相同。
- 仅保存目标 TerrainData，没有保存其他场景或删除资源文件。

## 验证

- 最终 Windows 64 位完整构建：Succeeded，0 errors，0 warnings，耗时约 299.48 秒。
- 生成程序：`UnityProject/PAO/output/terrain-build-validation-20261020_185307/PAO.exe`。
- 确认 `PAO_Data/Plugins/x86_64/lib_burst_generated.dll` 存在，Burst 编译仍启用。
- 确认构建结束后 TEMP / TMP 恢复原值，地形 .meta 与备份逐字节相同。
- 汇总核验：`final-verification.json`。
- Unity 编辑器实际读取的 TerrainData 检查：缺失原型从 1 降至 0。
- 项目内全部 8 个 TerrainData：无缺失原型，无越界植被实例索引。
- 修改前报告：`terrain-audit-before.json`。
- 修改后报告：`terrain-audit.json`。
- 修复记录：`terrain-repair.json`。
- 完整 Windows 64 位构建结果、错误列表和输出路径：`player-build.json`。

## 完整构建进一步发现的 Burst 临时目录问题

地形错误消除后，第一次完整验证在 Burst 1.8.24 的本机链接阶段失败：
`System.UnauthorizedAccessException`，调用位置为
`System.IO.Path.InternalGetTempFileName` → `CommandLineArguments.WriteToTempFile`。
该次结果保存在 `player-build-before-temp-fix.json`。

终端与 Unity 进程的临时文件探针均通过，表明需要区分外部编译工具的运行环境；
没有据此修改系统目录权限或安全设置。
新增 `WindowsBuildTempDirectory` 构建回调，让本项目 Windows 构建期间的
TEMP / TMP 使用 `Temp/WindowsBuildScratch`，并验证该目录可写。
成功构建后或失败退出后恢复原有进程环境变量，不修改系统/用户级环境变量。
Burst 编译保持启用。

临时目录探针记录：`temp-directory-audit.json`。

## 工具

编辑器菜单 `PAO/Validation/Audit Terrain Tree Prototypes` 可再次扫描并定位问题地形。
`PAO/Validation/Repair Unused Missing Terrain Tree Slots` 仅处理没有实例引用的空槽；
若空槽被任何植被实例引用，将拒绝修改，需先恢复原预制体。
`PAO/Validation/Validate Player Build` 将当前启用场景构建到新的 output 子目录，不启动游戏。

之前提出的网格资源删除清单仍在等待用户确认；本修复没有执行资源清理。
