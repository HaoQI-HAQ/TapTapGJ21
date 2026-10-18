# Map0 重构交付报告：第一章「醒来」

> 场景：`Assets/GameMain/Scenes/Map0.unity`
> 生成器：`Assets/GameMain/Scripts/Editor/Map0ChapterSetup.cs`（1330 行，幂等）
> 运行期脚本：`Assets/GameMain/Scripts/Gameplay/Map0TideSurface.cs`
> 设计文档：`Docs/LevelDesign/map0-refactor-plan.md`
> 截图：`Docs/Map0-refactor/shots/`（15 张）；改前备份：`Docs/Map0-refactor/backup/`（保留最近 3 份）

---

## 一、做了什么（一句话）

把 Map0 从「玩家 + 3 块白盒 + 一坨摆歪的 TFD 贴图建筑」重做成**第一章「醒来」**：
按《泡涌：意识之海》第一卷的三拍——**醒来浅滩 → 沉没回廊 → 涨潮竖井**——搭出 6–7 分钟主路 + 1 条上层道分支，接入真实潮水、三种泡泡教学、章节终点与世界观旁白，并把 wengjie 那套水下美术/光/水/焦散/后处理整体搬到 Map0。

## 二、怎么重建（幂等）

菜单 `Tools/PAO/关卡白盒/Map0 重构（第一章 醒来）`（priority 60）一键做四件事：

1. **清场**：删掉除 `Directional Light` 与 `takoyaki` 之外的所有根物体（旧的 Preset4 / 白盒残件 / 6 个散的 Cloth·Web 全部清掉）；
2. **空间 + 机制 + 系统**：重建 `Map0_Refactor`；
3. **美术/光/水/字幕**：重建 `Map0_Atmosphere`（从 wengjie 场景克隆后处理、字幕条、水面；从 wengjie 的 TFD 模板克隆建筑/植物；光照参数抄 wengjie）；
4. **章节接线**：把 `PAOUIConfig` 里 Map0 的 `spawnAnchor/goalAnchor` 改成 `Map0_Spawn/Map0_Goal`，标题改为「第一章 · 醒来」。

辅助菜单：
- `Map0 重构 · 只重建空间`（priority 61）
- `Map0 重构 · 美术/光/水/字幕`（priority 62）
- `Map0 重构 · 接线章节配置`（priority 64）
- `Map0 重构 · 场景报告`（priority 65）
- `Map0 重构 · 截图`（priority 66，输出到 `Docs/Map0-refactor/shots/`）

每次执行先从磁盘备份当前场景再重建，所以重复跑不会叠物体。

## 三、空间结构（世界坐标）

| 段 | 范围 | 地面/关键高度 | 内容 |
|---|---|---|---|
| **A 醒来浅滩** | x −32…32，z −28…14 | 0 | 出生平台、6 根断柱、2 截倒梁、一道 2.8 m 断墙（两面发光可粘带）、墙顶泡泡泉、两根门柱 + 收窄引导石 |
| **B 沉没回廊** | x −12…12，z 16…114 | 2.8 | 9 对石柱 + 5 道断拱；B1 填充锁（弹：TerrainBubble capacity 3）；B2 4.5 m 高台（粘：滞留提拉）；B3 裂墙（炸：泡膜封住的裂口，被炸弹炸开）；B4 12 m 断桥 + 旋转扫杆；尾段台阶上 C 入口 |
| **分支 上层道** | x 12…19，z 52…104 | 12 | 入口断梯 → 上层道 → 2 颗泡泡泉 → 尽头下梯回 C 入口；下方有「接住层」低台（掉下来不会整关重来） |
| **C 涨潮竖井** | x −25…25，z 115…165 | 井底 4 / 入口 8 / 塔顶 36.5 | 五层螺旋岩台（12×12，每层转约 72°）、井底上升气流（掉下去被吹回）、潮水总控 + 8 源 + 潮水开关、塔顶祭坛 + 发光的「日出」泡泡 |

**长视距**：从出生点穿过回廊门柱，一眼看到 150 m 外井口上方那颗发光的「第一个泡泡」（直径 16 m，y=60）；塔顶是唯一的全关俯瞰点。

## 四、系统与接线

