# 背景音乐接入（琵琶 / 古筝琵琶电子）

两首成品曲已经接进工程：关卡用 **琵琶**，主菜单用 **古筝 + 琵琶电子版**。
暂停菜单打开时音乐会跟着停，关掉后从原位置继续；音量走设置页里的「音乐」滑条。

## 放了什么

| 文件 | 原名 | 用在哪 |
| --- | --- | --- |
| `Assets/GameMain/Resources/PAOAudio/Music/Custom/Pipa_Solo.mp3` | `琵琶.mp3` | 除主菜单外的所有场景（Map0–Map5、Map3_5、cs、Control UI、wengjie 等） |
| `Assets/GameMain/Resources/PAOAudio/Music/Custom/Guzheng_Pipa_Electronic.mp3` | `古筝和琵琶_电子.mp3` | `Menu` |
| `Assets/GameMain/Resources/PAOAudio/MusicCatalog.json` | — | 场景 → 曲目 的映射表 |

两首都是 6:00 / 128 kbps / 44.1 kHz 立体声。文件名改成 ASCII 是为了 Android / IL2CPP 和目标平台的文件系统安全，中文原名记在上表里。

## 场景 → 曲目 的映射

映射写在 `MusicCatalog.json` 里，运行时读的是 `Resources/PAOAudio/MusicCatalog`，**改完不用重新编译**：

```json
{
  "entries": [
    { "scene": "Menu", "clip": "PAOAudio/Music/Custom/Guzheng_Pipa_Electronic",
      "gain": 0.40, "pauseWithGameplay": true, "muteSynthLayers": true },
    { "scene": "*",    "clip": "PAOAudio/Music/Custom/Pipa_Solo",
      "gain": 0.40, "pauseWithGameplay": true, "muteSynthLayers": true }
  ]
}
```

- `scene`：场景名，精确匹配优先；`"*"` 是兜底，所有没单独写到的场景都用它。
- `clip`：`Resources` 下的路径，不带扩展名。留空则回退到老规则 `Music/<场景名>_Base`。
- `gain`：相对增益（1 = 原始响度）。这是**曲目之间**的平衡，玩家调的总音量不在这里。
- `pauseWithGameplay`：是否随暂停菜单暂停。
- `muteSynthLayers`：是否关掉原来那套程序化合成的 Pulse / Tension 叠层。新曲子自带编曲，叠上去会串调，默认关掉；想听老叠层就改成 `false`。

给某一关单独换歌，加一条精确条目即可，例如：

```json
{ "scene": "Map4", "clip": "PAOAudio/Music/Custom/别的曲子", "gain": 0.3,
  "pauseWithGameplay": true, "muteSynthLayers": true }
```

## 导入参数

`Assets/GameMain/Scripts/Editor/PAOAudioImportSettings.cs` 会对 `Music/Custom/` 下的文件强制：

| 参数 | 值 | 原因 |
| --- | --- | --- |
| 压缩格式 | Vorbis, quality 0.7 | 6 分钟立体声 PCM 一首约 63 MB，压缩后约 8 MB |
| 加载方式 | Streaming | 音乐不需要常驻内存 |
| 预加载 | 关 | 进关时和淡入一起加载，不卡主线程 |
| 采样率 | 保持 44.1 kHz | 不做重采样 |

其余 `PAOAudio/` 下的合成音频维持原来的 PCM + Streaming，Map4 多轨对齐那套逻辑不受影响。

## 暂停

`PAOAudioDirector.SetMusicPaused(bool)`：只暂停音乐源（主曲 + 两个叠层），音效、旁白、环境声照常播；用的是 `AudioSource.Pause()`，位置保留。

暂停菜单不需要额外接线：`UIRuntime` 在 `SetPause(true)` / `ReleasePause()` 里已经调用了它，所以

- 暂停菜单、以及叠在暂停菜单上的设置 / 地图页打开时，音乐停；
- 回到游戏，音乐从原位置继续；
- 换场时 `OnSceneLoaded` 会无条件恢复播放，避免「从暂停菜单回主菜单后音乐再也不响」。

别的地方想手动控制：

```csharp
PAOAudioDirector.Instance.PauseMusic();
PAOAudioDirector.Instance.ResumeMusic();
bool paused = PAOAudioDirector.Instance.MusicPaused;
```

## 音量

音量链路是现成的，没有为新曲子加第二条控制：

