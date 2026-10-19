# Map1「水域 02」环境渲染 · 粉色水下（v8：曝光收正）

> 场景：`Assets/GameMain/Scenes/Map1.unity`
> 工具：`Assets/GameMain/Scripts/Editor/Map1EnvironmentSetup.cs`
> 菜单：`Tools/PAO/关卡白盒/Map1 环境渲染 · 粉色水下（一键搭建）`（幂等）
> 截图：`Docs/Map1-env/{before,after,compare}/`、`compare-sheet.png`
> 更新时间：2026-10-19 10:3x

---

## 一、迭代过程（按你的反馈）

| 版本 | 你的反馈 | 处理 |
|---|---|---|
| v2 | 太夸张 | 降饱和、去洋红 |
| v4/v5 | 焦散太夸张 | 焦散 0.28 → 0.05 |
| v5 | 还是不够氛围 | 加厚雾/压暗/加对比（v6） |
| v6 | 画面太压抑 | 提亮雾和环境光（v7） |
| v7 | 曝光过了 | **收曝光（v8，当前）** |

## 二、v8 参数（当前）

| 项 | v7（过曝） | **v8（当前）** |
|---|---|---|
| 后处理曝光 | +0.55 | **+0.15** |
| 环境光 | `(0.66,0.56,0.62)` × 1.30 | **`(0.62,0.54,0.60)` × 1.05** |
| 雾 | `(0.80,0.66,0.72)` × 0.007 | **`(0.72,0.58,0.65)` × 0.008** |
| 主光 | `(1,0.94,0.88)` × 1.75 | **`(1,0.94,0.89)` × 1.45** |
| 补光 | ×0.50 | **×0.42** |
| 后处理 | contrast 8 / sat +4 / Bloom 0.85@0.90 | **contrast 10 / sat +3 / Bloom 0.60@1.00** |
| 暗角 | 0.16 | **0.18** |
| 水面 | 更亮 | **收暗一档** |
| 焦散 | 0.06 | 0.06（保持克制） |

效果：01_player 的接近纯白像素只有 **0.30%**，平均亮度 0.287，粉雾层次保留，不再白到发光。

## 三、环境包含什么

全部由 `Map1EnvironmentSetup` 生成在 `Map1_Environment` 根下：

- `Water_Ceiling`（UnderwaterCeiling 水体壳）Y = 45.25，HalfSize 290.8，Skirt 118.3
- `Map1_PinkPostFX` 全局 Volume：ACES + ColorAdjustments + WhiteBalance + Bloom + Vignette + ShadowsMidtonesHighlights
- 4 × `Sunshafts (Area)` 光柱（Map1 粉色材质副本 `M_Map1_Sunshafts.mat`）
- 4 × `Ambient Particles` 浮尘
- 4 盏点光：T1 中央珍珠 / 东终点 / 西口袋 / 出口
- `Map1_FillLight` 冷色补光
- Main Camera 前 `PAO_CausticsScreen`（Map1 材质副本，`_WaterY=45.25`，低强度焦散）
- 主光 `Directional Light`：暖白 ×1.45，软阴影 0.80

水面 / 焦散 / Sunshaft 材质、Profile 全部是 Map1 专用副本，不改其他关卡。

## 四、截图

- `after/01_player.png`：Game 视角（曝光收正后的主验收图）
- `after/05_overview.png`：俯瞰
- `after/09_high_forward.png` 等：高机位
- `compare/01_player.png`、`compare/05_overview.png`、`compare/06_look_up_water.png`：改前/改后同机位对比
- `compare-sheet.png`：对比汇总

## 五、验证

| 项 | 结果 |
|---|---|
| 编译 | 0 error / 0 warning |
| 场景 | `Is Dirty=false`，Map1 已保存；Root 5 → 6（`Map1_Environment`） |
| 运行 | Play 曾跑约 15s：0 Exception / 0 Error；只有工程既有 `Water_Ceiling: OnMeshFilterChanged` 警告（Map4 也有） |

## 六、继续调（都在脚本顶部）

- 再压曝光：`kPostExposure` → 0 ~ 0.1；
- 更通透：`kAmbientIntensity` 微加；
- 更接近参考图那种蓝粉撞色：`kFillColor` 偏青蓝 + 加一盏青色点光 + 雾往冷粉调；
- 明显光柱：目前 prefab 光柱偏淡，需要单独加光锥网格；
- 更少焦散：`kCausticIntensity` → 0。

回退：`Tools/PAO/关卡白盒/Map1 环境渲染 · 移除环境`，或 `git checkout -- Assets/GameMain/Scenes/Map1.unity`。

## 七、已知限制

1. 只做环境层，不动几何 / 碰撞 / 机关；
2. 高机位里仍有少量深色 Void / 岩壁底面；
3. 未加 Map4 的近场雾球（UnderwaterRig）；
4. `PAOUIConfig` 里 Map1 的 `spawnAnchor = Island_Start` 仍无同名物体（旧 MISS）；
5. Sunshaft prefab 本身偏淡，Game 视角里光柱不明显。
