# MCP Unity 网桥（DSH ↔ Unity 编辑器）

2026-10-18 安装。DSH 里的 `mcp__unity__*` 工具靠它工作；没有它，Unity 工具全部不可用（端口 8090 拒绝连接）。

## 装了什么

| 组件 | 位置 | 版本 |
|---|---|---|
| Unity 网桥包 | `Packages/com.gamelovers.mcp-unity/`（embedded package，1.6 MB） | 1.4.0 |
| Node 桥 | DSH 插件 `dsh-unity-mcp` 内置（`vendor/mcp-unity-server`） | 1.4.0 |
| 设置 | `ProjectSettings/McpUnitySettings.json`（端口 8090，AutoStartServer=true） | 自动生成 |

## 为什么用 embedded package 而不是 git URL

上游文档写的是在 `manifest.json` 里加 `https://github.com/CoderGamester/mcp-unity.git`。这里改成把 1.4.0 的源码直接放进 `Packages/`，原因有两个：

1. **版本必须锁死 1.4.0**。DSH 插件内置的 Node 桥是 1.4.0，而上游 1.5.0 会主动拒绝 1.4.x 的 Node 桥（见上游 README 的 “Version 1.5.0 intentionally rejects 1.4.x Node bridges”）。git URL 默认拉 `main`（1.5.0），会直接连不上。
2. **躲开 npm 阻塞**。`McpUnityServer.InstallServer()` 在每次域重载时检查 `Server~/node_modules` 和 `Server~/build`，缺了就在**主线程同步**跑 `npm install` / `npm run build`（`McpUtils.RunNpmCommand` 里是 `ReadToEnd()` + `WaitForExit()`，没有超时，还有 stdout/stderr 缓冲区死锁风险）。这里预建了两个空目录让它跳过，见下。

## 两个空目录（不要删）

```
Packages/com.gamelovers.mcp-unity/Server~/node_modules/   （空）
Packages/com.gamelovers.mcp-unity/Server~/build/          （空）
```

它们只是让 `InstallServer()` 认为 Node 侧已就绪。**DSH 用的是插件里自带的 Node 桥，根本不需要这两个目录里的东西**；删掉它们，下次打开工程编辑器会卡在 npm 上。

注意：git 不跟踪空目录，且根 `.gitignore` 的 `build/` 规则让这两个目录无法随仓库分发。所以**在别的机器 / 新克隆上首次打开时**，如果 Unity 卡住或报 npm 错误，手动建这两个空目录再重启编辑器即可。

## 用法

1. 打开 Unity 编辑器并加载工程（网桥跑在编辑器进程内，编辑器不开就不可达）。
2. 确认 `unity_status` 返回“可达”，`mcp__unity__get_scenes_hierarchy` 能返回真实层级。
3. 端口在 `ProjectSettings/McpUnitySettings.json` 的 `Port` 字段；改了要同步 DSH 插件 `env.UNITY_PORT`。

已知限制：
- 进入 Play Mode 时域重载会断开 WebSocket（上游 FAQ 建议关掉 *Enter Play Mode Settings → Reload Domain*）。`run_tests` 同理。
- 网桥只监听 `[::1]:8090`（IPv6 loopback），别指望局域网访问（`AllowRemoteConnections=false`）。
- 网桥是“实时改场景”的通道，风险高于 batchmode：改完记得让 `save_scene` 落盘并在 git 里看 diff。

## 升级

1. 把新版包解压覆盖到 `Packages/com.gamelovers.mcp-unity/`。
2. **同时**升级 DSH 插件里的 Node 桥（`dsh-unity-mcp`），两边大版本必须一致。
3. 重开编辑器，跑 `unity_status` + `get_scenes_hierarchy` 复验。

## 回滚

删掉 `Packages/com.gamelovers.mcp-unity/` 与 `ProjectSettings/McpUnitySettings.json`，还原 `Packages/packages-lock.json`（安装前的备份在 `%TEMP%\pao-packages-backup-*`），重开编辑器。`manifest.json` 未改动（embedded package 不需要写进 manifest）。
