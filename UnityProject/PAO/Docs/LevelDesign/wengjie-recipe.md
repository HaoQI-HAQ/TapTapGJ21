# wengjie.unity → Map0 复刻配方

> 状态：只读逆向（Unity 编辑器当时开着 Map0，未执行任何菜单/Play/保存）。
> 场景文件：`Assets/GameMain/Scenes/wengjie.unity`（build index 12，水域 10）。
> 交叉参考：`Docs/LevelDesign/wengjie-detailed-spec.md`、`wengjie-build-log.md`、`wengjie-mechanics-matrix.md`。
> 标注 **TODO** 的是本次未查完/需在施工时确认的点。

---

## 0. 一句话结论

wengjie 是「纯几何 + 少量通用玩法组件」的关卡：**18 个命名 root 组 + 7 个 prefab 实例**，没有独立的主相机/玩家 root（玩家是 `takoyaki.prefab` 实例，相机/补光/焦散屏全挂在它下面），机关语言是**物理关节（ConfigurableJoint/HingeJoint/SpringJoint）+ RotatingHazard + WengjieSuperJump**，终点/叙事靠 `B_Goal(BubbleTideGoal)` + `WengjieStoryNode×15` + `WengjieSubtitleOverlay`，章节进出靠 `PAOUIConfig` 的 `spawnAnchor/goalAnchor` 名称匹配（运行时自动生成 ChapterGoalTrigger）。

**wengjie 里并不存在**：CollapsingFloor、TerrainBubble、StickySurface、BubbleTideDirector、BubblePopObstacle、tiaozhuan、ChapterGoalTrigger（这些是别的场景/运行时才用）。做 Map0「三种泡泡教学」必须显式补，不能指望从 wengjie 抄到。

---

## 1. 场景结构

### 1.1 root 分组（世界包围盒按内部 Transform 位置统计，不是 Renderer.bounds）

| root | 职责 | 世界范围 X / Y / Z | 节点数 |
|---|---|---|---|
| **Wengjie_P1_A** | 出生浅滩：出生台、起始广场、12 级螺旋坡、环桥、Spawn/FallRespawn | X[-36.4, 47.2] Y[-0.5, 12.3] Z[-30.0, 91.5] | 32 |
| **Wengjie_P1_B** | 断柱街市：桥、钟塔、四根斜柱、四段梯、深坑/下水道 | X[-33, 36] Y[0, 23] Z[0, 143] | 13 |
| **Wengjie_P1_C** | 峡谷井 + 塔顶：16 级台阶、66×66 井、6 层旋转岩台、环形塔顶、祭坛、B_Goal | X[-33, 33] Y[0, 66] Z[0, 233] | 36 |
| **Wengjie_Rotate** | 螺旋坡上空的主转环（RingPivot + 16 片）+ 3 个转盘 Disc_A/B/C | X[-29.4, 29.4] Y[0, 27.9] Z[0, 127] | 27 |
| **Wengjie_CounterRing** | 与主转环反向咬合的内环（RingPivotInner + 8 内带 + 8 内墙）+ 静态内台 | X[-21, 21] Y[0, 51.8] Z[0, 111] | 27 |
| **Wengjie_StreetRotate** | 钟塔外的街市环（y=12，12 块）与屋顶环（y=26，12 块） | X[-36, 36] Y[0, 26] Z[0, 146] | 27 |
| **Wengjie_Mechanisms** | 6 组物理机关：Plate_0/1、Seesaw_0/1、Spring_0/1 | X[-6, 9] Y[-1.6, 28.2] Z[0, 132] | 27 |
| **Wengjie_Mood** | 海草/植物装饰 32 件（Kelp/Grass/山石，纯视觉无碰撞） | X[-17, 38.6] Y[0, 56] Z[-30, 232] | 34 |
| **Wengjie_Art** | 可见的 `Art_Gate_0..2` + **35 个 y≈-460~-522 的 `T_src_*` 模板克隆（不可见垃圾，勿抄）** | X[-182.6, 111.2] Y[-522, 3.6] Z[-82.6, 103.9] | 39 |
| **Wengjie_Story** | 主线剧情触发 6 个（Story_0..5） | 沿主路 X=0，Z[-26, 200] | 7 |
| **Wengjie_Story2** | 第二/三幕剧情触发 6 个（Story2_0..5） | X=0，Z[0, 200] | 7 |
| **Wengjie_StoryDisc** | 转盘上语音 3 个（Disc_A/B/C） | 与 Disc 同位置 | 4 |
| **Wengjie_PostFX** | 全局 Volume（无子物体） | 原点 | 1 |
| **Water_Ceiling** | 头顶水面顶盖（UnderwaterCeiling 运行时生成壳体） | (0, 85.06, 100) | 1 |
| **CatchFloor** | 兜底接住层 400×1×500，y=-6，材质 WB_Ground | (0, -6, 100) | 1 |
| **SpawnPad** | 出生台 30×1×30，顶面 y=0 | (0, -0.5, -28) | 1 |
| **Cube** | **遗留测试方块**（12.36×1×7.6，材质丢失→品红），建议删 | (-14.19, -6.45, -29.88) | 1 |

另有 7 个 prefab 实例（见 1.3）。场景总 290 GameObject / 291 Transform / 74 MonoBehaviour / 234 MeshRenderer。

### 1.2 主路走向（起终点/高差）

- **出生**：`SpawnPad` 顶面 y=0 → `Spawn` 空物体 `(0, 1.2, -28)`（玩家实例同位置）。
- **浅滩 → 螺旋坡**：`Hub_Floor_S` 40×1×30 @ `(0,-0.5,-15)`；`A_Ramp_0..11` 每块 4×0.6×15.63，从 `(7.81,0.5,0.68)` 起、每级 +1 m、绕行约 270°：
  `(7.81,0.5,0.68) → (22.50,1.5,6.03) → (34.47,2.5,16.07) → (42.29,3.5,29.61) → (45,4.5,45) → (42.29,5.5,60.39) → (34.47,6.5,73.93) → (22.50,7.5,83.97) → (7.81,8.5,89.32) → (-7.81,9.5,89.32) → (-22.50,10.5,83.97) → (-34.47,11.5,73.93)`；
  末端 `A_RingBridge` 6×1×8 @ `(-36.37,12,66)`，rotY=-60°。
