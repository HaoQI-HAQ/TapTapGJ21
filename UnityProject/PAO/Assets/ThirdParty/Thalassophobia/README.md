# 水下效果 + 体积光（来自 Thalassophobia: Stylized Oceans）

从 `D:\untiy\CS_1` 那个参考工程导入了「水下处理 + 体积光」这一部分。
本文件记录**导入了什么、我改了什么、怎么用、怎么验证**。

---

## 一、导入位置

全部在 `Assets/ThirdParty/Thalassophobia/` 下，连 `.meta` 一起拷的（GUID 与原包一致，
所以和原包的材质/预制体引用关系完整保留）。

```
ThirdParty/Thalassophobia/
  Contents/Materials/Shaders/
      Stylized Caustics.shader            ← 原版（BiRP，参考用）
      Stylized Caustics URP.shader        ← ★ 我写的 URP 版（实际用这个）
      Stylized Water Surface.shader       ← BiRP-only，仅作参考，不要用
  Contents/Materials/Textures/
      foam-001.png            273 KB      ← ★ 焦散图案（唯一必需的贴图）
      SmallWaves.png            8 KB      ← 水面法线
      Caustics.asset                      ← 原版 CRT
      Caustics URP.asset                  ← ★ 我建的 CRT（指向 URP shader）
  Contents/Materials/
      Caustic.mat / Caustics.mat          ← 原版（指向 BiRP shader）
      Caustic URP.mat                     ← ★ 我建的（指向 URP shader，参数照搬）
      Water Fog Room.mat                  ← ★ 我建的房间尺度标定版
      Water Surface.mat
      Sunshafts.mat / Sunshafts 1.mat / Light Cone.mat
  Contents/Prefabs/
      Fog.prefab / Caustics.prefab / Water Surface.prefab
      Particles/Sunshafts.prefab
      Particles/Sunshafts (Area).prefab
      Particles/Ambient Particles.prefab
  Contents/Meshes/Light Cone.fbx
  PostFX/Deep Sea Profile URP.asset
  PostFX/Shallow Water Profile URP.asset
  Prefabs/UnderwaterRig.prefab            ← ★ 我搭的整套装置
```

**总体积约 600 KB**（不含 .meta）。

> 注意：`GameMain/Art/Materials/L4海草1/Stylized Fog.shader` 和 `Water Fog 1.mat`
> **是你之前就导入过的**（同一个包的植被部分），我没有重复导入、也没有移动它们。

---

## 二、我改了什么（3 个新文件 + 1 个新材质）

| 文件 | 为什么 |
|---|---|
| **`Stylized Caustics URP.shader`** | 原版是 BiRP 写的（`CGPROGRAM` + `UnityCustomRenderTexture.cginc`）。**数学逐行照搬**（两层滚动取 `min`），只把管线样板换成 URP 的 `Core.hlsl` |
| **`Caustic URP.mat`** | 指向上面的 URP shader。参数照搬原版：`_CausticScale: 2` / `_Intensity: 1.5` / `_Speed: 0.1` |
| **`Caustics URP.asset`** | CustomRenderTexture，1024²，每帧更新，指向 `Caustic URP.mat`，初始图用 `foam-001.png` |
| **`Water Fog Room.mat`** | ★ **按你们房间尺度重新标定的雾**，见下 |
| **`Prefabs/UnderwaterRig.prefab`** | 雾球 + 焦散灯 + 光柱方向灯，可直接拖进任何场景 |

### 为什么要重标定雾

原包 7 档预设是为**远洋大场景**（能看 300 米）调的。`Water Fog 1` 的距离阈值是：

```
_ColorStart1..4 = 2, 4, 6, 10 米     ← 6 米外就进入第 4 段
_FogIntensity = 2                     ← 不透明度 2，钳到 1 就饱和
```

**10 米外完全糊死。** 你们房间大约 20–30 米视距，直接用它等于全屏一坨蓝。

`Water Fog Room.mat` 只改了两个维度，**色调完全保留**：

| 参数 | 原版 | 房间版 |
|---|---|---|
| `_ColorStart1..4` | 2 / 4 / 6 / 10 米 | **4 / 9 / 16 / 28 米** |
| `_FogIntensity` | 2 | **1.15** |

