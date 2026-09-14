# AutoClickerTool 项目手册（写给未来的 AI 维护者）

> 读本手册即可上手修改本项目，**不需要通读源码**。若手册与代码冲突，以代码为准并顺手更新本手册。

## 1. 项目是什么

Windows 上的鼠标键盘自动化工具（WinForms 桌面应用），面向游戏挂机/自动点击场景。主要功能：

1. **鼠标连点** — 固定间隔自动点击，可选固定坐标或跟随光标、次数限制
2. **键盘连按** — 定时点按或按住；按键可用下拉框选单个键，或点「点击按键」按钮**捕获**（可捕获多个同时按下的键，如 `Shift+A`，捕获结果用 `Hotkey` 表示、新键替换旧键）
3. **录制回放** — 全局低级钩子录制真实鼠标/键盘操作成宏，可编辑、保存、按倍速循环回放；「宏库」页管理已存宏（▶ 快速触发、重命名、创建副本、删除）并可**自动启动程序**（回放开始/结束时）
3.1 **运行限制（连点 / 连按 / 回放 三者统一）** — 三个页面各放一个 `RunLimitBox`（同一 Size/Location，见 `RunLimit.cs`），三个选项：**指定次数 / 无限循环 / 运行时长(时·分·秒) 或 运行到指定时刻**；③ 里“时长”与“截止时刻”双向同步（改时长 → 截止 = 现在+时长；改截止 → 反算时长）
4. **全局热键** — 5 个功能开关支持任意组合键（含多键组合、鼠标侧键、媒体键）
5. **按键音效** — 任意按键可绑定 wav/mp3 音效，按键即播、新键覆盖旧音效（覆盖式播放）
6. **拟人化** — 高斯分布间隔、贝塞尔移动轨迹、落点漂移、点击微拖等，降低"脚本感"被游戏统计检测识别的概率（含总开关）
7. **三套输入注入方式** — SendInput / SendMessage / Interception 驱动级
8. **6 套主题 + 中英双语**，全部自绘控件（Clay 控件库），窗口边框/标题栏跟随主题
9. **每显示器 DPI 感知** — 跨屏拖动/缩放变化自动重建布局，不会出现控件溢出窗口
10. **开机自启动 + 静默启动 + 系统托盘** — 可选随 Windows 启动；静默启动时开机不弹窗、驻留托盘（双击托盘图标打开主界面，右键菜单退出）

## 2. 技术栈与硬约束（最重要）

- **纯 C# / WinForms / .NET Framework 4.0**，用系统自带 `csc.exe` 直接编译，**零外部依赖**（无 NuGet、无第三方 DLL）
- **编译方式**：在 src 下运行 [build.bat](../src/build.bat) → 输出 `..\AutoClicker.exe`。改完代码必须跑它验证编译通过
- **语法上限 = C# 5**：禁止字符串插值 `$""`、`?.`、`??`、`nameof`、表达式体成员、局部函数、`out var`、`Math.Clamp`。已有代码风格就是模板
- **中文文件路径**：编译参数带 `/codepage:65001`，源码统一 UTF-8
- 新增 `.cs` 文件后**必须把它加进 build.bat 的编译列表**，否则不会参与编译
- **应用图标**：`src\app.ico` 经 `/win32icon:app.ico` 嵌入 exe（窗口/任务栏/托盘共用）；由 [make_icon.ps1](../tools/make_icon.ps1) 生成（多尺寸 PNG 打包 ICO，改图标后重跑该脚本再 build）
- 编译前若 AutoClicker.exe 正在运行，会因文件占用失败 → 先关闭程序
- 所有 Win32 API 走 [NativeMethods.cs](../src/NativeMethods.cs) 的 P/Invoke，不引入新 DLL

## 3. 文件地图（src/）

