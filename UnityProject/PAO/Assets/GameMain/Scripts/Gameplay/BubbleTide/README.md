# 泡泡潮

一个**时间压力**机制：随着通关时间流逝，整个关卡会被泡泡从八个角落慢慢淹没。
潮水摸到终点时进入最后的限时倒计时；玩家可以反过来用三种泡泡对抗潮水。

---

## 三十秒上手

1. 打开 Unity，等编译完
2. 顶部 **工具** 菜单 → **PAO → 泡泡潮 → 安装到 URP Renderer**
3. 顶部 **工具** 菜单 → **PAO → 泡泡潮 → 一键搭建潮水**
   （会自动算出房间范围、摆好八个角、建好总控和终点）
4. 进 Play

看不到潮水就按 **工具 → PAO → 泡泡潮 → 检查安装状态**，它会逐项告诉你缺哪个。

调试快捷键（按住 Ctrl）：

| 键 | 作用 |
|---|---|
| `Ctrl` + `=` | 潮水快进 10 秒 |
| `Ctrl` + `-` | 潮水倒回 10 秒 |
| `Ctrl` + `0` | 直接跳到终点被淹那一刻 |

> 不用 1/2/3 是因为那三个键被泡泡类型切换占了。

---

## 它是什么，不是什么

**是**一张「每格第几秒被淹」的表，加上一次全屏光线步进。

**不是**几千个泡泡 GameObject。

这个区别决定了整个功能的性能上限，值得说清楚：

| | 真生成泡泡 | 本方案（场 + 光线步进） |
|---|---|---|
| 潮水大小对帧率的影响 | 线性恶化 | **零** |
| CPU 每帧 | 几十万次 Transform 写入 | 写**一个 float** |
| 内存 | 几百 MB | **150 KB**（一张 R8 3D 纹理） |
| Draw Call | 撑不住 | **恒定 1 个** |
| 物理 | 几千个 Rigidbody，PhysX 直接跪 | **零 Rigidbody、零 Collider** |

一个能淹掉 43×13×33 米房间的潮水，用 0.5 米的泡泡去填是几十万颗。
做成实体是没戏的 —— 所以这里从头到尾没打算生成它们。

---

## 架构

```
BubbleTideDirector          ← 场景里只放这一个。计时 + 烤制 + 胜负判定 + 反制接口
  ├─ BubbleTideField            体素场：多源 Dijkstra 算出「每格第几秒被淹」
  ├─ BubbleTideCellRenderer     把场导出成 3D 纹理，每帧推一个 float 给 shader
  ├─ BubbleTidePush             用场梯度把玩家推回去（零物理）
  └─ BubbleTideRenderFeature    URP 注入点
       └─ BubbleTidePass         两趟 Blit：光线步进 → 合成
            └─ BubbleTide.shader 在 3D 纹理里找潮水面并着色

BubbleTideAnchor ×8        八个角，各自声明速度倍率与出发延迟
BubbleTideGoal             终点标记，只声明「我是终点」
BubbleTideBridge           玩法与潮水之间唯一的桥（所有调用都判空）
```

关键设计：**终点只做标记、不负责任何逻辑**。原本的想法是「把脚本挂到终点上」，
但那样终点是谁、八个角在哪，就都成了对场景硬编码的依赖，关卡一改就得重配。
拆开之后，终点想换位置、想加第二个、想从单个角开始蔓延做教学关，都不用改代码。

---

## 速度是怎么算的

```
v_i = baseSpeed × speedMultiplier_i × (d_i / dMin)^alpha
```

`d_i` = 第 i 个角到终点的直线距离，`alpha` 就是 Inspector 上的 **距离指数**：

| alpha | 效果 |
|---|---|
| `0` | 八个角速度一样，最远的角最晚到（压迫感最弱） |
| **`0.7`** | **远角明显更快，但近角仍然先到 —— 推荐** |
| `1` | 八个角**同时**抵达终点（仪式感最强，但中段会突然一起到） |

**为什么默认不是 1**：让八路同时抵达，中段就没有「逐渐淹没」的过程了，
玩家体验到的是「前 40 秒没事，然后一瞬间全到」。`0.7` 兼顾两者。

另外注意速度是按**直线距离**算的，而实际路径会被障碍物拉长。所以某个角
明明离终点近、却隔着一堵墙，它实际会晚到 —— 这个「意外」是有趣的，
不用去修正，正好给关卡制造变化。

### 出发延迟是折算成速度的

`BubbleTideAnchor.StartDelay` 不是「晚点开始跑」，而是折算进速度里：

