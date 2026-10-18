# 2026-10-15

## QuickSearch-Win：微信划词不弹横条（排查中）

**源码位置**：`C:\Users\64381\WorkBuddy\2026-10-10-16-56-30\QuickSearch-Win`
（Rust + Tauri 2，`src/main.rs` 约 444KB，另有 win.rs / hook.rs / clip.rs。
构建产物 `target/release/QuickSearch.exe` 与桌面 `QuickSearch-新版.exe` 同源，
配置在 `%APPDATA%\QuickSearch\config.json`，日志在 `%APPDATA%\QuickSearch\log.txt`
——**仅当进程环境变量 QS_DEBUG=1 时才写日志**。）

**触发链路**（拖选路径）：
鼠标钩子 `hook.rs` → 消费线程 `main.rs:~1939` 判拖动>4px → `should_try_select()`
→ `schedule_capture()` → `capture_selection()`：备份剪贴板 → 注入 Ctrl+C
→ 等剪贴板序号变化(460ms) → `clipboard_owned_by_foreground()` → 读文本 → `show_bar`。

**已确证的事实**：
1. 微信 4.1.15.13 是 **Qt 5.15.14** 写的，主窗口类 `Qt51514QWindowIcon`
   （Weixin.exe pid 2280），界面绘制在子窗口 `MMUIRenderSubWindowHW`
   （style=WS_CHILD|WS_VISIBLE|WS_CLIPSIBLINGS，ex=**WS_EX_LAYERED|WS_EX_TRANSPARENT**）。
2. `WindowFromPoint` 在微信客户区返回的是 Qt 主窗口（MMUI 子窗口因 TRANSPARENT 被跳过），
   `point_in_client` 处处为真 → **`should_try_select` 对微信是通过的**，问题不在触发判定。
3. 注入输入本身没问题：`SendInput`/`mouse_event` 生效（光标移动），自建 LL 钩子能收到
   注入事件；QuickSearch 自己的钩子也能处理注入事件（日志出现 mouseup/up-check）。
4. 微信主窗口当前处于「隐藏」态（WS_VISIBLE 被清）。用 `WM_SYSCOMMAND SC_RESTORE`
   可强置可见，但此后**微信对注入的左键/右键/键盘毫无反应**（截图字节完全一致、
   右键不弹菜单、注入 Ctrl+C 后剪贴板序号不变）→ 该状态下微信不处理输入，
   无法用于复现。用 `mouse_event`/`SendInput` 复现微信场景**不可信**。
5. 对照组：记事本等普通窗口注入正常；`ImageGrab` 截图管道正常（能捕捉变化）。

6. 用户在微信里确实能选中文字（本人确认），但注入的拖选在微信里**建立不了选区**，
   注入的文字也进不了微信输入框 → 用注入方式复现微信场景不可靠。
7. 用户平时运行的是**桌面上的 `QuickSearch-新版.exe`**（进程名 `QuickSearch-新版`，
   主窗口 `QuickSearchBar`），且**以管理员权限运行**（非管理员 taskkill 会被拒绝）。
8. 单实例保护（`win.rs::already_running()`，命名互斥体
   `Local\QuickSearch-8F42A1C6-SingleInstance`）会导致新实例**秒退**；而且
   `main.rs:7589` 的 `=== start` 日志是异步批量落盘的（300ms 一刷），秒退时
   **连启动日志都来不及写** → 表现为「日志一行没有」，极易误判成日志没开。
   逃生口：环境变量 `QS_INSTANCE_TAG`（互斥体名字多一段 tag，可另起一份实例）。
9. 从本会话的沙箱里启动的 QuickSearch **跨回合会被回收**（同回合内能活）；用户自己
   启动的进程不受影响。→ 需要用户自己双击脚本启动。
10. 已交付 `QuickSearch-Win/qs-debug.cmd`：自我提权（`Start-Process -Verb RunAs`）
    → taskkill 掉旧实例（含 `QuickSearch-新版.exe`）→ `set QS_DEBUG=1` 启动。