| 文件 | 职责 |
|---|---|
| [Program.cs](../src/Program.cs) | 入口：`EnablePerMonitorDpiAware()`（PMv2 感知，失败回退系统感知）+ 启动 MainForm |
| [MainForm.cs](../src/MainForm.cs) | **主界面（~3200 行，最大文件）**：7 个胶囊标签页、配置加载/保存、事件列表虚拟模式渲染、各引擎开关的桥接、DPI 同步（SyncDpi）、DWM 边框主题、标题栏非客户区自绘置顶图钉、点击空白处让输入框失焦 |
| [Clay.cs](../src/Clay.cs) | **自绘控件库**：`Dpi` 缩放、`Clay` 绘制工具（圆角/阴影/渐变）、`ClayKit.InputShell`、`ClayButton`（**文字放不下自动缩字号** FitFont，下限 7pt 后 EndEllipsis）、`ClayCheck`、`ClayRadio`、`ClayGroup`、`ClayPanel`、`ClayNumericUpDown`（自绘▲▼）、`ClayComboBox`（OwnerDraw 自绘下拉）、`ClaySlider`（主题滑块）、`ClayMenuColorTable`/`ClayMenu`（主题右键菜单） |
| [Theme.cs](../src/Theme.cs) | 6 套主题调色板 + 风格参数（Dark/Glow/Bevel/Radius）。`Theme.Current` 全局单例，控件 OnPaint 实时读取实现换肤 |
| [Anim.cs](../src/Anim.cs) | **轻量动效引擎**：单个全局 Timer(16ms) 驱动，指数平滑逼近目标值（可中断/可重定向，等效可中断的 ease-out transition）；`Anim.To(setValue, current, target, tauMs)`；`EaseOutCubic`/`EaseInOutCubic`；`Anim.Enabled=false` 时全部瞬时（等效 prefers-reduced-motion）。空闲自动停 Timer |
| [Log.cs](../src/Log.cs) | **文件日志**：写 exe 同目录 `log.txt`，`Log.Info/Warn/Error`，**异步缓冲写入**(不阻塞钩子回调，消息中 `\r\n` 转义防行注入)，超 2MB 轮转 rename 成 `log.txt.old`；退出前调 `Log.Flush()` 落盘；写失败静默忽略 |
| [Util.cs](../src/Util.cs) | 共享小工具：`SleepInterruptible(ms, alive)` 可中断分段休眠(三引擎共用，新文件需在 build.bat 编译列表中) |
| [VersionInfo.cs](../src/VersionInfo.cs) | **集中版本号**（`Version` 常量）：窗口标题/关于/状态栏/日志/发布脚本共用，改版只改这里 |
| [Lang.cs](../src/Lang.cs) | 双语字典：以**英文原文为 key**，`Lang.T(key)` 按 `Lang.Code`("zh"/"en") 翻译；控件 `Name` 属性存英文原文，语言切换时 `ApplyLangWalk` 按 Name 递归刷新 |
| [InputSimulator.cs](../src/InputSimulator.cs) | **输入注入中枢**：鼠标/键盘事件按 `Method` 路由到 SendInput/SendMessage/InterceptionDriver。MoveTo 内部按拟人化走贝塞尔轨迹；Click 含按下-抬起微拖 |
| [Humanizer.cs](../src/Humanizer.cs) | 拟人化引擎：`Enabled` 总开关 + 间隔(高斯分布+偶发犹豫)/落点(抖动+漂移)/按键时长/轨迹(贝塞尔+Fitts 时长+smoothstep 加减速)四个子开关。**注意内部用 lock 保护共享 Random** |
| [InterceptionDriver.cs](../src/InterceptionDriver.cs) | Interception 驱动加载（DLL 迟到重试、空壳兜底） |
| [HotkeyManager.cs](../src/HotkeyManager.cs) | **全局热键引擎**：键盘+鼠标低级钩子监听，`Hotkey`（修饰键+触发键集合，多键组合）解析/显示/冲突检测。`GetName` 含左右修饰键精确名与媒体键名 |
| [MacroRecorder.cs](../src/MacroRecorder.cs) | **宏录制器**：低级钩子录全局操作。**按键精灵式合并**：连续移动合并为一条"移动到终点"(500ms 停顿阈值)；短按(≤250ms)合并为单击/按键事件；长按保留按下/抬起 |
| [MacroPlayer.cs](../src/MacroPlayer.cs) | 宏回放线程：按 DelayMs 分段 sleep 后 PlayEvent，支持倍速/循环；Move 事件的轨迹移动从延迟预算中扣除时间；**按住状态是 `Run()` 局部 HeldState**(停止时 finally 补发全部抬起)；「运行到时刻」跨天判定(目标时刻已过=次日)；**代际计数防 Stop→Start 竞态** |
| [AutoClicker.cs](../src/AutoClicker.cs) | 鼠标连点引擎（后台线程循环，代际计数防竞态） |
| [KeyboardSpammer.cs](../src/KeyboardSpammer.cs) | 键盘连按引擎（Tap/Hold 两模式，**用 `Hotkey Combo` 支持多键同时按下**，代际计数防竞态） |
| [RunLimit.cs](../src/RunLimit.cs) | **三个功能共用的运行限制控件**：`RunLimitMode`(Count/Infinite/Duration) + `RunLimitBox`（指定次数 / 无限循环 / 运行时长↔截止时刻双向同步）。三个页面用**同一 Size/Location** 嵌入（Run options 卡片 y=158 高 118，控件 12,30,516,78），保证位置一致 |
| [SoundFx.cs](../src/SoundFx.cs) | 按键音效：`SfxPlayer`（MCI 播放 wav/mp3，覆盖式：新播放前 stop+close 旧音效；`WarmUp()` 预热）+ `SfxManager`（键盘钩子按绑定表触发，只监听不拦截，过滤注入按键）。**所有 MCI 调用都在一条常驻线程 `SfxPlayer` 上串行执行**（见坑 28） |
| [AppConfig.cs](../src/AppConfig.cs) | 配置模型（JavaScriptSerializer 序列化到 `config.json`）+ `MacrosDir`/`SoundsDir` 目录常量与 `EnsureDataDirs()` |
| [AutoStart.cs](../src/AutoStart.cs) | **开机自启动**：`IsEnabled()`/`SetEnabled(bool)` 写/删 `HKCU\...\CurrentVersion\Run` 的 `AutoClickerTool` 值（值=带引号 exe 路径，失败静默） |
| [EventEditForms.cs](../src/EventEditForms.cs) | 两个小对话框：`DelayEditForm`（改延迟）、`EventAddForm`（添加宏事件，含单击/按键点按类型） |
| [HotkeyCaptureForm.cs](../src/HotkeyCaptureForm.cs) | 热键捕获对话框：**低级钩子捕获**（支持媒体键/侧键），按下组合→全松开→返回 `Captured`（Esc 取消）；可选 hint 参数被音效绑定复用 |
| [WelcomeForm.cs](../src/WelcomeForm.cs) | 首次启动欢迎窗口：选择默认语言 + 功能介绍 + 已阅关闭（`MainForm` 在 `config.json` 不存在时于 `Shown` 事件弹出） |
| [AboutForm.cs](../src/AboutForm.cs) | 「关于」对话框：版本/作者/项目主页/开源申明(MIT)/免责声明；作者与网址在文件顶部 `AUTHOR`/`WEBSITE` 常量处替换，双语文本在 `ApplyTexts` 里维护。入口在底部状态栏「关于」按钮 |
| [app.ico](../src/app.ico) | 应用图标（16~256px 多尺寸，`/win32icon` 嵌入 exe，窗口/任务栏/托盘共用；由 `tools/make_icon.ps1` 生成） |
| [使用说明.txt](../使用说明.txt) | 用户手册 |
| [config.json](../config.json) | 运行时生成的设置存档（删掉=恢复默认） |