```
v' = d / (d/v + delay)
```

因为多源 Dijkstra 只跑一次就把八个角一起解决了 —— 这是它最大的好处。
为了延迟去给每个角单独跑一遍，就跑八次了。对固定速度场来说两者精确等价。

---

## 反制：三条通路

这是让「泡泡潮」从一根会动的血条变成可玩系统的关键。没有它们，玩家面对潮水
只能跑；有了它们，玩家能主动争取时间。

| 玩家动作 | 对潮水做了什么 | 代码位置 |
|---|---|---|
| 炸弹泡泡爆炸 | 在潮水里**炸出一个洞** —— 范围内潮水被往后推 `6` 秒 | `Bubble.cs` → `BubbleTideBridge.OnBombExploded` |
| 黏浮泡泡粘成墙 | 那片区域潮水**变慢** —— 玩家自己筑的堤坝 | `Bubble.cs` → `OnStickyBubbleAnchored` |
| 弹力泡泡被地形吸收 | 等于从潮水里**抽走一份容量**，整条潮水一起慢 | `TerrainBubble.cs` → `OnBubbleConsumedByTerrain` |

三条都在 `BubbleTideDirector` 上有对应的可调参数（反制那一栏），
也都能单独关掉。想加第四条通路就在 `BubbleTideBridge` 里加一个方法。

> 局部修改**不会重跑 Dijkstra** —— 改的是「到达时间」本身，而不是速度场。
> 对「炸个洞」这种效果足够了，而且便宜几个数量级。

---

## 调参

分三处，各管一摊：

### 1. 玩法数值 → 场景里的 `BubbleTideDirector`

| 参数 | 作用 | 建议 |
|---|---|---|
| `格距` | 体素大小 | `0.5` 约 15 万格；`1.0` 只 1.9 万格但边缘明显变方 |
| `基准速度` | 潮水基准速度（米/秒） | `1.0` → 43 米的房间约 40 秒被淹穿 |
| `距离指数` | 见上一节 | `0.7` |
| `开局额外宽限` | 进 Play 后潮水先不动的秒数 | `3`，给玩家认路 |
| `时间流速` | 整体快慢，调试用 | `1`；试玩调参时开到 `3` 省时间 |

### 2. 观感 → URP Renderer 资产里的 **泡泡潮** Feature

| 参数 | 作用 | 建议 |
|---|---|---|
| `resolutionDivisor` | 分辨率除数，2 = 半分辨率 | 移动端保持 2，潮水边缘很软看不出差别 |
| `maxSteps` | **性能的主要旋钮** | `96` 在 0.5 米格距下能走 48 米 |
| `stepScale` | 步长相对格距的倍率 | `0.8` |
| `opacity` | 不透明度 | `0.85` |
| `rimIntensity` / `rimPower` | 菲涅尔亮边 | `1.5` / `2.4` |
| `noiseStrength` | 泡泡颗粒感 | `0.55`，调到 0 就是光滑塑料 |
| `useDepthOcclusion` | 用场景深度遮挡 | 需要 URP 资产里勾 **Depth Texture**，没勾会自动失效 |

### 3. 每颗泡泡 → `SDFBubble`（那套是独立系统，和潮水无关）

---

## 几个必须知道的坑

### 1. 深度纹理是关着的

三个 URP 资产的 `m_RequireDepthTexture` 都是 0。所以：

- **光线方向不能从深度反推。** 项目里那套泡泡 SDF 是从深度反推世界坐标的，
  泡泡潮这里**故意没这么做** —— 拿不到深度时整条射线都会是错的，
  表现就是潮水完全看不见，而且**不报任何错**，极难查。
  方向改用相机矩阵现算，不依赖任何深度纹理，永远正确。
- 不勾 Depth Texture 时**遮挡会失效**：潮水会透过墙被看到。
  画面照常，只是没有遮挡。想开就在 `URP-HighFidelity.asset` 里勾上。

### 2. 光线步进要处理「相机已经在潮水里」

玩家被淹之后，射线起点就已经落在淹没区里了。只找「没淹 → 被淹」的跨越点的话，
**画面里的潮水会整个消失**。所以 `MarchTide` 有一个 `invert` 参数，
相机在水里时改成找「被淹 → 没淹」，看到的是内壁。

### 3. 斜向扩散必须检查两条 L 形路径

否则泡泡会从墙与墙的夹角**斜着钻过去** —— 视觉上就是潮水渗进了本该封闭的角落。

### 4. 八个角通常正好嵌在墙里

