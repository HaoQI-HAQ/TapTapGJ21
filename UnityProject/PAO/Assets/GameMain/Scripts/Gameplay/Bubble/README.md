# 泡泡 SDF 材质

全屏 **SDF 光线步进（raymarching）** 泡泡：泡泡不是网格，而是着色器里现场求出的
「有符号距离场」。好处是重叠的泡泡会像肥皂泡一样**自然融合 / 粘连**，
不用物理接触也不需要任何美术资源。

```
SDFBubble（组件，挂在物体上）
  └─ Refresh() 把 position / 半径 / 颜色 / 壳厚 / 融合强度 打包成 BubblePayload
     └─ BubbleSDFManager（注册表 + 视锥剔除 + 复用数组，零 GC）
        └─ BubbleSDFRenderFeature（注入 URP）
           └─ BubbleSDFPass
              ├─ 上传 ComputeBuffer(_ShapeDataBuffer)
              ├─ Blit 相机颜色 → _SourceTexture（备份渲染前的画面）
              ├─ Blit → 半分辨率 HDR 中间图，shader pass 0 做 SDF 步进
              └─ Blit → 相机颜色，shader pass 1 合成
```

命名空间：`PAO.BubbleFX`。**不能**用 `PAO.Bubble` —— `PAO` 里已经有一个
`Bubble` 类，命名空间与类同名会让编译器解析不到类型（会报
`CS0101 命名空间"PAO"已经包含"Bubble"的定义`）。

---

## 安装

1. 打开 Unity，等编译完
2. 顶部 **工具** 菜单 → **PAO → 泡泡 SDF → 安装到 URP Renderer**
3. 顶部 **对象** 菜单 → **PAO → 泡泡 SDF → 生成测试泡泡群**
   （"对象"菜单需要先选中一个 GameObject 才可点，先在 Hierarchy 建个空对象）
4. 进 Play，或直接在 Scene 视图看

> Scene 视图右上角 **效果（Effect）→ 勾上 动画材质（Animated Materials）**，
> 否则看不到表面抖动动画，会以为 shader 没生效。

清掉测试泡泡：**对象 → PAO → 泡泡 SDF → 清除测试泡泡群**。
装错了或想重装：**工具 → PAO → 泡泡 SDF → 从 URP Renderer 卸载**。

---

## 接到现有的泡泡玩法上

`BubbleLauncher` 生成的物理泡泡（Rigidbody + Collider + `Bubble` 组件）
只管物理，视觉交给 `SDFBubble`。在 `BubbleLauncher.CreateBubble` 里
`return bubble;` 之前加：

```csharp
// 挂上 SDF 视觉。物理照旧由 Rigidbody/Collider 管，两者互不干扰。
var visual = bubble.AddComponent<PAO.BubbleFX.SDFBubble>();
visual.Radius = 0.5f;                 // 半径（米）
visual.Color  = settings.color;       // 沿用你调好的类型配色
visual.FuseStrength = 0.22f;          // 融合强度（米）

// 关掉物理球的外壳，只留 SDF 画面。
// 注意：Collider 千万别 disable，否则 Bubble 的 OnCollisionEnter 不触发，
// 浮粘泡的 FixedJoint 粘连逻辑会直接失效。
var shell = bubble.GetComponent<MeshRenderer>();
if (shell != null) shell.enabled = false;
```

`SDFBubble.Radius` 会乘上 `transform.lossyScale`，所以 `BubbleLauncher`
蓄力时改 `localScale` 让泡泡变大，SDF 视觉会自动跟着变大，不用额外同步。

---

## 调参

分两处：

- **每颗泡泡**：`SDFBubble` 组件面板 —— 半径、壳厚、颜色、融合强度、抖动
- **全局**：URP Renderer 资产里的 **Bubble SDF Render Feature** 面板

全局面板里最值得先动的几个：