11. 用户明确不接受「让我配合测试」，要求直接改好。
12. 本机对**微信**的合成输入被系统性中和：`SendInput`/`mouse_event` 一律
    返回成功、但**光标不移动**、微信无任何反应（同环境下对桌面/自建窗口注入正常）。
    → 结论：**在这台机器上无法用注入手段验证微信场景**，自动化复现此路不通。

## 已实施的修复（已编译并验证）

改动文件：`src/win.rs`、`src/main.rs`、`src/clip.rs`（未动 Cargo.toml）。
思路：取词环节不再「一条道走到黑」，三条通道轮换 + 不再因快照失败而整体放弃。

1. **快照失败不再放弃**（`capture_selection`）：以前 `clip::snapshot()` 拿不到就
   `return None`，而微信这类客户端会长时间占着剪贴板 → 取词在第一步就死。
   现在跳过还原、取词继续（最坏结果 = 用户自己按一次 Ctrl+C）。
2. **新增两条「窗口消息」取词通道**（`win::post_copy_to_focus` = WM_COPY；
   `win::post_ctrl_c_to_focus` = AttachThreadInput + SetKeyboardState(Ctrl) +
   PostMessage WM_KEYDOWN/UP(VK_C)）。原因：`SendInput` 造的按键带
   `LLKHF_INJECTED` 标记，做过反自动化处理的客户端会无视它们。
   候选目标：前台线程的 hwndFocus / hwndCaret / hwndActive + 前台窗口本身。
3. **`send_ctrl_c` 补上扫描码**（`MapVirtualKeyW`）并拆成 4 次 SendInput
   （Qt/Chromium 系会看扫描码）。
4. **剪贴板归属校验更准**（`clipboard_owned_by(target_pid)`）：除前台 pid 外，
   也接受「划词起点那个窗口」的 pid（多进程客户端里负责复制的未必是前台进程）。
   仅最后一轮放宽为「内容确实变了就收」（`fresh`），代价有界且日志留痕。
5. **`win::log_sync`**：启动行 / 单实例退出行改为同步落盘 —— 这几行在
   `process::exit` 前几百微秒产生，走异步队列会直接丢（排查时白猜两轮）。
6. 测试逃生口 `QS_SKIP_SENDINPUT=1`：跳过 SendInput 轮，用来单独验证窗口消息通道。

**验证**（`tools/verify-trigger.js`，4 场景）：
- 正常模式 **4/4**；`QS_SKIP_SENDINPUT=1` **4/4**；编译 1m47s 通过。
- 日志证据：正常 → `round=0 ch=SendInput changed=true owned=true fresh=true valid=true`；
  跳过 → `round=0 ch=WM_COPY ... valid=true`；空白拖动 → 各轮 `changed=false`，不弹。

**交付**：`Desktop\QuickSearch-新版-修复版.exe`（与 `target/release/QuickSearch.exe`
同哈希 `15ec2e88bba5e59afb3faaec7458b615`）。用户那个运行中的
`QuickSearch-新版.exe` 是旧版且文件被占用，需先退出再启用新版。

**仍未证实**：微信里那条失败到底属于 ①②③ 哪一种（合成输入被中和，无法取日志）。
修复按「三种都兜住」设计，但**微信实机效果需要用户跑一遍才能确认**。

## 20:35 用户跑完修复版：微信仍不弹，再来一轮

- 用户双击 `QuickSearch-新版-修复版.exe` 试了微信划词，**仍然不弹**。
- 又没拿到日志：日志开关当时只认环境变量 `QS_DEBUG`，**用户双击启动根本带不上**。
  → 教训：别让用户配环境变量。
- 因此新增**标记文件开关**：`win::log_on()` = `QS_DEBUG=1` || `QS_LOG=1` ||
  `%APPDATA%\QuickSearch\log.on` 存在。已创建 `log.on`，并自测通过
  （不带任何环境变量启动，日志确实增长）。
  自测时间：20:37，日志 51577 → 53509 字节。
