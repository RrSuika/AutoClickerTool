using System;
using System.Collections.Generic;

namespace AutoClickerTool
{
    /// <summary>
    /// 双语系统: 代码中以英文为键, T() 按当前语言返回本地化文本。
    /// Code = "zh" | "en", 缺失翻译时回退英文原文。
    /// </summary>
    internal static class Lang
    {
        public static string Code = "zh";

        private static readonly Dictionary<string, string> Zh = BuildZh();

        private static Dictionary<string, string> BuildZh()
        {
            var d = new Dictionary<string, string>();

            // 通用
            d["Auto Clicker {0}"] = "自动点击器 {0}";
            d["Ready"] = "就绪";
            d["{0} · Settings auto-save"] = "{0} · 设置自动保存";
            d["Topmost"] = "窗口置顶";
            d["Confirm"] = "确认";
            d["Error"] = "错误";
            d["OK"] = "确定";
            d["Cancel"] = "取消";
            d["About"] = "关于";

            // 标签页
            d["Mouse Clicking"] = "鼠标连点";
            d["Keyboard Spam"] = "键盘连按";
            d["Record & Play"] = "录制回放";
            d["Hotkeys"] = "热键";
            d["Advanced"] = "高级设置";
            d["Sound FX"] = "音效";
            d["Macro Library"] = "宏库";

            // 顶栏热键提示
            d["Click"] = "连点";
            d["Record"] = "录制";
            d["Play"] = "回放";
            d["Keyboard"] = "键盘";
            d["Stop"] = "停止";

            // 连点页
            d["Click Settings"] = "点击设置";
            d["Interval (ms):"] = "间隔(毫秒):";
            d["Mouse button:"] = "鼠标按键:";
            d["Left"] = "左键";
            d["Right"] = "右键";
            d["Middle"] = "中键";
            d["Follow cursor"] = "跟随当前鼠标位置";
            d["Fixed position:"] = "固定坐标:";
            d["Get mouse position"] = "获取鼠标位置";
            d["Count (0=infinite):"] = "次数(0=无限):";
            d["Start clicking ({0})"] = "开始连点 ({0})";
            d["Stop clicking ({0})"] = "停止连点 ({0})";
            d["Test"] = "测试一下";
            d["Test click tooltip"] = "立即发送一次点击, 测试目标程序能否被本软件点到。\r\n按当前「鼠标按键 / 固定坐标 / 跟随光标」设置执行, 不会开启连点循环。";
            d["Status: Idle"] = "状态:未运行";
            d["Status: Running"] = "状态:运行中";
            d["Tip: move the mouse to the target after starting. Do not click Start while the cursor is on the button,\r\nor it will click the button itself. If the game blocks standard injection, switch method in Advanced."]
                = "提示: 开始后请把鼠标移到目标位置。鼠标停在按钮上时不要点“开始”，否则会连点按钮本身。\r\n若游戏屏蔽了标准注入, 请到「高级设置」页切换注入方式。";

            // 键盘页
            d["Key Settings"] = "按键设置";
            d["Key:"] = "按键:";
            d["or type a key:"] = "或直接输入按键:";
            d["Tap (click once per interval)"] = "点按(按间隔点击一下)";
            d["Hold (until stopped)"] = "按住不放(直到停止)";
            d["Start spam ({0})"] = "开始连按 ({0})";
            d["Stop spam ({0})"] = "停止连按 ({0})";
            d["Key not recognized, please select from the list or type a letter/number"] = "无法识别该按键: 请从列表选择, 或输入字母/数字/按键名(如 F1、Space、Enter)";
            d["Tip: keys go to the foreground window. Switch to the target window first, then toggle with the hotkey.\r\nOr type a key directly: a letter/number, or a name like F1 / Space / Enter."]
                = "提示: 按键发往当前活动窗口。开始前请先切换到目标窗口，再用热键开关。\r\n也可以直接输入按键: 字母/数字, 或输入下拉列表中的按键名(如 F1、Space、Enter)。";

            // 按键名
            d["Space"] = "空格 Space";
            d["Enter"] = "回车 Enter";
            d["Backspace"] = "退格 Backspace";
            d["Shift"] = "Shift";
            d["Ctrl"] = "Ctrl";
            d["Alt"] = "Alt";
            d["CapsLock"] = "CapsLock";
            d["Up"] = "↑ 上";
            d["Down"] = "↓ 下";
            d["Left2"] = "← 左";
            d["Right2"] = "→ 右";
            d["Up arrow"] = "↑";
            d["Down arrow"] = "↓";
            d["Left arrow"] = "←";
            d["Right arrow"] = "→";
            d["Mouse left"] = "鼠标左键";
            d["Mouse right"] = "鼠标右键";
            d["Mouse middle"] = "鼠标中键";
            d["Mouse X1"] = "鼠标X1";
            d["Mouse X2"] = "鼠标X2";
            d["Left Shift"] = "左Shift";
            d["Right Shift"] = "右Shift";
            d["Left Ctrl"] = "左Ctrl";
            d["Right Ctrl"] = "右Ctrl";
            d["Left Alt"] = "左Alt";
            d["Right Alt"] = "右Alt";
            d["Left Win"] = "左Win";
            d["Right Win"] = "右Win";
            d["Mute"] = "静音";
            d["Volume down"] = "音量-";
            d["Volume up"] = "音量+";
            d["Next track"] = "下一首";
            d["Previous track"] = "上一首";
            d["Stop media"] = "停止媒体";
            d["Play/Pause"] = "播放/暂停";
            d["Insert"] = "Insert";
            d["Delete"] = "Delete";
            d["Home"] = "Home";
            d["End"] = "End";
            d["PageUp"] = "PageUp";
            d["PageDown"] = "PageDown";

            // 录制回放页
            d["Record / Play"] = "录制 / 回放";
            d["Start recording"] = "开始录制";
            d["Stop recording"] = "停止录制";
            d["Start playback"] = "开始回放";
            d["Stop playback"] = "停止回放";
            d["Save Macro"] = "保存宏";
            d["Load Macro"] = "加载宏";
            d["Clear"] = "清空";
            d["Playback Options"] = "回放选项";
            d["Speed:"] = "回放速度:";
            d["x (1 = original)"] = "倍 (1 = 原速)";
            d["Loop playback (until hotkey stops)"] = "循环回放(直到按热键停止)";
            d["Loop count:"] = "循环次数:";
            d["Run minutes:"] = "运行分钟:";
            d["Until:"] = "直到:";
            d["Invalid time, use HH:mm format"] = "时间格式无效, 请用 HH:mm (如 23:59)";
            d["Loop playback hotkey: {0}"] = "循环回放: 按 {0} 开始/停止回放";
            d["Events: {0}"] = "已录制事件:{0}";
            d["Recording live view ({0} shown)"] = "正在录制, 实时显示最近 {0} 条";
            d["Delay(ms)"] = "延迟(ms)";
            d["Event"] = "操作";
            d["Edit delay"] = "编辑延迟";
            d["Add event"] = "添加事件";
            d["Delete selected"] = "删除选中";
            d["Tip: recording captures mouse moves, clicks, wheel and keys; bound hotkeys and injected clicks are excluded.\r\nLoop count 0 = infinite; run minutes 0 = no limit; Until + time stops at that clock time. After stopping you can edit events: double-click a row or use the right-side buttons."]
                = "提示: 录制包含鼠标移动与按键, 已绑定的热键与程序自身注入的点击不会被录进宏。\r\n循环次数 0 = 无限; 运行分钟 0 = 不限时; 勾选\"直到\"并填 HH:mm 时刻即运行到该时刻停止。停止后双击某行可编辑延迟, 或用右侧按钮添加/删除事件。";

            // 宏库页
            d["Saved macros"] = "已存宏";
            d["Macro name"] = "宏名称";
            d["Events"] = "事件数";
            d["Modified"] = "修改时间";
            d["Play this macro"] = "播放此宏";
            d["Rename..."] = "重命名...";
            d["Rename"] = "重命名";
            d["Create a copy"] = "创建副本";
            d["Delete macro"] = "删除宏";
            d["Refresh"] = "刷新";
            d["Open macros folder"] = "打开宏文件夹";
            d["Select a macro first"] = "请先选择一个宏";
            d["Macro name:"] = "宏名称:";
            d["Macro \"{0}\" already exists"] = "宏 \"{0}\" 已存在";
            d["Macro renamed to {0}"] = "宏已重命名为 {0}";
            d["Rename failed: {0}"] = "重命名失败: {0}";
            d["Macro copy created: {0}"] = "已创建宏副本: {0}";
            d["Copy failed: {0}"] = "复制失败: {0}";
            d["Macro deleted: {0}"] = "已删除宏: {0}";
            d["Delete failed: {0}"] = "删除失败: {0}";
            d["Macro is empty or corrupted"] = "宏为空或文件损坏";
            d["Playing macro: {0}"] = "正在播放宏: {0}";
            d["Software control"] = "软件控制";
            d["Add program..."] = "添加程序...";
            d["Remove program"] = "移除程序";
            d["Launch programs when playback starts"] = "回放开始时自动启动程序";
            d["Launch programs when playback ends"] = "回放结束时自动启动程序";
            d["Select a program to launch"] = "选择要自动启动的程序";
            d["Program added: {0}"] = "已添加程序: {0}";
            d["Select a program first"] = "请先选择一个程序";
            d["Launch failed: {0}"] = "启动程序失败: {0}";
            d["Tip: click the ▶ button on the left of a saved macro to run it immediately; click the name to select it, then rename / copy / delete on the right.\r\nPrograms in the Software control list auto-launch when playback starts or ends."]
                = "提示: 点击宏名称左侧的 ▶ 立即播放该宏; 点击名称选中后, 可在右侧重命名/创建副本/删除。\r\n软件控制列表中的程序会在回放开始/结束时自动启动。";

            // 事件描述
            d["Move to ({0}, {1})"] = "移动到 ({0}, {1})";
            d["Left down"] = "左键按下";
            d["Left up"] = "左键抬起";
            d["Right down"] = "右键按下";
            d["Right up"] = "右键抬起";
            d["Middle down"] = "中键按下";
            d["Middle up"] = "中键抬起";
            d["Wheel {0}"] = "滚轮 {0}";
            d["Key down"] = "按下";
            d["Key up"] = "抬起";
            d["Left click"] = "左键单击";
            d["Right click"] = "右键单击";
            d["Middle click"] = "中键单击";
            d["Key tap {0}"] = "按键 {0}";
            d["Mouse move"] = "鼠标移动";
            d["Mouse left down"] = "鼠标左键按下";
            d["Mouse left up"] = "鼠标左键抬起";
            d["Mouse right down"] = "鼠标右键按下";
            d["Mouse right up"] = "鼠标右键抬起";
            d["Mouse middle down"] = "鼠标中键按下";
            d["Mouse middle up"] = "鼠标中键抬起";
            d["Mouse wheel"] = "鼠标滚轮";
            d["Keyboard down"] = "键盘按下";
            d["Keyboard up"] = "键盘抬起";
            d["Mouse left click"] = "鼠标左键单击";
            d["Mouse right click"] = "鼠标右键单击";
            d["Mouse middle click"] = "鼠标中键单击";
            d["Keyboard tap"] = "键盘点按";
            d["X:"] = "X:";
            d["Y:"] = "Y:";
            d["Wheel delta:"] = "滚轮增量:";
            d["Key:"] = "按键:";
            d["Delay (ms):"] = "延迟(毫秒):";
            d["Insert after selection"] = "插入到选中事件之后";
            d["Delay"] = "延迟";
            d["Delay only"] = "纯延迟(仅等待)";
            d["Key combo"] = "按键组合";
            d["Key combo {0}"] = "按键组合 {0}";
            d["Capture combo"] = "捕获组合键";
            d["Press the key combo...\r\nSupports Ctrl/Alt/Shift/Win + any key, multi-key combos\r\nRelease all keys to finish, Esc to cancel"]
                = "请按下组合键...\r\n支持 Ctrl/Alt/Shift/Win + 任意键, 可多键组合\r\n全部松开后完成, Esc 取消";
            d["Please capture a key combo first"] = "请先捕获一个组合键";
            d["Key combo down {0}"] = "组合键按下 {0}";
            d["Key combo up {0}"] = "组合键抬起 {0}";
            d["Capture key"] = "捕获按键";
            d["Special key:"] = "特殊按键:";
            d["Current key: {0}"] = "当前按键: {0}";
            d["Please select or capture a key first"] = "请先选择或捕获一个按键";
            d["Press a key or combo...\r\nSingle key, or Ctrl/Alt/Shift/Win combos\r\nRelease all keys to finish, Esc to cancel"]
                = "请按下按键或组合键...\r\n支持单个按键, 或 Ctrl/Alt/Shift/Win 组合键\r\n松开全部按键完成, Esc 取消";

            // 事件右键菜单
            d["Edit delay..."] = "编辑延迟...";
            d["Insert event here"] = "在此插入事件";
            d["Delete this event"] = "删除此事件";
            d["Move up"] = "上移";
            d["Move down"] = "下移";

            // 热键页
            d["Function hotkeys (click a button, then press the new combo)"] = "功能热键(点击右侧按钮后按下新组合键)";
            d["Clicker toggle"] = "鼠标连点开关";
            d["Recording toggle"] = "录制开关";
            d["Playback toggle"] = "回放开关";
            d["Keyboard toggle"] = "键盘连按开关";
            d["Stop all"] = "全部停止";
            d["Reset default hotkeys"] = "恢复默认热键";
            d["Tip: any key, Ctrl/Alt/Shift/Win combos, multi-key combos (e.g. Ctrl+Q+W), mouse side buttons X1/X2.\r\nChanges are saved to config.json immediately and restored on next launch.\r\nCombos with modifiers or F-keys are recommended; bare letters/numbers conflict with typing."]
                = "提示: 支持任意键、Ctrl/Alt/Shift/Win 组合、多键组合(如 Ctrl+Q+W)、鼠标侧键 X1/X2。\r\n热键修改后立即保存到 config.json, 下次启动自动生效。\r\n建议使用带修饰键的组合或 F 区键; 纯字母/数字键会与正常打字冲突。";
            d["Bind \"{0}\" to: {1} ?"] = "将「{0}」绑定为: {1} ?";
            d["Confirm hotkey"] = "确认热键";
            d["Hotkey conflicts with \"{0}\", not changed"] = "热键与「{0}」冲突，未修改";
            d["Hotkey for {0} changed to {1} (saved)"] = "{0} 热键已改为 {1} (已保存)";
            d["Restored default hotkeys and saved"] = "已恢复默认热键并保存";
            d["Restore default hotkeys (F6~F9/F12) ?"] = "恢复默认热键 (F6~F9/F12) ?";
            d["(unset)"] = "(未设置)";

            // 高级设置页
            d["Injection method (switch when a game blocks clicking)"] = "注入方式(游戏屏蔽自动点击时换用)";
            d["Injection method:"] = "注入方式:";
            d["SendInput - standard system injection (default)"] = "SendInput - 标准系统注入(默认)";
            d["SendMessage - direct window messages (bypasses injected-flag detection)"] = "SendMessage - 直发窗口消息(绕过注入标记检测)";
            d["Interception - driver-level injection (most thorough, needs driver)"] = "Interception - 驱动级注入(最彻底, 需安装驱动)";
            d["Keyboard uses scan code injection (experimental, some games only accept scan codes)"] = "键盘使用扫描码注入(实验性, 部分游戏只认扫描码)";
            d["Target window: foreground"] = "目标窗口: 前台";
            d["Specified title:"] = "指定标题:";
            d["Grab window title"] = "抓取窗口标题";
            d["Grabbing in 3s..."] = "3 秒后抓取...";
            d["Humanization"] = "拟人化";
            d["Enable humanization"] = "启用拟人化";
            d["Randomize interval ±"] = "随机化间隔 ±";
            d["Fixed position jitter ±"] = "固定坐标抖动 ±";
            d["pixels"] = "像素";
            d["Random press duration (40~180ms, human-like)"] = "随机按键时长(40~180 毫秒, 真人点击特征)";
            d["Mouse trajectory (smooth curve instead of teleport)"] = "鼠标移动轨迹(平滑曲线而非瞬移)";
            d["Interface"] = "界面";
            d["Language:"] = "语言:";
            d["Theme:"] = "主题:";
            d["中文"] = "中文";
            d["English"] = "English";

            // 启动与托盘
            d["Startup"] = "启动";
            d["Start with Windows"] = "开机自启动";
            d["Start silently (to tray)"] = "静默启动(最小化到托盘)";
            d["Show main window"] = "显示主界面";
            d["Exit"] = "退出";
            d["Auto-start enabled"] = "已开启开机自启动";
            d["Auto-start disabled"] = "已关闭开机自启动";

            // 按键音效页
            d["Key Sound Effects"] = "按键音效";
            d["Enable key sound effects (new key overrides the playing sound)"] = "启用按键音效(按下新键会打断正在播放的音效)";
            d["Add binding"] = "添加绑定";
            d["Delete binding"] = "删除绑定";
            d["Play sound"] = "试听";
            d["Open sounds folder"] = "打开音效文件夹";
            d["Volume:"] = "音量:";
            d["Global volume:"] = "全局音量:";
            d["Selected key volume:"] = "选中按键音量:";
            d["Sound file"] = "音效文件";
            d["Bindings: {0}"] = "绑定数量: {0}";
            d["Tip: assign a sound to any key; pressing the key plays the sound, and a newly pressed bound key overrides the currently playing one.\r\nPut sound files (wav/mp3) into the Sounds folder next to this program."]
                = "提示: 给任意按键绑定音效, 按下该键即播放; 新按下的已绑定键会打断正在播放的音效(覆盖式播放)。\r\n把 wav/mp3 音效文件放进本程序目录的 Sounds 文件夹。";
            d["Press the single key to bind a sound...\r\nOnly a single non-modifier keyboard key is supported\r\nRelease the key to finish, Esc to cancel"]
                = "请按下要绑定音效的单个按键...\r\n仅支持单个普通键盘键(不支持 Ctrl/Alt 等修饰键)\r\n松开按键完成设置, Esc 取消";
            d["Only a single non-modifier key is supported for sound binding"] = "音效绑定仅支持单个非修饰键";
            d["Sound bound: {0} → {1}"] = "已绑定音效: {0} → {1}";
            d["Press a key or combo to bind a sound...\r\nSingle key, or Ctrl/Alt/Shift/Win combos (e.g. Ctrl+C)\r\nRelease all keys to finish, Esc to cancel"]
                = "请按下要绑定音效的按键或组合键...\r\n支持单个按键, 或 Ctrl/Alt/Shift/Win 组合键(如 Ctrl+C)\r\n松开全部按键完成设置, Esc 取消";
            d["Sound binding deleted"] = "已删除音效绑定";
            d["Select a sound binding first"] = "请先选择一条音效绑定";
            d["Select sound file"] = "选择音效文件";
            d["Key sound effects enabled"] = "按键音效已开启";
            d["Key sound effects disabled"] = "按键音效已关闭";
            d["Driver mode: install the driver (github.com/oblitum/Interception, admin) and put interception.dll next to this exe.\r\nHVCI (memory integrity) may block unsigned drivers; rename this exe to dodge process-name checks."]
                = "驱动模式说明: 从 github.com/oblitum/Interception 安装驱动(需管理员权限), 并把 interception.dll\r\n放到本程序目录。驱动层注入与真实硬件输入无异。\r\n系统开启内核隔离/内存完整性(HVCI)时未签名驱动可能无法加载; 如游戏检测进程名, 可自行重命名本程序 exe。";
            d["Enable UI animations (hover / press / tab transitions)"] = "启用界面动效(悬停 / 按压 / 标签切换过渡)";

            // 状态消息
            d["Clicker running, press {0} to stop"] = "连点运行中，按 {0} 停止";
            d["Stopping clicker..."] = "正在停止连点...";
            d["Clicker stopped"] = "连点已停止";
            d["Sent one click"] = "已发送一次点击";
            d["Stop current task before testing"] = "请先停止当前任务再测试";
            d["Test interval too short, please wait"] = "点击测试间隔太短，请稍候";
            d["Got position: {0}, {1}"] = "已获取坐标: {0}, {1}";
            d["Get position in 3s..."] = "3 秒后获取...";
            d["Keyboard spam running, press {0} to stop"] = "键盘连按运行中，按 {0} 停止";
            d["Stopping keyboard spam..."] = "正在停止键盘连按...";
            d["Keyboard spam stopped"] = "键盘连按已停止";
            d["Recording stopped, {0} events"] = "录制已停止，共 {0} 个事件";
            d["Recording... (old macro cleared) press {0} to stop"] = "正在录制...(旧宏已清空) 按 {0} 停止";
            d["Recording failed to start"] = "录制启动失败";
            d["Event limit reached (200k), recording auto-stopped"] = "事件数达到上限(20万)，录制已自动停止";
            d["No macro to play, record first"] = "没有可回放的宏，请先录制";
            d["Playing... press {0} to stop"] = "正在回放... 按 {0} 停止";
            d["Stopping playback..."] = "正在停止回放...";
            d["Playback finished"] = "回放已结束";
            d["Stopped all ({0})"] = "已全部停止 ({0})";
            d["No macro to save"] = "没有可保存的宏";
            d["Macro saved: {0}"] = "宏已保存: {0}";
            d["Save failed: {0}"] = "保存失败: {0}";
            d["Load failed: {0}"] = "加载失败: {0}";
            d["Macro loaded: {0} events"] = "宏已加载: {0} 个事件";
            d["Macro cleared"] = "宏已清空";
            d["Config load failed, using defaults"] = "配置文件加载失败, 已使用默认设置";
            d["{0} hotkey config invalid, using default"] = "{0} 热键配置无效, 已用默认值";
            d["Driver mode unavailable (interception.dll not found), fell back to SendInput"] = "驱动模式不可用(未找到 interception.dll), 已回退到 SendInput";
            d["Warning: interception.dll not found, driver mode unavailable (see note below)"] = "警告: 未找到 interception.dll, 驱动模式不可用(见下方说明)";
            d["Driver mode unavailable: interception.dll missing or driver not installed"] = "驱动模式不可用: 未找到 interception.dll 或驱动未安装";
            d["Switch to the target window within 3 seconds"] = "请在 3 秒内切换到目标窗口";
            d["Grabbed window title: {0}"] = "已抓取窗口标题: {0}";
            d["Select an event first"] = "请先在列表中选择一个事件";
            d["Delay of event {0} changed to {1} ms"] = "事件 {0} 延迟已改为 {1} 毫秒";
            d["Event added at position {0}"] = "已在第 {0} 个事件后添加新事件";
            d["Event deleted"] = "已删除事件";
            d["Edit event delay"] = "编辑事件延迟";
            d["Add macro event"] = "添加宏事件";
            d["Delete event"] = "删除事件";
            d["Delete this event?"] = "确定删除该事件?";
            d["No event to edit"] = "没有可编辑的事件";
            d["Playing... press {0} to stop"] = "正在回放... 按 {0} 停止";

            // 捕获对话框
            d["Set hotkey"] = "设置热键";
            d["Press the new hotkey combo...\r\nSupports Ctrl/Alt/Shift/Win + any key, multi-key combos, mouse side buttons\r\nRelease all keys to finish, Esc to cancel"]
                = "请按下新的热键组合...\r\n支持 Ctrl/Alt/Shift/Win + 任意键, 可多键组合, 支持鼠标侧键\r\n全部松开后完成设置, 按 Esc 取消";
            d["Current combo: {0}\r\nKeep pressing other keys to extend the combo; release all to finish\r\nPress Esc to cancel"]
                = "当前组合: {0}\r\n继续按其它键可加入组合, 全部松开后完成设置\r\n按 Esc 取消";
            d["No key detected, try again... (Esc to cancel)"] = "未检测到按键, 请再试一次... (Esc 取消)";
            d["Display DPI changed, interface relaid out"] = "显示器缩放已变化, 界面已重新排布";

            return d;
        }

        /// <summary>按当前语言返回文本; 无翻译时返回英文原文。</summary>
        public static string T(string en)
        {
            if (Code == "zh")
            {
                string zh;
                if (Zh.TryGetValue(en, out zh)) return zh;
            }
            return en;
        }

        /// <summary>格式化本地化文本。</summary>
        public static string F(string en, params object[] args)
        {
            return string.Format(T(en), args);
        }
    }
}