## 4. 关键数据流与机制

### 4.1 宏录制 → 回放
- `MacroRecorder` 用 `WH_KEYBOARD_LL`/`WH_MOUSE_LL` 全局钩子录制。**过滤规则**：带注入标记（LLKHF_INJECTED/LLMHF_INJECTED）的事件不录；已绑定的热键触发键不录（防止回放误触开关）
- **合并逻辑（按键精灵风格）**：连续 WM_MOUSEMOVE 且距上条消息 ≤500ms → 只更新最后一条 Move 的 X/Y 并累加 DelayMs；鼠标键/键盘键按下后 ≤250ms 内抬起且无其它事件插入 → Down 事件改 Kind 为 LeftClick/RightClick/MiddleClick/KeyTap（枚举值 10~13，向后兼容旧存档），Up 丢弃；长按保留 Down/Up 两条
- `MacroEvent`：Kind + X/Y + Data(键码/滚轮量) + Combo(组合键字符串, 仅 KeyCombo* 用) + DelayMs（**与上一事件的间隔**，编辑事件时记住这个语义）。新增枚举 `Delay=14`(纯延迟)、`KeyComboTap=15`(组合键点按)、`KeyComboDown=16`(组合键按下)、`KeyComboUp=17`(组合键抬起)；组合键存 Combo 字段，回放走 `InputSimulator.HotkeyDown/Up/Tap`
- 录制时列表用 `ListView.VirtualMode` 实时刷新，20 万条也不卡；`_statusTimer` 每 300ms 触发刷新+DPI 同步
- 事件列表**右键菜单**：编辑延迟/插入事件/删除/上移/下移（`ClayMenu.Build()` 主题化）
- `MacroPlayer` 回放：Sleep(拟人化 DelayMs ÷ 倍速) → PlayEvent；Stop() 用 volatile 标志 + 10ms 分段 sleep 保证快速响应；Move 事件开启轨迹时先扣除预估轨迹时长再走 `MoveToWithTrajectory`
- 保存/加载：`JavaScriptSerializer` 序列化 `List<MacroEvent>` 为 JSON，对话框默认定位 `Macros` 文件夹（启动时自动创建）

### 4.2 热键引擎
- `Hotkey` = `Modifiers`(Ctrl/Alt/Shift/Win) + `Keys`(任意键，**可多键同时按**)。左右修饰键码（0xA0-A5）经 `Normalize()` 归一到通用码；显示名走 `GetName`（左右修饰键/媒体键精确名，`Lang.T` 本地化）
- 文本往返：`Hotkey.Parse("Ctrl+Shift+K")` ↔ `ToString()`，双向兼容（显示名如"鼠标X1"、"音量+"也可反解析）
- `HotkeyManager`：两个低级钩子维护 `_pressed` 集合，新按下的键补全某组合时 `TryFire`（按住期间只触发一次）。只响应非注入输入
- 录制时 `IsHotkeyKey(vk)` 过滤：vk 出现在任何绑定的 Keys 里就不录
- **回放期间抑制自触发**：Interception 驱动注入无 INJECTED 标记，`MacroPlayer` 运行时 MainForm 置 `HotkeyManager.Suppress`/`SfxManager.Suppress`，回放结束(Finished)恢复；`TryFire` 里 **Play(F8) 与 StopAll(F12) 例外始终放行**——否则回放一开始就停不下来（见坑 26）

### 4.3 三套注入方式（Advanced 页切换）
| 方式 | 原理 | 适用 |
|---|---|---|
| SendInput | 系统标准注入，带"注入"标记 | 默认，兼容性最好 |
| SendMessage | 直接 PostMessage 到目标窗口（绕过注入标记检测） | 游戏查 INJECTED 标记时；对 RawInput 游戏无效 |
| InterceptionDriver | 驱动层注入，与真实硬件无异 | 最彻底；需装驱动 + interception.dll 放 exe 旁 |

### 4.4 拟人化（防脚本检测）
- **总开关 `Humanizer.Enabled`** + 四个子开关：Timing（高斯分布间隔 ±N%，3σ 覆盖；3% 概率插犹豫停顿）、Position（±N 像素抖动 + 每次 ±1px 缓慢漂移游走）、PressDuration（高斯 40~180ms，默认固定 20ms）、Trajectory（贝塞尔曲线+随机曲率、Fitts 定律时长、smoothstep 加减速）
- 关闭总开关 = 全部固定值（与原版行为一致）；关闭轨迹 = 瞬移
- 高级设置页的总开关控制子项 Enabled 状态（`UpdateHumanizeChildState`）