- **转环段（y=12）**：`RingPivot` `(0,12,45)` + `A_Ring_0..15`（每块 ≈15.6×1×22，半径 29–36，绕 Y 每 22.5° 一片）；上方 `CounterRing` 内环 `RingPivotInner`(0,12,45) + `InnerBand_0..7`（18.4×0.5×6，y=36.6–46.05，反向）。
- **街市/钟塔**：`B_Bridge` 8×1×13 @ `(0,11.5,79.5)` → `B_BellTower` 24×22×24 @ `(0,23,110)`；`StreetPivot`(0,12,110) 挂 12 块 `B_Street`（12×0.8×14，半径 36）；`RoofPivot`(0,26,110) 挂 12 块 `B_Roof`（10×0.6×11.7，半径 30）；四段梯 `B_Ladder_0..3`（0.8×14×0.8，半径 33，y=19）、四根柱 `B_Column_0..3`（1.1×8×1.1，rotZ=10°，x≈31.9，z=101/107/113/119）、`B_Pit` 6×12×24、`B_Sewer` 8×3×24。
- **转盘段**：`Disc_A`(29.44,12.3,127)、`Disc_B`(-29.44,12.3,127)、`Disc_C`(-15,26.3,84.02)，各带 CapsuleCollider 的 Surface + SuperJumpZone；语音触发点同名 +0~2 m。
- **上井台阶**：`C_Stair_0..15` 10×0.5×1.8，`(0,26.25,148)` 起，每级 y+0.5、z+1.6 → `(0,33.75,172)`。
- **峡谷井（核心）**：`Hub_Floor_C` 66×1×66 @ `(0,33.5,200)`；井壁厚 2 m、高 36 m、中心 y=49：`C_Wall_N` 70×36×2 @z=233、`C_Wall_E/W` 2×36×66 @x=±33、南墙左右各 26 m + 门洞顶 `C_Wall_S_Top` 14×10×2；
  六层岩台 `C_Ledge_0..5` 16×1×16，半径 24，y=38→58（每层 +4，绕 Y 每层转 60°，累计 360°）。
- **塔顶/终点**：四块 `Hub_Floor_C_N/S/E/W` @y=63.5，围出中央 20×20 井口；`Altar_Core` 4×1.2×4 @ `(0,64.6,213)`；`B_Goal` @ `(0,65.6,213)`；**水面 y=85.06**（终点上方 19.46 m）。
- **高差/距离**：出生地面 0 → 终点 65.6 m（相对接住层 -6 是 71.6 m）；水平 z 从 -28 到 213（约 241 m）。按剧情节奏约 6–8 分钟：浅滩(0:00–0:40) → 坡/环(0:40–2:30) → 街市(2:30–4:30) → 转盘/台阶(4:30–6:00) → 峡谷井(6:00–7:30) → 塔顶终点(7:30–8:00)。

### 1.3 prefab 实例（7 个，全在 SceneRoots 里）

| 名称 | 源 prefab | 位置 / 缩放 |
|---|---|---|
| Art_Sunshaft ×3 | `Assets/ThirdParty/Thalassophobia/Contents/Prefabs/Particles/Sunshafts.prefab` | (0,18,-20) s=14；(0,30,110) s=16；(0,62,200) s=18 |
| Sunshafts (Area) ×3 | `.../Particles/Sunshafts (Area).prefab` | (0,22,0)；(0,34,110)；(0,62,200)（默认缩放） |
| takoyaki | `Assets/GameMain/Art/Models/takoyaki.prefab`（**玩家**） | (0,1.2,-28)，rot=identity（EulerHint y=95.831 只是提示） |

### 1.4 垃圾/坑对象（复刻时不要抄）

- `Cube` root：残留测试方块，材质 guid 全工程找不到（品红）。
- `Wengjie_Art` 里 35 个 `T_src_*`：y≈-460~-522、x 可达 -182，肉眼不可见，是「美术换装」菜单克隆后残留；可见的只有 `Art_Gate_0..2`（y≈3.4，scale≈0.0077 的 glb）。
- `Wengjie_Mechanisms` 的物理本体错位（见 4.5）。
- Spring_0/1 上各有 **2 个重叠的 SuperJumpZone**（菜单重复执行所致，应只留 1 个）。

---

## 2. 玩家与相机

### 2.1 玩家 prefab

- 路径：`Assets/GameMain/Art/Models/takoyaki.prefab`（guid `533472653752a5e42b3a814b5ef9cbac`，`PAOUIConfig.defaultPlayer` 指向它）。
- 层级：

  - takoyaki (root)：CharacterController + PlayerController + BubbleLauncher + BubbleTypeSwitcher + BubbleRideInteractor + BombBubbleManager + PlayerAnimatorDriver + BubbleResourceManager + JellyFaceSwitcher；模型子物体 shuimu_unity_5animations_face。
  - CameraPivot  local (-0.022, 0.43, 1.951)
  - └─ Main Camera local (0, 0, -3)
  - ── CharacterFillLight local (0, 0.5, -1)
  - ── (场景里额外挂) PAO_CausticsScreen
  - └─ 水母模型（嵌套 prefab，Animator + JellyFaceSwitcher，layer=Player）

- 关键字段（PlayerController）：`m_MoveSpeed 5`、`m_SprintMultiplier 1.8`、`m_JumpHeight 1.2`、`m_Gravity -20`、`m_RotationSpeed 12`、`m_CarrierMoveForce 6`、`m_LiftMoveMultiplier 0.45`、`m_CameraPivot=CameraPivot`、`m_PivotHeight 0.5`、`m_MouseSensitivity 2.5`、`m_MinPitch -35`、`m_MaxPitch 75`、`m_CameraTransform=Main Camera`、`m_CameraDistance 2`、`m_MinCameraDistance 0.6`、`m_CameraCollisionRadius 0.15`、`m_CameraCollisionMask ~0`、`m_CameraPullInSpeed 25`、`m_CameraReturnSpeed 6`、`m_LockCursorOnStart 1`、`m_AutoRelockCursor 1`、`m_ClickToRelock 1`。
- CharacterController：`height 0.7 / radius 0.2 / center (0,0,0) / slope 45 / step 0.3 / skin 0.08`。
- BubbleLauncher：`m_Loadout = Assets/GameMain/Resources/CSBubbleLoadout.asset`；Sticky/Bouncy/Bomb 三种泡泡 prefab = `nian_p.prefab` / `tan_P.prefab` / `bao_P.prefab`（`Assets/GameMain/Art/Models/`），参数内嵌在 launcher 里（lifeTime 20/10/13，cost 2–12，弹力泡 bounciness 0.9、ejectSpeed 14 等）。
- ⚠️ prefab 自身 root 的 Transform 不是零：pos `(-14.731731, 1.58, -30.18016)`、rot≈149°。场景实例/运行时生成都会覆写位置，但**手动拖进场景会跑偏**，必要时先把 prefab root 归零。

### 2.2 场景里的实例接线

