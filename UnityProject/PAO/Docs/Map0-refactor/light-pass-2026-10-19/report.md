# Map0 光感调整交付报告（2026-10-19 00:45）

> 场景：`Assets/GameMain/Scenes/Map0.unity`
> 新增工具：`Assets/GameMain/Scripts/Editor/Map0LightingPass.cs`
> 菜单：`Tools/PAO/关卡白盒/Map0 教程重构 · 光感调整`（priority 63）
> 改前快照：`Docs/Map0-refactor/light-pass-2026-10-19/Map0_before_light_20261019_002720.unity`
> 对比图：`compare/01…12.png`（左右并排）、`compare-sheet.png`（6 个关键视角）、`stats.csv`

---

## 一、一句话

Map0 的「发灰、发平、白成一片」不是灯不够亮，而是**后处理整条链路是断的**：`Map0_WhitePostFX` 的 Volume 没有任何 Profile，`Map0_WhiteProfile.asset` 里 4 个组件全是空引用。
本轮把 Profile 真正建出来（ACES + Bloom + Vignette + ColorAdjustments + WhiteBalance），重调环境光/雾/主补光/彩色点光，并把这套参数回写进生成器，重建也不会再丢。

## 二、根因（改前诊断）

| 项 | 改前 | 问题 |
|---|---|---|
| `Map0_TutorialAtmosphere/Map0_WhitePostFX` Volume | `sharedProfile = null` | 完全没有后处理 |
| `Map0_WhiteProfile.asset` | `components: [fileID:0] ×4` | 生成器只 `profile.Add<T>()`，没有 `AssetDatabase.AddObjectToAsset()`，保存后组件丢失 |
| 结果 | 无 ACES / 无 Bloom / 无暗角 / 无颜色分级 | HDR 高光直接裁白：改前 12 张里 6 张「接近纯白」像素占比 20–60% |
| 环境光 | Flat `(0.90,0.92,0.95)` × `1.15` | 环境光过高，方向光/阴影被压平 |
| 雾 | Exp2 `(0.94,0.95,0.97)` density `0.008` | 偏白偏厚，远处糊成一片 |
| 彩色点光 | 8 盏 intensity 8–26 / range 32–70 | 被高环境光冲淡，「简单→庞大→震撼」的彩色渐变看不出来 |

## 三、改了什么

### 1. 后处理（真正的重点）

`Map0_WhiteProfile.asset` 现在是**有真实子资产**的 Volume Profile：

| 效果 | 参数 |
|---|---|
| Tonemapping | ACES |
| ColorAdjustments | postExposure `+0.22`，contrast `+12`，saturation `+8` |
| WhiteBalance | temperature `+5`，tint `+2` |
| Bloom | threshold `1.05`，intensity `0.55`，scatter `0.70`，tint 暖白 |
| Vignette | intensity `0.26`，smoothness `0.45` |

Volume 重新挂回 `Map0_WhiteFX`（global / priority 10 / weight 1）。

### 2. RenderSettings / 相机

| 项 | 改前 | 改后 |
|---|---|---|
| Ambient | Flat `(0.90,0.92,0.95)` × `1.15` | Flat `(0.78,0.82,0.90)` × `1.10` |
| Fog | Exp2 `(0.94,0.95,0.97)` × `0.008` | Exp2 `(0.90,0.93,0.97)` × `0.0055` |
| Camera bg | `(0.96,0.965,0.975)` | `(0.93,0.95,0.98)` |
| Skybox | 无 | 无 |

### 3. 灯光

| 灯 | 改前 | 改后 |
|---|---|---|
| `Directional Light` | intensity `1.0`，色温偏黄，euler `(50,330,0)` | intensity `1.8`，暖白 `(1,0.97,0.93)`，euler `(52,-30,0)`，Soft Shadow 强度 `0.85` |
| `Tutorial_FillLight` | intensity `0.45` | intensity `0.40`，冷色 `(0.72,0.84,1)`，无阴影 |
| 8 盏 `ColorProgression/ColorLight` | 8/10/12/14/16/18/22/26 | 5/6/7/8/9.5/11/13.5/16（按 z 渐强，颜色与 range 不动） |

### 4. 生成器回写（防止重建回退）

`Map0TutorialSetup.cs`：

- `BuildAtmosphere()` 里 Volume 改成 `volume.sharedProfile = Map0LightingPass.EnsureProfile();`
- `BuildAtmosphere()` 结尾调用 `Map0LightingPass.ApplySceneLighting();`
- 旧的 `EnsureWhiteProfile()` 改成委托 `Map0LightingPass.EnsureProfile()`（旧实现会再次丢组件，已修）

也就是说：以后点「Map0 教程重构（第一章 诞生）」重建，光感也是这一套，不会退回发灰版。

## 四、验证

| 项 | 结果 |
|---|---|
| 编译 | `Map0LightingPass.cs` + `Map0TutorialSetup.cs`：0 error / 0 warning |
| Profile | 5 个效果都写成子资产；Volume `sharedProfile` 指向 `Map0_WhiteProfile`（guid `2d833c80…`） |
| 场景保存 | `Is Dirty=false`，Map0 已保存 |
| 12 张截图 | `tutorial-shots/` 已更新为改后版本；左右对比见 `compare/` |
| 画面统计（12 张均值） | 「接近纯白」像素占比：改前 27.9% → 改后 8.4%；平均亮度 0.665 → 0.478；饱和度均值 0.158 → 0.218 |
| 局部最明显 | 01 出生点：裁白 38.1% → 8.6%；07 爬升：57.2% → 15.0%；12 俯瞰：56.0% → 3.2% |

> 说明：局部亮度降得多，是 ACES + 降环境光的正常结果——白墙仍是白的（有受光面/阴影面之分），不是整体压暗；如果要更亮，见「五、调参」。

## 五、怎么调 / 怎么回退

### 调参（改一个地方就够）

`Assets/GameMain/Scripts/Editor/Map0LightingPass.cs` 顶部是全部参数，每组都有中文注释：

- 想让整体更亮：`kAmbientIntensity`、`kPostExposure` 加一点；
- 想让阴影更冷/更暖：`kAmbientColor`、`kFillColor`；
- 想更「电影感」：`kContrast`、`kVignetteIntensity` 加大；想更干净：减小；
- 想彩色更明显：`kColoredLightIntensity` 整体加大，或降低 `kAmbientIntensity`。

改完在 Unity 里执行菜单 `Tools/PAO/关卡白盒/Map0 教程重构 · 光感调整`，再执行 `Map0 教程重构 · 只截图` 即可重拍对比。

### 回退

```powershell
Copy-Item Docs\Map0-refactor\light-pass-2026-10-19\Map0_before_light_20261019_002720.unity Assets\GameMain\Scenes\Map0.unity -Force
```

（该快照包含你这次对 PinkWall 重复件的删除和 BombSeal / Sticky_Mark 的缩放，是「你当前修改」的完整状态。）

## 六、已知限制

1. 本轮只调光，不改场景内容/材质/摆放；截图里那些大面积半透明泡泡、深色背景是当前 Map0 的几何与材质本身（不是光造成的），若觉得黑背景太黑，需要另行补「背景/远景」几何或调材质。
2. 目前只在编辑模式用截图相机验证了后处理生效；没有重新跑完整 Play 试玩（上一轮 Play 是你手动在测，没打扰）。建议你再进 Play 看一眼实际手感。
3. 渐变/彩色光按 z 排布只覆盖 `ColorProgression` 下的 8 盏；如果后续在场景里手动加灯，需要重跑菜单才会统一压强度。