### 4.5 按键音效
- **v5 场景化**：`SfxScenes: Dictionary<string, SfxSceneData>`(场景名→绑定数据, 键 "" = 默认场景) + `SfxCurrentScene`。场景 = `Sounds\<场景名>` 子文件夹 + 独立绑定表；文件路径 = `CurrentSfxFolder() + 文件名`。旧扁平字段 `SfxBindings/SfxBindingVolumes/SfxComboBindings/SfxComboVolumes` 仅作 v4→v5 迁移（`Migrate` 归入默认场景）。`SanitizeUntrusted` 对每个场景的绑定字典都要做纯文件名校验
- 单键/组合绑定存场景数据内：键必须用字符串（见坑 13）。`SfxManager` 全局键盘钩子监听（**只监听不拦截**，与热键/录制并行），维护 `_down` 归一化按下集合：单键命中 `Bindings` 即播，组合键 `Satisfied()`(所有修饰键+键都按住)即播。**钩子回调只入队**（`Enqueue` 只保留最新一条保持覆盖式语义），后台线程执行 `SfxPlayer.Play(path, volume)`（MCI：新播放前 stop+close 旧的 = 覆盖式，`setaudio <alias> volume to N`，waveaudio 支持、mpegvideo 不支持静默忽略）。过滤注入按键（宏回放不触发音效）；回放期间 `Suppress` 抑制
- 音效页：**场景下拉（`cboSfxScene`，枚举 Sounds 子文件夹 + 配置里的场景键；`RefreshSfxScenesCombo`）** + **新建场景**（`CreateSfxScene`：弹窗输入名字 → 建子文件夹 + 空绑定数据 + 切过去；名字校验空/非法字符/重复）+ 总开关 + **全局音效音量滑块**（`sldGlobalVolume` 0~100 → `SfxVolume`，总音量）+ **选中项音量滑块**（`sldKeyVolume`，该键的**相对**音量，缺省 100%）+ 添加绑定（复用 HotkeyCaptureForm 捕获**单键或组合键**，文件复制进**当前场景**文件夹）+ 删除/试听/打开**当前场景**文件夹。统一列表用内部 `SfxKey` 条目(单键/组合键)填充；滑块是自绘 `ClaySlider`（不用系统原生 TrackBar）。**实际响度 = 单键相对音量 × 全局音效音量 ÷ 100**（`EffectiveSfxVolume`），全局 0 即全局静音。`SfxEnabled` 默认**开启**（新安装即生效；老配置保留已存值）

### 4.6 主题系统
- `Theme.Current` 提供全部颜色 + 风格开关（Dark=深色、Glow=霓虹光晕、Bevel=拟物斜面、Radius=圆角）
- 所有自绘控件 OnPaint **实时读取** `Theme.Current` → 切换主题只需 `Theme.Current = Theme.All[i]` + 整树 Invalidate
- `MainForm.ApplyTheme()`：递归 `WalkTheme` + 状态栏 + 列表刷新 + `ApplyFrameTheme()`（DWM 边框色/标题栏色/深色模式，旧系统静默失败）

### 4.7 DPI 与多显示器（本项目最大坑，改动前必读）
- 进程声明 **Per-Monitor V2 感知**（`SetProcessDpiAwarenessContext(-4)`，旧系统回退 v1）
- `Dpi.S` 是**运行时可变**的全局缩放系数，所有布局坐标经 `Dpi.X(v)` 缩放
- 三重机制保证"布局缩放 == 窗口所在显示器缩放"：
  1. **WM_DPICHANGED**（`MainForm.WndProc` 0x02E0）：跨屏拖动/改系统缩放时按新 DPI 重建界面 + 应用系统建议窗口矩形
  2. **`SyncDpi()` 轮询**（300ms 状态定时器）：用 `MonitorFromWindow`+`GetDpiForMonitor` 取**显示器真实 DPI**（不要信 `GetDpiForWindow`——窗口 DPI 属性跨屏后可能滞后）；与 `Dpi.S` 不一致 → `RebuildUiForDpi()`
  3. **`RecreateHandle()`**：布局与窗口 DPI 属性不一致时重建窗口句柄，消除 DWM 位图缩放（不重建的话窗口会被 DWM 拉伸/压缩 → 卡片溢出窗外、文字模糊）
- `RebuildUiForDpi()`：`Controls.Clear()` → `BuildUi()` → `WireEvents()` → `ApplyConfigToUi(_cfg)` → 恢复当前标签页。**所有界面状态必须能从配置回填，否则重建会丢状态**
- **卡片溢出的真正根因（不是 DPI）**：WinForms 的 `Anchor=Left|Right` 偏移在**控件加入父容器时**按父容器当时的宽度计算。若卡片在页面尚未停靠（默认 200px 宽）时就加入页面，偏移会按 200px 算出负值，页面随后拉伸到 560px 时卡片就被撑到 884px → 右侧溢出窗口。**修法：先停靠再填充**——`BuildUi` 先创建并 `Controls.Add` 骨架（4 个 Dock 面板）→ 把 6 个页面先 `_contentPanel.Controls.Add` 停靠（立即获得最终宽度）→ 再调用 `BuildXxxPage(page)` 填充内容。`RebuildUiForDpi` 里**严禁用 `SuspendLayout()` 包裹**——挂起会延迟 Dock 链尺寸传播，重新触发同样的溢出
- **隐藏页面也要先停靠**：`Dock=Fill` 的页面若 `Visible=false`，`Controls.Add` 时**不会被布局引擎拉伸**（宽度停在默认 200px），此时加入的卡片锚点同样按 200px 计算。所以 `BuildUi` 里页面要先以**可见状态**停靠并填充，**填充完成后**再 `page.Visible = (i == 0)`（见第 5 节）
- **调试注意事项**：非感知进程（如 PowerShell）查到的窗口尺寸/显示器 DPI 是**虚拟化后的值**（可能把 862px 窗口报成 575px），调试 DPI 问题要用 PMv2 感知的进程查询

