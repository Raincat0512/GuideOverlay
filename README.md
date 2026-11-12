# 攻略悬浮窗 GuideOverlay — 开发者交接文档

一个 **Windows 单文件 C# 悬浮窗工具**：把攻略图浮在游戏画面上，默认鼠标穿透，按住门控键（默认 `Alt`）才可交互。
本文是开发者交接文档；普通玩家请看 `使用说明.md`。

---

## 0. 当前状态（v1.1.0，2026-11-10）

| 项 | 状态 |
| --- | --- |
| 版本 | **1.1.0**：`AssemblyVersion`/`FileVersion` = `1.1.0.0`，exe 右键「属性 → 详细信息」可见；设置面板「诊断」区也显示 |
| 构建 | `build.bat` → `BUILD OK` + 产物路径与字节数；编译到 `obj\` 子目录、成功后才替换（exe 版本资源仍为正确文件名），失败/旧 exe 被占用时**不删旧 exe**（见 §1） |
| 自测 | `selftest.bat` → `EXITCODE=0`，`断言总数 577，失败 0`（基线 525，本次改造 +52），4 张 PNG（尺寸随窗口几何，当前约 470×835；见 §4.6） |
| 默认值 | 门控键 **Alt**；透明度 **85**；窗口高 = 工作区 **60%**（夹 320–900 DIP）、宽 = 高×9/16（夹 240–1200 DIP）；置顶开、吸附开 |
| 关键常量 | 门控轮询 20ms、保存防抖 500ms、图片缓存 3 张 / **8000 万**像素预算、预取仅 ≤800 万像素图、切片每片 2000px |
| 发布 | 玩家包（4 文件）与开发包（8 文件）由 `交付\make-package.ps1` 生成，zip + SHA256；见 §11 |
| 文档机检 | 改完代码/文档后跑 `交付\check-docs.ps1`（校验 §2 行号、BOM、exe 版本、selftest 断言数） |
| 未验证项 | 首启引导计时/提示条换行、设置面板实际布局、拖动看门狗、低权限弹窗**未做真机 GUI 手测**（自测不显示窗口）；第一次真实使用请重点看这四处 |

---

## 1. 交付物与运行环境

| 文件 | 说明 |
| --- | --- |
| `Program.cs` | **唯一源码文件**（约 1.67 万行，C# 5，UTF-8（含 BOM））。改代码只改它。 |
| `GuideOverlay.exe` | 编译产物（`build.bat` 生成）。带版本资源 `1.1.0.0`；**未签名**（分发注意 SmartScreen，见 §11.4）。 |
| `build.bat` | 用 Windows 自带 csc 编译（GBK 编码，首行 `chcp 936`；无第三方依赖）；加固行为见下。 |
| `selftest.bat` | 运行 `GuideOverlay.exe --selftest` 并回显退出码。 |
| `images\` | 默认图片文件夹。**交付包只带 2 张示例 PNG**；本机目录里可能还有你自己的攻略图（不进包）。 |
| `使用说明.md` | 面向玩家。 |
| `README.md` | 本文，面向开发者。 |
| `settings.ini` | **首次运行自动生成**（默认值 + 注释），可手工编辑；**不进发布包**。 |
| `settings.ini.bak` | 保存设置时自动产生的上一次配置（原子写备份）；**不属于交付物**，可随时删除。 |

**环境要求**

- Windows 7 SP1 及以上（实测目标 Win10/11），.NET Framework **4.x 运行时**（Win10/11 自带 4.8）。
- 编译只需 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（32 位系统回退 `Framework`）。
  **不需要** Visual Studio / MSBuild / .NET SDK / NuGet。
- 运行不需要管理员权限，不写注册表，不联网，不注入游戏进程，不装全局钩子，不模拟输入。

**构建**

```bat
build.bat
```

行为约定（不要改坏）：先编译到 `obj\GuideOverlay.exe`（输出名保持最终名，否则 exe 版本资源里的 InternalName/OriginalFilename 会带 `.new`），**只有编译成功才替换**旧的 `GuideOverlay.exe`；
编译失败打印「[失败] 编译错误，旧 GuideOverlay.exe 未改动」+ `pause` + `exit /b 1`；
旧 exe 正在运行（`move /y` 失败）打印「[失败] 程序正在运行，请先关闭悬浮窗后再构建」；
成功打印 exe 全路径与字节数 + `BUILD OK` + `exit /b 0`。不自动运行程序。

等价 csc 命令：

```bat
csc.exe /nologo /target:winexe /codepage:65001 /optimize+ /platform:anycpu ^
        /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
        /out:GuideOverlay.exe Program.cs
```

`/codepage:65001` 是**必须**的：源码含大量中文，缺了会乱码甚至编译报错（源码同时按 UTF-8（含 BOM）保存）。

**自测**

```bat
GuideOverlay.exe --selftest > selftest.log 2>&1
echo %ERRORLEVEL%
```

或双击 `selftest.bat`。成功时最后一行 `PASS`、退出码 0，并在 exe 同目录生成
`selftest-1.png`～`selftest-4.png`（适应窗口 / 200% / 下一张 / 透明度 60%）。

**版本查看**：exe 右键「属性 → 详细信息」（文件版本 `1.1.0.0`），或设置面板「诊断」区。

---

## 2. 代码组织（单文件内的区块顺序）

`Program.cs` 是**单文件**，按固定顺序分成 10 个区块，每个区块以 `// 区块名 — 说明` 注释开头
（区块名是中文职责名，不是文件名）。其中「共享字段」「主窗口」「主窗口绘制」三段都是
`partial class OverlayForm`，由编译器合成同一个类。
**查找代码请用类型名/方法名搜索**（区块起始行会随改动漂移；下表行号是 2026-11-10 基线，
改动后请跑 `交付\check-docs.ps1` 复核并更新）。

