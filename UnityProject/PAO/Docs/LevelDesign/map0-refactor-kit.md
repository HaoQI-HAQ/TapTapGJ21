# Map0 第一章「醒来」重建 · 可复用美术 / Prefab 清单

> 调研：kit-scout（只读）。Unity 编辑器当时打开的是 Map0（dirty，未对其做任何改动）。
> 数据来源：工程文件 YAML（wengjie/Map0/Map4/Map5 场景、glb/fbx 二进制、材质/预制体/URP 设置）+ Unity MCP 只读查询。
> 目标：给「醒来浅滩 → 沉没回廊 → 涨潮竖井」三段施工提供可直接拖拽的资产路径、尺寸与避坑说明。
> 标记：★ = wengjie 质量标杆里已经在用（同源）。⚠ = 坑。TODO = 本轮未测完。

---

## 0. 先读这段（5 条结论）

1. **TFD 神庙模块没有独立 .prefab。** 全部是模型文件里的子网格/子节点；在 Unity 里从 `.glb/.fbx` 展开后把节点拖进场景即可（会变成普通 Mesh 对象，可再自行 Save as Prefab）。
2. **干净模块库 = `Assets/GameMain/Art/Preset/01.glb`**：211 个 TFD 模块（单图集材质 `TFD_Atlas_1A`），4m 网格体系，LOD0 only。这是本次重建最推荐的「积木箱」。
3. **wengjie / Map4 / Map5 实际用的是「二次导出的整段模型」**，不是 01.glb：
   - `Assets/GameMain/Art/Models/SM_L5_mi_gong.glb`（150 mesh：城墙/铺地/柱/梁）
   - `Assets/GameMain/Art/Models/SM_L4_yi_ji_1glb.glb`、`SM_L4_yi_ji_2glb.glb`（156 mesh：遗迹/门/布/植物）
   - `Assets/GameMain/Art/Models/SM_L4_shan_shi.glb`、`sm_l5_shan_shi.fbx`（悬崖/巨石/石板）
   - `Assets/GameMain/Art/Models/SM_L4_zhi_wu_1 1.fbx`（灌木/草皮）
   - `Assets/GameMain/Art/Models/sm_l1.glb`、`sm_l3.5.glb`（TFD 散件集）
   这些文件的节点名就是 `TFD_<资产>_LOD0_<序号>`，与 wengjie 对象名一一对应 → **直接复用它们的观感与摆法最稳**。
4. ⚠ **单位/缩放不统一**（详见 §1.3）：
   - 01/03/04/05/06.glb 的节点自带 scale **0.01**（cm→m）；单拖 Mesh 资产才需要手动 0.01。
   - `sm_l5_shan_shi.fbx` / `SM_L4_zhi_wu_1 1.fbx` 的岩石/植物在 wengjie 里是 **scale 1**。
   - `Preset4.fbx` 模块是「米」单位，套在 Preset4 根 1.6629 下 → 4m 模块实际 **6.65m**。
5. ⚠ **工程里没有「珊瑚」资产**。wengjie 里的 `T_src_West_Coral_Rib_Cavern_Coral_Colony_*` 是挂在内置 Cube/Sphere 上的白盒占位（材质 WB_Bounce）。珊瑚/海葵需要新做，或用海草/草皮凑。

---

## 1. 建筑模块（TFD_*）

### 1.1 资产位置总览

| 库 | 路径 | 内容 | 体积 | 建议 |
|---|---|---|---|---|
| 干净模块库 | `Assets/GameMain/Art/Preset/01.glb` | 211 个 TFD 模块，1 材质 `TFD_Atlas_1A`，LOD0 | 25.2 MB | ★主力积木箱 |
| 预设场景 | `Assets/GameMain/Art/Preset/02.glb` ~ `06.glb` | 2.2k/1.4k/1.8k/1.5k 节点，已摆好的整段（含 LOD0-3、蜡烛、布、植物、机关） | 17–39 MB | 取「多级 LOD 件」「蜡烛/布/网」用 |
| 大预设 FBX | `Preset01.fbx`(76 MB)、`Preset2.fbx`(8.3 MB)、`Preset4.fbx`(73.6 MB)、`all Assets.fbx`(18.3 MB) | 整包摆放的旧预设；Preset01 仍被 `shiliuye map.unity` 引用 | 大 | ⚠ 不建议整包复用 |
| 二次导出（wengjie 同源） | `Assets/GameMain/Art/Models/SM_L5_mi_gong.glb` 等（见 §0.3） | 已按关卡需要筛过的模块段 | 3–26 MB | ★首选「像 wengjie」 |
| 散件模型 | `Assets/GameMain/Art/Models/1111.glb`、`sm_l1.glb`、`sm_l3.5.glb` | TFD 模块散件（1111.glb 与 SM_L5_mi_gong.glb 内容基本相同） | 3–9 MB | 备选 |

取用方式（Unity 内）：
1. Project 窗口展开 `01.glb` → 找到 `TFD_<名称>_LOD0` 子节点 → 拖入场景（自带 0.01 缩放，尺寸即真实米级）。
2. 只拖 Mesh（`Scene.xxx`）时：⚠ 网格是 cm 原始单位，**Scale 手动设 0.01**。
3. 需要多级 LOD 时从 `02–06.glb` 取同名 `_LOD0..3` 节点组，自己挂 LODGroup。

### 1.2 核心模块族（尺寸为 01.glb 实测，单位 m；网格=4m 体系）

