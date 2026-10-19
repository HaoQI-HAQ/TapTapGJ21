# Map4 可粘地形（StickySurface）批量设置

日期：2026-10-19

## 需求

Map4 这一关里，除了**地面**和**泡泡墙**，其余所有能撞到的物体都应该能被浮粘泡泡（浮粘泡泡 / Sticky 泡泡）粘住。
也就是给它们挂上 `PAO.StickySurface`（`Assets/GameMain/Scripts/Gameplay/StickySurface.cs`）。
判断逻辑在 `Bubble.cs`：`OnCollisionEnter` / 手持滞空时用 `GetComponentInParent<StickySurface>()` 查，所以
只要物体自身或它的父级上有这个组件即可。

## 实际做了什么

改了场景 `Assets/GameMain/Scenes/Map4.unity`。

### 一、美术模型（当前主要手段，3 个组件）

截图里的遗迹/山石是三个模型 prefab 实例，它们自带 **3885 个 MeshCollider**。Unity 不允许直接往
prefab 实例根节点加组件（那等于改预制体），第一次尝试用「往根节点写一个 added component」的
办法失败了：`m_AddedComponents` 的 `targetCorrespondingSourceObject` 里要写的是预制体里
**GameObject** 的 id，我当时填的是 Transform 的 id，Unity 直接报
`Prefab mismatch: ... data will be lost` 并把组件丢掉了（所以第一版墙上不粘）。

现在改成：给三个美术根节点各**套一层空物体**，组件挂在这层父物体上，靠
`GetComponentInParent<StickySurface>()` 让整棵子树都被继承：

| 父物体（新建） | 子物体（美术 prefab 实例） | 覆盖的碰撞体 |
| --- | --- | --- |
| `Map4_StickyArt_Ruins_A` | `SM_L4_yi_ji_1glb` | ~1988 |
| `Map4_StickyArt_Ruins_B` | `SM_L4_yi_ji_2glb` | ~1872 |
| `Map4_StickyArt_Mountain_Rock` | `SM_L4_shan_shi` | ~25 |

父物体是单位变换（position 0 / rotation 0 / scale 1），所以美术的世界位置没变。
副作用：`TFD_Pavement_*` 那 2 块地砖也在遗迹子树里，所以也会被继承成可粘。

### 二、白盒 / 碰撞代理（逐个挂，第一版）

第一版按白盒材质给 419 个「带非 Trigger 碰撞体」的物体逐个挂了 `StickySurface`。之后
`Map4_Whitebox` 下的白盒方块被手动删掉了（22:40 存盘），这些物体连同组件一起消失；
现在只剩美术碰撞代理（`TFD_*`）那 90 个组件还在。判定依据仍是白盒材质约定：

| 分类（按白盒材质 / 名字） | 数量 | 处理 |
| --- | --- | --- |
| `WB_Platform`（边缘、墙、平台） | 74 | 加 |
| `WB_Bounce`（弹力面） | 145 | 加 |
| `WB_Landmark`（地标、礁石、肋柱、天花板齿） | 89 | 加 |
| `WB_FloatSticky`（原本就标成可粘的浮泡） | 16 | 加 |
| `WB_Bomb`（炸弹材质方块） | 5 | 加 |
| 美术碰撞体代理（`TFD_Column/Gate/Wall/Beam/Railing…`，无材质） | 88 | 加 |
| `TFD_Pavement_*`（美术地砖铺装） | 2 | **跳过（算地面）** |
| `WB_Ground`（`Resonance_*`、`*_Cavern_Floor` 等） | 8 | **跳过（地面）** |
| `WB_Destructible`（粉色泡泡墙：`Atrium_Pink_Wall_*`、`*_Pink_Crust` 等） | 13 | **跳过（泡泡墙）** |
| `Terrain_1`（Terrain + TerrainCollider，地形地面） | 1 | **跳过（地面）** |
| `Sphere` / `Sphere (1)`（TerrainBubble，泡泡墙占位） | 2 | **跳过（泡泡墙）** |
| 纯 Trigger（`Narration_*` 叙述触发区） | 3 | 跳过（撞不到） |

判定用的就是关卡白盒脚本 `LevelWhiteboxSetup.cs` 的材质约定：
`WB_Ground` = 地面、`WB_Destructible` = 粉色泡泡墙（可破坏）、`WB_FloatSticky` = 可粘、
`WB_Platform` = 边缘/墙、`WB_Landmark` = 地标、`WB_Bounce` = 弹力、`WB_Bomb` = 炸弹。

## 怎么再调

菜单（Editor）：

- `Tools/PAO/关卡/可粘地形：给非地面物体加上 StickySurface`
- `Tools/PAO/关卡/可粘地形：移除场景里的 StickySurface`
- `Tools/PAO/关卡/可粘地形：查看场景统计`

实现见 `Assets/GameMain/Scripts/Editor/Map4StickySurfaceTool.cs`。
「哪些算地面 / 泡泡墙」在文件顶部的 `s_SkipMaterialNames`（材质名）和 `s_SkipNameKeywords`（名字关键字）里改，
改完重新跑一次菜单即可（对当前打开的场景生效，支持 Undo）。工具会跳过「父级已经挂了 StickySurface」的物体，
所以 `Map4_StickyArt_*` 覆盖到的美术子孙不会被重复挂。

**注意**：重新跑 `Tools/PAO/关卡白盒/搭建 Map2 - Map5` 会重建 `Map4_Whitebox` 根节点，
白盒上后加的那些组件会随之丢失（美术 `Map4_StickyArt_*` 那 3 个不受影响）；重建完再跑一次上面的菜单工具就行。

如果想让某几块美术也能粘/不能粘，最省事的办法是临时把那个 prefab 实例从 `Map4_StickyArt_*` 下面拖出来，
或者反过来单独给它套一层带 `StickySurface` 的空物体。

## 备份

- 第一版改动前的场景：[backup/Map4_before_sticky_20261019.unity](backup/Map4_before_sticky_20261019.unity)（2026-10-19 02:30 原始版本）
- 套父物体之前的场景：[backup/Map4_before_artwrap_20261019_2240.unity](backup/Map4_before_artwrap_20261019_2240.unity)（2026-10-19 22:40 你在 Unity 里存盘的版本）
- 批量修改用的脚本：[patch_sticky.js](patch_sticky.js)、[patch_art_roots.js](patch_art_roots.js)、[wrap_art.js](wrap_art.js)、[sticky_targets.json](sticky_targets.json)
- 回滚：把备份文件覆盖回 `Assets/GameMain/Scenes/Map4.unity`。