位置选得再准，角也会落在墙角或地板内部。`FindNearestAirCell` 会在种子附近
螺旋搜索最近的空格，找不到就放弃这个角并**打印警告**（而不是静默失效）。
八个角全无效时会报明确错误。

### 5. 玩家出生点原本和终点重合

场景里 `pao`（终点）在 `(-0.73, 0.56, -2.64)`，而玩家出生点在 `(-0.72, 0.37, -2.37)`
—— **是同一点**。一进 Play 就直接通关，这个功能根本没法试。

所以「一键搭建潮水」把终点放在**离玩家出生点最远的那个角**。
如果场景里已经有终点且离出生点不到 8 米，工具会在对话框里警告你。

### 6. 别把 Render Feature 装进 Packages

和泡泡 SDF 那套一样：安装器只处理 `Assets/` 下的 Renderer。URP 包里自带一个
隐藏的 `UniversalRendererData.asset`，改它 Unity 会警告
「asset(s) located in immutable packages were unexpectedly altered」，
而且包一升级修改就丢。装错了用 **从 URP Renderer 卸载** 清干净。

---

## 和现有系统的关系

潮水是**额外的一层压力**，不改变原有玩法：

- 泡泡的物理、粘连、载人、爆炸逻辑一行没动，只在三个时机加了钩子
- 三个钩子全部通过 `BubbleTideBridge`，**每个调用都判空** ——
  场景里没有潮水时全部静默跳过，玩法一点不受影响
- 所以可以做「有潮水的关」和「没潮水的关」，同一套玩法代码

玩家被推挤走的是 `CharacterController.Move` 而不是直接改 `transform`——
后者会绕过碰撞处理，把玩家塞进墙里。走 Move 的话贴墙被推会自然沿墙滑动。

玩家在泡泡里（`BubbleRideInteractor` 把 `CharacterController` 关掉时）
推挤会自动让位给泡泡的载具逻辑，两边不打架。

---

## 文件

| 文件 | 作用 |
|---|---|
| `Scripts/Gameplay/BubbleTide/BubbleTideDirector.cs` | 总控：计时、烤制、胜负、反制接口 |
| `Scripts/Gameplay/BubbleTide/BubbleTideField.cs` | 体素场：多源 Dijkstra、查询、局部修改 |
| `Scripts/Gameplay/BubbleTide/BubbleTideBridge.cs` | 玩法 ↔ 潮水的桥（静态方法，全部判空） |
| `Scripts/Gameplay/BubbleTide/BubbleTideAnchor.cs` | 潮水源（一个角） |
| `Scripts/Gameplay/BubbleTide/BubbleTideGoal.cs` | 终点标记 |
| `Scripts/Gameplay/BubbleTide/BubbleTideCellRenderer.cs` | 导出 3D 纹理，每帧推 float |
| `Scripts/Gameplay/BubbleTide/BubbleTidePush.cs` | 用场梯度推玩家 |
| `Scripts/Gameplay/BubbleTide/BubbleTideRenderFeature.cs` | URP RendererFeature + 观感参数 |
| `Scripts/Gameplay/BubbleTide/BubbleTidePass.cs` | 两趟 Blit |
| `Scripts/Editor/BubbleTideSceneSetup.cs` | 一键搭建 / 安装 / 卸载 / 状态检查 |
| `Art/Shaders/BubbleTide.shader` | pass 0 光线步进 + pass 1 合成 |

---

## 下一步可以做的

按「投入产出比」排序：

1. **把 GPU 泡泡换成真泡泡的外观。** 现在潮水面是一层带菲涅尔和薄膜干涉的膜，
   读起来是「泡泡在漫上来」，但看不到一颗颗的泡泡。想更明显就在着色函数里
   叠一层球面法线扰动 —— 成本很低，观感提升很大。
2. **给潮水加音效。** 泡泡的低频涌动声是这类压迫感最强的表达，比画面还重要。
3. **`TerrainBubble` 填满时炸开潮水一个口子。** 现在填满只是消失了，
   可以让它变成一次大范围的「退潮」。
4. **多终点。** 代码里 `m_Goals` 已经是数组了，判定也按数组写的，
   只是宽限时间取最严格的那个。想做「三个终点都要保住」只要改 UI 提示。
5. **HUD。** 现在左上角是一行 IMGUI 调试文字（`m_DrawHud`）。
   正式 UI 应该做一个潮位条，位置已经在 `Progress` / `FloodFillProgress` /
   `TimeUntilGoalFlooded` 上备好了。