| 区块（注释名） | 起始行 | 职责 |
| --- | --- | --- |
| 基础定义 | 33 | `AppInfo`、枚举、`ThemeTokens`、`Layout`（DIP 常量 + Px/Dip）、`ViewState`、`UiFonts`、`DpiHelper`、`POINT/RECT/Win32Const/NativeMethods`、`IntegrityProbe`（低权限检测）、`Util`、程序集版本特性 |
| 共享字段 | 603 | `OverlayForm` 的字段契约（交互与绘制两个 partial 共用，见 §4.1） |
| 视图几何 | 696 | **纯函数**几何/数学：缩放、锚点、夹取、窗口矩形/预设/吸附、工具条/卡片/toast/滚动条几何、长图、切片命名、自然排序。无副作用，selftest 直接断言 |
| 设置与热键 | 1842 | `AppSettings`、`SettingsStore`（ini 解析/夹取/原子写/回退）、`HotkeyManager`（注册/改键/校验/黑名单）、`Diagnostics`（崩溃日志/诊断包） |
| 图片库 | 4128 | 递归扫描（自然排序/跳过原因/切片产物过滤）、头部尺寸探测、解码 + LRU 缓存 + EXIF + GIF 首帧、预取、切片导出、`FolderWatcher` |
| 主窗口 | 5515 | 门控状态机、首启引导、鼠标/键盘、热键分发、右键菜单、换图、视图、拖放、保存防抖、设置面板单例 |
| 主窗口绘制 | 9616 | 全部绘制：图片（可见区/缓存/插值）、工具条、空状态卡片、引导提示条、toast、长图滚动条、描边、字体/位图缓存 |
| 设置面板 | 10955 | 非模态设置面板（图片/窗口/显示/缩放/热键/诊断六分区）+ `HotkeyCaptureDialog` |
| 自测 | 13484 | `--selftest`：临时环境、4 步截图、**≥577 条断言**（含性能量化）、PASS/FAIL、日志 |
| 程序入口 | 16538 | DPI 感知、`--selftest` 分派、单实例 Mutex、低权限提示、全局异常兜底、`Application.Run` |

**类清单**

```
OverlayForm (partial, sealed, : Form)   ← 共享字段 + 主窗口 + 主窗口绘制 三个 partial
SettingsForm (: Form) / HotkeyCaptureDialog (: Form)   ← 设置面板
AppSettings / SettingsStore / HotkeyManager / HotkeyEntry / Diagnostics / DiagnosticsContext  ← 设置与热键
ImageLibrary / ImageEntry / SkippedImage / ScanResult / SliceExportResult / FolderWatcher      ← 图片库
ViewMath / ToolbarItem / ToolbarLayout / CardItem / CardLayout / ToastLayout / NaturalStringComparer ← 视图几何
ThemeTokens / ViewState / Layout / Util / DpiHelper / NativeMethods / IntegrityProbe / AppInfo / 各枚举          ← 基础定义
UiFonts / Win32Const / HotkeyNative / DiagNative / CachedBitmap / OverlayMenuColorTable / PathSnapshot ← 内部辅助类
SelfTestRunner                                                                                 ← 自测
Program                                                                                        ← 程序入口
```

合计约 **38 个 class**（含 4 个嵌套类；`OverlayForm` 三个 partial 合成 1 个）、5 个 `struct`、10 个 `enum`，方法约 **570** 个。

---

## 3. 启动流程

```
Program.Main
 ├─ SetProcessDPIAware()                       // 系统级 DPI 感知，必须在任何窗口前
 ├─ args 含 --selftest ? ──yes──> AttachConsole(父进程) → SelfTestRunner.Run() → return 0/1
 │                              （不参与 Mutex、不注册热键、不读写用户 settings.ini、不启动首启引导）
 ├─ new Mutex("Local\GuideOverlay_SingleInstance_Mutex_v5")
 │    └─ 已存在 → 中文提示「已在运行」→ 退出
 ├─ Application.EnableVisualStyles() + SetCompatibleTextRenderingDefault(false)
 ├─ SettingsStore.Load(exeDir, null)          // 失败回退默认值，不崩
 ├─ 低权限检测（IntegrityProbe）→ 是 → 弹一次中文提示「当前权限受限，窗口可能无法切换点击穿透；
 │                                    建议以普通用户权限启动」+「退出 / 仍要运行」
 ├─ Application.ThreadException / AppDomain.UnhandledException → Diagnostics.WriteCrashLog + 中文提示
 ├─ new OverlayForm(settings)
 └─ Application.Run(form)
```

`OverlayForm` 构造顺序：初始化依赖 → 计算/恢复窗口矩形 → 加载图片库 → 建右键菜单 →
`GateTimer`(20ms) / `SaveTimer`(500ms) / toast 计时器 → 首启引导状态（`[meta] first_run`）→ 启动状态提示。
`OnHandleCreated` 里注册全局热键（失败逐条记状态、不崩）；`selfTestMode=true` 时跳过注册、
不启动 watcher、不读写用户配置、不显示引导。

**首次运行引导（20 秒）**：`FirstRun=true`（ini 不存在，或显式 `first_run=1`）时，
启动即处于「锁定交互」12 秒、窗口底部画一条 28 DIP 高提示条：
`按住 Alt 可操作窗口 · 按住 Alt 拖动移动 · Ctrl+Alt+T 锁定交互`。
12 秒后自动回到穿透态；提示条再保留 8 秒后消失并写入 `first_run=0`；
若用户在这 20 秒内按过门控键，立即隐藏提示条并结束引导。

---

## 4. 核心架构

### 4.1 交互模型状态机（最重要）

窗口默认 **鼠标穿透**。可交互状态由 **20ms** 轮询计算（`PollGateState`）：

```
desired = !WindowHidden && (Locked || IsInteractionBusy() || (GateHeld && InWindow))
```

- `GateHeld`：`GetAsyncKeyState` 读门控键（默认 `Alt`，可在设置面板「窗口」区改为 Ctrl/Alt/Shift），**不装钩子**。
- `InWindow`：`GetCursorPos` 是否在窗口矩形内。
- `IsInteractionBusy()`：拖动/拉伸/平移/右键菜单打开/设置面板打开/滚动条拖动 → 保持可交互直到结束，
  否则会出现「拖动中门控键松开 → 窗口瞬间穿透 → 拖拽中断」。
- 注意 `busy` 是**独立项**，不要并进 `GateHeld` 括号（旧 README 写法是错的）。

补充规则：

- **拖动看门狗**：门控 tick 里若 `Capture` 为真但左右键都已松开，清拖动标志并恢复穿透态（防「永久吃点击」；低权限下跳过，避免误伤）。
- **面板解除锁定即时生效**：设置面板打开期间的自动锁定可被用户覆盖，点「取消锁定交互」立即生效，不被 `IsInteractionBusy` 顶回去。
- **首启引导**：引导期间强制 `Locked=true` 12 秒（不改 ini 的三种交互状态），结束写 `first_run=0`。
- 门控键在设置面板改「窗口 → 门控键」：选中即保存并生效（代码每 tick 重读 `Settings.GateKey`，无需重启）。
  手工改 ini 的 `gate_key` 需要重启；写成非 Ctrl/Alt/Shift 的值会回退默认并在启动摘要提示。

`SetInteractive(bool)` 只切两个扩展样式，**绝不碰 `WS_EX_LAYERED`**：

| 状态 | 样式 | 焦点 |
| --- | --- | --- |
| 穿透 | `WS_EX_TRANSPARENT` + `WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW` | 不抢 |
| 门控可交互 | 去 `WS_EX_TRANSPARENT`，保留 `WS_EX_NOACTIVATE` | 不抢（点击不激活） |
| 锁定交互 | 两者都去掉 | 正常激活 |