| 族 | 代表节点（01.glb 内） | 尺寸 W×H×D | 三角面 | 用途 |
|---|---|---|---|---|
| 墙 | `TFD_Wall_01A_LOD0` / `_02A` | 0.55×4.05×4.00 / 0.55×4.05×4.00 | 384 / 413 | 标准 4m 墙段 ★wengjie(Wall_01A) |
| 墙 | `TFD_Wall_03A_LOD0` | 0.60×4.05×3.00 | 522 | 3m 墙段 |
| 墙 | `TFD_Wall_04A_LOD0` | 0.58×4.00×4.00 | 489 | 主廊墙 ★wengjie(Wall_04A)+Map0 现成 4 块 |
| 墙 | `TFD_Wall_05A_LOD0` | 0.55×1.05×4.01 | 216 | 矮墙/残墙（浅滩） |
| 墙 | `TFD_Wall_06A_LOD0` | 0.55×8.05×8.01 | 1383 | 8m 大墙/竖井壁 |
| 墙 | `TFD_Wall_07A_LOD0` | 0.55×2.61×2.01 | 178 | 2.6m 半埋残墙 |
| 墙 | `TFD_Wall_08A_LOD0` | 0.56×4.05×16.00 | 1580 | 16m 长墙 |
| 墙 | `TFD_Wall_09A_LOD0` / `TFD_Wall_09A_Door` | 1.00×4.05×4.01 / 0.39×1.02×2.50 | 805 / 302 | 带门洞的墙 + 门板 |
| 墙 | `TFD_Wall_10A_LOD0` | 0.39×4.02×4.00 | 302 | 薄墙/隔断 |
| 墙面装饰 | `TFD_Wall_Decoration_01A_L_LOD0`（_R 反向） | 0.70×4.20×20.00 | 946 | 20m 长壁雕/墙裙 |
| 墙基座 | `TFD_Wall_Plinth_01A_LOD0` | 2.00×3.04×1.00 | 260 | 柱础/墙脚 |
| 墙碑文 | `TFD_Wall_Writing_Tablet_01A..07A_LOD0` | ~0.35×2.72×1.72 | 1450 | 教学提示/叙事碑 ★推荐做泡泡教学说明 |
| 天花板 | `TFD_Ceiling_01A_LOD0` / `_02A` | 2.00×0.10×2.00 | 205 / 193 | 2m 顶板 ★wengjie |
| 天花板 | `TFD_Ceiling_03A_LOD0` | 4.00×0.10×4.00 | 172 | 4m 顶板 |
| 天花井盖 | `TFD_Ceiling_Manhole_01A_LOD0` | 4.00×0.29×4.00 | 442 | 竖井顶光洞 |
| 柱 | `TFD_Column_01A_Module_01..28_LOD0` | 约 2.0×4.0×2.0（Module_13 实测） | 92–859 | 柱列 ★wengjie(Module_13)+Map0 现成 |
| 柱（粗） | `TFD_Column_02A_Module_01..09_LOD0` | ~3×3×2（族） | 438–1178 | 竖井大柱 |
| 柱藤 | `TFD_Column_Vine_01A/02A/03A/05A/06A/07A` | TODO | — | 水下爬藤（TMesh 名在 Scene.xxx，按节点取） |
| 地面 | `TFD_Pavement_01A_LOD0` | 4.00×0.18×4.00 | 987 | 标准地砖 ★wengjie |
| 地面 | `TFD_Pavement_06A_LOD0` / `_07A` / `_08A` | 4.00×0.17×4.00 / 4×0.16×4 / 4×0.10×4 | 957 / 251 / 46 | 破损/薄地砖，08A 最省 |
| 地面 | `TFD_Pavement_Blocks_02A_LOD0` | 2.27×0.16×1.48 | 110 | 不规则碎铺（浅滩） |
| 地面 | `TFD_Pavement_Manhole_01A_LOD0` | 4.00×0.30×4.00 | 728 | 地井/下水口 |
| 格栅 | `TFD_Floor_Metal_Grate_01A_LOD0` | 4.00×0.11×4.00 | 648 | 金属格栅平台 |
| 陷阱地面 | `TFD_Floor_Trap_01A_LOD0..3`、`_Spikes_01A` | 约 2×0.1×2 | 288–1215 | 机关教学（可选） |
| 楼梯 | `TFD_Stairs_01A_LOD0` | 1.07×2.00×1.00 | 781 | 单跑小梯 |
| 楼梯 | `TFD_Stairs_02A_LOD0..3` | 2.07×2.00×1.00 | 781 | 双跑梯（带 4 级 LOD） |
| 旋转梯 | `TFD_Circular_Stairs_01A_L/R_LOD0` | 4.05×4.17×4.00 | 2485 | ★竖井主结构 |
| 旋转梯 | `TFD_Circular_Stairs_02A_L/R_LOD0`（+ `_Underside`） | 3.06×3.14×2.00 | 414–1077 | 小旋梯/顶棚 |
| 门/拱 | `TFD_Gate_01A_Module_01..06_LOD0` | 约 2×4×8（M03/04 最大） | 426–1380 | 教学门/场景转换 ★wengjie(Art_Gate=M03/04) |
| 梁 | `TFD_Beam_10A_LOD0` | 12.00×2.59×2.00 | 2331 | 大厅横梁 |
| 梁 | `TFD_Beam_12A_LOD0` | 4.38×2.59×2.00 | 836 | 短梁 |
| 栏杆 | `TFD_Railing_01A_LOD0` | 0.50×4.00×1.20 | 747 | 4m 栏杆 |
| 栏杆 | `TFD_Railing_02A_LOD0` | 0.51×1.10×3.24 | 293 | 平台护栏 |
| 屋顶 | `TFD_Corbelled_Roof_01A/02A_LOD0` | 4.00×1.75×4.00 | 555 / 493 | 叠涩顶/竖井顶部 |
| 体块 | `TFD_Building_Block_01A..06A_LOD0` | 01A 0.01³? 03A/04A 4.00×4.00×4.00 | 246–404 | 远景建筑体块 |
| 坑 | `TFD_Pit_01A_Module_01/02` | TODO（节点族） | — | 竖井底/水池坑 |
| 管件 | `TFD_Manhole_Pipe_01A`、`TFD_Metal_Bar_01A/02A` | 2.20×0.15×0.17（Metal_Bar_01A） | 92 | 管道/铁栏 |
| 脚手架 | `TFD_Scaffolding_01A/03A/04A` | 0.36×5.72×5.78（01A） | 1315 | 残破木架 |
| 木柱 | `TFD_Wooden_Pillar_01A..04A/06A_LOD0` | 0.27×0.29×4.49（01A） | 113–304 | 木桩/栈桥 |

