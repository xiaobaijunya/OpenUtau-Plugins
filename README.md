# 辅音时长调整 · OpenUtau 外置插件

**Consonant Length Adjuster for OpenUtau** —— 批量调整 OpenUtau 中辅音（音节开头辅音）的时长，支持 **CV / CVVC / AI** 三类音源与音素器。

> 插件在钢琴卷帘中通过「External（外部插件）」菜单打开，自带独立的参数窗口，可保存预设与自定义规则。

---

## 功能一览

- **三种模式**
  - **CV**：只移动当前音素的 **p0 / p1** 两个点（`preutter` / `overlap`），用于普通 CV 音源（一个音符一个发音）。
  - **CVVC**：移动**前一个音素**（VC，例如 `a n`）的 **pre 线 / 起点**，用于 CVVC 音源里辅音位于 VC 采样尾部的场景。
  - **AI**：音素列已经是 `sh` `ao` `ch` `e` 这样拆好的形式时，直接按名字匹配辅音音素并改变它的长度（起点前移/后移，终点不动）。
- **按音素开头匹配**：规则写前缀，例如 `sh` 会匹配 `shao`、`shi`、`shu`；最长前缀优先，所以 `s` 与 `sh` 可以同时存在。
- **基础预设**：内置汉语（拼音声母）与日语（罗马字 + 假名）的辅音规则，各自分成**清辅音**与**浊辅音**两组，可一键合并或替换。
- **完全可自定义**：随时添加 / 删除 / 启用 / 停用规则，改写前缀与倍率，例如 `b,g → 0.5×`、`sh,ch → 0.7×`。
- **整体倍率**：在每条规则倍率之上再乘一个全局倍率，方便统一「整体再快一点 / 慢一点」。
- **实时预览**：应用前显示每个音素的「原长度 → 新长度 ×倍率」，不会误改。
- **可撤销**：所有改动进入同一个 Undo 组，一次 Ctrl+Z 即可整体还原。
- **配置持久化**：规则、模式、参数自动保存，下次打开继续用。

---

## 安装

1. 到 [Releases](../../releases) 下载 `OpenUtau.Plugin.ConsonantLength-<版本>.zip`。
2. 解压出 `OpenUtau.Plugin.ConsonantLength.dll`。
3. 把它放进 OpenUtau 的 **Plugins** 文件夹：

   | 版本类型 | Plugins 目录 |
   | --- | --- |
   | 便携版 / 开发版（程序目录下没有 `installed.txt`） | `<OpenUtau 程序目录>\Plugins\` |
   | 安装版（有 `installed.txt`） | `文档\OpenUtau\Plugins\`（即 `%USERPROFILE%\Documents\OpenUtau\Plugins`） |
   | macOS | `~/Library/OpenUtau/Plugins/` |
   | Linux | `~/.local/share/OpenUtau/Plugins/` |

   > 也可以放进 Plugins 下的任意子文件夹，例如 `Plugins\ConsonantLength\`。
4. 重启 OpenUtau。配置会保存在 `Plugins\ConsonantLength\settings.json`。

**卸载**：删除 DLL 与 `Plugins\ConsonantLength\` 文件夹即可。

---

## 使用

1. 在钢琴卷帘中选中要处理的音符（不选则处理整个声部）。
2. 右键 → **External**（外部插件）→ **辅音时长调整 (Consonant Length)**。
3. 在窗口里选择模式、调整规则与倍率，看预览确认无误后点 **应用**。

窗口顶部会显示当前音轨、音源与音素器，方便确认用的是哪一种音源。

### 模式说明

#### CV 模式

```
                 p0        p1              p2
   ──────────────●─────────●───────────────●──────────────
                  \        /               /
     (辅音段)      \______/  ← 这段被缩放  → 元音从 p2 开始
   pre 线以前、p2 之前的区域就是辅音
