# 关卡可粘地形（StickySurface）批量设置

日期：2026-10-20

## 规则

除了**地面**和**泡泡墙**，其余能撞到的都挂 `PAO.StickySurface`（`Assets/GameMain/Scripts/Gameplay/StickySurface.cs`），
让浮粘泡泡能粘住。判定在 `Bubble.cs`：`GetComponentInParent<StickySurface>()`，
所以物体自己或**任意父级**上有这个组件就行 —— 美术模型用「套一层父物体」的办法一次覆盖整棵子树。

排除判据（材质 / 名字 / 组件）：

| 判据 | 含义 |
| --- | --- |
| `WB_Ground` 材质、名字含 `Pavement` / `Floor` / `Ground`、`Terrain` + `TerrainCollider` | 地面 |
| `WB_Destructible` 材质、`qian` 材质、`TerrainBubble` 组件 | 泡泡墙（粉色可破坏泡泡墙） |
| `WB_Void` 材质 | 远景虚空背板 |
| `M_kong_qi_qiang` 材质 | 空气墙（看不见的边界） |
| 只有 Trigger 碰撞体、已有 StickySurface（或父级已有） | 不用加 |

## 各关卡做了什么

| 场景 | 新增组件 | 套父物体的美术模型（每个 1 个组件） | 覆盖碰撞体 | 跳过 |
| --- | --- | --- | --- | --- |
| `Map1.unity` | 137 + 1 | `sm_l1` | ~90（模型自带）+137 | 地面名 21、空气墙 4、TerrainBubble 6 |
| `Map3.unity` | 286 + 1 | `sm_l3_ji_guan` | 346 + 286 | 地面名 1、空气墙 1、TerrainBubble 16 |
| `Map3_5.unity` | 1 + 1 | `sm_l3.5` | 70 + 40 | Terrain 1 |
| `Map4.unity` | 90 + 3 | `SM_L4_yi_ji_1glb`、`SM_L4_yi_ji_2glb`、`SM_L4_shan_shi` | ~3885 | 地面 8+1+2、泡泡墙 13+2 |
| `Map5.unity` | 38 + 2 | `SM_L5_mi_gong`、`sm_l5_shan_shi` | 1377+18 + 38 | 地面 13+1、泡泡墙 15+31、空气墙 1、虚空 1 |

Map5 之前已经有 557 个 StickySurface，其中 29 个挂在**地面 / 泡泡墙 / 虚空**上，
这次一并删掉了：`WB_Ground` 5 个、名字是地面 8 个、`WB_Destructible` 15 个、`WB_Void` 1 个。
现在 Map5 一共 568 个。

套父物体后的根节点名字分别是：
`Map1` → `StickyArt_sm_l1`；`Map3` → `StickyArt_sm_l3_ji_guan`；
`Map3_5` → `Map3_5_StickyArt_Sm_l3_5`；`Map4` → `Map4_StickyArt_Ruins_A / _Ruins_B / _Mountain_Rock`；
`Map5` → `StickyArt_SM_L5_mi_gong`、`StickyArt_sm_l5_shan_shi`。

## 为什么用「套一层父物体」

美术模型是 prefab 实例，它们的碰撞体挂在实例内部成千上万个对象上（Map4 有 3885 个）。
逐个挂要几千个组件；给实例根节点加一层带 `StickySurface` 的空父物体只要 1 个，靠父子继承覆盖整棵子树。
父物体是单位变换，美术位置不变；序列化格式照抄 Unity 自己「把 prefab 实例拖到空物体下」的写法
（`m_TransformParent` + 父物体 `m_Children` + `SceneRoots`），已在 Map4 上验证 Unity 能正常读回并存盘。

注意：**整棵子树**包括模型自己建模的地面部分。Map1/Map3 的地面主要靠名字/材质能区分，
但遗迹模型内部如果自带地板，也会一起变可粘 —— 需要的话可以单独排掉某个子物体。

## 校验结果

| 场景 | 文档数 | 重复 fileID | 悬空引用 | StickySurface |
| --- | --- | --- | --- | --- |
| Map1 | 1339 | 0 | 0 | 138 |
| Map3 | 2868 | 0 | 0 | 287 |
| Map3_5 | 854 | 0 | 0 | 2 |
| Map4 | 8434 | 0 | 0 | 93 |
| Map5 | 6628 | 0 | 0 | 568 |

## 在 Unity 里生效

磁盘文件改完后，Unity 里如果开着这个场景，必须**重新打开场景**（Project 里双击 / File → Open Scene），
弹「是否保存」选 **Don't Save**，否则旧场景会覆盖掉修改。

打开时 Console 会有若干条
`Script 'StickySurface' on GameObject ... has possibly missing Required Components!`，
因为 `StickySurface` 带 `[RequireComponent(typeof(Collider))]` 而父物体没碰撞体，
**不影响运行**（只是提示）。

## 回滚

`backup/` 下每个场景都有改动前的原件：

- `Map1.unity.before_sticky`
- `Map3.unity.before_sticky`
- `Map3_5.unity.before_sticky`
- `Map5.unity.before_sticky`
- `Map4_before_sticky_20261019.unity`、`Map4_before_artwrap_20261019_2240.unity`（在 `Docs/Map4-sticky/backup/`）

直接用备份覆盖回 `Assets/GameMain/Scenes/<场景>.unity` 即可。

## 工具

`Tools/SceneSticky/sticky_scene.ps1`（PowerShell，Unity 之外的批量改场景工具）：

```
$code = Get-Content -Raw Tools\SceneSticky\sticky_scene.ps1
& ([scriptblock]::Create($code)) -Scene 'Assets/GameMain/Scenes/MapX.unity'          # 只看报告
& ([scriptblock]::Create($code)) -Scene 'Assets/GameMain/Scenes/MapX.unity' -Apply   # 改文件
```

`verify_generic.ps1` 做结构校验（重复 id / 悬空引用 / 组件数），`inspect.ps1` 按名字查某个文档。

另外 Unity 里还有菜单 `Tools/PAO/关卡/可粘地形：…`（`Assets/GameMain/Scripts/Editor/Map4StickySurfaceTool.cs`），
对**当前打开场景**按材质规则逐个挂/移除 StickySurface，适合白盒几何和小改动。