### 1.3 ⚠ 缩放与命名约定（务必按这个来）

| 来源 | 场景里常见 scale | 说明 |
|---|---|---|
| `01.glb` / `03.glb` 等 glTF | 节点自带 **0.01**；手动拖 Mesh 时用 **0.01** | 网格原始单位≈厘米；4m 网格 = 400 原始单位 |
| `SM_L5_mi_gong.glb` 的 TFD 墙/地/柱 | wengjie 中 **0.01** | 同上（glTF） |
| `sm_l5_shan_shi.fbx`、`SM_L4_zhi_wu_1 1.fbx` 的岩石/植物 | wengjie 中 **1** | FBX 导入已换算，源文件里是「米」 |
| `Preset4.fbx` 模块 | Map0 中 **0.973 / 1.763 / 6.345** 等，父级 1.6629 | FBX 已按米导入；1.6629 是美术手动放大的整包倍数 |
| `SM_L4_yi_ji_1glb.glb` 的雕花/门 | 0.01 | glTF |

命名：
- glb/fbx 节点名形如 `TFD_Wall_04A_LOD0`、`TFD_Beam_10A__116_`（双下划线+数字 = 同资产多实例后缀）。
- wengjie 场景对象名形如 `T_src_TFD_Wall_04A_LOD0_7`（`T_src_` = 人工摆放时改的名，不是 prefab 名；**不要按它去找 prefab**）。
- ⚠ **所有 glb 的 Mesh 资产名一律是 `Scene.xxx`**（FBX 则是 `Image_xxx` 之类导入名），在 MeshFilter/Project 的 mesh 列表里无法按名字辨认模块 → 选件一定按「场景节点名 / 层级」找，不要按 Mesh 名找。
- 从 glb 拖进场景的对象自带 ` (1)` 之类后缀，属正常。

### 1.4 叙事 / 祭坛 / 道具（尺寸实测）

| 节点 | 尺寸 W×H×D | 三角面 | 用途 |
|---|---|---|---|
| `TFD_Altar_01A_LOD0` | 1.64×3.20×1.00 | 3100 | 大祭坛（竖井终点） |
| `TFD_Altar_02A_LOD0` | 1.64×1.64×0.80 | 2028 | 小祭坛（醒来点）★推荐 |
| `TFD_Sundial_01A_LOD0` | 6.00×6.00×3.09 | 4038 | 日晷/终局装置（竖井） |
| `TFD_Sarcophagus_02A_LOD0` + `_Lid_02A` | 3.17×1.68×1.00 / 3.16×1.66×0.74 | 3308 / 1125 | 石棺（叙事道具） |
| `TFD_Fountain_01A_LOD0`（带 LOD0-3 + MeshCollider） | 1.06×0.95×2.71 | 1831 | 喷泉/泉眼 |
| `TFD_Console_04A..07A_LOD0` | ~0.60×0.60×2.31 | 216–314 | 操作台/碑座 |
| `TFD_Table_01A/B_LOD0` | 0.69×0.69×1.22 | 430 | 石桌/供台 |
| `TFD_Urn_01A..07A_LOD0` | 0.46–0.68 | 242–443 | 陶罐（可做可破坏） |
| `TFD_Urn_Broken_01A..16A` | 0.1–0.9 | 378–886 | 碎陶（浅滩散落） |
| `TFD_Rune_03A/07A/08A`、`_Sides_*` | 小面片 | 98–484 | 发光符文（教学点） |
| `TFD_Stone_01A..09A` | 0.1–1.0 | 34–84 | 地面碎石 |
| `TFD_Giant_Skeleton_Ribcage_01A_LOD0` | 36.40×26.45×33.59 | 5189 | ★竖井高潮巨骸骨 |
| `TFD_Giant_Skeleton_Skull_01A_LOD0` | 17.43×11.33×9.45 | 2318 | 同上 |
| `TFD_Giant_Skeleton_Jaw/Neck/Tail_01A` | 15.8–43 m | 1654–2906 | 同上（骨架可拆散摆） |

---

## 2. 地形 / 岩石 / 悬崖 / 珊瑚 / 植物 / 布料 / 蜡烛 / 装饰

### 2.1 岩石 / 悬崖 / 石板（⚠ 尺寸两极，摆放前用 Bounds 复核）

| 资产 | 尺寸 W×H×D | 三角面 | 来源 / 说明 |
|---|---|---|---|
| `TFD_Canyon_Cliff_01A_LOD0` | ~37×34×42 | 7196 | ★wengjie 同款（`sm_l5_shan_shi.fbx`，scale 1）→ 远景悬崖/竖井壁 |
| `TFD_Canyon_Cliff_02A/03A` | 9.6×25×14.8 / 23×20×32 | 1932 / 2184 | 中景崖壁 |
| `TFD_Canyon_Cliff_01B/02B/03B` | 同 A 系列 | 同上 | 朝向变体 |
| `TFD_Rock_Large_15A_LOD0` | ~15×17.5×18.9 | 3851 | ★wengjie 同款（scale 1）→ 大地标石 |
| `TFD_Rock_Large_01A..16A` | 0.5–3 m 级（个别偏大） | 778–3258 | 主岩石（浅滩/竖井壁面） |
| `TFD_Rock_Slab_01A/02A/03A` | ~37×33×10（01A，偏大） | 1184–2033 | 可做平台/悬挑，必要时缩到 0.2–0.5 |
| `TFD_Rock_Small_01A..11B` | 0.1–0.3 | 462–1382 | 地面碎石点缀 |
| `SM_L4_shan_shi.glb` 内节点 | 同 TFD 名 | — | wengjie 同源的“已经挑好的岩石组” |