- 实例名 `takoyaki`，`m_LocalPosition (0,1.2,-28)`、rotation identity、scale 1；`m_Name` 通过 prefab override 改成 `takoyaki`。
- `PAO_CausticsScreen` 不是 prefab 内容，而是 **prefab 实例上的场景新增子物体**（PrefabInstance `m_AddedGameObjects` → 挂到 `Main Camera` 的 Transform）。
- 场景里**没有** `Main Camera`/`CameraPivot` root；找相机请找 `takoyaki/CameraPivot/Main Camera`。
- 若场景里没有 PlayerController，`UIRuntime.PrepareChapter` 会按 `spawnAnchor` 用 `Config.defaultPlayer` 生成 "Chapter Player"，朝向 goal；wengjie 因为已有实例不会生成。

### 2.3 Main Camera 参数

- Camera：`fov 60`、`near 0.3`、`far 1000`、`clearFlags Skybox`、背景黑、HDR on、MSAA on、`m_CullingMask all`、depth -1。
- UniversalAdditionalCameraData：`m_RenderPostProcessing 1`、`m_Antialiasing 0`、`m_RendererIndex -1`、`m_VolumeLayerMask bits=1`、`RequiresDepthTexture UsePipelineSettings`。

### 2.4 CharacterFillLight

- 挂在 `takoyaki/CameraPivot/Main Camera` 下（相机位置），local `(0, 0.5, -1)`。
- Light：`type Point`、`color (1, 0.95, 0.89)`、`intensity 1.8`、`range 30`、`shadows None`、`renderMode ForcePixel`、`lightmapBakeType Realtime`、`bounceIntensity 0`。
- **`cullingMask = 1<<6 = 64`**，只照 `Player` 层；玩家所有 Renderer 的 layer 必须是 `Player`（第 6 层，菜单 `Tools/PAO/Probe/Setup Character Fill Light` 会建层并改 prefab，注意它改的是 prefab 全局）。

### 2.5 PAO_CausticsScreen 挂法（全屏焦散叠加）

- 对象：`takoyaki/CameraPivot/Main Camera/PAO_CausticsScreen`（Quad primitive，无 Collider）。
- Transform：local `(0, 0, 1.5)`、scale `(8, 8, 1)`、rotation identity。
- MeshRenderer：材质 `Assets/GameMain/Art/Materials/M_PAOCausticsScreen.mat`；`shadowCasting Off / receiveShadows false / lightProbe Off / reflectionProbe Off`。
- 材质（Shader `PAO/CausticsScreen` = `Assets/GameMain/Art/Shaders/PAOCausticsScreen.shader`）参数：
  `_CausticColor (0.68,0.92,1)`、`_Intensity 0.28`、`_Scale 0.55`、`_Speed 2`、`_LineWidth 0.08`、`_SwellScale 0.045`、`_SwellStrength 0.6`、`_WaterY 85.05994`、`_DepthFade 2.5`、`_FadeStart 130`、`_FadeEnd 380`。
- Shader 关键：`Blend One One / ZWrite Off / ZTest Always / Cull Off`，用 `_CameraDepthTexture` 反投影世界坐标 + XZ 上的 Voronoi 画亮线 → 地面/墙/道具全都有焦散，不需要摆面片。**`_WaterY` 必须等于 Water_Ceiling 的 Y（85.06）**。

---

## 3. 水面 / 焦散 / 丁达尔 / 后处理

### 3.1 Water_Ceiling（头顶水面）

- GameObject `Water_Ceiling` @ `(0, 85.06, 100)`，scale 1；组件 `MeshFilter + MeshRenderer + UnderwaterCeiling`。
- `UnderwaterCeiling`（`Assets/GameMain/Scripts/Gameplay/Underwater/UnderwaterCeiling.cs`）：`m_HalfSize 510`、`m_SkirtDepth 400`。网格运行时生成（20 顶点：顶面 2 三角 + 4 片裙边），HideFlags.DontSave，**场景里只存两个数字**。
- 材质：`Assets/GameMain/Art/Materials/M_UnderwaterCeiling.mat`，Shader `PAO/UnderwaterCeiling`（`Assets/GameMain/Art/Shaders/UnderwaterCeiling.shader` + `UnderwaterCeilingCore.hlsl`），customRenderQueue 2999；关键参数：`_SurfaceColor (0.5,0.85,0.97)`、`_SkirtBottomColor (0.02,0.08,0.14)`、`_WindowColor (0.8,0.96,1)`、`_RippleSpeed 1.2 / _RippleStrength 0.55 / _RippleWidth 0.13 / _RippleScale 0.3`、`_SwellScale 0.055 / _SwellStrength 0.7`、`_FadeStart 260 / _FadeEnd 900`、`_Dither 0.004`。渲染器强制 `shadowCasting Off / receiveShadows false / probes Off`，无 Collider。
- 工具生成公式（`Tools/PAO/水下/为当前场景生成水面顶盖`，即 `UnderwaterCeilingSetup.BuildForScene`）：遍历场景 Renderer 求包围盒（排除粒子/拖尾/水面自身）→ `waterHeight = bounds.max.y + Clearance`；`halfSize = max(max(ext.x,ext.z)*1.4,120)+160`；`skirt = clamp(size.y+60,80,400)`；位置 = `(bounds.center.x, waterHeight, bounds.center.z)`。

### 3.2 焦散

- 最终版 = **屏幕空间全屏叠加**（2.5 的 PAO_CausticsScreen）。
- 历史版本（当前场景已删）：8 块 URP SimpleLit 水面 + `Wengjie_GroundCaustics` 面片（Legacy Additive 材质 `M_Wengjie_GroundCaustics.mat` + `T_Caustics_36.PNG`，renderQueue 3000，6 处 Quad 平铺）。不要再用。
- 清旧/换新菜单：`Tools/PAO/Probe/Fix Wengjie Water`（priority 140）删旧水面后调 UnderwaterCeiling；`Add Ground Caustics`（141）建 PAO_CausticsScreen；`Fix All Caustics`（139）一键两步。

### 3.3 丁达尔 Sunshafts（体积光柱）

