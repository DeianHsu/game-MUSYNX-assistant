# MUSYNX（同步音律喵赛克）自动打歌可行性调研

> 调研日期：2026-10-28 ｜ 目标：不依赖 OpenCV 截图识别，直接读内部谱面 + 歌曲时间轴，按音符时序触发命中。

## 0. 结论

**可以做，而且这条路比图像识别干净得多。**

MUSYNX（Steam 2018 原版）是 **Unity 2017.4.4f1 + Mono** 游戏，谱面是 **BMS 风格的纯文本**，社区已经有两套成熟的注入/Mod 方案（MelonLoader Mod、Mono.Cecil IL 补丁）公开可用。判定入口 `BMSLib.Note.Knock()` 已被第三方 Mod 直接挂钩，说明"绕过输入层、按时间轴驱动判定"在技术上是通的。

最佳形态不是"外部程序发按键"，而是**进程内 Mod**：读游戏自己的谱面对象和音频时钟，在正确的那一帧调用游戏自己的判定函数。外部 SendInput 方案在 Unity 的逐帧输入轮询下精度最差，只作为兜底。

## 1. 已经确认的技术底牌

| 项目 | 结论 | 证据 |
| --- | --- | --- |
| 引擎 | Unity 2017.4.4f1，Mono（非 IL2CPP） | keytap 项目 README，Build 25570400 实测 |
| Mod 加载 | MelonLoader **0.7.1** 可用 | bnfour/mx-mods 安装说明 |
| 程序集 | `MUSYNX_Data/Managed/Assembly-CSharp.dll`，类型可直接反射 | 多个项目 |
| 音频 | CRI ADX（`CriAtomSource` / `StreamingAssets/song_*.acb`） | keytap 资源实验记录 |
| 谱面 | `*.txt`，BMS 风格，打包在 `MUSYNX_Data/resources.assets` | keytap；Cpp_Mania_Player 解析器 |
| 输入 | 自研 `InputManager` + `InputBlendGroup` + `InputSetting`（不是直接用 Input.GetKey） | mx-mods QuickQuit / PlentifulStats |
| 判定 | `BMSLib.Note.Knock()` / `BMSLib.LongNote.Knock()`，返回 bool | keytap Patcher.cs |
| 结算 | `SongInfoCore.UpdateSync` / `get_SyncNumber` / `UploadScore` | keytap、mx-mods |

关键类型（已被公开 Mod 引用，说明名字稳定）：

- 玩法：`SongInfoCore`、`UI*_SongPlayer`（每个皮肤一个，如 `UI0_SongPlayer`）、`BMSLib.JudgeGrade`、`BMSLib.Note`、`BMSLib.LongNote`
- 流程：`SelectSongController`、`SelectSpeedScript`、`SongInfoPackageScript`、`OpeningSpectrumScript`
- 输入：`InputManager`、`InputBlendGroup`（`FinalStatus.down/hold/up`）、`InputSetting`、`ButtonType`、`InputDeviceType`
- 资源：`AssetBundleManager.GetBmsText`（取当前谱面文本）、`UserMemory`（`ButtonNumber` / `IsHard` / `JudgeCompensate` / `DisplayAdjustment`）
- 场景：`SelectSongScene`、`UIB_PlayingScene`、`UI0B_PlayingScene`、`SettlementScene`

## 2. 可直接借鉴的开源项目

### 2.1 直接相关（MUSYNX 本体）