### 2.2 植物 / 珊瑚 / 布料

| 资产 | 路径 | 说明 |
|---|---|---|
| Bush 灌木 | `Assets/GameMain/Art/Models/SM_L4_zhi_wu_1 1.fbx` → `TFD_Bush_01A_LOD0` / `TFD_Bush_02A` | ★wengjie 同款，材质 `L4植物/M_zhi_wu_1.mat`；场景里 scale 1 |
| 草皮 | 同上 → `TFD_Grass_Patch_01A/02A/03A_LOD0` | ★wengjie 同款；wengjie 用 scale 1.1–1.7 |
| 爬藤 | `01.glb`/06.glb → `TFD_Column_Vine_01A/02A`、`TFD_Wall_Vine_01A/02A`、`TFD_Railing_Vine_01A/02A`、`TFD_Gate_Vine_02A` | 节点在 06.glb/二次导出模型里；Leaf 材质来自 TFD 植被图集 |
| 棕榈 | `04/06.glb` → `TFD_Palm_Tree_01A/01B/01C/02A/02B/03A/03B/03C` | 可选，浅滩剪影 |
| 植物 | `06.glb` → `TFD_Plant_04A`(2.54×1.54×1.47)、`TFD_Plant_05A`(2.18×2.12×1.42) | 低矮丛生 |
| 海草（真 prefab） | `Assets/GameMain/Art/Materials/L4海草2/Kelp 1.prefab`、`Kelp 2.prefab`；`L4海草1/Deep Kelp 3.prefab`、`Deep Kelp 4.prefab` | ★Map4/Map5/wengjie 都在用；`Deep Kelp 4` 根 scale=4；摆动 shader `Distant Lands/Stylized Kelp` |
| 海草草地 | `L4草地/Green Sea Grass 1/2/3.prefab` | ★Map4/Map5 在用 |
| 布 / 幡 | `03.glb` → `TFD_Cloth_01B`(1.95×2.60)、`_02B`(1.88×1.87)、`_03B`(3.76×2.82)、`_05B`(3.52×3.60)、`_06B`(4.10×2.00)、`_08A/08B`(长幡 1.09×2.05×6.82) | Map0 目前用的就是 01B/02B/03B（scale 0.015–0.023，放大到 6–7m 铺满） |
| 绳 / 链 / 网 | `03.glb`/`02.glb` → `TFD_Chain_01A..04A`、`TFD_Rope_01A/02A/04A`、`TFD_Wall_Chain_01A..03A`、`TFD_Web_02A..05A` | Map0 已用 `TFD_Web_05A__8_`（scale 0.0196）；wengjie 无 |
| 蜡烛 / 火盆 | `03.glb` → `TFD_Braizer_01A`(0.91×0.91×2.44)、`TFD_Candle_01B..12B`(0.04–0.16)、`_Flame` 子节点、`TFD_Candles_Cluster_01A/02B/03B/04B`、`Candles_Corner_*`、`Candles_Edge_*`、`TFD_Wax_01B/02B/03A/03B` | 火盆是 wengjie 外最实用的光源道具；火焰是单独 mesh，可配自发光/粒子 |
| 珊瑚 | —— | ⚠ 工程内无珊瑚资产；wengjie 的 Coral_Colony 是白盒占位。需新做或用海草/植物替代（TODO） |

---

## 3. 水面 / 焦散 / 光柱 / 粒子 / 后处理 / 灯光

### 3.1 现成组合预制体（Thalassophobia，全部可用）

| 资源 | 路径 | 说明 |
|---|---|---|
| ★整套水下装置 | `Assets/ThirdParty/Thalassophobia/Prefabs/UnderwaterRig.prefab` | 子物体：`WaterFogVolume`(雾球) + `CausticsSpot`(朝下聚光灯，Intensity 1.5/Range 60/Spot 120，Cookie=`Caustics URP.asset`) + `SunshaftDirection`(光柱方向参考，默认关)。Map4/Map5 已在用 |
| 焦散 | `Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Caustics.prefab` | 方向光 + `Caustics.asset` CRT（旧版；wengjie 未用） |
| 雾体积 | `Contents/Prefabs/Fog.prefab` | 房间雾球（材质 `Water Fog Room.mat`） |
| 水面 | `Contents/Prefabs/Water Surface.prefab` | URP SimpleLit 水面（无折射；⚠ `Water Surface.mat` 的 `_WaterNormals1` 引用失效，原包即缺，无影响） |
| 光柱 | `Contents/Prefabs/Particles/Sunshafts.prefab`、`Sunshafts (Area).prefab` | ★wengjie 各用了 3 个；⚠ `Sunshafts.prefab` 材质槽为空，需手赋 `Contents/Materials/Sunshafts.mat`；Area 版已带材质 |
| 浮尘 | `Contents/Prefabs/Particles/Ambient Particles.prefab` | 水下浮尘 |
| 焦散材质 | `Contents/Materials/Caustic URP.mat` | shader `Stylized Caustics URP`，贴图 `foam-001.png`，参数 Scale 2 / Intensity 1.5 / Speed 0.1 |
| 房间雾材质 | `Contents/Materials/Water Fog Room.mat` | ⚠ README 写的 4/9/16/28m、Intensity 1.15 与资产实际不符：实际 `_ColorStart1..4 = 0.2/0.6/1.2/2.4`、`_FogIntensity=0.85`、`_FogDepthMultiplier=0.35`（房间尺度更浓版）。以资产为准 |

### 3.2 PAO 自己的水下「天」+ 全屏焦散（wengjie 的观感核心，强烈建议整套照搬）

