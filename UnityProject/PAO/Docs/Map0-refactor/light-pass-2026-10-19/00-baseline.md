# Map0 光感调整 · 基线记录（2026-10-19 00:2x）

> 目的：先把「你当前改到的 Map0」固定下来，再做光感调整；调整前后的场景/截图都有独立快照，可随时回退。

## 一、当前 Map0 修改（相对 git HEAD `c11f1ca map0`）

场景：`Assets/GameMain/Scenes/Map0.unity`

| 改动 | 对象 | 内容 |
|---|---|---|
| 删除重复件 | `PinkWall_Mid_0_0 (2)` / `(4)` / `(6)` / `(8)` | 4 个与主墙重叠的粉色泡墙副本全部从场景删掉（各 129 行 YAML，共 523 行） |
| 调整尺寸 | `BombSeal` | localScale `3.4, 4.2, 1.4` → `5.7355, 7.0850, 2.3617` |
| 调整尺寸 | `Sticky_Mark_E` | localScale `27.268, 13.143, 0.5397` → `4.3413, 4.4978, 0.5397` |
| 调整尺寸 | `Sticky_Mark_W` | localScale `27.268, 13.143, 0.5397` → `4.3413, 4.4978, 0.5397` |

完整 diff：`Docs/Map0-refactor/light-pass-2026-10-19/Map0-vs-HEAD.diff`

## 二、快照

| 文件 | 说明 |
|---|---|
| `Map0_before_light_20261019_002720.unity` | 调整前场景整文件（SHA-256 `0c7a9b5d…f8e197`，与当时磁盘文件一致） |
| `before/` | 改动前的 12 张教程截图（2026-10-18 23:28 版，含历史旧名截图） |
| `before-current/` | 退出 Play 后、调光前重拍的 12 张 before 截图（2026-10-19 00:41，与当时场景一致） |

## 三、当前光感现状（诊断）

1. **后处理链路断了**：`Map0_Tutorial/Map0_TutorialAtmosphere/Map0_WhitePostFX` 的 `Volume.sharedProfile = null`；
   目标资产 `Assets/GameMain/Art/Profiles/Map0_WhiteProfile.asset` 里 4 个组件引用全是 `{fileID: 0}`（空）。
   生成器 `Map0TutorialSetup.EnsureWhiteProfile()` 只做了 `profile.Add<T>()`，没有 `AssetDatabase.AddObjectToAsset`，
   保存时组件丢失、Volume 也没有真正引用到 profile。
   → **Bloom / Vignette / ColorAdjustments / ACES Tonemapping 实际全部没有生效**，HDR 高光直接裁白，画面发灰、发平。
2. RenderSettings（当前值）：
   - Fog：ExponentialSquared，color `(0.94, 0.95, 0.97)`，density `0.008`（偏白偏厚）
   - Ambient：Flat，color `(0.90, 0.92, 0.95)`，intensity `1.15`（环境光过高，压掉明暗对比）
   - Skybox：无
3. 灯光（10 盏）：
   - 主光 `Directional Light`：intensity `1.0`，color `(1, 0.957, 0.839)`，旋转 `(50, 330, 0)`，Soft Shadows
   - 补光 `Tutorial_FillLight`：Directional，intensity `0.45`，color `(0.86, 0.92, 1)`，无阴影
   - 8 盏彩色点光 `ColorProgression/ColorLight`：intensity `8 / 10 / 12 / 14 / 16 / 18 / 22 / 26`，range `32 / 36 / 38 / 40 / 42 / 44 / 52 / 70`
4. 相机：`takoyaki/CameraPivot/Main Camera`，SolidColor `(0.96, 0.965, 0.975)`，`renderPostProcessing = true`（管道本身没问题，是 profile 丢了）。

## 四、这次光感调整的目标（v1 方向）

- **修好后处理**：ACES Tonemapping + Bloom + Vignette + ColorAdjustments 真正生效；
- **把白空间从「糊成一片白」拉回有层次的干净白**：降环境光、降雾、让主光/阴影重新有方向性；
- **彩色光的「简单 → 庞大 → 震撼」渐变保留但更可见**：环境光降下来后，8 盏彩光按 z 渐强；
- 保持新手教学的白色基调，不走 wengjie 的水下蓝。

## 五、回退方式

```powershell
Copy-Item Docs\Map0-refactor\light-pass-2026-10-19\Map0_before_light_20261019_002720.unity Assets\GameMain\Scenes\Map0.unity -Force
```

或直接 `git checkout -- Assets/GameMain/Scenes/Map0.unity`（会退回到 HEAD，不含上面「一」的 4 处手动修改）。