1. **[bnfour/mx-mods](https://github.com/bnfour/mx-mods)** ★6 — MUSYNX 的 MelonLoader 模组合集（C#）。
   价值最高：给出了**已编译可用的工程结构**、Harmony 补丁写法、每个皮肤 SongPlayer 的枚举方式、`InputBlendGroup` 的构造方式、长条 `Knock/EndKnock/LiftUp` 生命周期。
   局限：只做 QoL，没有自动演奏。

2. **[dream486/keytap-chart-recorder](https://github.com/dream486/keytap-chart-recorder)** — 制谱器 + 把自定义谱面注入 MUSYNX 的 Mod（C#/Mono.Cecil）。
   价值最高：它已经**替换了游戏读取谱面的入口**（`AssetBundleManager.GetBmsText`），并在 `BMSLib.Note.Knock` 的 `ret` 上挂了钩子——这正是自动打歌要用的两个接口。
   同时它验证了：注入谱面后原版判定/长条/结算全部正常，且能屏蔽 `UploadScore`。

3. **[4accccc/Cpp_Mania_Player](https://github.com/4accccc/Cpp_Mania_Player)** — 支持 MUSYNX `*.txt` 的跨平台音游播放器（C++）。
   价值：`src/parsers/MuSynxParser.cpp` 是一份**干净的谱面格式参考实现**（时间单位、轨道映射、BGM 偏移）。

4. **[Ginsakura/MUSYNXSave](https://github.com/Ginsakura/MUSYNXSave)** — 存档解析（Python/C#）。
   价值：知道成绩、同步率、判定分布怎么存；可用于验证自动打歌的实际得分。

### 2.2 通用参考（其它音游的自动演奏/内存读取）

- [UnnamedOrange/osu-memory](https://github.com/UnnamedOrange/osu-memory)、[l3lackShark/gosumemory](https://github.com/l3lackShark/gosumemory)：外部进程读 osu! 内存（谱面+时间轴）的标准做法。
- [DeamonHunter/ArchipelagoMuseDash](https://github.com/DeamonHunter/ArchipelagoMuseDash)：Muse Dash 的 BepInEx Mod，示范了 Unity 音游里"改判定/改流程"的边界。
- [7BG-AUTOPLAY](https://github.com/luotianci4751/7BG-AUTOPLAY_C-plus-plus)：7 键音游的按键宏，代表**纯外部宏**路线的精度上限。
- Malody / beatoraja / LR2 内置的 AutoPlay：印证"进程内按谱面直接触发判定"是音游圈的常规做法，不是歪门邪道。

**没有找到任何公开的 MUSYNX 自动打歌项目**——需要自己写，但积木都齐了。

## 3. 谱面格式（已还原）

```text
BPM	180
Rank	3
NoteFactoryPrepare	100	100	200	1000
DefaultWeatherUIB	night
WAV	02	lazhuren
MusicNote	<bgmStartTick>	1	02
BeatLine	<start>	<i>	--	<end>
Note	<time>	<track>	<wavId>
LongNote	<time>	<track>	<wavId>	<endTime>
```

- 时间单位 **100ns tick**（`ms = tick / 10000`）。
- 轨道：4K 用 `3,4,6,7`；6K 用 `2..7`；2K 用 `4,6`。文件名带 `2T/4T/6T` 与 `_easy/_hard/_in`。
- 所有音符时间都要减去 `MusicNote` 的 BGM 起点，才是相对歌曲的毫秒。

也就是说：**谱面在进入游戏前就能离线解析**，不需要看屏幕。

## 4. 三条实现路线

| 路线 | 做法 | 精度 | 工作量 | 风险 |
| --- | --- | --- | --- | --- |
| **A. 进程内直接判定**（推荐） | MelonLoader Mod，拿谱面对象，到点调用 `Note.Knock()` | 极高（亚帧，可 <5ms） | 中 | 需按版本适配偏移 |
| **B. 进程内伪造输入** | Hook `InputManager.Update`，按轨道把 `InputBlendGroup.FinalStatus.down` 置真一帧 | 高（1 帧） | 中高 | 要摸清轨道↔group 映射 |
| **C. 进程外 + SendInput**（你设想的"触发按键"） | 读内存拿时钟+谱面，外部发键盘事件 | 低（1–2 帧 + 调度抖动） | 高 | Unity 逐帧轮询可能丢按键；还要过焦点/反作弊 |

C 看起来最"像人"，但在 Unity 里最吃亏：`Input.GetKeyDown` 只在帧边界采样，外部注入的 keydown/keyup 若落在两帧之间会被完全丢弃。要走 C 必须"按下并保持 ≥1 帧"，那精度就不可能优于 A/B。

**结论：A 为主，B 为备选（若 `Knock()` 直调会漏 UI/连击表现），C 只在不允许装 Mod 时考虑。**

## 5. 推荐架构（进程内 Mod）

```
[谱面源]  AssetBundleManager.GetBmsText / BMSLib 内部 Note 列表
      │  (解析成 NoteEvent{timeMs, lane, endMs})
      ▼
[调度器]  每帧读"判定用的时钟" → 取出 timeMs <= now + halfFrame 的音符
      │
      ▼
[触发层]  Note.Knock()  ← 走游戏原生判定、连击、音效、特效
      │
      ▼
[长条]    头判 Knock()，尾判在 endMs 调用 LongNote 的释放/结束路径
```

要点：

1. **时钟必须和判定用的是同一个**。keytap 记录过 CRI 音频钟与游戏钟差 15ms；用错钟会系统性偏早/偏晚。先 probe 确认 `UpdateSync` 的入参来源。
2. **半帧偏置**：在 `now + deltaTime/2 >= noteTime` 时触发，把量化误差从 `[0, frame]` 变成 `[-frame/2, +frame/2]`。60fps → ±8.3ms；240fps（关 VSync + `targetFrameRate=240`）→ ±2.1ms。
3. **多押**：同一毫秒的多个音符要同一帧内全部 Knock，不能拆帧。
4. **长条**：头、尾分开调度；中途不能松。
5. **自动校准**：跑一遍后读 `JudgeGrade` 分布，估计剩余偏移，写回 `JudgeCompensate` 或内部补偿量。
6. **成绩保护**：默认 **不调用 `UploadScore`**，不写官方排行榜。自己的存档想留就留，但别污染线上榜。

## 6. 精度分析

- 判定发生在主线程某一帧内，我们只能决定"哪一帧"，决定不了帧内时刻 → 误差下界 = 帧长。
- 原版判定窗口未公开，需要 probe 实测（用固定偏移扫描，看 EXACT 边界）。
- MUSYNX 的同步率是**连续百分比**（可以超过 100%），所以帧量化会直接体现在分数上，不只是"过不过"。
- 因此 240fps + 半帧偏置是性价比最高的组合；追求极限再做"预测下一帧音频位置"的前瞻触发。

## 7. 风险与边界

1. **版本适配**：Steam 更新会改 `Assembly-CSharp.dll`，IL 偏移/字段名可能变。用 Harmony 按"类型名+方法名"找，不要硬编码偏移；启动时做能力自检，不匹配就安全退出。
2. **《回归》是新游戏**：mx-mods 明确只支持 2018 原版，不支持 Return。若你装的是《回归》，整套假设要重验。
3. **排行榜**：`UploadScore` 存在，说明有线上榜。自动打歌上传 = 作弊，社区对"不可能成绩"非常敏感（萌百都记了梗）。默认屏蔽上传。
4. **Steam 校验/云存档**：替换 DLL 会被完整性校验覆盖；MelonLoader 是外挂式加载，相对温和但不保证。
5. **反作弊**：目前没看到硬反作弊，但"NO WARRANTIES"。

## 8. 落地步骤

| 阶段 | 产出 | 需要什么 |
| --- | --- | --- |
| P0 探针 | 一个只读 Mod：打印 `BMSLib` 全部类型/字段、当前谱面 Note 列表、每帧时钟、按键时调用栈 | 装回游戏 |
| P1 MVP | 4K 单点自动命中，控制台开关，跑通一首歌 | P0 结论 |
| P2 完整 | 长条、多押、6K、成绩保护、热键 | — |
| P3 校准 | 判定分布统计 + 自动补偿，冲 120%+ | — |

**当前机器状态**：`D:\Game\Steam\steamapps\common\MUSYNX` 只剩 `SavesDir`（存档还在，说明你玩过），游戏本体没装。P0 之前需要先装回 Steam 版。
