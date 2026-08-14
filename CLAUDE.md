# AutoClickerTool 项目手册（写给未来的 AI 维护者）

> 读本手册即可上手修改本项目，**不需要通读源码**。若手册与代码冲突，以代码为准并顺手更新本手册。

## 1. 项目是什么

Windows 上的鼠标键盘自动化工具（WinForms 桌面应用），面向游戏挂机/自动点击场景。主要功能：

1. **鼠标连点** — 固定间隔自动点击，可选固定坐标或跟随光标、次数限制
2. **键盘连按** — 定时点按或按住某个键
3. **录制回放** — 全局低级钩子录制真实鼠标/键盘操作成宏，可编辑、保存、按倍速循环回放
4. **全局热键** — 5 个功能开关支持任意组合键（含多键组合、鼠标侧键、媒体键）
5. **按键音效** — 任意按键可绑定 wav/mp3 音效，按键即播、新键覆盖旧音效（覆盖式播放）
6. **拟人化** — 高斯分布间隔、贝塞尔移动轨迹、落点漂移、点击微拖等，降低"脚本感"被游戏统计检测识别的概率（含总开关）
7. **三套输入注入方式** — SendInput / SendMessage / Interception 驱动级
8. **6 套主题 + 中英双语**，全部自绘控件（Clay 控件库），窗口边框/标题栏跟随主题
9. **每显示器 DPI 感知** — 跨屏拖动/缩放变化自动重建布局，不会出现控件溢出窗口

## 2. 技术栈与硬约束（最重要）

- **纯 C# / WinForms / .NET Framework 4.0**，用系统自带 `csc.exe` 直接编译，**零外部依赖**（无 NuGet、无第三方 DLL）
- **编译方式**：在 src 下运行 [build.bat](src/build.bat) → 输出 `..\AutoClicker.exe`。改完代码必须跑它验证编译通过
- **语法上限 = C# 5**：禁止字符串插值 `$""`、`?.`、`??`、`nameof`、表达式体成员、局部函数、`out var`、`Math.Clamp`。已有代码风格就是模板
- **中文文件路径**：编译参数带 `/codepage:65001`，源码统一 UTF-8
- 新增 `.cs` 文件后**必须把它加进 build.bat 的编译列表**，否则不会参与编译
- 编译前若 AutoClicker.exe 正在运行，会因文件占用失败 → 先关闭程序
- 所有 Win32 API 走 [NativeMethods.cs](src/NativeMethods.cs) 的 P/Invoke，不引入新 DLL

## 3. 文件地图（src/）

