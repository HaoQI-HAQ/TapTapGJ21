# 旁白 AI 语音设计：能力验证与候选音色

> 时间：2026-10-19 · 环境：本机（Windows）DSH 会话
> 产物：`AudioPipeline/_voice_design/ab/`（试听页与音频）· 脚本：`Tools/AV/design_voice.py`、`Tools/AV/make_voice_design_report.py`

## 一句话

**可以，DSH 能直接在本机生成 AI 语音**，不需要外部平台账号：用 `edge-tts`（微软神经语音，在线合成）+ `PyAV`（解码/重采样），一条命令产出 32 kHz / 16-bit / mono WAV，并附 manifest。已对 Map0 现有 5 段旁白跑出 4 组候选，见 `AudioPipeline/_voice_design/ab/index.html`。

## 实现方式（为什么是这条）

| 方案 | 结论 |
|---|---|
| Windows SAPI（现管线，`synthesize_narration.ps1`） | 只有 Huihui 女声，只有 Rate 可调，做不出“少年感/气声/句尾控制” |
| **edge-tts** | **采用**。免 key、zh-CN 有 8 个神经音色，支持 rate / pitch / volume 参数，接近“按设定调参”而非单纯换音色 |
| ElevenLabs Voice Design / Gemini TTS | 真正的“文本 prompt 设计音色”，但需要 API key 与外网额度；本机未配置，未使用 |
| 本地离线模型（Piper 等） | 无现成模型与权重，下载成本高于收益 |

注意：edge-tts 不是「写一段话就生成新音色」的 voice design，而是**音色选择 + prosody 参数化**。上一轮那份英文描述在这里被翻译成了 `voice / rate / pitch / volume` 四个可执行旋钮。

## 已生成的候选（5 段合计时长）

| 方案 | 参数 | 时长 | 相对现版 |
|---|---|---|---|
| 现版 | SAPI Huihui（女声）· Rate −2 · 22.05 kHz | 96.54 s | — |
| A | Yunxi · rate −5% · pitch −25 Hz | 64.66 s | −33% |
| **B（推荐起点）** | Yunxi · rate −15% · pitch −25 Hz | 72.24 s | −25% |
| C | Yunxia（男童）· rate −10% · pitch −15 Hz | 72.82 s | −25% |
| D | Yunyang（成年男声）· rate −8% · pitch −15 Hz | 63.12 s | −35% |

## 关键发现（会影响决策）

1. **现版旁白是女声**（Microsoft Huihui Desktop），不是少年男声。换成 Yunxi 是一次“音色定义”的改动，不只是优化。
2. **edge-tts 没有真正的 14–16 岁少年男声**：Yunxi 偏青年（约 20 岁）、Yunxia 是男童且偏卡通。想要“清亮少年但克制”，B 是最接近的可行解，但“少年”和“不像人”这两件事在现模型上无法同时拿满。
3. **时长缩短 25–35%**：5 段从 96.5 s 到 63–73 s。`Map0NarrationDirector` 按片段长度排字幕，触发节奏能自适应，但实机停顿感会变，切换后必须走一遍「旁白自检」菜单与实机。
4. **非 bit 级可复现**：每次请求合成的文件不同（听感一致）。manifest 记录参数与文本 sha1；验收请以听感为准。
5. **Edge TTS 是微软在线朗读服务**：开发/试听可用；正式发行建议换 Azure Speech（同音色、可商用授权）或 MiniMax / 豆包等，保留同一套参数即可平移。

## 复现命令

```powershell
# python 用 DSH 自带运行时
$py = 'C:\Users\oppp\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe'

& $py Tools\AV\design_voice.py --lines AudioPipeline\map0_narration_slim.json \
  --voice zh-CN-YunxiNeural --rate=-15% --pitch=-25Hz \
  --out AudioPipeline\_voice_design\ab\11_yunxi_r15

& $py Tools\AV\make_voice_design_report.py
```

`design_voice.py` 输出 `*.wav`（32 kHz/16-bit/mono）+ `*.mp3`（源）+ `manifest.json`；`--limit N` 可只跑前 N 条做试听。

## 下一步