```
AudioListener.volume        ← 设置页「总音量」      (UISettingsData.master)
  └─ AudioSource.volume     ← 设置页「音乐」× gain   (UISettingsData.music × catalog.gain)
       └─ 音乐源（主曲 / Pulse / Tension）
```

`PAOAudioDirector.Update()` 每帧把 `settings.music × musicTrim` 写进音乐源，所以在设置页拖动「音乐」滑条是实时生效的，暂停中也一样（恢复播放时按新音量继续）。
音效走同一设置页的「音效」滑条，旁白跟着音效走。

## 实测响度与增益

`PAOMusicTools.MeasureCLI` 实测（两首峰值都是满幅 1.0）：

| 曲目 | RMS | dBFS | catalog gain |
| --- | --- | --- | --- |
| Pipa_Solo | 0.1423 | -16.9 | 0.40 |
| Guzheng_Pipa_Electronic | 0.1409 | -17.0 | 0.40 |

0.40 的 gain 在默认「音乐」=0.8、总音量 0.8 下，有效 RMS ≈ 0.045（约 -27 dBFS），
比原来程序化合成的 `Map*_Base`（有效 RMS ≈ 0.024）响一档，但仍压在音效之下。觉得音乐抢戏就把两个 gain 一起往下调。

## 首次放入音频时注意

第一次把音频放进 `Music/Custom/` 时，如果新脚本还没编译完，Unity 可能先用**旧版**后处理导入一遍，
曲目就会留在 PCM（每首约 60 MB）。跑一次菜单 `Tools/PAO/Audio/Reapply Featured Import Settings`
（或 `-executeMethod PAO.AV.Editor.PAOMusicTools.ReapplyCLI`）强制重导即可，`.meta` 会变成 Vorbis + Streaming。

## 循环

`AudioSource.loop = true`，两首曲子都是整曲循环。

MP3 编码器在头尾各带一点填充，可能让接缝处多出十几毫秒。工程里导入时会把它们压成 Vorbis，Unity 按解码后的采样数截断，正常情况下听不出接缝。如果某个平台听起来有断点，把这对应的曲目换成无填充的 WAV 再导入即可，导入规则不用改。

## 验收

```powershell
# 响度测量（只写 Logs/music-loudness.txt，不改资源）
Unity.exe -batchmode -nographics -projectPath <工程> `
  -executeMethod PAO.AV.Editor.PAOMusicTools.MeasureCLI

# 接入验收（目录解析、导入参数、关卡选曲、暂停菜单、音乐滑条、换场切歌）
Unity.exe -batchmode -nographics -projectPath <工程> `
  -executeMethod PAO.AV.Editor.PAOMusicVerification.RunCurrentCLI

# Map4 专项（整曲 + 原有多轨共存，只读场景，不保存）
Unity.exe -batchmode -nographics -projectPath <工程> `
  -executeMethod PAO.AV.Editor.PAOMusicVerification.RunMap4CLI
```

结果分别写到 `AVVerification/music-report.txt` 和 `AVVerification/music-map4-report.txt`，全部通过时进程退出码为 0。
当前工程已跑过：

- 接入验收 **42 项通过 / 0 失败**（Map1 播放琵琶、暂停菜单暂停与恢复、音乐滑条缩放、切到 Menu 换成电子版）
- Map4 专项 **33 项通过 / 0 失败**（六个音源仍在、合成 Pulse / Tension 音量被压到 0）

响度数据在 `Logs/music-loudness.txt`。

## 改动清单

- 新增 `Resources/PAOAudio/Music/Custom/{Pipa_Solo,Guzheng_Pipa_Electronic}.mp3`（含 Unity 生成的 `.meta`）
- 新增 `Resources/PAOAudio/MusicCatalog.json`
- 新增 `Scripts/AV/PAOMusicCatalog.cs`（目录数据结构 + JSON 加载）
- 新增 `Scripts/Editor/PAOMusicTools.cs`（重导导入参数 + 响度测量）
- 新增 `Scripts/Editor/PAOMusicVerification.cs`（验收）
- 新增 `Docs/bgm-integration.md`（本文）
- 改 `Scripts/AV/PAOAudioDirector.cs`（按目录选曲、增益、暂停 API）
- 改 `Scripts/UI/V2/UIRuntime.cs`（暂停/恢复时同步音乐）
- 改 `Scripts/Editor/PAOAudioImportSettings.cs`（成品曲目单独走 Vorbis + Streaming）