- 3 个 `Art_Sunshaft`（`Sunshafts.prefab`，Thalassophobia 第三方）+ 3 个 `Sunshafts (Area)`（`Sunshafts (Area).prefab`）。
- 位置/缩放见 1.3；全部是 prefab 实例，保留 prefab 不要展开。
- 粒子：`ParticleSystem + ParticleSystemRenderer`，材质 `Assets/ThirdParty/Thalassophobia/Contents/Materials/Sunshafts.mat`；`looping/prewarm/playOnAwake`，`startLifetime 15`，`maxNumParticles 1000`，shape=Cone(angle 25, length 5, scale 10,1,10)，emission 20/s，renderer `m_RenderMode 1`(Stretched Billboard)、`lengthScale -60`、`m_MaxParticleSize 0.5`。
- 另：`Assets/GameMain/Art/Shaders/PAOSunshaft.shader` 是项目自写光柱 shader（注释说明为何头顶水面改成不透明顶盖），当前场景的 Sunshafts 用的是第三方 prefab 材质，不是它（TODO：确认是否还有地方用 PAO/Sunshaft）。

### 3.4 Volume / 后处理

- 组件：`Wengjie_PostFX` 上的 `Volume`（URP）：`m_IsGlobal 1`、`priority 10`、`blendDistance 0`、`weight 1`。
- Profile：`Assets/ThirdParty/Thalassophobia/PostFX/Shallow Water Profile URP.asset`，override 项：
  - Bloom：`intensity 1`、`scatter 0.572`（threshold 0.9 不 override）；
  - Tonemapping：`mode 2 (ACES)`；
  - ColorAdjustments：`postExposure 0.15`、`contrast 8`、`saturation -12`；
  - Vignette：`intensity 0.22`。
- URP 相机 `m_RenderPostProcessing=1`，Volume 层 mask=1（Default）。

### 3.5 方向光

- `Directional Light` @ `(0,3,0)`，localEuler `(50, -30, 0)`。
- Light：`m_Type 1 (Directional)`、`color (1, 0.95686, 0.83922)`、`intensity 1`、`shadows m_Type 2 (Soft)`、`shadowStrength 1 / bias 0.05 / normalBias 0.4`、`cullingMask all`、`colorTemperature 6570（未启用）`。

---

## 4. 机关系统

### 4.1 组件库盘点：wengjie 真正用的 vs 任务点名要复用的

wengjie 中 MonoBehaviour 只有 74 个：`RotatingHazard×39`、`WengjieStoryNode×15`、`WengjieSuperJump×7`、`WengjieSubtitleOverlay×1`、`BubbleTideGoal×1`、`FallRespawn×1`、`UnderwaterCeiling×1`、`Volume×1`、`UniversalAdditionalLightData×1`、UGUI Text/Outline/Shadow/CanvasScaler 共 7。

| 任务点名的系统 | 在 wengjie | 实际被谁用 |
|---|---|---|
| CollapsingFloor | ❌ 0 | `shiliuye map`×7 |
| TerrainBubble | ❌ 0 | Map1×4、Map5×31、shiliuye×7 |
| StickySurface | ❌ 0 | Map5×557、shiliuye×3 |
| BubblePopObstacle | ❌ 0 | shiliuye×27 |
| BubbleTideDirector | ❌ 0 | 全工程场景都没有（未落地） |
| BubbleTideGoal | ✅ 1（B_Goal） | wengjie |
| BouncyBubbleAxisMover | ❌ | shiliuye×1 |
| tiaozhuan | ❌ | Map1×1 |
| PlayerController | ✅ prefab 内 1 | 全部关卡 |

→ **Map0 的三种泡泡教学（粘/弹/炸）必须手动摆**：粘 = 给墙挂 `StickySurface`；弹 = `TerrainBubble`（地形泡被弹力泡填充）、`WengjieSuperJump`（推弹力泡）；炸 = `TerrainBubble` + `WengjieProximityBurst` / 炸弹泡。wengjie 只演示了「弹力泡超级跳」这一种。

### 4.2 RotatingHazard（`Assets/GameMain/Scripts/Gameplay/RotatingHazard.cs`）

- 语义：`m_Axis`(默认 up)、`m_Speed`=度/秒（带符号）、`m_PingPong`（来回摆）、`m_SwingAngle`（摆动上限，度）；`Update` 里 `transform.Rotate(axis, delta, Space.Self)`。挂在**空 pivot** 上绕井道中心扫，也挂在每块板上形成复合运动。
- 实例（39）：
  - `StreetPivot`(0,12,110) speed 8 pingpong swing10 + 12 块 `B_Street` 各自 ±6（pingpong=false，同向/反向交替）；
  - `RoofPivot`(0,26,110) speed -10 pingpong swing10 + 12 块 `B_Roof` 各自 ±6；
  - `RingPivot`(0,12,45) speed 4 pingpong swing12 + `A_Ring_0..15`（无组件，只有几何）；
  - `RingPivotInner`(0,12,45) speed 8 pingpong swing10 + `InnerBand_0..7` speed 8/12/-10/-14 交替、pingpong swing10；
  - `Disc_A` speed 9 / `Disc_B` -12 / `Disc_C` 7（pingpong=false）。
- 复合旋转会让同一块板「公转+自转」；Map0 若只要一圈扫描，只在 pivot 上挂、子板别挂。

### 4.3 物理机关 Plate / Seesaw / Spring（`Wengjie_Mechanisms`，菜单 116/117 生成）

| 组 | 位置 | 结构（Cube 尺寸） | 物理 |
|---|---|---|---|
| **Plate_0/1** | (-6,12.6,66) / (6,12.6,100) | `Plate` 5×0.4×5；`Base` 6×0.6×6 local y=-1.6；`LinkedGate` 0.8×5×6 local (3,-0.4,0)；`LinkRod` 3×0.25×0.25 local (1.5,0.6,0)（纯视觉） | Plate: Rigidbody mass20 drag1.5 angDrag3 + ConfigurableJoint(x/z/角全锁，y Limited limit1.2, yDrive spring900 damper60 maxForce4000)；Gate: mass18 + ConfigurableJoint(limit3, yDrive 400/40/2000) + SpringJoint(connectedBody=Plate, spring1200, damper50, maxDistance3) |
| **Seesaw_0/1** | (0,12.6,120) / (0,26.6,132) | `Beam` 10×0.5×3；`Pivot` 0.6×1.8×3 local y=-0.9 | Beam: mass25 centerOfMass0 + HingeJoint(axis=(0,0,1), limits ±22°) |
| **Spring_0/1** | (0,12.6,88) / (0,26.6,108) | `Pad` 4×0.4×4；`Frame` 4.6×1.2×4.6 local y=-1.2；`SuperJumpZone`（触发器） | Pad: mass10 drag0.5 + ConfigurableJoint(y Limited limit1.5, yDrive 2600/22/9000) |
| 通用 | — | 材质：`M_Mech_Plate`/`M_Mech_Move`（URP Lit）+ WB_Ground/WB_Bomb/WB_FloatSticky | 另有 `Assets/GameMain/Art/PhysicMaterials/M_SuperBouncy.physicMaterial`（bounciness1, friction0, Maximum combine），菜单 133 生成并套到 Plate/Spring |