| 文件 | 职责 |
|---|---|
| [Program.cs](src/Program.cs) | 入口：`EnablePerMonitorDpiAware()`（PMv2 感知，失败回退系统感知）+ 启动 MainForm |
| [MainForm.cs](src/MainForm.cs) | **主界面（~1600 行，最大文件）**：6 个胶囊标签页、配置加载/保存、事件列表虚拟模式渲染、各引擎开关的桥接、DPI 同步（SyncDpi）、DWM 边框主题 |
| [Clay.cs](src/Clay.cs) | **自绘控件库**：`Dpi` 缩放、`Clay` 绘制工具（圆角/阴影/渐变）、`ClayKit.InputShell`、`ClayButton`、`ClayCheck`、`ClayRadio`、`ClayGroup`、`ClayPanel`、`ClayNumericUpDown`（自绘▲▼）、`ClayComboBox`（OwnerDraw 自绘下拉）、`ClaySlider`（主题滑块）、`ClayMenuColorTable`/`ClayMenu`（主题右键菜单） |
| [Theme.cs](src/Theme.cs) | 6 套主题调色板 + 风格参数（Dark/Glow/Bevel/Radius）。`Theme.Current` 全局单例，控件 OnPaint 实时读取实现换肤 |
| [Lang.cs](src/Lang.cs) | 双语字典：以**英文原文为 key**，`Lang.T(key)` 按 `Lang.Code`("zh"/"en") 翻译；控件 `Name` 属性存英文原文，语言切换时 `ApplyLangWalk` 按 Name 递归刷新 |
| [InputSimulator.cs](src/InputSimulator.cs) | **输入注入中枢**：鼠标/键盘事件按 `Method` 路由到 SendInput/SendMessage/InterceptionDriver。MoveTo 内部按拟人化走贝塞尔轨迹；Click 含按下-抬起微拖 |
| [Humanizer.cs](src/Humanizer.cs) | 拟人化引擎：`Enabled` 总开关 + 间隔(高斯分布+偶发犹豫)/落点(抖动+漂移)/按键时长/轨迹(贝塞尔+Fitts 时长+smoothstep 加减速)四个子开关。**注意内部用 lock 保护共享 Random** |
| [InterceptionDriver.cs](src/InterceptionDriver.cs) | Interception 驱动加载（DLL 迟到重试、空壳兜底） |
| [HotkeyManager.cs](src/HotkeyManager.cs) | **全局热键引擎**：键盘+鼠标低级钩子监听，`Hotkey`（修饰键+触发键集合，多键组合）解析/显示/冲突检测。`GetName` 含左右修饰键精确名与媒体键名 |
| [MacroRecorder.cs](src/MacroRecorder.cs) | **宏录制器**：低级钩子录全局操作。**按键精灵式合并**：连续移动合并为一条"移动到终点"(500ms 停顿阈值)；短按(≤250ms)合并为单击/按键事件；长按保留按下/抬起 |
| [MacroPlayer.cs](src/MacroPlayer.cs) | 宏回放线程：按 DelayMs 分段 sleep 后 PlayEvent，支持倍速/循环；Move 事件的轨迹移动从延迟预算中扣除时间 |
| [AutoClicker.cs](src/AutoClicker.cs) | 鼠标连点引擎（后台线程循环） |
| [KeyboardSpammer.cs](src/KeyboardSpammer.cs) | 键盘连按引擎（Tap/Hold 两模式） |
| [SoundFx.cs](src/SoundFx.cs) | 按键音效：`SfxPlayer`（MCI 播放 wav/mp3，覆盖式：新播放前 stop+close 旧音效）+ `SfxManager`（键盘钩子按绑定表触发，只监听不拦截，过滤注入按键） |
| [AppConfig.cs](src/AppConfig.cs) | 配置模型（JavaScriptSerializer 序列化到 `config.json`）+ `MacrosDir`/`SoundsDir` 目录常量与 `EnsureDataDirs()` |
| [EventEditForms.cs](src/EventEditForms.cs) | 两个小对话框：`DelayEditForm`（改延迟）、`EventAddForm`（添加宏事件，含单击/按键点按类型） |
| [HotkeyCaptureForm.cs](src/HotkeyCaptureForm.cs) | 热键捕获对话框：**低级钩子捕获**（支持媒体键/侧键），按下组合→全松开→返回 `Captured`（Esc 取消）；可选 hint 参数被音效绑定复用 |
| [WelcomeForm.cs](src/WelcomeForm.cs) | 首次启动欢迎窗口：选择默认语言 + 功能介绍 + 已阅关闭（`MainForm` 在 `config.json` 不存在时于 `Shown` 事件弹出） |
| [AboutForm.cs](src/AboutForm.cs) | 「关于」对话框：版本/作者/项目主页/开源申明(MIT)/免责声明；作者与网址在文件顶部 `AUTHOR`/`WEBSITE` 常量处替换，双语文本在 `ApplyTexts` 里维护。入口在底部状态栏「关于」按钮 |
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
- 绑定表存 config：单键 `SfxBindings: Dictionary<int,string>`(键码→文件名) + `SfxBindingVolumes: Dictionary<int,int>`(键码→音量 0~100)；组合键 `SfxComboBindings: Dictionary<string,string>`(组合串如 "Ctrl+C"→文件名) + `SfxComboVolumes`(组合串→音量)。`SfxManager` 全局键盘钩子监听（**只监听不拦截**，与热键/录制并行），维护 `_down` 归一化按下集合：单键命中 `Bindings` 即播，组合键 `Satisfied()`(所有修饰键+键都按住)即播 → `SfxPlayer.Play(path, volume)`（MCI：新播放前 stop+close 旧的 = 覆盖式，`setaudio <alias> volume to N`，waveaudio 支持、mpegvideo 不支持静默忽略）。过滤注入按键（宏回放不触发音效）
- 音效页：总开关 + **全局音量滑块**（`sldGlobalVolume` 0~100 → `SfxVolume`）+ **选中项音量滑块**（`sldKeyVolume`，选中列表项后单独覆盖全局）+ 添加绑定（复用 HotkeyCaptureForm 捕获**单键或组合键**）+ 删除/试听/打开 Sounds 文件夹。统一列表用内部 `SfxKey` 条目(单键/组合键)填充；滑块是自绘 `ClaySlider`（不用系统原生 TrackBar）

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
- 启动流程：`Load` → 按语言/主题构建 UI → `ApplyConfigToUi` 回填（`_applying` 标志抑制控件事件）→ `PushToEngines` 把注入/拟人化设置同步到各静态引擎
- **新增配置项**：AppConfig 加字段 + ApplyConfigToUi 回填 + SaveSettings 同步 + PushToEngines 转给引擎（按需），缺一不可

## 5. UI 结构