| 参数 | 作用 | 建议 |
|------|------|------|
| `resolutionDivisor` | SDF 分辨率除数，2 = 半分辨率 | 移动端保持 2，边缘很软看不出差别 |
| `iridescenceIntensity` | 薄膜干涉彩虹强度 | 1 ~ 2 |
| `iridescenceScale` | 彩虹密集度，越大色环越多 | 1.5 ~ 3 |
| `specularPower` | 高光锐度 | 96 左右；调大高光点更小更亮 |
| `opacity` | 不透明度 | 0.6 最像肥皂泡，调高变实心玻璃球 |
| `rimPower` | 亮边聚拢程度 | 2.2；调大亮边更窄更贴边 |
| `globalFuse` | 全局额外融合强度 | 0.05 |

### 壳厚（Thickness）

`Thickness` 是**占半径的比例**，0.02 ~ 0.15 比较像真泡泡。
它同时决定光线步进的精细度：壳越薄，shader 里为了不漏检会用更小的步长，
所以**壳厚是观感与性能的主要权衡点**。调到 0.01 以下容易出噪点。

---

## 几个必须知道的坑

### 1. 结构体：必须是纯 float 的 struct，80 字节 / 20 个字段

`BubblePayload`（C#）与 shader 里的 `struct ShapeData`（HLSL）
必须都是 **80 字节 = 20 个 float**，字段顺序完全一致：

| 偏移 | C# / HLSL | 内容 | 个数 |
|------|-----------|------|------|
| 0  | `posType` | position.xyz, shapeType | 4 |
| 16 | `sizeOp`  | size.xyz（直径）, operation | 4 |
| 32 | `color`   | rgba | 4 |
| 48 | 散装 float | shellRatio, fuseStrength, distortion, distortionFreq | 4 |
| 64 | 散装 float | blendStrength, numChildren, phase, pad0 | 4 |

HLSL 那边多一个 `pad1`，凑到 20 个 float。

**这个结构踩过三个坑，都是同一个根因：数错了字段个数。**

最初的定义里有三个「位置/尺寸/颜色」的组，我把它记成「3 个 + 3 个 + 4 个 + 4 个 + 4 个 = 18 个 float = 72 字节」，
**但尺寸组其实是 4 个**（`sizeX/sizeY/sizeZ/operation`），
颜色组也是 4 个，所以真值是 **20 个 float = 80 字节**。

由此连锁出了三次失败：

1. 我用 `sizeof(float) * 18` 当 stride 去建 `ComputeBuffer`，和实际的 80 对不上
   → `SetData` 抛 `One of C# data stride (80 bytes) and Buffer stride (72 bytes)
   should be multiple of other`。
   我误判成「Mono 没兑现 `Pack=4` 给 float3 补了填充」，
   其实**布局一直是对的，是我写死的常量错了**。
2. 顺着错误判断，我把「float3 + float」改成「float4 + 散装 float」并加了
   `[StructLayout]` 说明 —— 布局改动是安全的（纯 float 更好），但常量还是 72。
3. 又为了绕开 `m_Enabled` 和 `MonoBehaviour` 基类撞名，把 struct 改成了 class
   → `SetData` 抛 `must be blittable ... is not of value type`。
   **`ComputeBuffer.SetData` 只接受值类型数组**，class 永远不行。

现在的防线：`BubblePayload.ValidateLayout()` 在建 `ComputeBuffer` 之前
**先数字段个数**（这条最可靠），再比 `Marshal.SizeOf`。不对就报人话并跳过建缓冲区。

所以改字段时必须同步四处：

1. HLSL 里的 `struct ShapeData`
2. `BubblePayload.Stride`（80）
3. `BubblePayload.FieldCount`（20）
4. `SDFBubble.Refresh()` 里的赋值

**并且：不要往这个结构里加 `Vector3` / `float3` / `bool` / `int`**，
也不要让 IDE 按字母序重排字段（顺序必须跟 HLSL 一致）。
字段名也不要加 `m_` 前缀 —— struct 一旦被 Unity 序列化，
`m_XXX` 容易和 `MonoBehaviour` 基类撞，报
"The same field name is serialized multiple times in the class or its parent class"。
本结构也不加 `[System.Serializable]`，它是纯 GPU 裸数据。

