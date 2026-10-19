# Map0 旁白二次精简：只留「关键机制教学」

> 时间：2026-10-18 深夜（在「11 段精简版」之后再砍一刀）
> 场景：`Assets/GameMain/Scenes/Map0.unity`｜数据：`Assets/GameMain/Scripts/Gameplay/Map0NarrationData.cs`
> 生成器：`Assets/GameMain/Scripts/Editor/Map0TutorialSetup.cs`

## 一句话

Map0 的旁白从 **11 段 / 约 3 分 35 秒** 砍到 **5 段 / 约 1 分 37 秒**（-55%）：
只留「破坏 / 恢复 / 消耗 / 蓄力 / 工具也是材料」五条机制教学，开场叙事、出口提示、结尾提问全部不播。

## 保留的 5 段（关键机制教学）

| id | 标题 | 教学点 | 触发位置（近似 z） | 配音时长 |
|---|---|---|---|---|
| M-005 | 第一道墙 | 破坏：前方粉色泡泡墙要打破才能过 | 粉墙前 −21.5 | 17.9 s |
| M-007 | 消失并不永久 | 恢复：被打破的泡墙会重新长回来 | 粉墙后 −6 | 19.1 s |
| M-008 | 代价与补充 | 消耗：发射消耗泡泡值，且会自己回升 | 资源区 10 | 19.4 s |
| M-011 | 蓄力 | 蓄力：按住左键蓄力，越大越强也越贵 | 中段 34 | 19.7 s |
| M-013 | 工具也是材料 | 材料：打出的泡泡落地会变成粉色材料 | 爬升前 58 | 20.5 s |

合计 **96.6 s**。

## 删掉的 6 段（从 11 段精简版里去掉）

| id | 标题 | 原定位 | 配音时长 |
|---|---|---|---|
| M-001 | 最初的空白 | 开场「白色不等于存在」独白 | 20.7 s |
| M-002 | 一个意外 | 泡泡诞生独白 | 19.8 s |
| M-003 | 关于名字 | 「就叫你『你』」独白 | 13.9 s |
| M-004 | 第一次等待 | 主角醒不来的玩笑 | 15.5 s |
| M-014 | 出口 | 出口方向提示（「沿着前方的道路」） | 18.5 s |
| M-016 | 第一个问题 | 结尾提问（「你也只是一颗泡泡呢？」） | 29.8 s |

合计删掉 **118.2 s**。完整 36 段稿件和全部 26 条旧配音仍保留在 `Docs/Map0-refactor/narration-beats.json`、git 历史与 `AudioPipeline/_narration_raw_map0*` 里，可随时恢复。

## 场景与资源的变化

- `Map0_Tutorial/Map0_Narration` 子物体：**34 → 5**（删掉 32 个旧 `PY-01-*` 触发 + 2 个停留区 + 11 个 `Slim_*`，换成 5 个 `Mech_M005/007/008/011/013`）。
- 播放器 `Map0NarrationDirector.m_Clips`：**11 条 → 5 条**，无缺失引用。
- `Assets/GameMain/Audio/Map0VO`：**27 个 wav（约 44.5 MB）→ 5 个 wav（约 4.1 MB）**；删除的语音文件都在 git 历史里（`git checkout <commit> -- Assets/GameMain/Audio/Map0VO` 可取回）。
- `Map0PlayerFix` 不再开场强播 M-001（只保留 CharacterController 兜底）。
- 操作提示区（`Hint_Spawn/Wall/Sticky/Bomb/Exit`，UI Toast，无配音）不受影响。

## 改动文件

