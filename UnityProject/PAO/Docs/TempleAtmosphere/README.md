# Map2 / Map2_5 / Map3 环境氛围与 wengjie 焦散

按本轮提供的参考图，使用青蓝水雾、淡紫天光、柔和阴影、Map4 同款动态焦散和少量漂浮微粒。场景几何、机关、玩家和碰撞保持原有配置。

实际修改工程：`D:/TapTap21/TapTapGJ21-clone/UnityProject/PAO`。

## 预览

`preview.png` 为本次调柔后的四张地图效果合图；前三张地图另有入口、大厅、终点和焦散细节截图，wengjie 另有角色视角、地面细节与修改前截图。

## 调整

- 每张场景的 `Map2_Environment`、`Map2_5_Environment` 或 `Map3_Environment` 下包含水面、后处理、补光、光柱和漂浮微粒。
- `TempleAtmosphereVolume` 引用该场景独立的 `*_WaterHaze.mat`。可调水色、上层水色、雾浓度、近处清晰距离与最大雾量。
- 各场景主光 `Directional Light` 的 Cookie 直接引用 Map4 使用的 `Caustics.asset`，投射尺寸为 15×15，亮度由 3.36 调至 2.1，方向与 Map4 相同。焦散亮度可通过主光强度调整，水纹大小可通过附加灯光数据中的 Cookie Size 调整。
- Map2 / Map2_5 / Map3 的曝光由 +0.45 调至 -0.10 / -0.15 / -0.10，泛光由 0.38 调至 0.12，降低阴影对比、光柱和局部补光，水雾上层颜色减少泛白。
- wengjie 移除旧的全屏网纹和地面叠加层，改由同款主光 Cookie 投射。独立石材副本降低金属度以接收漫反射水光，独立后处理将泛光设为 0.15、曝光设为 -0.15，不影响其他地图使用的源材质和配置。
- `Assets/GameMain/Art/Environment/TempleAtmosphere` 保存各地图独立的水面、光柱、水雾材质与后处理配置。
- 菜单 `Tools/PAO/环境氛围/搭建 Map2 + Map2_5 + Map3` 可重新搭建；配色初值集中在 `TempleEnvironmentSetup` 的 `Palettes` 中。
- 菜单 `Tools/PAO/环境氛围/导出三个地图预览` 导出当前保存配置的效果。
- 在 Scene 窗口打开场景灯光与后处理显示，即可查看完整光色。游戏相机已开启后处理和深度。
- 扬声器图标是 Game 窗口开启 Gizmos 后显示的编辑器辅助图标。`PAOAudioIconVisibility` 在编辑器加载与进入运行时关闭 AudioSource / AudioListener 的可用图标，也可通过菜单 `隐藏音频调试图标` 应用；不修改音量与音频组件。
- 水面参数校验只记录重建请求，在后续更新中修改网格，避免 OnValidate 引发 `OnMeshFilterChanged` 警告。

## 实现与验证

水雾通过后处理之前的 URP 全屏混合通道渲染。三种画质的 Renderer 均已安装，只有包含 `TempleAtmosphereVolume` 的场景相机会启用；Scene 窗口使用当前活动场景的数据。

焦散使用 Map4 原有的主光 Cookie 投射方式：`Assets/ThirdParty/Thalassophobia/Contents/Materials/Textures/Caustics.asset` 通过 `Caustic.mat` 对 `foam-001.png` 做双层滚动采样，产生细碎柔和的流动水光。共享资源及 Map4 场景均未修改，水雾通道已移除程序化网纹，避免重复叠加。各场景复用既有水面壳与水下粒子资源。

`verification.txt` 记录保存后重新加载的验证结果。四张场景碰撞数量分别保持为 1763、2308、283、200；原有玩法组件、位置和启用状态通过搭建时核对，重新加载后均无丢失脚本。`caustics-render-check.txt` 记录四张地图真实相机渲染与黑色 Cookie 对照的像素差异，确认水光实际投射。`*_receipt.json` 与 `wengjie-verification.txt` 保存验证计数和备份路径；场景备份保存在工程外的 `D:/TapTap21/PAO-EnvironmentBackups`。