| 系统 | 对象 | 说明 |
|---|---|---|
| 出生锚点 | `Map0_Refactor/Systems/Map0_Spawn` @(0,0.2,−20) | 章节配置 `spawnAnchor` |
| 终点锚点 | `Map0_Refactor/C_RisingWell/Map0_Goal` @(0,38.6,160) | 章节配置 `goalAnchor`；挂 `BubbleTideGoal`（radius 0.8 / grace 8 s / reach 3 m） |
| 章节完成 | `BubbleTideDirector.Won → UIRuntime.OnLevelWon → CompleteChapter()` | 复用已有代码，零改动 |
| 潮水 | `C_RisingWell/C_BubbleTide` + 8 × `C_TideAnchor_i` + `C_TideGate` | autoStart=false，玩家踏进井口 1.2 s 后开始；explicit bounds；**实测终点被淹 60.6 s** |
| 潮水可视面 | `C_TideSurface` + `Map0TideSurface` | 见「已知限制」：URP 渲染 Feature 未装，用一张半透明水面按 `Director.Progress` 抬升来表达涨潮 |
| 坠落重生 | `Systems/FallRespawn` | kill y=−8，回 `Map0_Spawn` |
| 井底上升气流 | `C_Updraft` | `WhiteboxUpdraft` 11 m/s，掉进井底不会卡死 |
| 剧情字幕 | `Systems/Story_0…Story_7` | 8 句世界观原文，`WengjieStoryNode` + 从 wengjie 克隆的独立字幕条；暂未配 TTS 语音 |
| 资源 | 3 颗泡泡泉（A 墙顶 / B2 高台 / 分支 2 颗） | `TerrainBubble`，填满给泡泡值 |

## 五、美术/光/水配方（对齐 wengjie）

- 地面：`M_Wengjie_StoneBlue`；墙体：`M_Wengjie_StoneBlue_B`（和 wengjie 同一套石材）
- 建筑：从 wengjie 场景克隆的 TFD 模板（`T_src_TFD_Wall_01A/04A`、`T_src_TFD_Gate_01A_Module`、`T_src_TFD_Canyon_Cliff`、`T_src_TFD_Rock_Large`、Pavement/Grass/Bush、珊瑚），**装饰件统一关投影**以免把回廊遮黑
- 植物：从 wengjie `Wengjie_Mood` 的 `Mood_Plant_*` 克隆 40 株，散布三段的墙脚/岩台边缘
- 水：`Water_Ceiling`（`UnderwaterCeiling`，按场景包围盒自动算：y=80.0，半边 296.5 m，裙边 129 m）
- 焦散：主相机下 `PAO_CausticsScreen` 全屏 Quad，用 **Map0 自己的材质副本** `M_Map0_CausticsScreen`（`_WaterY=80`，不会改到 wengjie 的共享材质）
- 丁达尔：8 个 Thalassophobia `Sunshafts / Sunshafts (Area)` 实例（scale 16）
- 后处理/雾/环境光：从 wengjie 抄 RenderSettings（fog exp2 0.0035 / 0.22·0.42·0.52）+ 克隆 `Wengjie_PostFX` Volume；第一章环境光从 0.85 抬到 1.25，另加一盏冷色补光
- 灯光：12 盏暖/冷点光（回廊 5、分支 2、井 4、祭坛 1）
- 发光材质：`M_Map0_FirstBubble`（地标泡）、`M_Map0_StickyGlow`（可粘带）、`M_Map0_AltarGlow`（祭坛）、`M_Map0_TideWater`（潮水面）

## 六、验证记录

| 项 | 结果 |
|---|---|
| 编译 | `Map0ChapterSetup.cs` + `Map0TideSurface.cs` **0 error / 0 warning** |
| 场景规模 | `Map0_Refactor` 147 个 Transform，`Map0_Atmosphere` 197 个，场景共 281 个 Renderer；场景文件 868 KB |
| 章节检查（`Tools/PAO/Probe/Chapter Check`） | `01 Map0 build=OK file=OK spawn=OK goal=OK 第一章 · 醒来` |
| 潮水烘焙（Play 模式实测） | 233 ms，65×44×68 = 194,480 格（0.8 m），实心 30,277 / 可达 164,203 / 封闭 0；**终点被淹 60.6 s** |
| 运行异常 | Play 模式 12 s 内无 Exception；已知旧问题（UISettings 空引用等）本轮未复现新错误 |
| 截图 | `Docs/Map0-refactor/shots/01…15`（出生/粘墙/回廊/填充锁/高台/裂墙/分支/井口/塔顶/俯瞰/抬头/平视/井内/祭坛/塔身） |