其余（`_FogColor1~5` 五个颜色、`_LightColor`、`_LightFalloff`、`_FogDepthMultiplier`）原样不动。

**觉得还是太浓/太淡，就调这四个 `_ColorStart`** —— 建议开始值 ≈ 你能看穿的距离 ÷ 5。

---

## 三、怎么用（5 步）

1. **等 Unity 编译完**，确认 Console 里没有 shader 报错
2. 打开你要做水下的场景，把 **`Prefabs/UnderwaterRig.prefab`** 拖进去
3. 选中拖进来的 `UnderwaterRig`，**把位置设到房间中心**
4. 展开 `WaterFogVolume`，**把 Scale 设成能罩住房间**（默认 100 → 球直径 100 米）
5. 展开 `CausticsSpot`，**把 Y 设到地板上方 15~25 米**

### 三个物体的作用

| 子物体 | 作用 | 关键参数 |
|---|---|---|
| `WaterFogVolume` | 水下滤镜。**Scale 决定雾球多大** | Scale（默认 100） |
| `CausticsSpot` | 焦散。朝下的聚光灯，Cookie = 动画焦散图 | Y 高度、Range（默认 60）、Intensity（1.5） |
| `SunshaftDirection` | 光柱方向参考。**默认关着**，只是个方向指示 | 旋转 = 光柱方向 |

### 加体积光和浮尘

把这三个预制体**拖成 `UnderwaterRig` 的子物体**：

```
Contents/Prefabs/Particles/Sunshafts.prefab          ← 光柱（沿 SunshaftDirection 的方向摆）
Contents/Prefabs/Particles/Sunshafts (Area).prefab   ← 整片天光版，二选一
Contents/Prefabs/Particles/Ambient Particles.prefab  ← 浮尘
```

⚠️ **`Sunshafts.prefab` 的材质槽是空的**（原包就是这样，用的是默认粒子材质）。
拖进去之后手动把 **`Contents/Materials/Sunshafts.mat`** 赋给它的 ParticleSystemRenderer。
`Sunshafts (Area).prefab` 已经带好材质了。

### 后处理

`PostFX/Deep Sea Profile URP.asset`（深水：Bloom + Vignette + Tonemapping）和
`Shallow Water Profile URP.asset`（浅水：多一个 ColorAdjustments）。
直接拖到场景里的 Global Volume 上，或者替换它现有的 Profile。

---

## 四、验证清单

打开 Unity 后按这个顺序确认：

- [ ] **Console 没有 shader 编译错误**（重点看 `Stylized Fog` / `Stylized Caustics URP`）
- [ ] 材质没有变粉（粉色 = shader 编译失败）
- [ ] 进 Play，画面整体偏青蓝 → 雾生效
- [ ] 地板上有游动的亮网 → 焦散生效
- [ ] 抬头看远处，越远越糊 → 深度渐变生效

我做过静态校验（所有 GUID 引用都能解析、9 项定向检查全过），但
**shader 能否编译、观感对不对，只能在 Editor 里看**。

---

## 五、已知问题

| 问题 | 说明 |
|---|---|
| `Water Surface.mat` 有一个失效引用 | `_WaterNormals1` 指向的贴图**原包里就缺失**（源工程也找不到）。那个槽位 SimpleLit 不用（它读 `_BumpMap`，是好的），**无影响** |
| `Stylized Water Surface.shader` 不能用 | BiRP surface shader + `GrabPass`，URP 下不工作。想要带折射的水面必须重写；目前建议用 `Water Surface.prefab`（URP SimpleLit，能跑但不带折射和泡沫） |
| 雾球不跟随相机 | 原包也是这样（挂在 `Lighting` 空物体下）。只要球罩住整个场景就没问题。如果关卡很大，可以考虑挂到相机下 |
| 焦散灯阴影 | 我设成 `m_Type: 0`（无阴影），避免额外开销。想要遮挡可以改成软阴影 |

---

## 六、来源

`D:\untiy\CS_1\Assets\Distant Lands\Thalassophobia - Low Poly Oceans`
（Unity 2022.3.34f1c1 + URP 14.0.11，与本项目版本一致）