- 压力板逻辑（菜单 118）：`LinkedGate.localPosition=(3,-0.4,0)`，再加可见 `LinkRod`；闸门与踏板用 SpringJoint 耦合（压板→闸门升起）。
- 视觉语言（菜单 119）：可压/可站=深石板+橘边；会动/可转=橘色轴心；可粘=浮粘泡同色（材质 M_Mech_Plate / M_Mech_Move 等）。

### 4.4 WengjieSuperJump（`Assets/GameMain/Scripts/Gameplay/WengjieSuperJump.cs`）

- 字段：`m_Speed 28`、`m_Cooldown 1.5`、`m_Radius 5`；`OnTriggerEnter` 只认 `PlayerController`，在玩家周围 `OverlapSphere` 找类型名含 "bouncy" 的 Rigidbody，把 `velocity.y` 设 28 —— **只在弹力泡里才有反应**。
- 挂法：`SuperJumpZone` 子物体，local `(0,1.6,0)`，BoxCollider `size (5,3.2,5)`、isTrigger=true；共 7 个：Disc_A/B/C 各 1 + Spring_0/1 各 2（重复，建议留 1）。

### 4.5 ⚠️ wengjie 的机关本体全部错位（必须修正后再抄）

菜单 116 创建子物体时用了 `go.transform.position = pos`（世界坐标）而不是 localPosition，而父容器已经被挪到关卡里，于是最终场景里：

- `Plate_0/Plate`、`Plate_1/Plate`、`Spring_0/Pad`、`Spring_1/Pad`、`Seesaw_0/Beam`、`Seesaw_1/Beam` 的世界坐标都是 **(0,0,0)**；
- `Base`=(0,-1.6,0)、`Frame`=(0,-1.2,0)、`Pivot`=(0,-0.9,0) —— 全部堆在出生广场原点。
- 只有菜单 118 修过的 `LinkedGate`/`LinkRod` 在正确位置（它们用了 localPosition）。

→ **Map0 复刻时：容器放 (x,12.6,z)，子物体 localPosition 用 (0,0,0)/(0,-1.6,0)/(3,-0.4,0) 等相对值**。不要照抄 wengjie 的世界坐标。也可顺手把 wengjie 修掉（属重建而非逆向，需 Lead 批准）。

### 4.6 泡泡潮（涨潮）现状

- 代码齐全：`Assets/GameMain/Scripts/Gameplay/BubbleTide/` → `BubbleTideDirector`（体素烘焙 3D 纹理，m_CellSize 0.5、m_BaseSpeed 1、m_SpeedExponent 0.7、四角速度倍率 0.5–3、可显式 bounds、泡泡半径 0.35 / 间距 0.5）、`BubbleTideAnchor`、`BubbleTideGoal`、`BubbleTideField`、`BubbleTidePush`、`BubbleTideCellRenderer`、`BubbleTideRenderFeature`、`BubbleTidePass`；编辑器工具 `BubbleTideSceneSetup`：
  - `Tools/PAO/泡泡潮/安装到 URP Renderer`(20)、`从 URP Renderer 卸载`(22)、`一键搭建潮水`(24)、`检查安装状态`(26)；另有 `Tools/PAO/泡泡潮/新建卡通泡泡材质`(60)、`挂上卡通泡泡渲染（新版）`(61)。
- **但 wengjie 场景里没有任何 BubbleTide 物体**：B_Goal 上的 `BubbleTideGoal` 只是标记（`m_Radius 0.5`、`m_GraceSeconds 5`、`m_ReachDistance 3`、`m_RequireConfirmKey 0`、`FloodDeadline` 永远不会被写）。所谓「涨潮」目前只是章节文案/字幕。
- Map0 若要做真「涨潮竖井」：先 `安装到 URP Renderer` → `一键搭建潮水`（会自动摆 1 个 BubbleTide 总控 + 8 个角 + 1 个终点，并写 bounds）→ 再把 `WengjieTideGate`（触发后才开始涨，`m_Delay 1.2`）挂到进井触发区。

---

## 5. 章节 / 终点 / 叙事接线

### 5.1 PAOUIConfig（`Assets/GameMain/Resources/PAOUIConfig.asset`）

- `chapters[9] = { scene: "wengjie", spawnAnchor: "Spawn", goalAnchor: "B_Goal", title: "水域 10", description: "一口正在被灌满的井。三条路都通向它，但没有一条是免费的。", objective: "在潮水淹没祭坛之前，爬上峡谷的最高处。" }`。
- `additionalGameplayScenes` 含 `wengjie`；`defaultPlayer` = takoyaki.prefab。
- **锚点按名字在场景里查找**（`FindAnchor` 遍历 root 的全部子 Transform），必须精确同名、且物体在场景里（不能只在 prefab 内）。
- ⚠️ Map0 的配置项是 `spawnAnchor: Ground_FrontOpenSpace` / `goalAnchor: FarLandmark_Base`；本次只读检查时 Map0 活场景只有 `Directional Light / takoyaki / Map0_Refactor` 三个 root，**这两个锚点不存在** → Map0 现在无法自动生成出生点/终点。重建时必须建这两个名字的空物体（或改 PAOUIConfig，但那是 Assets，需 Lead 统一改）。

### 5.2 运行时 PrepareChapter（`Assets/GameMain/Scripts/UI/V2/UIRuntime.Chapters.cs`）

1. `PrepareChapter(scene)`：`index = Config.IndexOf(scene.name)`，取 chapter 定义。
2. 若场景里 `FindFirstObjectByType<PlayerController>() == null`：把旧 Camera 全关掉，用 `Config.defaultPlayer` 在 `SurfacePoint(spawnAnchor)` 生成 "Chapter Player"（SurfacePoint = Collider/Renderer bounds 顶部中心；角色高度补偿 +0.15），朝向 goal。
3. 终点优先级：场景已有 `ChapterGoalTrigger` → 用它；否则已有 `tiaozhuan` → 用它；否则若 `goalAnchor` 存在 **且场景里没有 `BubbleTideDirector`** → 在 `SurfacePoint(goal)` 新建 `"Chapter goal · B_Goal"`：`ChapterGoalTrigger{requireInteract=true, nextScene=下一章}` + BoxCollider(3,3,3,center 0,1.5,0) + `UIChapterBeacon`。
- wengjie 实际情况：有玩家实例 → 不生成玩家；没有 ChapterGoalTrigger/tiaozhuan/director → **运行时在 B_Goal 处自动生成终点触发器**，玩家到达后按交互键 → `UIRuntime.ConfirmChapterGoal` → `CompleteChapter`（保存进度、结算页、下一章按钮）。
- `tiaozhuan`（`Assets/GameMain/Scripts/UI/tiaozhuan.cs`）是旧方案：OnTrigger 碰到 PlayerController → 若 `UIRuntime.Instance.IsGameplay` 则 `CompleteChapter(sceneName)`，否则 `SceneManager.LoadScene`；目标场景必须在 Build Settings。wengjie 没用它。

