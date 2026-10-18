# wengjie · 焦散（焦线）落地记录

> 对应反馈：「少了焦线」。2026-10-18 修复。

## 之前为什么一条焦散都没有

1. **头顶水面没有焦散**：wengjie 原来的水面是 8 块 `Water Surface` 平面，
   材质是 `Universal Render Pipeline/Simple Lit`（半透明纯色）。
   SimpleLit 不吃焦散贴图，所以从水下往上看只有一片平的蓝板。
2. **地面焦散不生效**：之前用的是 Thalassophobia 的 `Caustics` 预制体
   （**方向光 + cookie**）。URP 只给主方向光渲染 cookie，
   附加方向光 cookie 一律不渲染（Unity 会警告
   "Additional Directional light ... cookie will not be visible"）。
   所以不管怎么调 Mode / Cookie Size，地面上都不会出现焦散。

## 现在的做法

### ① 头顶水面：换成项目自带的焦散水面（Map4 同款）

- 用 `Tools/PAO/水下/为当前场景生成水面顶盖`
  （`PAO/UnderwaterCeiling` shader + `M_UnderwaterCeiling.mat`）。
- 这个 shader 本身就用 **两层滚动 Voronoi 细胞边界**画亮线，
  从下往上看就是「水面的焦散亮线 + 天窗透光 + 远处收敛」，和 Map4 的水面是同一套。
- wengjie 里生成的对象名：`Water_Ceiling`（`UnderwaterCeiling` 组件，
  水面高度由关卡包围盒自动算：`最高点 + 12 米`）。
- 同时删掉了 8 块 SimpleLit 平面和那个不生效的 `Caustics` 方向光。

### ② 地面/墙面/道具：全屏焦散叠加

- Shader：`Assets/GameMain/Art/Shaders/PAOCausticsScreen.shader`
- 材质：`Assets/GameMain/Art/Materials/M_PAOCausticsScreen.mat`
- 场景对象：`takoyaki/CameraPivot/Main Camera/PAO_CausticsScreen`
  （相机前方 1.5 米的全屏 Quad，透明队列 + 加法混合 + ZTest Always）

原理：

```
_CameraDepthTexture → 反投影出每个像素的世界坐标 → 用世界 XZ 画 Voronoi 亮线
→ 乘「水面以下深度」遮罩 + 距离淡出 → 加法叠到画面上
```

因为用的是屏幕深度，**地面、台阶、墙、道具、建筑全都会自动有焦散**，
不用一块块摆面片 —— 即「铺到全部场景」。

## 调参入口

| 想要的效果 | 改哪里 |
|---|---|
| 地面亮线太亮/太暗 | `M_PAOCausticsScreen.mat` 的 `_Intensity`（默认 0.28） |
| 地面亮线太密/太疏 | `_Scale`（细胞/米，默认 0.55；越大越密） |
| 地面亮线太粗 | `_LineWidth`（默认 0.08） |
| 地面亮线流动速度 | `_Speed`（默认 2） |
| 水面亮线太亮/太疏/太粗 | `M_UnderwaterCeiling.mat` 的 `_RippleStrength` / `_RippleScale` / `_RippleWidth` |
| 水面亮线看得到的距离 | `_RippleFadeStart` / `_RippleFadeEnd`、`_FadeStart` / `_FadeEnd` |
| 水面高度变了 | `PAO_CausticsScreen` 材质的 `_WaterY` 要跟着改（默认 85.06） |

## 一键工具（`PAOWaterCausticsProbe.cs`）

- `Tools/PAO/Probe/Fix All Caustics` —— 重做 wengjie 水面 + 地面焦散（幂等，可重复跑）
- `Tools/PAO/Probe/Capture Views` —— 从主相机 / 抬头 / 地面 / 俯视各拍一张到 `Docs/_caustics/`
- `Tools/PAO/Probe/Scene Report` —— 打印水面对象、材质、关卡包围盒
- `Tools/PAO/Probe/Big Renderers` —— 打印尺寸 > 30 米的大物体（排查背景/天空）

## 已知限制

- 目前只接到 **wengjie**。其他关卡要做，只要在对应场景跑一次
  `Fix All Caustics`（水面顶盖会自动量场景尺寸；地面焦散会自动跟随主相机）。
- `PAO_CausticsScreen` 是挂在玩家主相机下的场景对象，换相机 / 换相机构造时要一起挪。
- UI V2 在「直接从编辑器 Play 关卡」时仍会刷
  `UISettings.Current` 空引用（`UIRuntime.Gameplay.cs:253`），与焦散无关，是上一轮 UI 工作的遗留。

---

## 追加（2026-10-18 下午）：丁达尔光 + 水母补光

### 丁达尔光（Sunshafts）

- 旧问题：Thalassophobia 的光柱粒子用的是 URP `ParticlesUnlit` + **软粒子（Soft Particles）**，
  靠场景深度淡出；头顶水面换成不透明顶盖以后，光柱被深度缓冲挡掉/淡没
  —— 看起来就是「丁达尔光没有了」。
- 修复：新写 `Assets/GameMain/Art/Shaders/PAOSunshaft.shader`
  （`Sunshafts.mat` 改用它）：
  - `ZTest Always`：光柱永远画在最上层，水面顶盖挡不住（风格化处理）；
  - 沿公告板长边做软边渐变（无贴图也有两头虚的光柱）；
  - 近/远距离淡出；加法混合；
  - 不再使用软粒子，因此不会被深度淡出吃掉。