## 七、已知限制 / 风险（诚实版）

1. **潮水的 URP 渲染 Feature 仍未安装**。工程里 `BubbleTideRenderFeature` 以前造成过黑屏，wengjie 的记录是「恢复前必须先修 `BubbleTidePass` 的 blit 源」；本轮没有动共享 Renderer，改用 `Map0TideSurface`（半透明水面随 `Director.Progress` 抬升）来表达涨潮。**玩法（推挤 / 终点淹没 / 失败）是真的，泡泡潮的粒子视觉不是。**
2. **还没人肉完整通关**。潮水 60.6 s、五层岩台、B4 扫杆、B3 炸弹破墙这些数字都只验证了「搭得出来、烤得出来」，没验证「一定走得过去」。建议真人试玩后按 §八 调。
3. 美术是「wengjie 同款白盒 + TFD 克隆装饰」，不是重做的正式资产；部分墙面仍是纯色大块（尤其竖井外墙）。
4. 8 句旁白只有字幕，没有 TTS 语音（wengjie 的 `vo_0..12` 是另一套台词，不能直接复用）。
5. `Map1` 的 `spawnAnchor` 在章节检查里是 MISS（工程既有问题，不是本轮引入）。
6. 场景里 `Map0_Atmosphere/ArtTemplates` 有 50 个缩到 y≈−500 的模板物体（wengjie 同款做法），会拉偏「场景包围盒」类统计，但不影响水面计算（水面只量 `Map0_Refactor`）。

## 八、调参入口（交给下一轮）

| 想改什么 | 改哪里 |
|---|---|
| 潮水快慢 / 截止时间 | `C_BubbleTide` 的 `m_BaseSpeed`（现在 0.8 → 60.6 s；越大越快） |
| 潮水什么时候开始 | `C_TideGate`（WengjieTideGate `m_Delay`，1.2 s） |
| 潮水宽限 / 到达距离 | `Map0_Goal` 的 `BubbleTideGoal`（grace 8 s / reach 3 m） |
| 填充锁要几发 | `B1_Plug` 的 `TerrainBubble.m_Capacity`（现在 3） |
| 裂墙硬不硬 | `B3_CrackedSeal` 的 `m_Capacity`（现在 12，只能炸弹炸开） |
| 扫杆速度 | `B4_SweepPivot` 的 `RotatingHazard`（42°/s，±78°） |
| 灯光/雾 | `Map0_Atmosphere/Map0_Lights`、`Map0_PostFX`、RenderSettings |
| 整体重摆 | 重新点「Map0 重构（第一章 醒来）」即可（幂等） |

## 九、变更文件

改动：
- `Assets/GameMain/Scenes/Map0.unity`（整关重建）
- `Assets/GameMain/Resources/PAOUIConfig.asset`（Map0 章节：锚点/标题/描述/目标）

新增：
- `Assets/GameMain/Scripts/Editor/Map0ChapterSetup.cs`（生成器 + 截图/报告/接线菜单）
- `Assets/GameMain/Scripts/Gameplay/Map0TideSurface.cs`（潮水可视面）
- `Assets/GameMain/Art/Materials/M_Map0_*.mat` ×5（CausticsScreen / FirstBubble / StickyGlow / AltarGlow / TideWater）
- `Docs/LevelDesign/map0-refactor-plan.md`（设计稿）
- `Docs/LevelDesign/map0-refactor-kit.md`、`Docs/LevelDesign/wengjie-recipe.md`（并行调研产出）
- `Docs/Map0-refactor/`（报告 / 15 张截图 / 3 份改前场景备份）

> 共享材质 `M_PAOCausticsScreen.mat` 被构建过程改过 `_WaterY`，已用 `git checkout` 还原；Map0 改用独立副本，不再影响 wengjie。