切换后调 `SetWindowPos(HWND_TOPMOST, NOMOVE|NOSIZE|NOACTIVATE)` 刷新 Z 序。
`WM_MOUSEACTIVATE` 在非锁定态返回 `MA_NOACTIVATE`，双保险。
`CreateParams` 显式加 `WS_EX_TOOLWINDOW`（否则 Alt+Tab 会出现两个窗口）。

### 4.2 绘制管线

- 主窗口是 **layered 风格窗口，禁止用子控件**（子控件不走逐像素 alpha，半透明会变黑）。
  所有 UI 都是 `OnPaint` 自绘 + 手动命中测试。
- 图片绘制只画可见部分：`ViewMath.VisibleSourceRect` 把视口反变换到源图坐标求交，
  目标矩形 = `offset + src * zoom`。**绝不在每帧把整张大图缩到窗口**（4K 图会卡）。
- 适应窗口模式用 `FitCache`（预缩放位图，1:1 贴图），校验 `(index, clientSize, zoom, bitmap)` 后复用。
- 缩小用 `HighQualityBicubic` + `ImageAttributes(WrapMode.TileFlipXY)`（消除边缘白边），
  放大用 `NearestNeighbor`（保持像素锐利）。
- 局部失效：`InvalidateViewport / InvalidateToolbarArea / InvalidateToastArea / InvalidateOverlay`。
- 字体只在 `RefreshThemeFonts()` 建，绝不在 paint 里 `new Font`；所有 GDI+ 对象 `using`/`finally Dispose`。
- 主题色全部来自 `ThemeTokens`（`CurrentTheme`/`Colors`），深色/浅色都能看。
- 首启引导提示条（28 DIP）与长图滚动条（6 DIP）也走自绘；滚动条区域与右下 12×12 DIP 拉伸热区（`Layout.ResizeGripDip`）重叠时，
  命中测试**拉伸角优先**（先判 `ResizeSE`，再判滚动条）。

### 4.3 坐标系统

| 坐标 | 单位 | 用途 |
| --- | --- | --- |
| 屏幕坐标 | 物理像素 | `SetWindowPos`、`GetCursorPos`、`GetWindowRect` |
| 客户区坐标 | 物理像素 | 鼠标事件、绘制、命中测试 |
| 图片坐标 | 源图像素 | 平移/缩放/切片 |
| DIP | 逻辑像素 | `settings.ini`、布局常量、UI 尺寸；`Layout.Px(dip, dpi)` / `Layout.Dip(px, dpi)` |

换算：`screen = image * View.Zoom + View.OffsetX/Y`，反变换 `image = (screen - offset) / zoom`。
DPI 用系统 DPI（`GetDeviceCaps(LOGPIXELSX)`，进程 DPI 感知）。

### 4.4 图片管线（30 + 40）

```
ChangeFolder / ReloadImages
  └─ ImageLibrary.Scan(folder)        递归、跳过 ReparsePoint、跳过 .slices 目录与 *-slice-NN.png、
       │                              自然排序、逐文件 0 字节/格式/头部尺寸/超大 校验 → Skipped 列表
       └─ 指纹判定：文件数 + 总字节 + 最大 LastWriteTimeUtc 未变 → 只刷新跳过列表，不重载当前图
       └─ LoadImageAtIndex(i)
            ├─ ImageLibrary.GetBase(i) 解码（FileShare.ReadWrite|Delete → 不锁文件）
            │    ├─ GIF SelectActiveFrame(0)（首帧）
            │    ├─ new Bitmap(image) 克隆（释放流）
            │    └─ EXIF Orientation 1–8 旋转/翻转
            ├─ CurrentBase = 缓存对象（**不得 Dispose**）
            ├─ ManualRotate != None → CurrentBitmap = 派生副本（CurrentBitmapOwned=true，旧的先 Dispose）
            └─ 视图：长图 → fit-width + 顶部对齐；否则适应窗口
```

- 缓存策略：最多 **3 张**、总解码像素 ≤ **8000 万**（超预算只留当前），`Trim(keepIndex)` 保留当前 ±1 邻居用于预取。
- 预取：仅对**单图像素 ≤ 800 万**且非长图模式的图预取邻居；大图/长图跳过（防内存峰值）。
- 切片产物：导出到 **`源图所在目录\.slices`**（默认），扫描时**不收录** `.slices` 目录和
  `*-slice-NN.png`（Skipped 原因「切片产物（不加入图库）」），避免切片污染图库。
- 自动刷新保留阅读位置：重扫后当前图**路径未变**时，保存并还原 `Zoom / Mode / Offset / 手工旋转 / 长图模式 / 滚动位置`；
  路径变了才重置。
- `ClearCache()` 在**扫描成功、即将写入 `Images` 之前**调用；扫描失败不动缓存（掉盘/目录临时不可用不丢当前图）。
- **重扫后必须重新 `GetBase` 更新 `CurrentBase`**。

### 4.5 设置与热键

- `SettingsStore.Load(exeDir, explicitPath)`：`explicitPath != null` 时严格只读写该路径
  （selftest 注入临时 ini，`StrictPath=true`，失败静默不回退）。
- 默认路径 `exe\settings.ini`；不可写 → `%APPDATA%\GuideOverlay\settings.ini`（`UsingFallbackPath=true`）。
- 写入：`.tmp-<pid>-<guid>` + `File.Replace`（原子，生成 `.bak`），**UTF-8（含 BOM，`new UTF8Encoding(true)`）**；
  `File.Replace` 不可用时先备份旧文件再覆盖拷贝，**任何失败都不删旧文件**，原因写入 `LastSaveError`；
  保存失败会删除本次 `.tmp`，连续失败 30 秒退避（不再每次重试）。
- 读取：若解析结果含 `\uFFFD` → 用 `Encoding.Default` 重读一次；仍失败按回退逻辑并提示「配置编码不是 UTF-8」。
- 启动读配置：`exe\settings.ini` 与 `%APPDATA%\GuideOverlay\settings.ini` 都存在时取 mtime 较新者，
  避免「上次回退到 %APPDATA%、下次又读回 exe 旧文件」导致设置回滚。
- 所有读入值**夹取**：opacity 20–100、尺寸 ≥240×180、布尔只认 0/1、theme/gate_key 白名单、
  folder 无效回退 `exe\images`、recent 逐项校验存在性并去重。
- 默认值：`GateKey=Alt`、`Opacity=85`、`FirstRun` 由 ini 决定（见 §3）、`TopMost=true`、窗口高=工作区 60%。
- 热键 id = `0x4000 + (int)HotkeyAction`（`HotkeyManager.IdBase`），`WM_HOTKEY` 里反查枚举。
  全部带 `MOD_NOREPEAT`。`Rebind` 流程：校验 → 注销旧 → 注册新 → 失败回滚旧并返回中文错误。