| 资源 | 路径 | 关键参数（wengjie 实测） |
|---|---|---|
| 水面顶盖组件 | `Assets/GameMain/Scripts/Gameplay/Underwater/UnderwaterCeiling.cs` | 运行时生成“顶面+裙边”壳体，Mesh 不落盘；参数 `HalfSize=510`、`SkirtDepth=400` |
| 水面材质 | `Assets/GameMain/Art/Materials/M_UnderwaterCeiling.mat`（shader `PAO/UnderwaterCeiling`） | `_WaterY` 由物体 Y 决定；wengjie 物体位置 (0, 85.06, 100) |
| 水面 shader | `Assets/GameMain/Art/Shaders/UnderwaterCeiling.shader` | Opaque + 写深度，自带波纹/天窗/裙边渐变 |
| 全屏焦散 quad | wengjie 里 `PAO_CausticsScreen`（Main Camera 子物体，Quad，localPos (0,0,1.5)、scale (8,8,1)） | 材质 `Assets/GameMain/Art/Materials/M_PAOCausticsScreen.mat`（shader `PAO/CausticsScreen`），靠相机深度的世界坐标画 Voronoi 亮线；参数 `_WaterY=85.06 / _Intensity=0.28 / _Scale=0.55 / _FadeStart=130 / _FadeEnd=380` |
| 焦散 shader | `Assets/GameMain/Art/Shaders/PAOCausticsScreen.shader` | ZTest Always、加法混合，**不需要**改 URP Renderer Feature |

⚠ 注意：URP 的附加方向光 cookie 不渲染（项目里 Thalassophobia 的 cookie 焦散在 URP 下失效），所以 wengjie 改用上面的全屏焦散 quad；不要再走“方向光 cookie”方案。

### 3.3 后处理 / 灯光（wengjie 配方）

| 项 | 值 / 路径 |
|---|---|
| 全局 Volume | `Wengjie_PostFX`（Volume, IsGlobal=1），Profile = `Assets/ThirdParty/Thalassophobia/PostFX/Shallow Water Profile URP.asset` |
| Shallow 配方 | Bloom：intensity 1 / scatter 0.572；Vignette：intensity 0.22；ColorAdjustments：postExposure +0.15、contrast +8、saturation −12；Tonemapping：ACES（mode 2） |
| 深水替代 | `Assets/ThirdParty/Thalassophobia/PostFX/Deep Sea Profile URP.asset`（Bloom+Vignette+Tonemapping，无 ColorAdjustments） |
| 方向光 | 颜色 (1, 0.957, 0.839)、Intensity 1、Soft Shadows（wengjie 与 Map0 相同） |
| 雾（wengjie） | Exponential Squared，颜色 (0.22, 0.42, 0.52)，Density **0.0035**；Ambient Intensity 0.85 |
| 雾（Map4 / Map5） | color (0.435, 0.722, 0.887) / (0.382, 0.745, 1)，Density **0.02**（更白更浓，适合大白盒远景） |
| 其它粒子 prefab | `Assets/GameMain/Art/VFX/VFX_Dust_1.prefab`（Map5 用了 10 个）、`TFD_Portal_01A.prefab`（传送门，VFX 目录） |
| URP Renderer Features | `Assets/Settings/URP-HighFidelity-Renderer.asset`：SSAO + Decal + `BubbleSDFRenderFeature`（没有焦散 feature，别去找） |

### 3.4 Map0 现状（基线差距）

- 当前 Map0 只有：`Map0_Whitebox`（+ 2 个 `Blocker_LargeDestructibleBubble` + `Void_Backdrop_NoCollider`）、Main Camera、Directional Light、takoyaki、Preset4 实例、14 个 TFD 残件、6 个 03.glb 布/网散件。
- **没有**：雾（m_Fog: 0）、Volume/后处理、UnderwaterRig、Water_Ceiling、PAO_CausticsScreen、光柱、浮尘、海草。
- 结论：第一章重建时，水/光/雾/后处理这一套要按 §3.2–3.3 整体补上，否则达不到 wengjie 观感。

---

## 4. wengjie 实际用到的资产（按用途分组；供“像 wengjie 一样搭”）

> 解析 `wengjie.unity` YAML：290 GameObject / 234 MeshRenderer / 7 个 prefab 实例 / 1 个全局 Volume。
> 关键事实：**wengjie 的 TFD 美术只有 ~35 个对象**，质量感主要来自水/雾/材质/构图，不是堆资产。

### 4.1 结构白盒（160 个内置基础网格对象）

| 内容 | 数量 | 材质 |
|---|---|---|
| 拱/环/街/屋顶/平台等白盒几何（`A_Ring_*`、`B_Street_*`、`C_Stair_*`、`Deck_*`、`InnerWall_*`、`Frame`…） | 160 | ★`M_Wengjie_StoneBlue.mat` 59 个；`WB_Ground` 40；`WB_Platform` 43；`WB_Bounce` 9；`WB_Void` 2；`WB_Landmark` 1 |
| 珊瑚占位 | 2 | `WB_Bounce`（内置 Cube/Sphere，不是真珊瑚） |
| 机关 | 6 | `M_Mech_Plate` 6 / `M_Mech_Move` 4 |

`M_Wengjie_StoneBlue.mat` = glTF PBR shader（`glTF-pbrMetallicRoughness.shadergraph`），贴图直接引用 **`SM_L5_mi_gong.glb` 内部**的 baseColor/metallicRoughness/normal（guid `b44b8876…`）→ 想复刻蓝石观感，直接复用这个材质 + 该 glb 的贴图即可。

### 4.2 wengjie 的 TFD 模块（按源文件）