- 水面顶盖 `M_UnderwaterCeiling.mat` 的 `m_CustomRenderQueue` 改成 `2999`
  （透明段最前）+ shader 主 Pass `ZWrite Off`：水面在天空盒之后、所有透明光柱之前画，
  光柱就能盖在水面上；同时不写深度，不再参与遮挡透明粒子。
- 一键工具：`Tools/PAO/Probe/Capture Views` 里新增 `04_sunshaft.png` 专门验证光柱。

### 水母补光（CharacterFillLight）

- 问题：场景只有一盏主方向光 + 很暗环境光，角色背光/侧光角度就是一团黑。
- 方案：
  - 新增层 **Player（layer 6）**，玩家预制体里所有 Renderer 都放进这层；
  - 玩家预制体 `Assets/GameMain/Art/Models/takoyaki.prefab` 的 `Main Camera` 下挂
    `CharacterFillLight`（点光，暖白 `(1,0.95,0.89)`，强度 6，范围 30，无阴影，`cullingMask` 只照 Player 层）；
  - 灯跟着相机走 ⇒ 永远从“玩家看得见的那一侧”补光，任何角度都不会全黑，
    而且只影响玩家，不把场景整体提亮。
- 一键工具：`Tools/PAO/Probe/Setup Character Fill Light`（幂等，会清掉旧灯再建一盏）。
- 排查：`Tools/PAO/Probe/Play State` —— Play 中点一下，打印 timeScale / UIRuntime 阻塞状态 /
  PlayerController+CharacterController 状态 / 光标锁，用来查“角色不能动”这类问题。

### 二次回调（按“把光调回去”）

- 光柱太亮/太大，压回接近之前观感：`Sunshafts.mat` 的 `_Intensity` → **0.45**，
  `_BaseColor` alpha → **0.22**（颜色 0.75/0.88/1）。
- 补光太抢：`CharacterFillLight` 强度 6 → **1.8**（范围 30 不变）。
- 截图：`Docs/_caustics/00_main_camera.png`、`04_sunshaft.png`。

### 三次回调（按“丁达尔光又没有了”）

- 真正原因：`Sunshafts (Area)` 粒子的 **startColor alpha 只有 0.525**，
  再乘材质 alpha，0.45/0.22 那档等效亮度约 0.1 —— 基本看不见。
- 还想再调：只改 `_Intensity` 即可，不用重开场景。

### 四次回调（按“我需要的丁达尔光只是场景的”）

- 关掉“全屏叠加”感：`PAOSunshaft.shader` 从 `ZTest Always` → **`ZTest LEqual`**。
  光柱现在会被地形/建筑/角色正常遮挡，只作为**场景里的光**存在，不再糊在整个画面上。
- 因为做了深度遮挡，可用的光柱变少，强度抬到 `_Intensity = 12`
  （`_BaseColor` = `(0.82,0.93,1,0.38)`）。
- 还想更淡：`_Intensity` 12 → 8；更明显：→ 16。
- 截图：`Docs/_caustics/00_main_camera.png`（光柱被平台正常遮挡）、`04_sunshaft.png`（水下的光柱）。

### 中文输入法（IME）导致不能移动

- 现象：Windows 输入法在中文状态时，WASD 会被输入法当成组合文本吞掉，角色不动。
- 修复：`PlayerController.Awake()` 里加 `Input.imeCompositionMode = IMECompositionMode.Off;`
  —— 游戏内关闭 IME 组合，按键直接进游戏。
- 如果编辑器 Game 视图里仍被吞键：这是 Unity 编辑器自身的 IME 行为，
  切一下输入法（Shift / Ctrl+Space 到英文）或直接看打包后的 exe 即可。

### 字幕（“我字幕呢？”）

- 根因有两个：
  1. 直接从编辑器 Play 关卡时 `UISettings.Current` 可能是 null，`RefreshNarration`
     第一行就空引用 → 字幕永远不显示（同时 `UpdateVisualGuidance` 也每帧报错）；
  2. V2 字幕的 `CanvasGroup` 在 UI 重建/切换后被销毁，缓存引用变“假 null”，
     `Present/HideImmediately` 里取 alpha 直接抛异常 → 整条字幕不显示。
- 修复（V2）：
  - `UIRuntime.Initialize` 里兜底 `UISettings.Initialize()`；
  - `RefreshNarration / UpdateTransientUI / ShowTutorial / Guidance / CollectMemory`
    都对 `UISettings.Current` 做空判断；
  - `UICinematicSubtitleMotion` 不再依赖 CanvasGroup（没有就只做逐字动画），
    所有 alpha 操作都带 null 判断；
  - `ShowNarration` 每次显示前检查字幕控件，缺了整组重建（`EnsureNarrationRoot`）。
- 兜底（wengjie）：新增场景字幕条 **`Wengjie Subtitle`**
  （独立 Canvas + 中文文字 + 描边，排序 1500，不依赖 UIRuntime），
  由 `WengjieSubtitleOverlay` 驱动；`WengjieStoryNode` 现在优先用它显示。
- 一键工具：`Tools/PAO/Probe/Add Wengjie Subtitle`（在 wengjie 场景里重建字幕条）、
  `Test Subtitle` / `Check Subtitle`（Play 中点一下验证显示状态）。