```

- 只改 `preutter`（p0）与 `overlap`（p1）：倍率 0.7 表示辅音缩短到 70%。
- 勾选「同时缩放 overlap」时 p1 跟着 p0 按同一比例移动，音头过渡保持一致；取消勾选则 p1 不动。
- 音素没有 `preutter`（例如纯元音、静音、AI 音源）时会自动跳过。

#### CVVC 模式

CVVC 中辅音通常位于 **VC 音素**（如 `a n`）采样的尾部，当前音符的第一个音素是 CV（如 `a`）。此模式会把 **紧邻当前音符第一个音素的 VC 音素** 的起点（也就是它的 pre 线）向左/向右移动：

```
   音符1: a           音符2: a
   [ CV a ]  [ VC a n ] [ CV a ]
              ↑ 移动这个音素的 pre 线 → n 更长 / 更短
```

- 只会处理「附着在当前音符上」或「名字形如 `a n`」的前一个音素；普通 CV 音素（如 `ma`）不会被误改。
- 起点移动量由倍率换算成毫秒再换算成 tick，受「最短 / 最长」与「不能越过前一个音素」限制。

#### AI 模式

AI 音源（DiffSinger / ENUNU / VOGEN / VOICEVOX 等）的音素列通常已经是 `sh`、`ao`、`ch`、`e` 这样的形式：

- 按规则匹配到辅音音素（例如 `sh`）后，直接移动它的起点来改变长度，**终点保持不变**。
- 元音音素通常匹配不到任何辅音前缀，因此不会被改动。

---

## 规则与预设

规则 = **前缀 + 倍率**。匹配时对音素名（以及它映射到的 oto 别名）做「开头匹配」，多条命中时取**最长前缀**。

| 写法 | 含义 |
| --- | --- |
| `sh` | 匹配 `shao` / `shi` / `shu` / `sha` … |
| `b` | 匹配 `ba` / `bo` / `bi` … |
| `k,か,き,く,け,こ` | 一条规则写多个前缀，用逗号分隔（日文假名别名也能匹配） |
| `a n` 中的 `n` | VC 别名会按空格 / 连字符拆分，取辅音那一侧再匹配 |

内置预设（可在窗口的「③ 音素匹配规则」里选择后「合并载入」或「替换规则」）：

- **汉语 · 清辅音**：b p d t g k f h j q x zh ch sh z c s
- **汉语 · 浊辅音**：m n ng l r y w
- **日语 · 清辅音**：k s t h p ch ts sh f（含 か行/さ行/た行/は行/ぱ行/っ 假名）
- **日语 · 浊辅音**：g z j d b（含 が行/ざ行/だ行/ば行/ま行/な行/ら行/や行/わ行 假名）

默认倍率是作者按经验给的建议值，请按自己的音源习惯修改。

> 提示：默认值整体偏「缩短」。如果你想要相反的效果，把倍率改成大于 1（例如 `1.3`）即可拉长。

---

## 参数

| 参数 | 说明 |
| --- | --- |
| 作用范围 | 「仅选中的音符」或「全部音符」；没有选中时自动使用全部音符。 |
| 整体倍率 | 与每条规则的倍率相乘，默认 1.00。 |
| 最短 / 最长 (ms) | 结果的保护范围：CV 限制 `preutter`，CVVC / AI 限制音素时长。 |
| 同时缩放 overlap | CV 模式下 p1 是否跟随 p0 一起缩放。 |

---

## 常见问题

**Q：为什么点应用后没有变化？**
A：看预览区的统计。
- 「未匹配」很多：检查规则前缀是否写对（例如音源用假名别名时，规则里要写假名）。
- 「无需调整」很多：
  - CV 模式下音素的 `preutter` 为 0（AI 音源、纯元音、静音）——请改用 AI 模式。
  - 倍率 × 整体倍率 = 1.00。
- 「N 个音符还没有音素」：声部还没被音素化（先让 OpenUtau 加载完音源 / 切换音素器）。

**Q：可以连续应用两次吗？**
A：可以，但倍率是**相对当前值**的。第二次 0.7 是在第一次结果上再乘 0.7。建议一次调到位，或先 Ctrl+Z 撤销。

**Q：会改动音符时值 / 节奏吗？**
A：不会。CV 模式只改发音点；CVVC / AI 模式只移动音素本身的起点，音符的 position/duration 不变。

**Q：支持官方 OpenUtau 吗？**
A：支持。插件只依赖 OpenUtau 公开的 `BatchEdit` 接口与音素时间编辑命令（`PhonemePreutterCommand` / `PhonemeOverlapCommand` / `PhonemeOffsetCommand`），官方 OpenUtau 0.1.5xx（含以上）都具备这些 API；本仓库在 OpenUtau 自定义渲染分支上开发，并已用官方源码核对接口一致。

---

## 从源码构建

需要 .NET 8 SDK。

```powershell
# 方式一：指向已安装的 OpenUtau 里的 OpenUtau.Core.dll
$env:OPENUTAU_CORE_PATH = "C:\Program Files\OpenUtau\OpenUtau.Core.dll"
dotnet build src\OpenUtau.Plugin.ConsonantLength -c Release