| 源文件 | 数量 | 对象 | 用途 |
|---|---|---|---|
| `Assets/GameMain/Art/Models/SM_L5_mi_gong.glb` | 12 | 6×`TFD_Pavement_01A`、5×`TFD_Wall_01A`、4×`TFD_Wall_04A`（对象名 `T_src_…`，scale 0.01） | 铺地 + 主墙 |
| `Assets/GameMain/Art/Models/SM_L4_yi_ji_1glb.glb` | 6 | `TFD_Gate_01A_Module_03/04`（另挂 `Art_Gate_0/1/2` 空物体） | 场景门/转折 |
| `Assets/GameMain/Art/Models/sm_l5_shan_shi.fbx` | 8 | 6×`TFD_Rock_Large_15A`、4×`TFD_Canyon_Cliff_01A`（scale 1） | 大地标岩石/崖壁，材质 `L5山石/New Material.mat` |
| `Assets/GameMain/Art/Models/SM_L4_zhi_wu_1 1.fbx` | 8 | 4×`TFD_Bush_01A`、4×`TFD_Grass_Patch_01A`（scale 1–1.7） | 植被，材质 `L4植物/M_zhi_wu_1.mat` |
| `Assets/GameMain/Art/Materials/L4海草1/Kelp.fbx` / `L4草地/Grass.fbx` | 32 + 1 | `Mood_Plant_0..25` | 海草点缀（Kelp 1 / Kelp 4 材质） |

### 4.3 wengjie 的水下/灯光/后处理

- `Water_Ceiling`：UnderwaterCeiling（HalfSize 510 / SkirtDepth 400，Y=85.06）+ `M_UnderwaterCeiling.mat`
- `PAO_CausticsScreen`：Main Camera 子物体 Quad + `M_PAOCausticsScreen.mat`
- `Wengjie_PostFX`：全局 Volume + Shallow Water Profile
- 方向光 1 盏（暖白、Intensity 1、Soft Shadows）；`Sunshafts` ×3 + `Sunshafts (Area)` ×3（Prefab 实例）
- 场景无 UnderwaterRig（雾靠场景 Fog + 水面顶盖），与 Map4/Map5 的 UnderwaterRig 方案不同 —— **Map0 建议两套都参考：Fog 全局 + Water_Ceiling/焦散屏是 wengjie 方案；UnderwaterRig 是 Map4/5 的近场雾球方案**

---

## 5. Map0 现有 Preset4 的来源与去留判断

### 5.1 来源（YAML 证据）

- Map0.unity 里那个对象名叫 **Preset4** 的实例，`m_SourcePrefab` guid = `0dad305bca6769346b77bbadd3eb0e01` → **`Assets/GameMain/Art/Preset/Preset4.fbx`**（73.6 MB，FBX 模型 prefab，不是 .prefab 资产）。
- Transform：位置 `(-51.45, 3.14, 1.44)`，旋转 0，**Scale 1.6629**（YAML L1820–1831；名字 override 在 L1888–1892），挂 `LODGroup`。
- 整个实例有 **373 条 prefab override**，其中约 350 条是把 `m_LODs.Array.data[*].renderers.Array.data[*].renderer` 置空（LOD 引用被手工打断），说明这个 preset 的 LOD 已处于“半残”状态。
- 实例下被实际保留/摆好的模块（普通 Mesh 对象，共 14 个，名称被改成 TFD_*）：
  - 4×`TFD_Wall_04A_LOD0`：localScale (0.973, 1.763, 6.345)，再乘 1.6629 → 世界约 **0.93 × 11.73 × 42.20 m**
  - 5×`TFD_Pavement_01A_LOD0`：localScale 1 → 世界 **6.65 × 0.31 × 6.65 m**
  - 2×`TFD_Ceiling_01A_LOD0_1`：世界 7.26×0.17×8.09 / 39.76×0.17×3.73
  - 2×`TFD_Column_01A_Module_13_LOD0`：世界 **3.33 × 6.65 × 3.33 m**
  - 另有 6 个 `TFD_Cloth/Web` 散件来自 **`03.glb`**（guid `2a2d15e7…`），scale 0.015–0.023，是普通对象（非 prefab），散布在场景里。
- Map0 其它内容：`Map0_Whitebox`（ProBuilder 白盒）+ `Blocker_LargeDestructibleBubble` ×2 + `Void_Backdrop_NoCollider`、Main Camera、Directional Light、takoyaki prefab 实例。

### 5.2 去留判断：**不建议保留整包 Preset4，重建时删掉这个实例**

理由：
1. 73.6 MB 的单一模型 + 一个 LODGroup，LOD renderer 已被 null 掉约 350 个 → 性能/可维护性都差。
2. Map0 实际只用到 4 种模块（Wall_04A / Pavement_01A / Ceiling_01A / Column_01A_Module_13），这 4 种在干净库里都有（尺寸也一致）；从 01.glb 或 `SM_L5_mi_gong.glb` 重新取 20–30 个，比继承 73 MB 预设干净。
3. 目前被摆好的 14 件缩放是“1.66 倍”的旧世界尺度（墙高 11.7 m、地砖 6.65 m），与新章节尺度未必匹配；重建直接以 4 m 网格重新排更可控。
4. `M_Bldg_A/B/C.mat`、`M_Wengjie_GroundCaustics.mat`（含 `T_Caustics_36.PNG`）当前**没有任何场景在用**，可作为备选材质，不必从 Preset4 继承。

如果要保留视觉：只把上面 4 种模块按新尺度重摆一遍，**删除 Preset4 实例本体**（不要 Save 成新 prefab）。

---

## 6. 推荐清单（三段共用；数量按“最值得做的 20–30 个”）

> 路径格式：`模型文件 → 节点名`；★ = wengjie 同款；【现成】= Map0 里已有可直接复用。
> 所有 TFD 件建议统一：4 m 网格、`01.glb` 取件（节点自带 0.01）。

### A. 醒来浅滩（0:00–2:30，明亮、低矮、可读）