菜单 **工具 → PAO → 泡泡 SDF → 检查安装状态** 会打印 stride 供对照。

### 2. 安装器不会碰 Packages 里的资产

URP 包里自带一个隐藏的 `UniversalRendererData.asset`。
早期的安装器用 `AssetDatabase.FindAssets` 无差别遍历，把它也改了，
Unity 会警告「The following asset(s) located in immutable packages were
unexpectedly altered」，而且包一升级修改就丢。

现在安装只处理 `Assets/` 下的 Renderer。如果之前误装过，
用 **工具 → PAO → 泡泡 SDF → 从 URP Renderer 卸载** 清干净再重装。

### 3. 抖动（Distortion）的渐隐半径不能调小

`BubbleShellDistance` 里抖动项乘了一个 `saturate(1 - |dist| / (radius * 1.1))`。
这个渐隐是**必须**的：抖动量最大 `radius/4`，会改变 SDF 的零等值面。
如果渐隐半径比 `|dist|` 的实际范围还小，抖动会在壳体内部被截断成台阶，
而那些台阶会被光线步进当成真表面 —— 泡泡里面会冒出脏壳。
`radius * 1.1` 是覆盖壳体厚度所需的最小值，别调小。

### 4. 薄壳步进

球壳的 SDF 在壳外远处等于到外壳的距离，可以大步走；
一旦靠近壳面就必须把步长压到远小于壳厚，否则光线会直接穿壳而过（漏检）。
`Raymarch` 里用「到最近那颗泡泡球面的解析距离」判断，
贴壳时切到 `minThickness * 0.4` 的保守步长。

### 5. `_SourceTexture` 要手动绑

`Blitter.BlitCameraTexture` 不会把「它自己刚写入的那张图」绑给下一趟要用的
`_MainTex`，所以 `BubbleSDFPass` 里手动 `SetGlobalTexture`。
（参考实现里也是这么干的，注释写着「不知道为啥不会将图像自动传进去」。）

### 6. 深度图是关着的

三个 URP 资产的 `m_RequireDepthTexture` 都是 0。shader 用
`_CameraDepthTexture` 只为了拿光线起点，拿不到时会退回远平面重建，
**画面照常**，只是泡泡不会被前面的实体遮挡。想开遮挡就在
`URP-HighFidelity.asset` 里勾 Depth Texture。

---

## 文件

| 文件 | 作用 |
|------|------|
| `Art/Shaders/BubbleSDF.shader` | pass 0 光线步进 + pass 1 合成 |
| `Art/Shaders/BubbleSDFInclude.hlsl` | SDF 图元、smin/smax/ssub、调色板 |
| `Scripts/Gameplay/Bubble/BubblePayload.cs` | GPU 数据结构，80 字节 / 20 字段 |
| `Scripts/Gameplay/Bubble/BubbleSDFManager.cs` | 注册表 + 视锥剔除 |
| `Scripts/Gameplay/Bubble/SDFBubble.cs` | 挂在泡泡上的组件 |
| `Scripts/Gameplay/Bubble/BubbleSDFRenderFeature.cs` | URP RendererFeature + 全局调参 |
| `Scripts/Gameplay/Bubble/BubbleSDFPass.cs` | 每帧的 Blit 编排 |
| `Scripts/Editor/BubbleSDFInstaller.cs` | 安装 / 卸载 / 状态菜单 |
| `Scripts/Editor/BubbleSDFSceneTools.cs` | 生成 / 清除测试泡泡群 |
| `Scripts/Editor/BubbleSDFVerify.cs` | 命令行自检（回归用） |

### 命令行自检

```
Unity.exe -batchmode -nographics -projectPath <工程路径> ^
  -executeMethod PAO.BubbleFX.EditorTools.BubbleSDFVerify.Run -quit
```

退出码 0 = 全过。检查项：shader 能否编译、pass 数、结构体字段数 / 大小、Renderer 装配。