### 4.8 配置
- `AppConfig` 字段即 config.json 结构；界面改动即时 `SaveSettings()`（从控件读值 → 写 `_cfg` → Save）
- **加载时清洗不可信数据**：`SanitizeUntrusted` 白名单 `LaunchPrograms`(绝对路径+exe/lnk) 与音效绑定纯文件名校验（见坑 19）
- 启动流程：`Load` → 按语言/主题构建 UI → `ApplyConfigToUi` 回填（`_applying` 标志抑制控件事件）→ `PushToEngines` 把注入/拟人化设置同步到各静态引擎
- **新增配置项**：AppConfig 加字段 + ApplyConfigToUi 回填 + SaveSettings 同步 + PushToEngines 转给引擎（按需），缺一不可

### 4.9 启动与系统托盘
- 两个开关在「高级设置」页「启动」卡片：`AutoStart`(开机自启动) / `StartMinimized`(静默启动)
- 开机自启动：`AutoStart.SetEnabled(bool)` 写/删 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `AutoClickerTool` 值(值 = 带引号的 exe 路径)。ctor 里 `SetEnabled(_cfg.AutoStart)` 幂等对齐注册表与配置；勾选时即时写注册表 + 保存
- 静默启动：`MainForm.SetVisibleCore` 拦截**首次** `Show`（`_firstShow` 标志消费一次后恢复正常），使程序启动后不弹窗、以托盘后台运行
- 托盘：`BuildTray()` 创建**常驻** `NotifyIcon`（双击 = 打开主界面，右键菜单 = 显示主界面/退出）；`Shutdown()` 统一收尾（停引擎松键、释放钩子/音效、保存设置，`_shutdownDone` 保证只执行一次），窗口关闭(X)与托盘「退出」共用。`ApplyLanguage` 里同步刷新托盘菜单文案与图标文本
- **注意**：静默启动只对「启动那一刻」生效；打开主界面后点 X 仍是**退出程序**（不会缩回托盘）

## 5. UI 结构

- 顶部栏：热键提示文字 + **「窗口置顶」图钉按钮（`btnTopmost`，📌 图标，右上角；`Tab=true` 胶囊样式，`Selected` 高亮 = 置顶中；点击走 `SetTopmost`，与托盘菜单同步）**；胶囊标签条（**7 页**：鼠标连点/键盘连按/录制回放/热键/高级设置/音效/宏库）；底部状态栏（`SetStatus()` 写消息，自动加 ToolTip；左侧 `lblStatusDot` 是运行指示点——任一引擎运行 = `Clay.Run` 色、空闲 = `Clay.InkSoft`，由 `UpdateStatusDot()` 刷新，挂在 `UpdateAllUi()` 与 300ms 状态定时器上）。**语言切换时 `ApplyLanguage` 末尾 `SetStatus(Lang.T("Ready"))`**——状态栏旧消息是旧语言的快照，必须刷新
- 分组卡片标题（`ClayGroup.OnPaint`）绘制一枚主题强调色小圆点 + 主墨色（`Clay.Ink`）标题，与卡片内 `InkSoft` 标签形成层级
- 页面构建函数：`BuildClickerPage / BuildKeyboardPage / BuildMacroPage / BuildHotkeyPage / BuildAdvancedPage / BuildSfxPage / BuildLibraryPage(Panel page)`，签名是 `void Xxx(Panel page)`——**页面由 `BuildUi` 先创建并停靠到 `_contentPanel`，再传给构建函数填充内容**（先停靠再填充，锚点才正确）。宏库页 `BuildLibraryPage` 含已存宏列表（▶ 列快速触发 + 重命名/副本/删除，文件在 `AppConfig.MacrosDir`）+ 软件控制（自动启动程序，`Process.Start`）
- **运行限制卡片「Run options」在三个功能页里用完全相同的 `Grp("Run options", 10, 158, 540, 114)` + `RunLimitBox(12,32,516,78)`**（鼠标连点/键盘连按/录制回放），这是刻意的：位置一致方便用户对比调整。三页的顶部设置卡分别是 Click Settings / Key Settings / Record+Play（含回放速度第二行），其余控件（开始按钮、状态、提示）位于卡片下方同一纵坐标
- **卡片标题占位**：`ClayGroup.OnPaint` 把标题画在卡片顶部 `y≈7~27`，所以**卡内第一行控件必须从 y≥30 开始**，否则标题会和控件文字叠在一起（v2.5 修过这个 bug）
- 「点击按键」（键盘连按页）用 `HotkeyCaptureForm` 捕获按键/多键组合，结果存 `_spamHotkey`（`Hotkey`），按钮文案由 `UpdateSpamKeyButtonText()` 维护（按钮 `Name` 留空，避免 `ApplyLangWalk` 把捕获到的按键覆盖成提示文字）
- 循环热键提示 `lblLoopHint` 显示当前回放热键
- 常用辅助：`Lbl(en,x,y)`/`Tip(en,x,y)`（Name 存英文原文供翻译）、`Grp(en,x,y,w,h)` 分组卡片（锚定 Left|Right，随窗口伸缩）、`ClayKit.InputShell(inner,x,y,w)` 给输入控件套圆角外壳
- **输入控件一律用 `ClayNumericUpDown` / `ClayComboBox`**（不要用系统原生 NumericUpDown/ComboBox，系统箭头是白色与主题冲突）
- 新增文案流程：Lang.cs 的 `BuildZh()` 加 `d["English key"] = "中文"`；界面代码用 `Lang.T("English key")`；控件 Name 设英文原文可自动获得语言切换能力（右键菜单项、ListView 列名需在 `ApplyLanguage` 手动刷新）

## 6. 常见坑