| 文件 | 改动 |
|---|---|
| `Assets/GameMain/Scripts/Gameplay/Map0NarrationData.cs` | 只留 5 段机制教学 |
| `Assets/GameMain/Scripts/Gameplay/Map0PlayerFix.cs` | 去掉开场强制旁白 |
| `Assets/GameMain/Scripts/Editor/Map0TutorialSetup.cs` | 重写 `BuildNarration`；新增「只重建旁白（精简）」菜单；删掉旧 21 个 PY-01 触发/停留区生成代码 |
| `Assets/GameMain/Scenes/Map0.unity` | 旁白物体 34 → 5，配音表 11 → 5，场景已保存 |
| `Assets/GameMain/Audio/Map0VO/` | 27 → 5 个 wav |
| `AudioPipeline/map0_narration_slim.json`、`Docs/Map0-refactor/narration-beats-slim.json` | 同步为 5 段 |

## 重建 / 自检

- 重建旁白（不影响关卡摆放）：菜单 `Tools/PAO/关卡白盒/Map0 教程重构 · 只重建旁白（精简）`
- 自检：菜单 `Tools/PAO/关卡白盒/Map0 教程重构 · 旁白自检`

自检输出（2026-10-18 23:49）：

```
[Map0 旁白自检 · 关键机制教学精简版]
  数据段： 5 段 / 20 句（M-005 / M-007 / M-008 / M-011 / M-013）
  精简规格：只留 破坏 / 恢复 / 消耗 / 蓄力 / 材料 5 段机制教学；开场叙事、出口提示、结尾提问不播
  播放器：OK（配音 5 条，缺 0 条）
  触发区： 5 个（异常 0 个）
  停留区： 0 个
  字幕条： OK
  配音文件： 5 个（Assets/GameMain/Audio/Map0VO）
```

编译：`recompile_scripts` 0 error / 0 warning；场景 `Is Dirty=false`。

## 想再调整

- 还想再少一段：从 `Map0NarrationData.All` 删掉对应 beat，并从 `kMechanicBeatIds` / `BuildNarrationTriggers` 删掉对应项，再跑一次「只重建旁白（精简）」。
- 想恢复某段：从 git 取回 `m_0xx.wav` + `.meta`，把 beat 加回数据，重建旁白即可。
- 说明：本轮只做「少说话」，没有改写保留的 5 段台词（沿用原 SAPI 配音）。

## 2026-10-19 换音色：SAPI → edge-tts Yunxi（A/B 的 B 方案）

- 动机：原 SAPI 是**女声**（Microsoft Huihui）且 Rate −2 偏拖，与「清亮少年、克制不表演」的旁白方向不符。
- 方案：`edge-tts` 的 `zh-CN-YunxiNeural`，`rate=-15%`、`pitch=-25Hz`；32 kHz / 16-bit / mono WAV。
- 替换方式：**同路径覆盖** `Assets/GameMain/Audio/Map0VO/m_005|007|008|011|013.wav`，`.meta`/GUID 不变，场景引用与 `m_Clips` 无需改动。
- 新时长：13.51 / 14.21 / 13.63 / 15.55 / 15.34 s，合计 **72.24 s**（原 96.54 s，−25%）。
  字幕按片段长度比例分配，会自动变快；触发区 / 停留区 / 场景摆放均未改。
- 自检（2026-10-19 18:2x，Map0 加加载后跑菜单）：数据 5 段/20 句；播放器 OK（配音 5 条，缺 0）；触发区 5 个（异常 0）；停留区 0；字幕条 OK；配音文件 5 个。Unity 重导入日志确认 5 个 wav 均以原 GUID 重新导入，0 error。
- 备份：`AudioPipeline/_voice_design/_backup_map0vo/`（原 5 个 wav + .meta）。回滚：`git checkout -- Assets/GameMain/Audio/Map0VO`。
- 响度：新片段 RMS ≈ −25 dBFS，旧版 ≈ −23 dBFS，整体约低 2 dB；实机若偏小声，调 `Map0NarrationDirector` 的 AudioSource 音量或设置页「音效」滑条，不要逐条压限。
- 试听 / A-B：`AudioPipeline/_voice_design/ab/index.html`；生成脚本 `Tools/AV/design_voice.py`；决策记录 `Docs/AV/voice-design.md`。