- 同时把 `capture_selection` 的日志加厚成「一次跑完就能定位」：
  - `capture: 开始 目标pid=? 快照=ok/失败 复制前剪贴板="前20字"`
  - `capture: round=N ch=? changed=? owned=? fresh=? valid=? 目标=? owner=? 前台=? 读到="前40字"`
  （**不管归属校验过没过都读一次剪贴板**，这样"到底有没有东西复制进来"一眼可见）
- 重新编译 1m45s 通过；`verify-trigger.js` 回归 **4/4**。
- 交付：`Desktop\QuickSearch-诊断版.exe`（含修复 + 标记文件日志）。
- 待用户用它复现微信划词 → 读 `%APPDATA%\QuickSearch\log.txt` 定位。
- ⚠ 排查结束后记得删掉 `%APPDATA%\QuickSearch\log.on`，恢复正常（不写日志）行为。

## 20:39 拿到决定性日志 → 真正根因找到

用户用诊断版在微信里划词后，日志（`capture:` 新增字段）给出结论：

```
up-check: ... trySelect=true ... target=0xa913b0 cls=Qt51514QWindowIcon
capture: 开始 目标pid=2280 快照=ok
capture: round=0 ch=SendInput changed=true owned=true fresh=false valid=false 目标=2280 owner=2280 前台=2280 读到=None
capture: round=1 ch=WM_COPY   changed=true owned=true fresh=false valid=false ... 读到=None
capture: round=2 ch=按键消息  changed=true owned=true fresh=false valid=false ... 读到=None
start_capture: 没拿到文字，不弹
```

**根因**：微信**确实响应了复制**（三通道序号都变、owner=2280=目标 pid、归属校验通过），
但 `clip::read_text()` 只认 `CF_UNICODETEXT`，**读出来是空的**（`读到=None`）。
→ 问题不是"弹不弹"，也不是"注不注入"，而是**读剪贴板的格式口径太窄**。

**修复（第 2 轮）**：
1. `clip.rs` 新增多格式读：`read_text_open`/`read_text_any` ——
   `CF_UNICODETEXT(13)` → `CF_TEXT(1, CP_ACP/GBK)` → `CF_OEMTEXT(7, CP_OEMCP)` →
   `CF_HTML`（抠片段纯文本，按**字节**偏移切）。ANSI 转换用
   `MultiByteToWideChar`（不能当 UTF-8 解）。
2. `capture_selection` 改为**轮询读**（460ms 内每 40ms 试一次），覆盖
   延迟渲染 / 异步复制（"序号变了但数据还没就位"）。
3. 读不到时记 `clip::format_names()`（格式**名字**，含自定义格式注册名）→
   下一轮不用再来回。
4. `read_current` / `late_text_patch` 也改走 `read_text_any`（Ctrl+C 那条路同样受益）。
5. 删掉旧的 `read_text()`（被取代且触发 dead_code 警告，项目标准零警告）。

**验证**：`verify-trigger.js` 正常模式 **4/4**、`QS_SKIP_SENDINPUT=1` 模式 **4/4**。
⚠ 踩坑：用户自己的实例也在跑且 `log.on` 开着日志，两个实例往同一份 log.txt 写，
导致测试"取最后一行 show_bar"取错对象、误报 [2] FAIL。→ 测试实例改用
**隔离 APPDATA**（`run_regression.js` 里设 `APPDATA=<临时目录>`）后稳定 4/4。

**交付**：`Desktop\QuickSearch-新版-修复版.exe`（md5 `5d2362bfc36e18943b29519e118558af`）。

## 20:48 用户反馈「还是不行」→ 拿到第 2 份决定性日志

同一份日志里同时出现**一次成功**和**多次失败**，形状非常清楚：

成功那次：
```
capture: round=1 ch=WM_COPY changed=true owned=true fresh=true valid=true 目标=2280 读到=Some("没有其它")
capture: 最终拿到 4 chars → show_bar: "没有其它"     ← 横条真的弹出来了，取词正确
```
失败那几次：
```
capture: round=0 ch=SendInput changed=true owned=true fresh=false valid=false 读到=None
capture: round=0 未取到词 changed=true 剪贴板格式=[]        ← 注意 owner 仍是 2280
```

