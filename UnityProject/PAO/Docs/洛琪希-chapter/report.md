# 「洛琪希」关卡接入章节 UI

> 场景：`Assets/GameMain/Scenes/洛琪希.unity`
> 工具：`Assets/GameMain/Scripts/Editor/LuoqixiChapterSetup.cs`
> 菜单：`Tools/PAO/关卡白盒/洛琪希关卡 · 接入章节 UI`（幂等）
> 截图：`Docs/洛琪希-chapter/shots/`
> 时间：2026-10-19 10:48

---

## 一、做了什么

把「洛琪希」接成正式章节，复用现有 UI V2 的整套界面，不另做一套：

| 项 | 改动 |
|---|---|
| **章节数据** | `PAOUIConfig.chapters` 末尾新增 `洛琪希`：标题「水域 11 · 洛琪希」、描述、目标、出生/终点锚点 |
| **章节地图** | 菜单 →「章节地图」自动多出 Stage 11 的卡片（标题/描述/目标由配置提供，样式与其它章节完全一致） |
| **HUD** | 进入关卡后自动显示顶部章节目标（objective）、泡泡资源、三技能、暂停/设置/存档等，全部走现有 UIRuntime |
| **场景锚点** | 新增 `洛琪希_Systems/洛琪希_Spawn`（玩家出生位置）和 `洛琪希_Systems/洛琪希_Goal`（终点锚点） |
| **终点** | 运行时会在 `洛琪希_Goal` 自动生成 `ChapterGoalTrigger` + `UIChapterBeacon`（发光柱/光点），交互后走章节完成流程 |
| **Build Settings** | 场景已加入，Build Index 13 |
| **调试** | 同时加入 `additionalGameplayScenes`，直接打开洛琪希场景 Play 也能看到 HUD/暂停 UI（不用先从菜单进） |

## 二、章节文案（可改）

当前写入 `PAOUIConfig.asset` 的内容：

- 标题：`水域 11 · 洛琪希`
- 描述：`巨兽的骸骨沉在岩浆与深渊之间，最后一段水流从这里经过。`
- 目标：`穿过岩浆与深渊，抵达巨兽骸骨的出口。`

要改文字直接改 `PAOUIConfig` 对应的 chapter 字段（或改 `LuoqixiChapterSetup.cs` 顶部常量后重跑菜单）。

## 三、终点位置（占位）

场景里目前没有现成出口，`洛琪希_Goal` 先放在离出生点最近的「石碑/拉杆」广场：
`(-228.52, 22.78, -276.10)`（`TFD_Wall_Writing_Tablet_07A_LOD0` 上方）。

- 如果实际出口在别处：把 `洛琪希_Goal` 空物体拖到出口位置，重跑菜单即可（锚点已有则只更新位置）。
- 终点触发区是 3×3×3，带发光 beacon，和其它章节一致。

## 四、验证

| 项 | 结果 |
|---|---|
| 编译 | 0 error / 0 warning |
| 场景 | `Is Dirty=false`；Build Index 13 |
| 配置 | `chapters=11`、`IndexOf(洛琪希)=10`、`Supports(洛琪希)=True` |
| 运行 | 直接 Play 洛琪希场景：`PAO UI V2 / UI Canvas V2 / Gameplay HUD` 全部激活；0 Exception / 0 Error |
| 终点 | 运行时成功生成 `Chapter goal · 洛琪希_Goal`（ChapterGoalTrigger + UIChapterBeacon） |

截图：
- `Docs/洛琪希-chapter/shots/01_player.png`：出生视角
- `02_overview.png` / `03_overview_back.png`：巨兽骸骨全景
- `04_canyon_forward.png`：峡谷方向

## 五、注意

1. 章节目前排在 **章节列表最后（Stage 11）**。由于解锁规则是「完成上一章」，当前存档只完成到 Map1，所以菜单里洛琪希卡片会显示「未解锁」；直接打开场景 Play 不受影响。
   - 想让它排到前面 / 立刻可进入：调整 `PAOUIConfig.chapters` 里的顺序即可（或告诉我，我改）。
2. 它是最后一章，因此通关洛琪希后会走「结局/结算」流程；如果只想做番外、不通结局，需要把它挪到 wengjie 前面或改成 additionalGameplayScenes 专用调试关。
3. 关卡本身目前没有机关（拉杆/门只是模型，没挂 `LeverInteractable` / `MechanismDoor`），也没有现成出口，所以终点先用石碑广场占位。
4. 关卡灯光较暗，本轮只做 UI 接入，没有动场景环境光/雾。

## 六、重复执行

改完文字/终点位置后点菜单：

```
Tools/PAO/关卡白盒/洛琪希关卡 · 接入章节 UI
```

幂等：已有章节条目会更新，不会重复插入；Build Settings / additionalGameplayScenes 会自动去重。