1. ~~在 A/B 页选定音色（B / A / C）。~~ → 已选 **B**（2026-10-19）。
2. ~~生成 5 段正式版 → 替换 `Assets/GameMain/Audio/Map0VO`~~ → **已完成**：B 已覆盖 Map0VO 的 5 个 wav（同路径、`.meta`/GUID 不变），旁白自检通过（播放器 5 条 / 缺 0、触发区 5 异常 0、字幕条 OK）。新合计 72.24 s（原 96.54 s）。详见 [narration-trim.md](../Map0-refactor/narration-trim.md)。
3. 实机走图：确认音量（新片段整体低约 2 dB）与字幕节奏。
4. 若继续推广：完整稿 500+ 行需要按句切分 + 统一参数重生成；wengjie 的 13 句 `vo_0..12` 是同一角色，需一并决策。
5. 想保留“程序假装存在”的诡异感：可在 B 之上加轻度后期（带通 + 轻微失真/降采样），作为“人声但不对”的中间态，需要时再单独做。

## 云端 key 接入（真正的 Voice Design）

> 更新：2026-10-19 18:20。**结论：本机没有任何 TTS / voice-design key，云端这条线暂时卡在“缺 key”。**

已确认的事实：

- `~/.dsh/.credentials.yaml` 里只有 **DeepSeek 的 LLM key**；DeepSeek 不提供语音合成接口，无法用于 TTS。
- 环境变量、项目内 `.env` / secret 文件：无。
- 端点可达性（无 key 探测，401/403/404 都说明网络通）：ElevenLabs ✅、Fish Audio ✅、OpenAI ✅、Azure Speech ✅、SiliconFlow ✅、MiniMax ✅、火山引擎 ✅、Gemini ✅。

适配器已就绪：[design_voice_api.py](Tools/AV/design_voice_api.py)，一条命令切换 provider：

| provider | 需要的 key | 拿到什么 | 备注 |
|---|---|---|---|
| `gemini` | Gemini API key（AI Studio 有免费额度） | 预置音色 + **风格 prompt**，中文可用 | 最省事，推荐先跑 |
| `elevenlabs-design` | ElevenLabs key（Starter 起） | **一段文字描述 → 生成 3 个新音色**（预览 wav + generated_voice_id） | 最接近“AI 语音设计”本意 |
| `elevenlabs` | ElevenLabs key + voice_id | 现成音色 + stability/speed | 设计出音色后用这个跑全量 |
| `openai` | OpenAI key（或任意兼容 base_url） | `gpt-4o-mini-tts`，`instructions` 就是风格提示 | 兜底 / 走中转 |

key 放法（任选其一，无需改代码）：

1. `C:\Users\oppp\.dsh\voice_keys.json`
2. `<项目>\AudioPipeline\.voice_keys.json`（本机开发用，**不要提交**）

```json
{ "gemini": { "key": "AIza..." } }
```

命令：

```powershell
$py = 'C:\Users\oppp\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe'

# 1) Gemini 试 1 条
& $py Tools\AV\design_voice_api.py --provider gemini --voice Kore --limit 1 \
  --out AudioPipeline\_voice_design\gemini

# 2) ElevenLabs 生成 3 个候选音色
& $py Tools\AV\design_voice_api.py --provider elevenlabs-design \
  --out AudioPipeline\_voice_design\el_design

# 3) 选定 voice_id 后跑全部 5 句
& $py Tools\AV\design_voice_api.py --provider elevenlabs --voice <generated_voice_id> \
  --out AudioPipeline\_voice_design\el_final
```

冒烟测试（无有效 key 时）：Gemini 返回 `400 API_KEY_INVALID`、ElevenLabs 返回 `401` —— 请求格式与链路都通，只差真 key。

## 产物清单

- `AudioPipeline/_voice_design/ab/index.html` —— A/B 试听页（浏览器打开，逐句对比 5 个方案）
- `AudioPipeline/_voice_design/ab/{00_current_sapi,10_yunxi_r5,11_yunxi_r15,12_yunxia_r10,13_yunyang_r8}/` —— 音频与 manifest
- `Tools/AV/design_voice.py` —— 生成管线（edge-tts，无需 key）
- `Tools/AV/design_voice_api.py` —— 云端 key 适配器（gemini / elevenlabs / elevenlabs-design / openai 兼容）
- `Tools/AV/make_voice_design_report.py` —— 试听页生成
- 注意：`AudioPipeline/_voice_design/` 目前是未跟踪目录，建议加进 `.gitignore`；两个脚本建议提交。