### 5.3 B_Goal / 终点

- GameObject `B_Goal` 是 `Wengjie_P1_C` 的子物体，@ `(0, 65.6, 213)`，只挂 `BubbleTideGoal`（`m_DrawGizmo 1`，gizmo 色金黄）。
- 它没有 Collider/Renderer → 运行时 SurfacePoint 取自身位置；自动终点触发器中心 `(0, 67.1, 213)`，玩家站在 Altar_Core(顶面 65.2) 上进入 3×3×3 触发区 + 按交互。
- 若接入 BubbleTide，则 `FloodDeadline/IsFlooded` 由 Director 写入，`IsReached` 由 Director 判定；`m_GraceSeconds` 变成「被淹后还能冲进去的宽限」。

### 5.4 WengjieStoryNode 触发点（15 个，全部 BoxCollider trigger、无 Renderer）

组件字段：`m_Line`(字幕)、`m_Speaker`、`m_Voice`(AudioClip)、`m_Once 1`、`m_Delay 0.2`、`m_Volume 1`；`OnTriggerEnter` 只认 `PlayerController`，`Invoke` 0.2s 后播 2D AudioSource + 字幕。

| 路径 | 位置 | 说话人 | 台词 | 配音 | 触发盒 |
|---|---|---|---|---|---|
| Wengjie_Story/Story_0 | (0,1,-26) | 泡泡 | 甜甜圈！ | vo_0.wav | 6×4×4 |
| Wengjie_Story/Story_1 | (0,2,2) | 泡泡 | 往上走，水在涨。 | vo_1.wav | 6×4×4 |
| Wengjie_Story/Story_2 | (0,13,66) | 回音 | 粘住墙，下面是空的。 | vo_2.wav | 6×4×4 |
| Wengjie_Story/Story_3 | (0,13,79) | 回音 | 有人试过，没走完。 | vo_3.wav | 6×4×4 |
| Wengjie_Story/Story_4 | (0,35,194) | 钟声 | 钟响的时候，水到顶。 | vo_4.wav | 6×4×4 |
| Wengjie_Story/Story_5 | (0,64.6,200) | 泡泡 | 到了。然后呢？ | vo_5.wav | 6×4×4 |
| Wengjie_Story2/Story2_0 | (0,13,82) | 回音 | 别停下，它们会记住你。 | vo_6.wav | 6×4×4 |
| Wengjie_Story2/Story2_1 | (0,13,125) | 回音 | 这里以前有人住。 | vo_7.wav | 6×4×4 |
| Wengjie_Story2/Story2_2 | (0,27,135) | 回音 | 抬头，路在上面。 | vo_8.wav | 6×4×4 |
| Wengjie_Story2/Story2_3 | (0,35,195) | 钟声 | 往下没有底。 | vo_9.wav | 6×4×4 |
| Wengjie_Story2/Story2_4 | (0,50,200) | 钟声 | 水在等你。 | vo_10.wav | 6×4×4 |
| Wengjie_Story2/Story2_5 | (0,64,198) | 泡泡 | 只剩最后一段。 | vo_11.wav | 6×4×4 |
| Wengjie_StoryDisc/StoryDisc_Disc_C | (-15,28.3,84.02) | 泡泡 | 飞起来！ | vo_12.wav | 10×5×10 |
| …Disc_A | (29.44,14.3,127) | 泡泡 | 飞起来！ | vo_12.wav | 10×5×10 |
| …Disc_B | (-29.44,14.3,127) | 泡泡 | 飞起来！ | vo_12.wav | 10×5×10 |

- 音频目录：`Assets/GameMain/Audio/WengjieVO/vo_0.wav … vo_12.wav`（命名约定 vo_N 与节点索引对应，菜单 115 按此自动加载）。
- 生成菜单：`Tools/PAO/关卡白盒/wengjie 剧情配音点位`(115)、`剧情点位 第二批`(127)、`转盘首次语音`(129)。

### 5.5 WengjieSubtitleOverlay（场景自带字幕兜底，不依赖 UIRuntime）

- root：`Wengjie Subtitle`（**场景根上的独立 Canvas**，不是 prefab）：
  - Canvas：`RenderMode ScreenSpaceOverlay`、`sortingOrder 1500`；
  - CanvasScaler：`ScaleWithScreenSize`、reference 1920×1080、match 0.5；
  - CanvasGroup + `WengjieSubtitleOverlay{m_Group, m_Speaker, m_Body}`。
- 子物体 `Speaker`：Text fontSize **26**，anchor/pivot (0.5,0)，anchoredPos (0,168)，sizeDelta 1400×96；
  `Body`：fontSize **36**，anchoredPos (0,108)，sizeDelta 1400×96。
  字体 `Assets/GameMain/Art/Fonts/26汉仪趣报简.ttf`；白色；对齐 MiddleCenter；`Outline` (黑 0.92, 1,-1) + `Shadow` (黑 0.72, 0,-2)；`raycastTarget=false`。
- 行为：`Show(speaker,text)` 静态入口；0.22 s 淡入 / 0.35 s 淡出；显示时长 `max(4, text.Length*0.13)+1` 秒；"旁白/Narrator" 不显示名字。
- 生成菜单：`Tools/PAO/Probe/Add Wengjie Subtitle`（priority 152）。

### 5.6 存档 / 出生锚点如何工作

- 进度：`UIRunState.Progress`（`completed` 场景名列表、`memories`、`tutorialsSeen`、`ending`、`playSeconds`、`bubbleType`）；`CompleteChapter` → `UIRunState.Complete(lastScene)` + `SaveSnapshot(UIRunState.AutoSlot=3)`。
- 存档槽：`SaveSystem` / `SaveLoadManager` / `SaveSlotData`（`sceneName`、`runProgress`、`SavedAtLocal`）；自动槽 3；读档用 `sceneName` 重新加载场景。
- **spawnAnchor/goalAnchor 不存档**：每次 `PrepareChapter` 都从 `PAOUIConfig` 按名字在场景里重新找；所以锚点改名/删除会导致读档后没有出生点/终点。
- TODO：SaveSnapshot 的字段级细节（`UIRuntime.Panels.cs` 501 附近）未逐行读，施工时以 SaveLoadManager 为准。