**推断（关键）**：`owner=2280`（微信握着剪贴板）但 `格式=[]` 且**读到 None**
（连复制前那份哨兵文本都读不到）→ 微信复制是**两步**：先 `EmptyClipboard`
清空、**并且一直握着剪贴板不撒手**，等内部渲染/助手进程把数据准备好才写进去。
"握着"期间我们连 `OpenClipboard` 都打不开。

**而我们的重试每 ~460ms 就重投一次复制请求 → 让它重新清空+重新计时 →
每一轮都差一点点，永远等不到落地**。这就是"十次九不弹、偶尔弹一次"。

**第 3 轮修复**：
1. `SEL_READ_WINDOW_MS = [1500, 800, 800]`：每轮投递后的读窗口，第一轮给足。
2. **窗口内不再重投**（重投会重置对方的写盘过程）。
3. 读窗口分两档：序号没变时只等 250ms（"没选中"是最常见情况，不占住取词线程），
   **序号一变立刻拉长到上限**等数据落地。
4. `BUSY` 等待上限 1.2s → 4.5s（最坏 3.7s 的取词不能被丢弃）。
5. `clip::describe_formats()`：明确区分 `打不开（被别的程序握着）` / `空的（没有任何格式）`。
6. `open_clipboard()` 重试 24×8ms → 30×10ms（≈300ms）。

**回归**：正常模式 4/4、`QS_SKIP_SENDINPUT=1` 4/4。

**顺手清理**：
- 之前自动化测试把哨兵文本 "Quick Search 选" 留在了用户剪贴板里，
  导致 Ctrl+C 那条路弹出的"预填文字"是这条测试串（看起来像 bug）。已清空剪贴板。
- 删掉我放在桌面的 `QuickSearch-诊断版.exe`。

**交付**：`Desktop\QuickSearch-新版-修复版2.exe`（md5 `0d1e6679745b2799e30aecca1a4f8493`）。
注意旧版 `QuickSearch-新版-修复版.exe` 仍在运行（文件被占用，覆盖不了）。

## 20:57 用户再反馈「还是不行」→ 根因定案：微信不把选区交给剪贴板

新日志（带剪贴板状态区分）：
```
capture: 开始 目标pid=2280 快照=ok 复制前剪贴板=None
capture: round=0 ch=SendInput changed=true owned=true 读到=None
capture: round=0 未取到词 changed=true 剪贴板=空的（没有任何格式）   ← 关键
（round=1 WM_COPY / round=2 按键消息 同样：空的）
```
关键推论链：
1. `owner=2280`（微信握着剪贴板）+ **没有任何格式** + 读到 None（连复制前的旧内容都读不到）
   → 微信**清空了剪贴板但没写任何东西**；
2. 而 20:47 那次成功（`读到=Some("没有其它")`）紧跟用户**手动按 Ctrl+C** 之后
   → 手动复制有效、**合成输入无效**；
3. 结论：微信把"带 LLKHF_INJECTED 标记的按键"当自动化挡掉了（它自己的钩子
   在我们钩子之后被调用，可以吞掉事件，这也解释了为什么我们自己能看到注入的
   Ctrl+C、微信却收不到）。
→ **微信这条路上"请对方复制"根本走不通**，必须换取词原理。

**第 4 轮修复：加 OCR 兜底通道（不依赖对方配合）**
- `capture_by_ocr(rect)`：复制通道全失败时，**把用户划过的那块直接截图 + 本地 OCR**。
- `ocr_ready_image()`：灰度 → Otsu 找文字/底色分界 → 少数派判为文字 → 输出**白底黑字**。
  必须做：选中文字带高亮底色，反色小图直接送 Windows OCR 会把中文认成偏旁堆
  （实测带蓝色高亮时「测试ABC123玻璃折射」→「《试ABC123玻氵贝刂}」）。