| # | 资产 | 路径 | 用途 |
|---|---|---|---|
| 1 | 小祭坛 ★ | `Preset/01.glb → TFD_Altar_02A_LOD0`（1.64×1.64×0.8） | 出生点“醒来”地标 |
| 2 | 标准地砖 | `Preset/01.glb → TFD_Pavement_01A_LOD0`（4×4） | 浅滩主铺地 |
| 3 | 碎铺 | `Preset/01.glb → TFD_Pavement_Blocks_02A_LOD0`（2.27×1.48） | 不规则岸边 |
| 4 | 矮残墙 ★ | `Preset/01.glb → TFD_Wall_05A_LOD0`（4×1.05） | 半埋遗迹轮廓 |
| 5 | 碎石 | `Preset/01.glb → TFD_Rock_Small_01A/03A/06A/08A_LOD0`（0.1–0.3m） | 散落点缀 |
| 6 | 大地标石 ★ | `Models/sm_l5_shan_shi.fbx → TFD_Rock_Large_15A_LOD0`（scale 1） | 2–3 块做剪影 |
| 7 | 远景崖 ★ | `Models/sm_l5_shan_shi.fbx → TFD_Canyon_Cliff_01A_LOD0` | 浅滩背后的“世界边” |
| 8 | 灌木/草皮 ★ | `Models/SM_L4_zhi_wu_1 1.fbx → TFD_Bush_01A_LOD0` / `TFD_Grass_Patch_01A_LOD0` | 浅滩植被 |
| 9 | 海草 prefab ★ | `Art/Materials/L4海草2/Kelp 1.prefab`、`L4海草1/Deep Kelp 3.prefab`、`L4草地/Green Sea Grass 1.prefab` | 成片海草 |
| 10 | 火盆/蜡烛 | `Preset/03.glb → TFD_Braizer_01A_LOD0`、`TFD_Candle_03B` | 暖光点，引路 |
| 11 | 破布 | `Preset/03.glb → TFD_Cloth_01B_LOD0` | 吊挂残布（Map0 已有同款） |
| 12 | 浮尘 | `Art/VFX/VFX_Dust_1.prefab` | 水下颗粒 |

### B. 沉没回廊（2:30–5:30，三种泡泡教学；封闭、柱列、可重复门）

| # | 资产 | 路径 | 用途 |
|---|---|---|---|
| 1 | 主廊墙 ★ | `Preset/01.glb → TFD_Wall_01A_LOD0` / `TFD_Wall_04A_LOD0`（4×4） | 回廊墙，Map0 已有 4 块 04A【现成】 |
| 2 | 顶板 ★ | `Preset/01.glb → TFD_Ceiling_01A_LOD0` / `TFD_Ceiling_02A_LOD0`（2×2） | 回廊顶 |
| 3 | 柱列 ★【现成】 | `Preset/01.glb → TFD_Column_01A_Module_13_LOD0`（2×4×2） | 教学区柱列 |
| 4 | 教学门 ★ | `Preset/01.glb → TFD_Gate_01A_Module_03_LOD0` / `_04_LOD0` | 三道泡泡教学门/分隔（wengjie Art_Gate） |
| 5 | 台阶 | `Preset/01.glb → TFD_Stairs_01A_LOD0`（1.07×2×1） | 高低差与跌落 |
| 6 | 栏杆 | `Preset/01.glb → TFD_Railing_01A_LOD0` | 回廊边缘防掉落 |
| 7 | 碑文 ★推荐 | `Preset/01.glb → TFD_Wall_Writing_Tablet_01A_LOD0` | 泡泡操作提示板 |
| 8 | 符文 | `Preset/01.glb → TFD_Rune_07A`（小面片） | 教学点发光标记 |
| 9 | 蛛网/破布【现成】 | `Preset/03.glb → TFD_Web_05A`、`TFD_Cloth_03B` | 沉浸感，Map0 已有 |
| 10 | 海草/草 ★ | `Art/Materials/L4海草1/Deep Kelp 3.prefab`、`L4草地/Green Sea Grass 2.prefab` | 回廊角落 |
| 11 | 光柱 | `ThirdParty/Thalassophobia/Contents/Prefabs/Particles/Sunshafts (Area).prefab` | 破顶漏光 |
| 12 | 浮尘 | `ThirdParty/Thalassophobia/Contents/Prefabs/Particles/Ambient Particles.prefab` | 回廊体积感 |

### C. 涨潮竖井（5:30–8:00，垂直、宏大、终局）

| # | 资产 | 路径 | 用途 |
|---|---|---|---|
| 1 | 旋转梯 ★推荐 | `Preset/01.glb → TFD_Circular_Stairs_01A_L_LOD0` / `_R_LOD0`（4.05×4.17×4） | 竖井主结构（成对攀升） |
| 2 | 大柱 | `Preset/01.glb → TFD_Column_02A_Module_01_LOD0` | 环绕井壁 |
| 3 | 横梁 | `Preset/01.glb → TFD_Beam_10A_LOD0`（12×2.6×2）/ `TFD_Beam_12A_LOD0` | 井内横梁/悬挑 |
| 4 | 井底坑 | `Preset/01.glb → TFD_Pit_01A_Module_01` | 井底水面/落点 |
| 5 | 井壁崖 | `Preset/01.glb → TFD_Canyon_Cliff_03A_LOD0`（23×20×32） | 竖井内壁 |
| 6 | 悬挑平台 | `Preset/01.glb → TFD_Rock_Slab_01A_LOD0` | 落脚的悬挑岩板 |
| 7 | 井顶门 | `Preset/01.glb → TFD_Gate_01A_Module_04_LOD0` | 终点门 |
| 8 | 巨骸骨 ★推荐 | `Preset/01.glb → TFD_Giant_Skeleton_Ribcage_01A_LOD0` + `Skull_01A_LOD0` | 终局奇观 |
| 9 | 日晷 | `Preset/01.glb → TFD_Sundial_01A_LOD0`（6×6×3） | 终局装置/目标 |
| 10 | 大祭坛 | `Preset/01.glb → TFD_Altar_01A_LOD0`（1.64×3.2） | 终点祭坛 |
| 11 | 叠涩顶 | `Preset/01.glb → TFD_Corbelled_Roof_01A_LOD0`（4×1.75×4） | 井顶收口 |
| 12 | 长幡 | `Preset/03.glb → TFD_Cloth_08A`（1.09×2.05×6.82） | 垂直布幡 |
| 13 | 海草柱 | `Art/Materials/L4海草2/Kelp 2.prefab`、`L4海草1/Deep Kelp 4.prefab`（根 scale 4） | 竖向海草群 |
| 14 | 巨岩 ★ | `Models/SM_L4_shan_shi.glb → TFD_Rock_Large_15A_LOD0` | 竖向节奏 |
| 15 | 水面顶盖 | `Art/Materials/M_UnderwaterCeiling.mat` + `UnderwaterCeiling.cs`（HalfSize/SkirtDepth 按井尺寸） | 抬头水面（照 wengjie） |
| 16 | 全屏焦散 | Main Camera 子 Quad + `Art/Materials/M_PAOCausticsScreen.mat` | 全场景焦散 |
| 17 | 水下装置 | `ThirdParty/Thalassophobia/Prefabs/UnderwaterRig.prefab` | 近场雾球+焦散灯 |
| 18 | 后处理 | Global Volume + `ThirdParty/Thalassophobia/PostFX/Shallow Water Profile URP.asset` | Bloom/Vignette/调色 |