- **热键可禁用**：设置面板「热键」区每行有「禁用/启用」按钮；禁用 = `Rebind(action, Keys.None)`，
  ini 里写 `none` 或留空；「启用」恢复默认组合。
- **热键黑名单**（UI 与 ini 两条路径最终都走 `ValidateCombo`）：`Alt+F4`、`Ctrl+C/V/X/Z/A/S/F`、
  `PrintScreen`、`Alt+Tab`、`Ctrl+Esc`、`Ctrl+Alt+Del`、所有 Win 组合。UI 拒绝时中文提示；
  ini 读到黑名单组合 → 回退默认 + 启动摘要提示。
- 录制改键时**挂起全部全局热键**（`HotkeyCaptureDialog` 显示前注销、关闭后 `try/finally` 恢复），
  否则旧热键会吞掉录制按键。
- 1409（`ERROR_HOTKEY_ALREADY_REGISTERED`）→ `HotkeyStatus.FailedInUse`。
  **被占用时窗口聚焦也没用**（按键被占用者拿走），只能改键。

### 4.6 自测架构

`SelfTestRunner.Run()`：

1. 建临时目录（`%TEMP%\GuideOverlay-selftest-<pid>\`；不可写依次回退 exe 同目录 `.selftest-tmp`、`%LOCALAPPDATA%`），
   用 GDI+ 生成 1600×900 / 900×1600 测试图 + 临时 settings.ini。
2. `new OverlayForm(settings, selfTestMode: true)`：不注册热键、不启动 watcher、不碰用户配置、不显示首启引导。
3. 4 步动作 + `DrawToBitmap` 截图 + 断言（尺寸、非空白采样、zoom、index、status、opacity）。
4. 纯函数断言（**577 条**，本次改造 525→577，+52 条）：锚点、夹取、适应公式、自然排序、窗口预设/夹取、工具条几何、
   settings 往返/夹取、扫描跳过、热键解析/校验/黑名单、切片、吸附、长图、最近文件夹、Mutex 不参与；
   新增覆盖：`FirstRun` 三态解析、`IsSliceArtifact`、扫描指纹、`ShouldPrefetch`、面板尺寸自适应、
   `HitTest` 拉伸角优先、`AssemblyVersion.ToString(3) == AppInfo.Version`。
5. **性能量化段（§十二）**：`Stopwatch` 100 次 `ZoomAtPoint`/`ClampOffsets` 取 avg+p95、
   30 次 `DrawToBitmap` 单帧 p95（<33ms）、20 次切图 p95（<500ms）、私有内存峰值、GDI 前后差；
   全部数字写入日志，不靠肉眼。
6. 输出 `PASS`/`FAIL: 原因`、`selftest.log`（UTF-8）、4 张 PNG；退出码 0/1。
   （stdout 被重定向到 `selftest.log` 时只写 stdout，避免两个句柄互相覆盖。）

---

## 5. 关键函数索引

**几何/数学（全部纯函数）**

| 函数 | 职责 |
| --- | --- |
| `ClampManualZoom(z)` / `FitZoom(iw,ih,vw,vh)` / `FitWidthZoom` | 缩放范围与适应公式（适应允许 <0.1） |
| `ZoomAtPoint(oldZoom,offX,offY,newZoom,ax,ay,out nx,out ny)` | 锚点缩放：`p=(a-off)/z; n=a-p*newZoom` |
| `ImageCoordX/Y` | 视口→图片坐标 |
| `ClampOffsets` / `CanPanX/Y` | 平移夹取（大于视口不露底，小于视口居中） |
| `VisibleSourceRect` | 只绘制可见源图区域 |
| `DefaultWindowRect` / `ClampWindowRect` / `PresetWindowRect` | 窗口矩形（默认高=工作区 60%、宽=高×9/16） |
| `SnapMove` / `SnapResize` | 边缘吸附（12 DIP，不跨屏） |
| `ToolbarGeometry` / `HitTestToolbar` | 工具条布局与命中（窄窗按 Close→LongMode→LockImage→Fit 渐进隐藏） |
| `EmptyCardGeometry` / `HitTestCard` | 空状态卡片 |
| `ToastGeometry` | toast 矩形 + 中间省略文本 |
| `ScrollbarTrackRect` / `ScrollbarThumbRect` / `ClampScroll` / `LongImageStepPx` | 长图滚动（滚动条 6 DIP） |
| `IsLongImage` / `SliceCount` / `SliceSourceRect` / `SliceFileName` / `IsSliceArtifact` | 长图判定与切片（index 1 基；切片产物过滤） |
| `CompareNatural` / `NaturalComparer` | 自然排序（数字段按数值，平局按原始段 Ordinal） |

**主窗口**

| 函数 | 职责 |
| --- | --- |
| `PollGateState()` | 20ms 门控状态计算（见 §4.1） |
| `SetInteractive(bool)` | 切穿透/可交互扩展样式 |
| `WndProc` | `WM_MOUSEACTIVATE` / `WM_HOTKEY` |
| `HandleHotkeyAction(action)` | 18 个热键动作分发（禁用项不注册） |
| `OnMouseDown/Move/Up/Wheel/Leave` | 拖动窗口、拉伸、平移、滚轮缩放、工具条/卡片/滚动条交互、双击 |
| `BeginWindowDrag/UpdateWindowDrag/EndWindowDrag` | 中键或 Ctrl+Alt+左键移动窗口 |
| `BeginResize/UpdateResize/EndResize` | 右下 12 DIP 拉伸（最小 240×180 DIP，夹工作区） |
| `BeginImagePan/UpdateImagePan/EndImagePan` | 门控+左键平移图片 |
| `SetImageLocked(locked, toast)` | 「锁图」开关（锁上后门控拖动只移动窗口；仅本次运行） |
| `FitToWindow` / `SetZoom` / `SetZoomPreset` / `Rotate` / `SetLongImageMode` | 视图操作 |
| `LoadImageAtIndex` / `ReloadImages` / `ChangeFolder` / `NavigateImage` / `GoToImage` | 图片切换（Reload 保留阅读位置） |
| `StartWatcher` / `OnWatcherChanged` | 自动刷新（800ms 防抖，UI 线程回调） |
| `SetOpacityPercent` / `SetTopMost` / `SetLocked` / `SetTheme` / `SetPositionLocked` / `SetSnapEnabled` | 显示设置 |
| `ApplyWindowRect` / `GetWindowRectPx` / `SaveWindowRectToSettings` | 窗口矩形（物理像素 ↔ DIP） |
| `HideOverlay` / `ShowOverlay` | 隐藏/呼出（工具条 `×` 与 `Ctrl+Alt+G` 都走这里；隐藏前检查 `CanHide`） |
| `RebindHotkey` / `IsHotkeyRegistered` / `BuildHotkeyTableText` | 热键（含禁用/黑名单校验） |
| `OpenSettingsPanel(section)` / `NotifySettingsPanelChanged` | 设置面板单例 |
| `ShowToast(text, ms, urgent)` | 状态提示（默认 3s，urgent 7s） |
| `ScheduleSave()` / `SaveNow()` | 500ms 防抖保存 |
| `BuildDiagnosticsContext()` | 诊断上下文 |

**绘制**：`OnPaint`、`PaintViewportImage`、`PaintToolbar`、`PaintEmptyCard`、`PaintFirstRunHint`（引导提示条）、
`PaintToast`、`PaintScrollbar`、`PaintInteractiveBorder`、`RebuildFitCache`、`RefreshThemeFonts`、
`DisposePaintResources`、`CurrentToolbarLayout`、`CurrentCardLayout`。

**设置**：`SettingsForm`（六分区 + `ApplyTheme` + `RefreshFromMain` + `RefreshHotkeyTable` + 门控键下拉 +
每行「禁用/启用」）、`HotkeyCaptureDialog`（`ProcessCmdKey` 捕获、Esc 取消、`ValidateCombo` + 表内重复校验、
打开时挂起全局热键）。

**本次改造新增/改动的实现点**（以最终代码命名为准）：`AppSettings.FirstRun` + `[meta] first_run`、
首启引导状态机与 28 DIP 提示条、门控键下拉、`ImageLocked`（锁图）、切片产物过滤/默认 `.slices`、
扫描指纹与阅读位置保持、`ShouldPrefetch`、`SettingsPanelSize`、拖动看门狗、20ms 门控轮询、
热键黑名单、录制挂起热键、BOM ini 写读。

---

## 6. `settings.ini` 完整格式

```ini
; 攻略悬浮窗设置文件（UTF-8 编码）。可手工编辑，多数项重启后生效。
[meta]
first_run=0        ; 首次运行引导：0=已看过；手改成 1 = 下次启动再显示一次引导