- 按行裁剪 + 四周补白：条带里的**相邻行残字**会把分字带偏（「玻璃折射」→「玻氵贝刂} 折射」）；
  裁到真正的文字行后**外围补 12px 白边**（贴边裁 + 太矮时 OCR 会直接报"没有识别到文字"）。
- 引擎跟随用户在设置里选的：`cfg.ocr_engine`（用户是 windows；配了百度密钥时可切百度提高准确率）。
- 测试逃生口：`QS_FORCE_OCR=1`（跳过复制通道）、`QS_DUMP_OCR=1`（把兜底截到的图存盘）。
- `is_empty()` 快照不还原：空快照还原 = EmptyClipboard，会把对端迟到写入的数据抹掉。
- 等待期间按 300ms 采样剪贴板状态（诊断）。

**验证**：记事本放已知文字 → 划词 → 兜底 OCR 拿到 22~30 字，
识别率约 90%（错的是左右结构字被拆分：「折」→「扌斤」）。
`verify-trigger.js` 正常模式 **4/4**（空白拖动仍不弹）。

**交付**：`Desktop\QuickSearch-新版-修复版3.exe`（md5 `317392a4107742e10f32b04657d6c539`）。

## 21:34 用户点出根本问题：「选中就弹条，不是选中就复制」

用户原话：「为什么是用剪切板的？我的目的是选中文字之后就自动弹出横条……
**我不想要说 选中就复制**」。
→ 之前 4 轮都在「修模拟复制那条链」（还原、时序、格式、OCR 兜底），方向从一开始就偏了：
**取词不该默认必须借用剪贴板**。

**第 5 轮：改成三条通道，全程不碰剪贴板**（用户拍板选「UIA 直读 + OCR 兜底」）
1. **标准编辑控件直读**（`win::edit_selection_text_at`）：`EM_GETSEL` 取选区下标 +
   `WM_GETTEXT` 取正文。两条都是系统**代为跨进程封送**的消息，不用注入、不复制。
   覆盖记事本 / 各种输入框（这些控件**没有 UIA 文本模式**，必须单独一条）。
   ⚠ EM_GETSEL 只取**返回值**里的打包下标（LOWORD/HIWORD），不能传指针 ——
   指针不会跨进程封送，写了会打崩对方。
2. **UIA TextPattern**（`win::uia_selection_text`）：`CUIAutomation` →
   `ElementFromPoint/ElementFromHandle` → 元素上溯最多 10 层找 `TextPattern` →
   `GetSelection` → `GetText`。覆盖浏览器（Chromium 系）/Office/现代编辑器。
3. **OCR 兜底**：程序没有 UIA 文本模式（微信这类自绘 UI）时读划过的那块屏幕。
- 老「模拟复制 + 还原」通道保留但**默认不走**，只在 `QS_FORCE_COPY=1`（测试用）时启用。
- 快照还原改回**无条件还原**（空快照也还原 = EmptyClipboard），杜绝"选区留在剪贴板"。

**性能坑（都实测踩过）**：
- 进程内第一次调 UIA 要加载 UIAutomationCore + 建跨进程代理，**>1 秒** →
  启动时后台 `uia_warm_up()` 预热（实测预热本身只要 22~23ms）。
- 没有 UIA 文本模式的进程**不会报错、而是每次问询要 ~700ms**（元素上溯一圈）→
  加了「该 pid 没有 UIA」的负缓存 + 重试 2 次，避免划词要等 2 秒。

**验证**：`verify-trigger.js` **4/4**；日志实证 `start_capture: 编辑控件 通道拿到 26 chars`
（精确取到选区，剪贴板全程未被触碰）；空白拖动正确地不弹。

**交付**：`Desktop\QuickSearch-新版-修复版.exe`（md5 `85bb8445747185d11f7a03edea4065d2`），
旧的 修复版2/3/4 已删。
⚠ 仍待用户确认；确认可用后删 `%APPDATA%\QuickSearch\log.on` 恢复正常日志行为。