# 方式二：把 OpenUtau.Core.dll 放到 lib\ 下，构建时会自动使用
dotnet build src\OpenUtau.Plugin.ConsonantLength -c Release
```

产物：`src\OpenUtau.Plugin.ConsonantLength\bin\Release\OpenUtau.Plugin.ConsonantLength.dll`

运行测试：

```powershell
dotnet run --project tests\ConsonantLength.Tests        # 规划逻辑单元测试
dotnet run --project tests\PluginLoadProbe -- <OpenUtau.Core.dll> <plugin.dll>
```

> 插件以 `net8.0` 编译，只引用编译期的 `OpenUtau.Core.dll` 与 `Avalonia`（运行时使用 OpenUtau 自带的 Avalonia），所以发布包只有一个 DLL。

---

## 目录结构

```
src/OpenUtau.Plugin.ConsonantLength/
  ConsonantLengthAdjuster.cs     BatchEdit 入口（菜单项 + 打开窗口）
  ConsonantLengthWindow.cs       Avalonia 参数窗口
  VoicePartAdapter.cs            OpenUtau 文档对象 <-> 纯规划逻辑
  Model/ConsonantRule.cs         规则 / 设置模型
  Model/ConsonantPresets.cs      汉语 / 日语 清浊预设
  Model/PhonemeMatcher.cs        音素名前缀匹配
  Model/ConsonantPlanner.cs      CV / CVVC / AI 三种模式的纯逻辑
  Model/PluginSettingsStore.cs   配置读写与日志
tests/ConsonantLength.Tests/     规划逻辑测试（不依赖 OpenUtau）
tests/PluginLoadProbe/           模拟 DocManager 的插件加载探测
```

## 更新日志

### 1.0.0
- 首个版本：CV / CVVC / AI 三种模式，汉语/日语清浊预设，自定义规则，实时预览。

## 许可证

[MIT](LICENSE)

---

## English summary

**Consonant Length Adjuster** is an external OpenUtau batch-edit plugin that scales consonant durations of the selected notes (or the whole part) with configurable prefix rules.

- **CV mode** — scales `preutter` / `overlap` (points p0/p1) of the phoneme at the note onset.
- **CVVC mode** — moves the start (pre line) of the VC phoneme that precedes the note's vowel phoneme.
- **AI mode** — matches consonant phonemes by name (`sh`, `ch`, …) and moves their start to change their length.
- Built-in presets for Mandarin and Japanese consonants, split into voiceless / voiced groups; rules are fully editable (`prefix,prefix2=ratio`), plus a master ratio and min/max clamps.
- Install: drop `OpenUtau.Plugin.ConsonantLength.dll` into the OpenUtau `Plugins` folder and restart. Right-click notes → **External** → **辅音时长调整 (Consonant Length)**.
- Build with .NET 8 SDK against `OpenUtau.Core.dll` (`OPENUTAU_CORE_PATH`).