1. **钩子回调必须快**：LL 钩子超时会被系统摘除，回调里只做轻量操作（录制/触发/入队）；重活（弹窗等）用 `BeginInvoke` 或事件转 UI 线程（`Ui()` helper 封装 InvokeRequired）。**现状**：音效 MCI 播放已移入后台队列（`SfxManager.Enqueue`→ThreadPool，只保留最新一条保持覆盖式语义）；日志为异步缓冲写入；三个钩子回调整体 try/catch 兜底（托管异常穿越原生边界会终止进程）
2. **模拟输入会触发自己的钩子**：靠 INJECTED 标记过滤，新增注入路径时确保 SendInput/PostMessage 都带标记或不经过钩子。**例外**：Interception 驱动注入无 INJECTED 标记，回放期间靠 `Suppress` 抑制（见 4.2）；`HotkeyCaptureForm` 同样过滤注入按键/点击，并忽略落在对话框自身范围内的鼠标点击（否则打开对话框点一下就把"鼠标左键"录成热键）
3. **ClayNumericUpDown 的内部子控件**：框架会创建 `UpDownButtons`/`UpDownEdit` 两个子控件（顺序因框架版本而异）。**只隐藏 `UpDownButtons`**（否则系统按钮拦截右侧 ▲▼ 区域点击）；**保留 `UpDownEdit` 并样式化**（`Visible=true`、`BorderStyle=None`、`BackColor=InputBg`、`ForeColor=Ink`，`LayoutEdit()` 把它定位到文字区左侧）——这样数值输入框能**点击后键盘直接输入数字**（OnPaint 不再手动画数值文本，交给编辑框显示）。若再次隐藏 `UpDownEdit` 会导致无法键盘输入
4. **NumericUpDown.Height 会被框架钳制**到字体首选高度（约 23~24px），InputShell 里设置的高度可能不生效——布局按实际高度居中即可
5. **多线程共享 Random**：Humanizer 全部随机数在 `lock(Lock)` 内生成（多个引擎线程并发调用）
6. **JavaScriptSerializer 字段兼容**：序列化按字段声明顺序，向后兼容靠"新字段给默认值"，不要改旧字段名/类型。宏事件的枚举值 10~13 是新增的，旧存档仍可加载
7. **语言切换**会重建下拉框 Items——新增下拉框若含本地化文本，需在 `ApplyLanguage` 里同步重建
8. **MCI 音效**：按扩展名选设备（wav=waveaudio，其余=mpegvideo），不支持的文件静默失败；播放用 `play ... notify` 异步，覆盖式播放前必须 stop+close 旧别名
9. **DWM 属性**（边框色/标题栏色/深色模式）在窗口句柄重建后需重新设置（`OnHandleCreated` 里已处理）
10. **ClayNumericUpDown 连发**：自绘 ▲▼ 用 `_repeatTimer` 实现长按连发——首次连发延迟 `RepeatDelay`(450ms)、之后 `RepeatRate`(60ms)。**每次 OnMouseDown 都要把 Interval 重置回 RepeatDelay**，否则单击（按下约 100ms）会多触发一次 Tick，出现"点一下跳 2 格"
11. **按钮阴影竖直残影 / 黑色边框**：非光晕主题（Clay/Skeuo）的硬阴影若只垂直下移(`Offset(0,3)`)且圆角与按钮相同，其左/右竖直边缘会在左下/右下角露出"小竖直线"；若改为横向膨胀(`Inflate(body,3,0)`)又会在按钮四周露出黑色边。**正确修法：`s.Offset(0, off)` 且阴影圆角用 `radius + off`**——阴影下沿圆角与按钮下沿圆角在竖直方向"接上"，竖直线消失且不产生四周黑边；**同时把半透明阴影预混成不透明色**(`DrawShadow` 现接收 `bg` 参数, 用 `Shadow.A` 与背景色混合), 避免半透明填充在未清空的缓冲上渲染成黑边(首次显示黑边、重绘后消失)。**光晕主题(Glow)的描边同样要预混**(`Premix(bg, Shadow, a)` 不透明画笔), 否则深色主题下半透明描边在首次绘制时也会出现发黑的边缘
12. **白色方形边框**：自绘控件（ClayButton/ClayGroup/ClayPanel/ClayCheck/ClayRadio）OnPaint 里 `FillRectangle` 若用自身 `BackColor`（默认 CardBg 近白）而非父容器背景，放在页面(WindowBg 奶油色)上时四角会露出近白色方块。**修法：一律用 `Parent != null ? Parent.BackColor : BackColor` 填四角**，圆角形状才真正镂空呈现
13. **JavaScriptSerializer 字典键必须为字符串**：反序列化 `Dictionary<int,...>` 会抛 `InvalidOperationException`（"键必须为字符串或对象"），导致**整个 config.json 加载失败、静默回退默认值**（曾长期存在：音效绑定字段用 int 键，配置文件从未真正加载过）。**修法：配置字段一律用 `Dictionary<string,...>`**（JSON 中键本就是字符串，旧存档兼容），在界面层用 `int.Parse`/`int.TryParse` 转换后传给引擎（引擎内部 `Dictionary<int,...>` 不受影响）
14. **隐藏页控件没有窗口句柄**：只有当前可见页的控件会被创建句柄；`EnumChildWindows` 查不到隐藏页控件属正常现象（WinForms 懒创建句柄）。需要操作隐藏页控件时先切换页面或访问其属性触发 `CreateControl`
15. **给自绘控件加动效**：控件存一个 `float` 字段（如 `_hoverT`）+ **一个稳定的 `Action<float>` 委托字段**（构造函数里初始化成 `v => { _hoverT = v; Invalidate(); }`），事件里调 `Anim.To(委托, 当前值, 目标值, tauMs)`，OnPaint 用 `Anim.EaseOutCubic(_hoverT)` 做颜色/透明度插值。**委托必须存字段（同一实例），否则 Anim 无法去重、会累积重复动画条目**；`Anim.Enabled=false` 时 `Anim.To` 直接瞬达目标（等效减少动态效果）。tau 参考：悬停 70~100ms、按压 50~100ms、标签过渡 120ms、勾选 110ms（全部 <300ms 符合 UI 动效规范）。**DPI 重建/换主题 `Controls.Clear()` 会销毁旧控件——重建前必须 `Anim.Clear()`**，Anim.Tick 内部也捕获回调异常（控件已销毁时停止该动画），否则动画回调会访问已销毁控件崩溃
16. **Interception 驱动的过滤器必须 try/finally 复位**：`interception_set_filter(ALL)→send→set_filter(NONE)` 若 send 抛异常而过滤器未复位，驱动会把**整机键盘/鼠标输入全部拦截**（用户键鼠失效）。`SendKey`/`SendMouse` 里 send 必须包在 try 里、复位在 finally 里
17. **回放停止时按住键要补发抬起**：`MacroPlayer` 回放中 `LeftDown/KeyDown/KeyComboDown` 后若未到对应 `Up` 事件就停止，键会卡住。按住状态是 **`Run()` 局部 HeldState**（`ReleaseHeld(held)` 在 finally 补发抬起）——线程局部化后旧代线程不会干扰新代按键；同理 `KeyboardSpammer` Hold 模式已在 finally 松开
18. **引擎 Stop→Start 竞态**：三个引擎用**代际计数**防双线程并发——`Start()` 自增 `_gen` 并让新线程绑定该代际，`Stop()` **只置停止 flag、不自增**（若 Stop 自增，线程 finally 里 `_gen == gen` 恒为 false → `Stopped`/`Finished` 事件永不触发，手动停止后 UI 卡在「停止」态、回放停止后热键/音效保持抑制）。循环内 `Alive(gen)` 同时比对 flag 与代际，finally 里只有当前代际的线程才清 flag/触发事件。`_gen` 需 `volatile`（跨线程读）。退出时 `Shutdown()` 调 `WaitExit(500)`（超时 Abort，确保 finally 已执行）再结束进程
19. **config.json 是不可信输入**：`AppConfig.Load` 里 `SanitizeUntrusted` 清洗——`LaunchPrograms` 只保留绝对路径且扩展名为 exe/lnk（bat/cmd/URL 丢弃并 Log.Warn）；音效绑定值必须等于 `Path.GetFileName(v)`（防 `..\` 路径穿越）。「软件控制」的 AddProgram 对话框只允许 exe/lnk，`LaunchPrograms()` 运行时二次校验白名单
20. **标签条与按钮文字宽度**：标签条 `LayoutTabStrip()` 按当前语言文本测量所需宽度——放得下时用自然宽度(**只有需要压缩时才按比例分配**, 最后一块吃余量), 放不下等比压缩；构建、`ApplyLanguage`、窗口 `Resize` 时都调用（构建期条宽未布局时用 546 兜底）。`ClayButton` 文字绘制在左右各 6px 内边距的矩形里，放不下自动缩字号（下限 7pt）。新增按钮时无需手工计算文字宽度
21. **窗口尺寸与 DPI**：`ClientSize = Dpi.X(560)×Dpi.X(530)` 是设计尺寸；`WM_DPICHANGED` 里只采用系统建议的**位置**，尺寸强制回到设计尺寸（否则建议矩形可能与布局宽度不一致, 右侧控件被窗口边缘裁掉）。状态栏「关于」/版本号右锚定、状态文本固定宽+省略号；卡片内控件一律固定坐标（不要右锚定, 否则拉大窗口时被拖走）
22. **弹窗也要套主题边框**：主窗口与所有对话框（关于/欢迎/热键捕获/事件编辑）共用 `Clay.ApplyFrameTheme(Handle)`（DWM 边框色/标题栏色/深色模式），新加对话框时别漏；任务栏图标 = 主窗口 `Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)`（与 exe 图标一致）
23. **运行限制统一模型**：三个引擎的停止条件都是 `RepeatCount`(0=无限) + `UntilAt`(DateTime?, null=不限)。
    - UI 侧：`RunLimitBox`（`RunLimit.cs`）负责三个选项与「时长 ↔ 截止时刻」双向同步，向引擎只暴露 `EngineRepeatCount`/`GetUntilTarget()`；**模式 ③ 下 `RepeatCount=0`（循环轮询）直到截止时刻**。保存 `*UntilPrimary`（用户最近以截止时刻为准）进 config，加载时按该标志决定采纳绝对时刻（过期顺延次日）还是从时长重新锚定——防重启漂移。
    - **不要恢复 `RunMinutes`/`UntilTime`(string) 那套旧字段**：`AppConfig` 里 `ClickMinutes/ClickUntilTime/SpamMinutes/SpamUntilTime/PlayLoop/PlayMinutes/PlayUntilTime` 仅作 v3→v4 迁移用（`Migrate` 换算成 `*LimitMode/*Seconds/*UntilAt`）。
    - `Util.ParseUntilTime` 同时接受 "HH:mm"（今天/明天，过夜挂机）与 "yyyy-MM-dd HH:mm:ss"（绝对时刻，字面处理）；**只走 `TryParseExact`**，不要加宽松 `DateTime.TryParse` 兜底（会让用户输入到一半的 "2026-08" 被误判为合法）。
    - 每个引擎的停止条件检查都在各自循环体内（`KeyboardSpammer` Hold 模式按 10ms 粒度）。
24. **点击微拖必须复位光标**：`InputSimulator.Click` 的「点击微拖」会在按下/抬起间把真实光标移 ±2px；若不在抬起后复位到按下前位置，「跟随光标」模式下每次点击都累积漂移（用户反映光标越点越往右跑）。抬起后用 `MoveStep(orig.x, orig.y)` 复位即可（微拖本身仍保留，防检测特征不变）
25. **`SendInput` 绝对坐标必须带 `MOUSEEVENTF_VIRTUALDESK`**：`SendInputMove` 按整个虚拟桌面归一化（`SM_XVIRTUALSCREEN` 等），若不加该标志，系统会把 0~65535 只映射到**主显示器**——主屏不在 (0,0)（副屏在主屏左侧/上方）时回放整体偏移（典型症状：在 2K 副屏录制，回放到主屏坐标系里“靠右”）。改动这里务必保持“归一化范围”与“标志”一致
26. **回放期间的 `Suppress` 必须放行 F8/F12**：`HotkeyManager.Suppress` 是给驱动级注入（无 INJECTED 标记）做自触发防护的，但它同样会吞掉用户真实按键。若把「回放开关(Play)」也一起抑制，回放一开始就再也停不下来（用户案例：循环 900 次后只能 Ctrl+Alt+Del）。因此 `TryFire` 里 `Suppress` 只抑制 Clicker/Record/Keyboard，**Play 与 StopAll 始终放行**；`MacroRecorder.IsHotkeyKey` 已保证宏里不含已绑定的热键键，代价可接受。**但捕获模式（捕获新热键/连按键）不能用 `Suppress`**——按 F8/F12 会误触发功能：捕获期间用独立的 `CaptureActive` 标志，`TryFire` 里它对全部动作（含 Play/StopAll）暂停。
27. **钩子回调必须 try/catch + 始终 `CallNextHookEx`**：`HotkeyManager`/`MacroRecorder`/`SfxManager` 的低级钩子回调里，托管异常穿越原生边界会终止进程；并且异常路径也要 `CallNextHookEx`，否则这条输入会被静默吞掉
28. **MCI 必须在同一条常驻线程上调用**：`mciSendString` 会在调用线程上建立隐藏通知窗口/设备上下文，线程一退出这套上下文就失效 → 第一次播放静默失败、"必须先点一次试听才能出声"。所以 `SfxManager` 启动时开一条**常驻**播放线程，`WarmUp()` + 所有 `Play`（含「试听」）都在这条线程上跑；**不要退回 `ThreadPool.QueueUserWorkItem`**（线程会退出）。`open` 失败要重试一次并把 `mciGetErrorString` 的内容写进 `log.txt`
29. **音效音量是「乘法」不是「覆盖」**：`SfxVolume`(全局音效音量) 是总音量，`SfxBindingVolumes`(单键) 是**相对**音量(缺省 100)，实际响度 = 两者相乘 ÷ 100。全局 0 = 全局静音。**不要**再把单键音量当成"覆盖全局的绝对值"，否则全局拉 0 仍有键会响；`SfxPlayer.Play` 也要在 `volume<=0` 时直接返回不打开设备
30. **输入框失焦**：`WireClickToUnfocus` 给页面/卡片/标签挂 Click → `ActiveControl = null`，让 NumericUpDown/TextBox 点击空白处即提交并停止光标闪烁；新增容器时不用管（递归挂），但**不要**给按钮/列表挂（它们本来就抢焦点）
31. **标题栏自绘（非客户区）已停用**：Win11 上 DWM 会覆盖 `WM_NCPAINT` 的非客户区自绘（图钉画了也看不见，残留命中区还可能干扰最小化按钮）。`PinRectClient` 现恒返回 `Rectangle.Empty`（`DrawTitleBarPin`/`PinHitTest`/`OnMouseMove` 热区随之失效，代码保留以便回滚）。置顶入口改为顶栏 `btnTopmost` 按钮 + 托盘菜单「窗口置顶」，**不要再恢复标题栏自绘图钉**

## 7. 验证流程

1. 改代码 → `build.bat` 确认编译通过（先关掉正在运行的 AutoClicker.exe）
2. 改到纯逻辑模块（`HotkeyManager`/`AppConfig`/`Lang` 等）时 → 跑 [run_tests.bat](../tests/run_tests.bat) 单测（编译无 WinForms 依赖的模块成控制台测试 exe 并运行，全绿=通过）
3. 启动 `..\AutoClicker.exe` 检查界面/功能；`config.json` 删掉可恢复出厂
4. **检查多显示器/多 DPI 场景**（本项目用户为混合 DPI 双屏）：窗口在两屏间拖动、改系统缩放，确认界面自动重建且无溢出/模糊
5. 改动影响热键/录制/回放/音效时，实测一遍完整流程（录制→编辑→保存→加载→回放）
6. 测试注入工具（PowerShell 等）看到的窗口尺寸是虚拟化的，验证 DPI 要用感知进程或用应用自身日志
7. **发版** → 运行 [publish.bat](../tools/publish.bat)（读 [VersionInfo.cs](../src/VersionInfo.cs) 的版本号 → 跑单测 → `build.bat` → 打包 `publish\AutoClickerTool-<版本>.zip`，含 exe+README+CHANGELOG+使用说明）。改版只改 `VersionInfo.Version`，打完包 `git tag v<版本>` 并在 [CHANGELOG.md](CHANGELOG.md) 顶部追加条目