- 顶部栏：热键提示文字 + 置顶开关；胶囊标签条（6 页）；底部状态栏（`SetStatus()` 写消息，自动加 ToolTip）
- 页面构建函数：`BuildClickerPage(Panel page) / BuildKeyboardPage(Panel page) / ... / BuildSfxPage(Panel page)`，签名是 `void Xxx(Panel page)`——**页面由 `BuildUi` 先创建并停靠到 `_contentPanel`，再传给构建函数填充内容**（先停靠再填充，锚点才正确）
- 常用辅助：`Lbl(en,x,y)`/`Tip(en,x,y)`（Name 存英文原文供翻译）、`Grp(en,x,y,w,h)` 分组卡片（锚定 Left|Right，随窗口伸缩）、`ClayKit.InputShell(inner,x,y,w)` 给输入控件套圆角外壳
- **输入控件一律用 `ClayNumericUpDown` / `ClayComboBox`**（不要用系统原生 NumericUpDown/ComboBox，系统箭头是白色与主题冲突）
- 新增文案流程：Lang.cs 的 `BuildZh()` 加 `d["English key"] = "中文"`；界面代码用 `Lang.T("English key")`；控件 Name 设英文原文可自动获得语言切换能力（右键菜单项、ListView 列名需在 `ApplyLanguage` 手动刷新）

## 6. 常见坑

1. **钩子回调必须快**：LL 钩子超时会被系统摘除，回调里只做轻量操作（录制/触发/播音效）；重活（弹窗等）用 `BeginInvoke` 或事件转 UI 线程（`Ui()` helper 封装 InvokeRequired）
2. **模拟输入会触发自己的钩子**：靠 INJECTED 标记过滤，新增注入路径时确保 SendInput/PostMessage 都带标记或不经过钩子
3. **ClayNumericUpDown 的内部子控件**：框架会创建 `UpDownButtons`/`UpDownEdit` 两个子控件，必须按**类型名**隐藏（子控件顺序因框架版本而异），否则系统按钮拦截 ▲▼ 区域点击
4. **NumericUpDown.Height 会被框架钳制**到字体首选高度（约 23~24px），InputShell 里设置的高度可能不生效——布局按实际高度居中即可
5. **多线程共享 Random**：Humanizer 全部随机数在 `lock(Lock)` 内生成（多个引擎线程并发调用）
6. **JavaScriptSerializer 字段兼容**：序列化按字段声明顺序，向后兼容靠"新字段给默认值"，不要改旧字段名/类型。宏事件的枚举值 10~13 是新增的，旧存档仍可加载
7. **语言切换**会重建下拉框 Items——新增下拉框若含本地化文本，需在 `ApplyLanguage` 里同步重建
8. **MCI 音效**：按扩展名选设备（wav=waveaudio，其余=mpegvideo），不支持的文件静默失败；播放用 `play ... notify` 异步，覆盖式播放前必须 stop+close 旧别名
9. **DWM 属性**（边框色/标题栏色/深色模式）在窗口句柄重建后需重新设置（`OnHandleCreated` 里已处理）
10. **ClayNumericUpDown 连发**：自绘 ▲▼ 用 `_repeatTimer` 实现长按连发——首次连发延迟 `RepeatDelay`(450ms)、之后 `RepeatRate`(60ms)。**每次 OnMouseDown 都要把 Interval 重置回 RepeatDelay**，否则单击（按下约 100ms）会多触发一次 Tick，出现"点一下跳 2 格"
11. **按钮阴影竖直残影 / 黑色边框**：非光晕主题（Clay/Skeuo）的硬阴影若只垂直下移(`Offset(0,3)`)且圆角与按钮相同，其左/右竖直边缘会在左下/右下角露出"小竖直线"；若改为横向膨胀(`Inflate(body,3,0)`)又会在按钮四周露出黑色边。**正确修法：`s.Offset(0, off)` 且阴影圆角用 `radius + off`**——阴影下沿圆角与按钮下沿圆角在竖直方向"接上"，竖直线消失且不产生四周黑边；**同时把半透明阴影预混成不透明色**(`DrawShadow` 现接收 `bg` 参数, 用 `Shadow.A` 与背景色混合), 避免半透明填充在未清空的缓冲上渲染成黑边(首次显示黑边、重绘后消失)
12. **白色方形边框**：自绘控件（ClayButton/ClayGroup/ClayPanel/ClayCheck/ClayRadio）OnPaint 里 `FillRectangle` 若用自身 `BackColor`（默认 CardBg 近白）而非父容器背景，放在页面(WindowBg 奶油色)上时四角会露出近白色方块。**修法：一律用 `Parent != null ? Parent.BackColor : BackColor` 填四角**，圆角形状才真正镂空呈现

## 7. 验证流程

1. 改代码 → `build.bat` 确认编译通过（先关掉正在运行的 AutoClicker.exe）
2. 启动 `..\AutoClicker.exe` 检查界面/功能；`config.json` 删掉可恢复出厂
3. **检查多显示器/多 DPI 场景**（本项目用户为混合 DPI 双屏）：窗口在两屏间拖动、改系统缩放，确认界面自动重建且无溢出/模糊
4. 改动影响热键/录制/回放/音效时，实测一遍完整流程（录制→编辑→保存→加载→回放）
5. 测试注入工具（PowerShell 等）看到的窗口尺寸是虚拟化的，验证 DPI 要用感知进程或用应用自身日志