---

## 6. 可复刻步骤清单（在另一个场景从零搭同质量关卡）

### 6.1 前置

1. URP 项目（已配置 `UniversalAdditionalCameraData/Volume`），URP Renderer 在 `Assets/Settings`（BubbleTide 若要用还需装 Render Feature）。
2. 现有资源：`takoyaki.prefab`（玩家）、`CSBubbleLoadout.asset`、三种泡泡 prefab、`M_Wengjie_StoneBlue.mat`、`WB_Platform/WB_Ground/WB_Bounce`、`M_Mech_Plate/M_Mech_Move`、`M_UnderwaterCeiling.mat`、`M_PAOCausticsScreen.mat`、`Shallow Water Profile URP.asset`、`Assets/GameMain/Audio/WengjieVO/`、字体 `26汉仪趣报简.ttf`。
3. 场景加入 Build Settings，并在 `PAOUIConfig.chapters` 增加/修改条目（scene 名、spawnAnchor、goalAnchor）。

### 6.2 编辑器菜单序列（Tools/PAO/… 按 priority 从小到大）

> 这些菜单大多会 `OpenScene(wengjie)` 并 SaveScene；在 Map0 复用需复制/改 `kScenePath` 或手动搭建（**当前 Unity 有人在使用，禁止直接执行**）。

| 步骤 | 菜单 | 产出 |
|---|---|---|
| 1 | `关卡白盒/搭建 wengjie 中期（断柱廊 + 峡谷）`(71) | 初始骨架 |
| 2 | `关卡白盒/wengjie P1-1 河岸 + 弧形坡 + 村台`(79) / `P1-2 断柱街市`(80) / `P1-3 峡谷井 + 塔顶`(81) | P1_A/B/C 三大段几何（数值见 1.2） |
| 3 | `关卡白盒/wengjie 出生点兜底`(92) | SpawnPad 30×30、CatchFloor 400×500、Spawn、FallRespawn(-4, Spawn) |
| 4 | `关卡白盒/wengjie 轮转地形`(84) / `反向咬合（内环逆转）`(85) / `街市大轮转+小轮转`(97) / `街市板运动`(96) / `运动改成循环`(98) | 四个转环 + RotatingHazard 参数 |
| 5 | `关卡白盒/wengjie 换成物理机关`(116) → `物理机关 第二批`(117) → `机关：删装饰 + 连因果`(118) → `机关：视觉语言`(119) | 6 组机关（注意 4.5 的错位问题） |
| 6 | `关卡白盒/wengjie 超级跳（仅弹力泡）`(131) → `超级跳加大力度`(132) → `机关调成超级弹`(133) | 7 个 SuperJumpZone（28 m/s）+ M_SuperBouncy.physicMaterial |
| 7 | `关卡白盒/wengjie 剧情配音点位`(115) → `剧情点位 第二批`(127) → `转盘首次语音`(129) | 15 个 WengjieStoryNode（表见 5.4） |
| 8 | `关卡白盒/wengjie 补后处理`(123) | Wengjie_PostFX Volume + Shallow Water Profile |
| 9 | `水下/为当前场景生成水面顶盖`(90) 或 `Probe/Fix Wengjie Water`(140) | Water_Ceiling + M_UnderwaterCeiling |
| 10 | `Probe/Add Ground Caustics`(141) 或 `Probe/Fix All Caustics`(139) | 相机前 PAO_CausticsScreen（_WaterY 自动取 Water_Ceiling.y） |
| 11 | `Probe/Setup Character Fill Light`(150) | 改玩家 prefab：Player 层 + CharacterFillLight（注意会影响所有关卡） |
| 12 | `Probe/Add Wengjie Subtitle`(152) | Wengjie Subtitle Canvas |
| 13 | 美术：`wengjie 美术换装`(72) → `建筑（从 Map5 抓）`(105) → `地面换石材`(106) / `地面染蓝`(107) / `岩壁染蓝`(108) / `建筑配色（三档）`(109) / `对比度修正`(110) | Wengjie_Art/Mood、材质替换 |
| 14 | 清理：`删掉挡视野的建筑`(120) / `视线检查`(121) / `环境通透`(122) / `清理散乱摆件`(124) / `清掉所有地砖`(125) / `收尾`(126) | 去噪；wengjie 最终仍残留 4.5/1.4 的垃圾，说明这步要人工复核 |
| 15 | `Probe/Chapter Check`(906) | 检查 10 章 scene/build/锚点是否 OK |

### 6.3 手动步骤（菜单做不了的）

1. **玩家**：把 `takoyaki.prefab` 拖进场景 → 改名 `takoyaki` → position = Spawn 点；确认 `CameraPivot/Main Camera/CharacterFillLight` 存在。
2. **出生点**：在出生位置建空物体 `Spawn`；新建 `FallRespawn` 空物体，`m_KillHeight=-4`、`m_SpawnPoint=Spawn`。
3. **终点**：在终点建空物体 `B_Goal` + `BubbleTideGoal`（radius .5 / grace 5 / reach 3）。
4. **锚点名必须与 PAOUIConfig 一致**（wengjie: Spawn / B_Goal；Map0: Ground_FrontOpenSpace / FarLandmark_Base）。
5. **字幕条/焦散屏/水面**：若菜单不可用，可手动按 2.5/3.1/5.5 的值复刻（PAO_CausticsScreen 一定挂在 Main Camera 下，local (0,0,1.5)、scale (8,8,1)）。
6. **相机**：不要另加相机；相机在玩家 prefab 内。若场景有白盒相机，PrepareChapter 会自动禁用。
7. **按 4.5/1.4 清理**：删 `Cube`、删 y<-100 的 T_src_*、重复 SuperJumpZone、修正机关本体 localPosition。
8. **BubbleTide（如果 Map0 要真涨潮）**：`Tools/PAO/泡泡潮/安装到 URP Renderer` → `一键搭建潮水` → 摆 `WengjieTideGate` → 把 `B_Goal` 的 BubbleTideGoal 留给 Director 接管（有 Director 时 PrepareChapter 不再自动生成终点触发，需要手工放 ChapterGoalTrigger 或依赖 Director 的胜利判定）。
9. **三种泡泡教学**：给墙/柱挂 `StickySurface`；摆 `TerrainBubble`（容量 5、gather 12、respawn 8）+ 弹力泡来源；用炸弹泡 + `WengjieProximityBurst`(delay .15, launch 20, upBias .55) 做「炸」；这些在 shiliuye/Map5 有现成用法，建议从那里拷物体。
10. **Map0 章节文案**：改 `PAOUIConfig.chapters[0]` 的 title/description/objective 为「醒来 / 沉没回廊 / 涨潮竖井」口径（改 Assets，需 Lead 统一）。