[window]
left=1400          ; DIP，相对当前屏幕工作区左上角（首次运行自动计算）
top=120
width=473          ; 示例值；首次运行 = round(高×9/16)，夹 240…1200
height=840         ; 示例值；首次运行 = 工作区高×60%，夹 320…900
opacity=85         ; 20-100，默认 85
topmost=1          ; 1/0
locked=0           ; 锁定窗口位置与大小 1/0
snap=1             ; 边缘吸附 1/0
last_image=03.jpg  ; 相对图片文件夹的路径（找不到时回退第一张）

[images]
folder=images      ; 相对 exe 或绝对路径；无效回退 exe\images
watch=1            ; FileSystemWatcher 自动刷新 1/0
recent1=
recent2=
recent3=
recent4=
recent5=

[behavior]
gate_key=Alt       ; 门控键：Ctrl / Alt / Shift；默认 Alt（Ctrl 常被游戏用作蹲/静步）

[ui]
theme=light        ; light / dark

[hotkeys]
zoom_in=Ctrl+Alt+Up
zoom_out=Ctrl+Alt+Down
prev=Ctrl+Alt+Left
next=Ctrl+Alt+Right
fit=Ctrl+Alt+F
jump1=Ctrl+Alt+1
; ... jump2..jump9
opacity_down=Ctrl+Alt+[
opacity_up=Ctrl+Alt+]
toggle_lock=Ctrl+Alt+T
toggle_hide=Ctrl+Alt+G
; 值改成 none 或留空 = 禁用该热键（设置面板「热键」区每行也有「禁用/启用」按钮）
; 黑名单组合（Alt+F4、Ctrl+C、Ctrl+V、Ctrl+X、Ctrl+Z、Ctrl+A、Ctrl+S、Ctrl+F、
; PrintScreen、Alt+Tab、Ctrl+Esc、Ctrl+Alt+Del、Win 组合）不允许设置，写了会回退默认并提示
```

- 文件由程序首次运行时自动生成；编码 UTF-8（**含 BOM**，程序写入带 BOM；手工编辑用 UTF-8 保存即可）。
- 坐标/尺寸以 DIP 存储，加载时按当前 DPI 转物理像素；记忆位置不在任何屏幕工作区内时回退默认位置
  （拔掉副屏的情况）。删除 `[window]` 的 `left/top` 或整个 `settings.ini` 可恢复到默认窗口位置。
- 设置面板里的改动**立即生效**；手工编辑 ini 的多数项需要重启程序。

---

## 7. 扩展指南

**加一个全局热键动作**
1. 「基础定义」区块的 `HotkeyAction` 枚举末尾加一项（注意值唯一）。
2. 「设置与热键」区块：`HotkeyManager.DefaultCombo`、`DefaultComboText`、`IniKeyOf`、`DisplayNameOf` 各加一条。
3. 「主窗口」区块：`HandleHotkeyAction` 加 `case`。
4. `HotkeyManager.IdBase + (int)action` 自动生效，无需改注册逻辑（记得把新组合加进 `ValidateCombo` 黑名单检查）。

**加一个工具条按钮**
1. 「基础定义」区块的 `ToolbarButton` 枚举加值。
2. 「视图几何」区块的 `ToolbarGeometry` 里 `AddToolbarSpec(...)` 加一项（含宽度 DIP 常量），
   注意窄窗渐进隐藏顺序（Close→LongMode→LockImage→Fit）。
3. 「主窗口绘制」区块遍历 `layout.Items` 绘制（已支持任意项，无需改）。
4. 「主窗口」区块的 `ExecuteToolbarButton` 加 `case`。

**加一个设置分区**
1. 「基础定义」区块的 `SettingsSection` 加值。
2. 「设置面板」区块：导航数组 + `CreateXxxPage()` + `ShowSection` 分支 + `RefreshFromMain` 同步。
3. 控件改动只调 owner 的 setter，再 `ScheduleSave()`。

**加一种图片格式**
1. 「图片库」区块的扩展名白名单（`IsSupportedExtension` 一处集中判断）加后缀。
2. `ImageLibrary.TryReadImageSize` 头部尺寸探测与 `GetBase` 解码路径一般无需改；不支持的编码器
   会走 `Skipped`（原因写进状态提示）。
3. 在自测里加断言：`TryBuildEntry` 对该扩展名返回可加载/跳过原因正确。
   注意：webp 系统无解码器，别指望 `Image.FromStream` 能读。

**加一个右键菜单项**
1. 「主窗口」区块的菜单构建处 `CreateMenuItem` 加一项；勾选态放到菜单刷新逻辑（打开菜单前统一刷）。
2. 菜单项回调只调 owner 的方法（`SetXxx` / `ShowXxx`），不要在里面直接改 settings 字段。
3. 菜单是每次打开重建还是复用实例，跟现有实现保持一致；新项记得在 `RefreshMenuChecks` 里同步勾选态。

**加一条 selftest 断言**
1. 纯函数优先（「视图几何」「设置与热键」区块），实现后直接 `Check(条件, "中文描述")`。
2. 需要窗口的用 `selfTestMode: true` 的 `OverlayForm`，不要在断言里操作真实用户桌面。
3. 断言失败只记 FAIL 并汇总，不中断整个自测；新增断言后 `断言总数` 会变，README/日志里的 525 是历史基线（当前 577），不是硬上限。

**改主题色**：只改 `ThemeTokens.Light()/Dark()`；`Colors` 会传播到主窗口与设置面板。
深色主题还要照顾标准控件（见 §8 第 23 条）。

---

## 8. 已知限制与技术陷阱（改代码前必读）

1. **独占全屏盖不住**：系统限制，必须无边框窗口化。别试图用 `SetWindowPos(HWND_TOPMOST)` 解决。
2. **`WS_EX_LAYERED` 不能手改**：WinForms 自己管理（`Opacity` 属性需要它）；手动 `SetWindowLong`
   会把窗体标记搞乱导致整窗不绘制。穿透/交互只切 `WS_EX_TRANSPARENT`/`WS_EX_NOACTIVATE`。
3. **layered 窗口不要加子控件**：半透明区域会变黑。所有 UI 自绘。
4. **不要每帧整图缩放**：用 `VisibleSourceRect` + 目标矩形，适应窗口用 `FitCache`。
5. **GDI+ 对象必须 Dispose**：位图、字体、画刷、Pen、ImageAttributes。`GetGuiResources` 可用来验证泄漏。
6. **`Image.FromFile` 会锁文件**：必须 `FileStream(FileShare.ReadWrite|Delete)` + `Image.FromStream` +
   `new Bitmap(image)` 克隆，然后释放流。
7. **`Cursor.Position` 不适合高频轮询**：用 `GetCursorPos`（内部会做 `Cursor.Position` 类似的转换，但开销低）。
8. **`GetAsyncKeyState` 只读状态**：不装钩子、不模拟输入，符合「不注入」约束；但门控模式下滚轮
   消息通常到不了悬浮窗（不抢焦点），所以提供 `Ctrl+Alt+↑/↓` 与锁定交互作为替代。
9. **`WM_MOUSEACTIVATE` 返回 `MA_NOACTIVATE`** 可避免点击抢焦点；但右键菜单/设置面板需要焦点，
   打开期间临时 `Locked=true`，关闭恢复；用户在面板里取消锁定要能立即覆盖（见 §4.1）。
10. **`Timer` 歧义**：单文件里同时 `using System.Threading;` 与 `System.Windows.Forms;`，
    裸写 `Timer` 会 CS0104。UI 计时器一律写 `System.Windows.Forms.Timer`。
11. **`Layout` 与 `Control.Layout` 事件重名**：在 `Form` 派生类里裸写 `Layout.Px(...)` 会 CS0079，
    必须写 `global::Layout.Px(...)`（40/50 已如此）。**新增代码请沿用 `global::Layout.`**。
12. **`Form.Opacity` 只在 layered 窗口有效**：设 1.0 与 0.99 行为不同，20%–100% 区间安全。
13. **DPI**：进程是系统级 DPI 感知，副屏与主屏缩放不同时副屏会被系统拉伸发虚（已知限制）。
14. **单实例 Mutex 名**：`"Local\\" + AppInfo.SingleInstanceMutexName`（`Local\` 作用域限定在当前登录会话，
    终端服务/快速用户切换下不会把另一个用户的实例拦掉）；`--selftest` **不参与** Mutex，
    所以自测可以在程序运行时并行跑。
15. **热键被占用**：`RegisterHotKey` 返回 1409 → `FailedInUse`；不要反复重试（会抢别人的）。
    改键**立即生效**（`Rebind` 已回滚保护）；面板每行可「禁用/启用」。
16. **保存防抖**：窗口拖动/拉伸/透明度滑杆会高频改设置，必须走 `ScheduleSave()`（500ms），
    退出时 `SaveNow()`。
17. **文件夹监视**：`FileSystemWatcher` 事件在线程池线程，必须 `SynchronizationContext.Post` 回 UI 线程；
    网络盘/缓冲区溢出会触发 `Error` 事件，写 `LastError` 并提示手动重新扫描，不要崩。
    刷新要保留阅读位置（见 §4.4），并且用扫描指纹避免无谓重载。
18. **WinForms 会在 `Show()` 时用 `CreateParams` 重刷扩展样式**，把 `OnHandleCreated` 里
    `SetWindowLong` 加的 `WS_EX_TRANSPARENT`/`WS_EX_NOACTIVATE` 抹掉。所以样式不能只在
    `OnHandleCreated` 设一次：`ApplyExtendedStyles()` 每次按「期望态」比对后再写，
    `PollGateState()` 每 tick 调一次自愈，`OnShown` 再补一次。（这个 bug 真出现过：启动后
    默认不穿透，点击被悬浮窗吃掉。）
19. **鼠标捕获可能丢**：拖动/拉伸/平移途中 Alt+Tab、UAC、别的程序 `SetCapture`、RDP 切换都会让
    `MouseUp` 永远不到，四个拖动标志卡死 → `IsInteractionBusy()` 恒真 → 窗口不再穿透。
    `OnMouseCaptureChanged`/`OnDeactivate` 必须调 `CancelInteractions()` 收尾（幂等，别重复写盘）；
    门控 tick 里的**拖动看门狗**是最后一道保险（低权限下跳过）。
20. **`Environment.TickCount` 是 32 位**，约 24.8 天回绕；toast 计时等长周期比较用单调时钟。
21. **低完整性 / AppContainer 进程读不到键盘状态**：Windows 的 UIPI 让这类进程的
    `GetAsyncKeyState` 一律返回 0 —— 门控键永远判为「未按下」。这不是逻辑 bug：用资源管理器
    正常双击（Medium IL）启动就一切正常；被沙箱/启动器以 Low IL 拉起才会中招。
    `IntegrityProbe.IsRestricted()`（基础定义区块）用 `GetTokenInformation`
    查 `TokenIntegrityLevel`/`TokenIsAppContainer` 检测；命中时启动弹一次中文提示
    「当前权限受限，窗口可能无法切换点击穿透；建议以普通用户权限启动」+「退出 / 仍要运行」，
    选择继续后保持锁定交互策略（不新增危险行为），并写入诊断与日志。
    **排查这类「按键没反应」问题，先查进程完整性级别**：
    `OpenProcessToken` + `TokenIntegrityLevel`（Low=0x1000 / Medium=0x2000）。
    本机沙箱里所有子进程都是 0x1000，所以自测里注入/读取键盘一律无效（自测不依赖真实按键）。
22. **`RotateFlip` 反复旋转会累积内存**：旋转要基于解码后的**派生副本**，每次旋转前先 Dispose 旧副本，
    只保留当前一份；大图（>800 万像素）连转几次内存会明显上涨。不要对原图反复原地 `RotateFlip`。
23. **深色主题不是只换 `BackColor`**：WinForms 标准控件（`ComboBox`/`NumericUpDown`/`TrackBar`/滚动条/
    `MessageBox`）的深色支持有限，要么单独处理，要么接受"深色里控件仍是系统色"；不要为了完美深色
    去手绘标准控件（成本高、易引入输入法/焦点 bug）。
24. **GDI+ 大图 OOM**：解码前必须先用头部尺寸探测（`TryReadImageSize`）挡掉超大图；
    当前上限是**总像素 > 6000 万**或**单边 > 32767** 跳过；旋转/切片不要整图解码，
    切片按 2000px 高逐片取源矩形。解码、缩放、旋转派生副本都计入像素预算（见 §4.4）。

---

## 9. 排障

| 现象 | 排查 |
| --- | --- |
| 编译报中文乱码 / 语法错（CS1056） | `build.bat` 是否带 `/codepage:65001`；`Program.cs` 是否 UTF-8（含 BOM） |
| 编译报 CS0122（'DoubleBuffered' 不可访问 等） | `Control.DoubleBuffered` 是 protected：用 `SetStyle(ControlStyles.OptimizedDoubleBuffer, true)` 或自绘，不要直接赋值 |
| 编译失败 / 更新 exe 失败 | `build.bat` 失败或 exe 被占用时**不会删旧 exe**；看窗口里的中文提示，关掉正在运行的程序再构建 |
| 程序起不来/闪退 | `%TEMP%\GuideOverlay-crash-*.log`（`Diagnostics.WriteCrashLog`）；或设置面板「诊断 → 导出诊断包」 |
| 悬浮窗不显示 | 是否被隐藏（`Ctrl+Alt+G` 或工具条 `×` 后隐藏）；是否在屏幕外（删除 `settings.ini` 的 `[window] left/top` 或整体删除 settings.ini） |
| 首启没看到引导 / 想再看一次 | 把 `settings.ini` 的 `[meta] first_run` 改成 `1` 重启；或删除 settings.ini（会回到默认配置） |
| 按门控键工具条不出现、也没有关闭按钮 | 先查进程完整性级别（见 §8 第 21 条）：Low IL/AppContainer 下 `GetAsyncKeyState` 恒 0。启动时若弹了「当前权限受限」提示，选「退出」后改用资源管理器正常双击启动 |
| 热键没反应 | 设置面板「热键」区看状态；被占用只能改键；`Ctrl+Alt+方向键` 常被显卡驱动占用；黑名单组合会被拒绝 |
| 门控键不生效 / 想换成别的键 | 设置面板「窗口 → 门控键」改 Ctrl/Alt/Shift（立即生效）；手工改 ini 的 `[behavior] gate_key` 需重启 |
| 自动刷新后图片位置/缩放被重置（旧版） | 新版本已保留阅读位置（当前图路径未变时）；若仍被重置，看是否换了文件夹或图片被改名 |
| 切片导出后图库里没有 | 正常：切片默认导出到 `源图目录\.slices`，且 `*-slice-NN.png` 不会加入图库（§4.4） |
| 图片不显示 | 看状态提示/设置面板「图片」区的跳过列表（格式/损坏/超大）；webp 系统无解码器；超大 = 总像素 >6000 万或单边 >32767 |
| 自测失败 | 看 `selftest.log` 的 `[FAIL]` 行；`--selftest` 不依赖用户配置，失败基本是代码问题 |
| GDI 泄漏 | `Diagnostics.GdiObjectCount()`；正常运行时该值应稳定（不随翻页/缩放单调增长） |

---

## 10. 验收对照（提示词 → 实现）

| 要求 | 位置 |
| --- | --- |
| 单文件 C# 5、WinForms + System.Drawing、无第三方 | `Program.cs` 全文；`build.bat` |
| 默认鼠标穿透、门控键、锁定、隐藏 | §4.1 `PollGateState` / `SetInteractive` |
| 单实例、`--selftest` 隔离 | `Program.Main` |
| exe 版本资源（`1.1.0.0`） | §1 / §0；`AssemblyVersion`/`FileVersion` 特性；诊断区显示版本 |
| 门控键默认 Alt + 设置面板可改（立即生效） | §4.1 / `SettingsForm` 窗口分区 |
| 首次运行 20 秒引导（锁定 12s + 提示条） | §3 / §4.1 |
| 工具条 `×` = 隐藏；彻底退出走右键菜单 | §4.1 / §5（`HideOverlay`） |
| 18 个热键可禁用、录制时挂起、黑名单校验 | §4.5 |
| 图片扫描/自然排序/跳过原因/6000 万像素 + 单边 32767 上限 | `ImageLibrary.Scan` / `TryReadImageSize` / `IsOversized` |
| 懒加载缓存 3 张 / **8000 万**像素预算 / 预取仅 ≤800 万 / GIF 首帧 / EXIF | `ImageLibrary.GetBase` / `PrefetchNeighbors` |
| 切片默认导出到 `.slices`、切片不进图库 | §4.4 / `IsSliceArtifact` |
| 自动刷新保留缩放/位置（扫描指纹） | §4.4 / `ReloadImages` |
| 缩放 0.1–10×、适应窗口 <0.1、锚点不漂移、夹取 | `ViewMath.ZoomAtPoint/ClampOffsets/VisibleSourceRect` |
| 键盘/热键缩放以光标为锚点（鼠标不在窗口内退化中心） | `SetZoom` 锚点参数 / `HandleHotkeyAction` |
| 浅色 GUI、工具条（含「锁图」开关）、空状态卡片、toast、右键菜单、设置面板 6 分区 | §4.2 / §5 |
| 透明度 20–100 三处同步；默认 85 | `SetOpacityPercent`（主窗口 + 菜单勾选 + 面板滑杆） |
| 多屏/DPI/记忆位置；默认高=工作区 60% | `ClampWindowRect` / `DpiHelper` / `TryRestoreWindowRect` / `DefaultWindowRect` |
| settings.ini 原子写（UTF-8 含 BOM）+ %APPDATA% 回退 + 读侧编码回退 | `SettingsStore` |
| v5：自动刷新/拖入文件夹/最近 5/吸附/预设/旋转/长图/切片/改键/崩溃日志/深浅主题 | 见 §2 各区块 |
| `--selftest` 4 步截图 + 断言 + PASS/FAIL + 退出码 | `SelfTestRunner` |

### 与提示词的有意偏差（2 处，均为口头要求、待确认）

> 上一版曾把工具条 `×` 改成"彻底退出"，本次已按提示词改回**隐藏**（退出走右键菜单），故不再列为偏差。

1. **工具条去掉 `100%` 按钮**。提示词 §八 8.1 / 提示词 §八 8.2 / §十五 / §十九 19.9 的工具条清单里有 `100%`；
   用户口头要求主界面不需要它（**无书面记录，待确认**）。
   `100%` 功能仍在：右键菜单「缩放 ▸ 100%」、设置面板「缩放」区、「缩放」预设，未删任何能力。
   工具条为 `◀ | 页码 | ▶ | 适应窗口 | 锁图 | [长图] | 设置… | ×`，总宽不变（锁图沿用原 100% 的 52 DIP）。
2. **新增「锁图」开关**（提示词没有）。用户口头要求：「按住门控键有时只能拖图片、拖不动窗口」（**待确认**）。
   提示词 §二 的原逻辑是「可平移则平移图片，否则移动窗口」；新增开关让用户可**强制**：
   锁上后按住门控键拖动一律移动窗口。实现为 `OverlayForm.ImageLocked` +
   `ViewMath.ShouldPanImage(canPan, imageLocked)`（纯函数，selftest 4 条断言），
   入口：工具条「锁图」、右键菜单「锁图（拖动只移动窗口）」、设置面板「缩放」区复选框。
   仅本次运行有效，不写入 settings.ini（与旋转一致）。

---

## 11. 发布与分发

### 11.1 两种发布包

| 文件 | 玩家包 | 开发包 |
| --- | --- | --- |
| `GuideOverlay.exe` | ✅ | ✅ |
| `使用说明.md` | ✅ | ✅ |
| `images\01-示例竖版.png`、`images\02-示例横版.png` | ✅ | ✅ |
| `Program.cs` / `build.bat` / `selftest.bat` / `README.md` | ❌ | ✅ |
| `settings.ini` / `settings.ini.bak` / `images\755…png`（用户数据） | ❌ | ❌ |

- **玩家包**：4 个文件，未压缩 **490,875 B**（zip 实测 **206,582 B**，SHA256 由脚本打印）——解压 → 双击即用。
- **开发包**：8 个文件（玩家包 4 + `Program.cs`/`build.bat`/`selftest.bat`/`README.md`），
  未压缩约 **1.19 MB**（zip 约 **384 KB**；精确字节数与 SHA256 以 `make-package.ps1` 输出为准）——可本机重建、可跑自测。
- 两种包都保留顶层文件夹 `GuideOverlay-<玩家包|开发包>-v1.1\`，解压不散落。

### 11.2 打包脚本

```powershell
# 预演（只打印将打包的文件）
.\交付\make-package.ps1 -Kind Player
.\交付\make-package.ps1 -Kind Dev