**合计推荐 12 + 12 + 18 = 42 个条目（去重后约 30 个资产类别）**；其中 ★wengjie 同款约 12 个，【现成】3 个。

---

## 7. TODO / 未验证（给 Lead 的边界说明）

1. `TFD_Bush/Grass/Palm` 的精确尺寸未测（这类节点在 glb 里是“无 mesh 的父节点”，需在 Unity 里读 MeshRenderer.localBounds；wengjie 里靠 scale 1–1.7 目测）。本轮已确认它们的源文件与材质。
2. `TFD_Pit_01A_Module_01/02`、`Candles_Cluster/Corner/Edge`、`Column_Vine` 等族的尺寸未逐一测（节点结构特殊），施工时以 Unity Bounds 为准。
3. TFD 图集贴图内嵌在 glb 内（`TFD_Atlas_1A` 等），未导出成独立 png；若要自制材质变体，需在 Unity 里选中 glb 内材质 Extract Materials/Textures。
4. Map4/Map5 里 `UnderwaterRig` 实例的具体 transform（位置/缩放）未展开（prefab instance 的 root 被 override，YAML 未逐条解析）；建议施工时直接参照场景里现成的那个实例复制。
5. wengjie 摄像机的 `UniversalAdditionalCameraData`（PostProcessing/Antialiasing）在 takoyaki.prefab 内，未展开；默认相机 + 全局 Volume 即可复现。
6. 珊瑚/海葵资产缺失（见 §0.5）：需要美术新做，或先用海草/草皮替代。
7. 以下材质当前无任何场景引用，可作备选：`M_Bldg_A/B/C.mat`、`M_Wengjie_GroundCaustics.mat`（+ `T_Caustics_36.PNG`）。

---

## 附：路径速查

```
# 干净模块库（4m 网格，单图集）
Assets/GameMain/Art/Preset/01.glb
# 多 LOD / 蜡烛 / 布 / 植物 / 机关
Assets/GameMain/Art/Preset/02.glb .. 06.glb
# 旧整包预设（不建议）
Assets/GameMain/Art/Preset/Preset01.fbx / Preset2.fbx / Preset4.fbx / all Assets.fbx

# wengjie / Map4 / Map5 同源二次导出（★首选风格来源）
Assets/GameMain/Art/Models/SM_L5_mi_gong.glb          # 城墙/铺地/柱/梁
Assets/GameMain/Art/Models/SM_L4_yi_ji_1glb.glb        # 遗迹/门
Assets/GameMain/Art/Models/SM_L4_yi_ji_2glb.glb        # 遗迹/植物/布
Assets/GameMain/Art/Models/SM_L4_shan_shi.glb          # 岩石组
Assets/GameMain/Art/Models/sm_l5_shan_shi.fbx          # 悬崖/巨石（wengjie 用法）
Assets/GameMain/Art/Models/SM_L4_zhi_wu_1 1.fbx        # 灌木/草皮（wengjie 用法）
Assets/GameMain/Art/Models/sm_l1.glb / sm_l3.5.glb     # TFD 散件集

# 植物 / VFX prefab
Assets/GameMain/Art/Materials/L4海草1/Deep Kelp 3.prefab
Assets/GameMain/Art/Materials/L4海草1/Deep Kelp 4.prefab   # root scale 4
Assets/GameMain/Art/Materials/L4海草2/Kelp 1.prefab
Assets/GameMain/Art/Materials/L4海草2/Kelp 2.prefab
Assets/GameMain/Art/Materials/L4草地/Green Sea Grass 1.prefab
Assets/GameMain/Art/Materials/L4草地/Green Sea Grass 2.prefab
Assets/GameMain/Art/Materials/L4草地/Green Sea Grass 3.prefab
Assets/GameMain/Art/VFX/VFX_Dust_1.prefab
Assets/GameMain/Art/VFX/TFD_Portal_01A.prefab

# 水 / 焦散 / 光柱 / 后处理
Assets/ThirdParty/Thalassophobia/Prefabs/UnderwaterRig.prefab
Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Particles/Sunshafts.prefab
Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Particles/Sunshafts (Area).prefab
Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Particles/Ambient Particles.prefab
Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Caustics.prefab
Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Fog.prefab
Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Water Surface.prefab
Assets/ThirdParty/Thalassophobia/PostFX/Shallow Water Profile URP.asset
Assets/ThirdParty/Thalassophobia/PostFX/Deep Sea Profile URP.asset
Assets/GameMain/Art/Materials/M_UnderwaterCeiling.mat
Assets/GameMain/Art/Materials/M_PAOCausticsScreen.mat
Assets/GameMain/Art/Shaders/UnderwaterCeiling.shader
Assets/GameMain/Art/Shaders/PAOCausticsScreen.shader
Assets/GameMain/Art/Shaders/PAOSunshaft.shader
Assets/GameMain/Scripts/Gameplay/Underwater/UnderwaterCeiling.cs
Assets/GameMain/Art/Materials/M_Wengjie_StoneBlue.mat   # glTF PBR，贴图引用 SM_L5_mi_gong.glb
```