### 6.4 验收清单

- [ ] Play 后玩家出现在 Spawn（不是 0,0,0），相机角度正常，角色补光只照玩家。
- [ ] 抬头能看到水面壳（内表面），低头/墙面有焦散亮线；焦散 `_WaterY` 与水面对齐。
- [ ] 三个转环转动、反向咬合、Disc 能靠弹力泡超级跳飞上去。
- [ ] 15 个 Story 点不重复触发，字幕/语音都能出；独立 Canvas 排序 1500 在 HUD 之上。
- [ ] 到 B_Goal 出现交互提示，交互后进结算 → 下一章按钮可用；`UIRunState.Progress.completed` 记录 wengjie/Map0。
- [ ] 掉到 y<-4 能回 Spawn；CatchFloor 接得住。
- [ ] 场景保存后无品红材质、无 y≈-500 的隐形垃圾、无重叠物理本体。

---

## 7. 已知坑（Map0 最容易踩的 10 条）

1. **玩家/相机不是场景 root**：找 `takoyaki` 实例；相机在 `takoyaki/CameraPivot/Main Camera`；不要另加 Camera。
2. **PAO_CausticsScreen 是相机子物体**（场景新增到 prefab 实例的 Main Camera 下），不是独立 root；`_WaterY` 必须 = 水面 Y。
3. **水面网格是运行时生成的**（UnderwaterCeiling），场景只存 HalfSize/SkirtDepth；别手动建 mesh；材质 queue 2999。
4. **章节完成靠锚点名字**：`PAOUIConfig` 的 spawnAnchor/goalAnchor 必须能在场景里按名字找到；Map0 目前缺 `Ground_FrontOpenSpace/FarLandmark_Base`，会既不能自动出生也不能自动终点。
5. **有 BubbleTideDirector 时不会再自动生成终点触发器**：接线方案二选一，别混。
6. **wengjie 没有 tiaozhuan/ChapterGoalTrigger**，终点是运行时生成的；不要在 wengjie 里再手动放一个（会覆盖优先级）。
7. **机关本体错位**（4.5）：所有 Plate/Pad/Beam 在世界原点；复刻用 localPosition。
8. **WengjieSuperJump 只在弹力泡里生效**（推的是 bouncy Rigidbody 的 vy），空手踩没反应是设计，不是 bug。
9. **RotatingHazard 在 pivot 和每块板上重复挂**，是复合运动；只要一圈扫描时别给子板挂。
10. **CharacterFillLight 的 cullingMask=Player 层(1<<6)**：玩家 renderer layer 不对就没有补光；Setup 菜单改的是 prefab，跨关卡生效。

---

## 8. TODO（未查完，施工时确认）

- [ ] `WengjieLevelSetup.cs`（6619 行）只读了关键段；**菜单完整执行顺序**是按 priority 推断的，未逐条验证最终场景由哪一遍生成（部分菜单互相覆盖）。
- [ ] `UIRuntime.Gameplay.cs` 的 `ShowSubtitle/ShowNarration`、`SaveSnapshot`、`LoadScene` 细节未逐行读。
- [ ] `BubbleTideDirector` 后半段字段（StartDelay、失败判定等）未读完；`BubbleTideRenderFeature` 是否已装进 `Assets/Settings` 的 URP Renderer 未确认。
- [ ] `PAOSunshaft.shader` 当前是否仍被任何材质引用未确认。
- [ ] wengjie 全部 BoxCollider 尺寸未逐件导出（复刻用 Transform.localScale 即可，见 1.2）。
- [ ] Map0 现活场景（3 root：Directional Light / takoyaki / Map0_Refactor）是他人正在编辑的状态，本文件不作为 Map0 现状基线。
- [ ] 三种泡泡教学在 wengjie 无现成实例：建议直接参考 `shiliuye map`（StickySurface/TerrainBubble/BubblePopObstacle 用法最全）。

---

## 9. 参考文件索引

- 场景：`Assets/GameMain/Scenes/wengjie.unity`、`Assets/GameMain/Scenes/Map0.unity`
- 玩家/相机：`Assets/GameMain/Art/Models/takoyaki.prefab`、`Assets/GameMain/Scripts/Gameplay/PlayerController.cs`
- 关卡组件：`Gameplay/WengjieStoryNode.cs`、`Gameplay/WengjieSubtitleOverlay.cs`、`Gameplay/WengjieSuperJump.cs`、`Gameplay/RotatingHazard.cs`、`Gameplay/FallRespawn.cs`、`Gameplay/Underwater/UnderwaterCeiling.cs`、`Gameplay/BubbleTide/BubbleTideGoal.cs`、`Gameplay/BubbleTide/BubbleTideDirector.cs`
- 其它机关：`Gameplay/CollapsingFloor.cs`、`Gameplay/TerrainBubble.cs`、`Gameplay/StickySurface.cs`、`Gameplay/BubblePopObstacle.cs`、`Gameplay/WengjieTrap.cs`、`Gameplay/WengjieTideGate.cs`、`Gameplay/WengjieColumnRelease.cs`、`Gameplay/WengjieProximityBurst.cs`、`Gameplay/BouncyBubbleAxisMover.cs`
- 章节/UI：`UI/V2/UIConfig.cs`、`UI/V2/UIRuntime.Chapters.cs`、`UI/V2/ChapterGoalTrigger.cs`、`UI/V2/UIRunState.cs`、`UI/tiaozhuan.cs`、`Resources/PAOUIConfig.asset`
- 搭建工具：`Editor/WengjieLevelSetup.cs`（全部 wengjie 菜单）、`Editor/UnderwaterCeilingSetup.cs`、`Editor/PAOWaterCausticsProbe.cs`（Fix Water/Add Ground Caustics/Setup Character Fill Light/Add Wengjie Subtitle）、`Editor/BubbleTideSceneSetup.cs`、`Editor/PAOVisualInstaller.cs`
- 材质/Shader：`Art/Materials/M_UnderwaterCeiling.mat`、`Art/Materials/M_PAOCausticsScreen.mat`、`Art/Shaders/UnderwaterCeiling.shader`、`Art/Shaders/PAOCausticsScreen.shader`、`ThirdParty/Thalassophobia/PostFX/Shallow Water Profile URP.asset`
- 音频：`Assets/GameMain/Audio/WengjieVO/vo_0.wav … vo_12.wav`
- 设计文档：`Docs/LevelDesign/wengjie-detailed-spec.md`、`wengjie-build-log.md`、`wengjie-mechanics-matrix.md`、`wengjie-10min-plan.md`