# 实际生成 zip + 打印 SHA256
.\交付\make-package.ps1 -Kind Player -Execute
.\交付\make-package.ps1 -Kind Dev -Execute
```

- 输出目录 `D:\Desktop\工具\_发布\`；文件 `GuideOverlay-玩家包-v1.1[.zip]` / `GuideOverlay-开发包-v1.1[.zip]`。
- 脚本内置白名单与安全断言：缺文件直接报错；白名单里混入 `settings.ini*` 或 `images\755*` 也会直接报错。
- 默认工程目录是本脚本上上级的 `悬浮窗\GuideOverlay`（回写后的原项目）；在审查副本里打包时加 `-ProjectRoot <工程目录>`。
- 打包前会**断言 exe 的 FileVersion = 1.1.0.0** 并与 `Program.cs` 的 `AssemblyVersion` 交叉核对：版本不符（例如误用旧工程）直接报错，避免「v1.1 标签 + 旧内容」的包。
- 包内**绝不包含**：`settings.ini`、`settings.ini.bak`、用户攻略图、`selftest.log`/`selftest-*.png`、
  `.selftest-tmp`、`~*probe*.tmp`、`_归档`、`_build`、`_发布`。

### 11.3 升级与配置保留

- 玩家包不含 `settings.ini`：新版**覆盖解压到原文件夹**即可保留 `settings.ini` 和用户自己的图片。
  解压到**新目录**会"丢配置"（配置读写都在 exe 同目录），这是设计使然，发布说明要写清。
- 升级前建议备份 `settings.ini`（程序保存时也会自动留 `settings.ini.bak`）。
- 无自动更新、不联网、无更新检查；版本以 exe 文件版本（`1.1.0.0`）为准。

### 11.4 SmartScreen / 杀软预期（发给别人的现实问题）

- exe **未签名**。经浏览器/网盘/QQ 下载后会带 Mark-of-the-Web，首次运行大概率弹
  「Windows 已保护你的电脑」：点「**更多信息 → 仍要运行**」。
- 杀软可能对「全局热键 + 读取键盘状态 + 未签名 csc 产物」做启发式拦截：加入信任/白名单；
  不要直接双击**聊天窗口/网页里**的 exe（会被低权限宿主拉起，门控按键失效，见 §8 第 21 条）。
- 发布 zip 时附 **SHA256**；接收方可先校验再运行。最干净的替代是给源码版，让朋友本机 `build.bat`
  （本地产物没有 MOTW）。
- 可选：把 exe 提交 Microsoft Defender 误报申诉（wdsi 文件提交页），减少长期误报。
  代码签名成本高、自签名证书对 SmartScreen 无效，个人项目不建议。

### 11.5 发布检查清单

- [ ] `build.bat` → `BUILD OK`，产物路径/字节数正常
- [ ] `(Get-Item .\GuideOverlay.exe).VersionInfo.FileVersion` = `1.1.0.0`
- [ ] `selftest.bat` → `EXITCODE=0`，`失败 0`，4 张 PNG 470×835（取决于当前窗口几何）
- [ ] `make-package.ps1 -Kind Player -Execute` → 包内只有 4 个文件，无禁止项
- [ ] `使用说明.md` 含 SmartScreen 处理、版本查看、改键生效口径
- [ ] zip 附 SHA256；发布说明写"**覆盖解压到原文件夹**以保留配置"
- [ ] 新机器实测：双击 → 看到 20 秒引导提示 → 按住 Alt 出现工具条（一次成功率 ≥ 90%）

---

## 12. 工程化下一步（建议，尚未实施）

- `git init` + `.gitignore`：`GuideOverlay.exe`、`settings.ini`、`settings.ini.bak`、`selftest.log`、
  `selftest-*.png`、`.selftest-tmp/`、`*.tmp`、`_归档/`、`_发布/`。
- `CHANGELOG.md`：把本次行为变更（版本资源、Alt 门控、首启引导、`×` 隐藏、热键禁用、切片 `.slices`、
  刷新保位置、默认 85%/60%、单边 32767）写成 v1.1 条目。
- `LICENSE`：自用可写"保留所有权利 + 免责声明"；`images\755…png` 是**第三方攻略图**，不进仓库、不进包。
- 文档机检：`交付\check-docs.ps1`（§2 行号 / BOM / exe 版本 / selftest 断言数），改完代码顺手跑一次。
