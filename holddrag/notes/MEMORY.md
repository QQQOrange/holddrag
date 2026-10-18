# HoldDrag 项目长期笔记

## 项目身份
- 产品：HoldDrag — Windows 桌面工具，低级键鼠钩子（`WH_KEYBOARD_LL=13` / `WH_MOUSE_LL=14`）+ 分层窗口拖拽框，按住触发键后可拖动鼠标下方的任意窗口。
- 技术栈：C# / .NET 10 WinForms，**框架依赖**单文件发布（`net10.0-windows7.0`，x64，`LangVersion 14.0`，`--self-contained false -p:PublishSingleFile=true`）。
- 源码位置：`D:\Workbuddy\2026-10-12-18-38-07\HoldDrag\`（`src\` 为 2026-10-12 快照，已与线上 exe 分叉）。
- 配置：`C:\Users\64381\AppData\Roaming\HoldDrag\config.json`；日志 `holddrag.log`。
- 自签标记：合成事件带 `dwExtraInfo = 1215261796`（MagicExtraInfo），用于区分自身注入。

## 关键代码坐标（反编译产物内行号）
反编译基准文件：`D:\Workbuddy\2026-10-12-18-38-07\HoldDrag\work\deployed\HoldDrag.decompiled.cs`

| 类型 | 起始行 |
|---|---|
| Coordinator | 812 |
| FrameColors | 3517 |
| FrameStyles | 3717 |
| HookEngine | 4203 |
| Log | 5094 |
| Native | 5289 |
| OverlayWindow | 6335 |
| LayeredBitmapOverlay | 6958 |
| ActivationFrame | 7298 |
| SettingsForm | 13703 |
| FramePreviewBox | 13840 |
| StyleGallery | 14166 |
| TriggerState | 21943 |
| WindowActions | 22179 |

自定义窗口类名：`HoldDragOverlayWindow`（L6574）、`HoldDrag.Probe.Target`（L3284）。

## 反编译流程（重要）
```
./sdk/dotnet/dotnet.exe work/ilspycmd/tools/net10.0/any/ilspycmd.dll \
  -o <out> --bundle-entry HoldDrag.dll "C:/Users/64381/Desktop/HoldDrag.exe"
```
- 必须用**项目自带 SDK** 的 `dotnet.exe`（系统 dotnet 无 SDK）。
- 单文件 bundle 必须指定 `--bundle-entry HoldDrag.dll`，否则报 "single-file bundle"。
- `ilspycmd.dll` 位于 `net10.0`，不是 `net8.0`。

## 反编译源码二次编译（做探针时）的补丁清单
1. 删除所有 `^\[(assembly|module):.*$` 行（`[module: RefSafetyRules(11)]` → CS8335；`[assembly: TargetFramework]` 重复 → CS0579）。
2. `<>y__InlineArray17` → 改名（如 `zzy_InlineArray17`），否则 CS1001/CS1514 非法标识符。
3. file-scoped `namespace X;` → 块式 `namespace X { ... }`，否则 CS8956。

## 探针工程位置
- `…\HoldDrag\work\renderprobe\` — 样式墨量量测（提取 FrameStyles.cs，桩 Native）
- `…\HoldDrag\work\overlayprobe\` — 真实 ActivationFrame 显示 + 抓屏
- `…\HoldDrag\work\formprobe\` — 打补丁的全量反编译源码，`Main` 支持 `click` / `click2`

> 抓屏必须 `SetProcessDpiAwarenessContext(PerMonitorV2 = -4)`，否则坐标虚拟化导致全 0。
> 确定性渲染优先用控件 `DrawToBitmap`，`CopyFromScreen` 会抓到别的窗口内容。

## 三个已知缺陷（2026-10-16 定位，**当日已修复并验证**）
1. **设置页无法拖动**：`WindowActions.FindTarget` 只接受 `lpdwProcessId != OwnPid`，本进程窗口被跳过。
   → 已修：新增 `IsOwnProcessTarget(h)` + 静态开关 `AllowDragOwnWindows`（config `AllowDragOwnWindows`，默认 true）。
2. **粗细参数视觉不生效**：① `StyleGallery.OnPaint` 把粗细钳到 `Math.Min(_thickness, 3)`；② `FrameStyles.MarginFor` 以窗口可见边缘为描边中心，一半被遮挡；③ 外扩模式 `RaiseAboveAll=false` 不置顶。
   → 已修：删钳制；`MarginFor = ceil(StrokeFor)+光晕`；`Draw` 内缩 `budget−stroke/2`；4 处 `RaiseAboveAll = true`。
3. **触发键激活后左键穿透点击**：① HoldMs 阈值到达前左键按下已放行，激活后 `PostPairedButtonUp` 补发 WM_LBUTTONUP 形成完整点击；② `FindTarget` 返回 0 时按下/抬起都放行。
   → 已修：阈值期间**缓冲**按下（`_bufferedDown`/`_bufferedPt`）不投递；激活时无条件吞掉；删除补发抬起；短按触发键时用 `SendInput` 归还点击（`FlushBufferedClicks`）。

### 修复工程与验证
- 修复工程 `…\HoldDrag\work\fixed\`（WinExe，入口 `Program.Main`）；对照工程 `…\work\base\`；探针 `…\work\probefixed\`（`frame`/`click`/`click2`/`click3`）。
- 发布形态 = **框架依赖单文件**：`--self-contained false -p:PublishSingleFile=true`（632KB vs 原始 629KB）。
- 自检：基线同源重建 661/661 → 修复版 **671/671** 全过。
- ⚠️ **自检里有 13 项断言的是旧缺陷行为**（1 处「按住阶段放行」、4 处 `PhantomUpSentForTest >`、6 处配对抬起详情、1 处 `CaptureCornerPixel` 取点），移植补丁时必须一并倒转。
- ⚠️ `AdoptHeldButtonIfAny` 补起拖动的起点必须用**当前光标**而非缓冲点，否则补起那一帧窗口会跳变。
- `--selftest` 会覆盖 `%AppData%\HoldDrag\selftest.txt`；原文件已备份到交付目录 `evidence\selftest-before-fix.txt`。

## 第二轮追加修复（2026-10-16，用户要求「尽可能修复剩余风险」）
4. **拖自身窗口会抢走设置页控件的输入**：已修 —— `WindowActions.IsPointOverOwnInteractiveControl(pt)`（`WindowFromPoint→GA_ROOT→本进程合法窗口→类名可交互`）在三条路径上让路：`HandleButtonDown` 接管前、`OnMouseCore` 阈值缓冲分支、`AdoptHeldButtonIfAny`。
   - ⚠️ **WinForms 会把系统控件类名包成 `WindowsForms10.<基础类名>.app.0.xxxxx_r3_ad1`**（实测滑块 = `WindowsForms10.msctls_trackbar32.app.0.3279090_r3_ad1`）。比对前必须剥掉 `WindowsForms10.` 前缀、截到下一个 `.` 之前，否则原生控件永远识别不出来。
   - 白名单枚举在 `WindowActions.InteractiveClasses`；Label 不在内 → 标题栏/边框/空白区仍可拖整窗。
   - ⚠️ **`GroupBox` 的窗口类就是 `BUTTON`**，只靠类名根本无法与真按钮区分 —— 第三轮才补上样式位判定（见下）。
5. **自检 T80 偶发失败**：已修 —— 宿主标题带自身 PID 唯一化 + 启动前清同前缀孤儿（`KillStaleWindowHosts`）+ 收尾只杀本次窗口所属进程（`KillWindowHostProcess`，拒绝杀自身）。原 `finally` 用 `PidOfWindow(num)` 会杀错残留宿主并漏掉真正自己拉起的那个 → 失败自我复制。
6. **`Solid` 外扩边距**：`MarginFor` 的 `"Solid" => num + 3` 改 `=> num`（外扩量 = `ceil(1.8t)`）。**下限不可再压**：笔画宽 `1.8t` 要求外扩量 ≥ 笔画宽，否则描边被浮层边界裁掉。

- 自检：**677/677**（Debug 连跑 3 次 + 发布版 exe 复验均为 677/0）。发布体积 **637,095 字节**；补丁 **44 块**；源码 **24,056 行 / 676 KB / 纯 CRLF**。
- ⚠️ **任何会移动光标的自检用例，结束必须 `SetCursorPos` 还原**。T86 起初没还原，导致**下一次**运行里 T63「最大化窗口右键」报红（光标 `1920,1080`、窗口 `-1520,-730 262x71`）——单跑通过、连跑必挂。
- 探针 `frame` 现已同时跑 `Dashed` 与 `Solid` 各 4 档，输出「外扩边距 / 外露墨量 / 外侧可见带宽 / 笔画宽」。

## 第三轮修复（2026-10-16，用户报障：同时按 Enter + 左键）
7. **「外框已出现，但左键毫无反应、拖不动窗口」** —— 两条根因叠加：
   - **主因**：阈值期间被缓冲的按下，原设计要等**激活后的第一次鼠标移动**才由 `AdoptHeldButtonIfAny` 收养。按住触发键+左键后**先不动**就永远不收养。
     → 已修：新增 `Coordinator.AdoptBufferedButtonsNow(pt)`，在 `OnTriggerActivated` **激活瞬间**就收养；`ShowHoverFrame` 改为仅在未拖动时调用。
     取舍：只收养 `_bufferedDown`，**绝不动 `_passedDown`**（后者目标窗口实收了按下，必须靠用户移动表明意图）；收养前查 `IsButtonPhysicallyDown`（同刻松手要清账）、查 `IsPointOverOwnInteractiveControl`（控件上要还给控件）。
   - **次因**：`GroupBox` 用 `BUTTON` 类 → 被白名单误判为可交互控件 → 设置页成片分组区的按下被放行给容器（容器不吃鼠标）。
     → 已修：`IsInteractiveClassName(string)` 改为 **`IsInteractiveControl(nint h)`**，类名匹配到 `Button` 后追加样式位判定 `(GetWindowLong(h, GWL_STYLE=-16) & 0xF) == 0x7 (BS_GROUPBOX)` 即判为非交互。
   - 顺带补 `HandleButtonDown` 里 `Begin` 返回 false 的诊断日志。

### ⚠️ 最重要的方法教训
**旧用例把缺陷本身写成了预期**：修前的 T86 断言 `Check(!Drag.IsDragging, "此刻还没有拖动会话（正是用户看到的状态）")` —— 缺陷一修，这条断言立刻变红。改断言时才意识到「它红着的时候恰好是修复生效的时候」。**移植/复跑时若见到这条断言必须倒转**。（同类历史坑：第一轮有 13 项断言旧缺陷行为。）

### 第三轮验证（自检 678 项）
- Debug 连跑 3 次：678/678、678/678、668/668（668 那次是 T39 因桌面有窗口压住测试目标而整段环境跳过）。
- 发布版单文件 exe 连跑 3 次：三次 **678/678**。
- 第二轮交付 exe 在同一桌面连跑 6 次：6×677/677（用于 A/B）。
- 发布体积 **639,655 字节**；补丁 **49 个 hunk / +766 −104**（`git apply` 后逐字节一致）；源码 **24,213 行 / 684,039 字节 / 纯 CRLF**。

### 顺带修掉两条「环境敏感」假红用例（不改产品语义）
- **T69「40% 外框必须明显比 100% 淡」**：旧判定 `num3 < num2*0.6` 等价于假设「背景远比外框暗」。外框半透明 → 读数 = `alpha×外框 + (1-alpha)×背景`。本机壁纸是深青（实测 `RGB(4,70,91)` → 青色度 23），三次观测 29/36/46 与 `0.4×60+0.6×bg`（bg=8/23/37）逐项吻合 → 产品没问题，是阈值写错。改为**先量背景 bg**，再断言 `num3 <= int(0.4*num2+0.6*bg)+4` 且 `num2-num3 >= (num2-bg)/2`。
- **T80「外部全屏窗口真实链路」**：`SetWindowPos(..., 20u=SWP_NOACTIVATE|SWP_NOZORDER)` 不置顶 → 桌面中心的 Edge 窗口盖住测试窗口，「指针下是哪窗口」返回 Edge（`FindTarget` 行为本来就对）。改为 `HWND_TOPMOST(-1) + SWP_SHOWWINDOW(0x40)`，并加前置校验：命中≠测试窗口就**明确跳过**而非误判产品。
- 支撑工具：`WindowFromPoint` 用 Python ctypes 查（PowerShell `Add-Type` 被安全策略禁用）；屏幕取样用 GDI32 `BitBlt` + `GetDIBits`（`greenness = G - (R+B)/2`）。
- 仍有偶发环境敏感：T63「最大化窗口右键降级」与 wgesture 抢占回归（真实输入 + 真实最大化窗口）。A/B 证明**两版共有**，非第三轮引入。

- 交付目录：`D:\Workbuddy\2026-10-16-12-48-00\`（exe / patch / source.txt / 报告 `HoldDrag-三问题根因与修复实施.md`（第十章＝第三轮）/ `evidence\`（`5-*` 为第三轮证据））。
- ⚠️ 交付目录里的旧 exe 若仍在运行会锁住文件、无法覆盖；本轮已关闭遗留实例（PID 40328，14:41 由上一轮验证时启动，且一直占着全局钩子）。

## 真实输入链路端到端验收（2026-10-16 第四轮，**补上了此前一直缺的一环**）
- **教训**：此前所有自检都是**进程内直调** `Coordinator.OnMouse()` / `Trigger.SimulateKeyDown()`，
  **绕过 `HookEngine` 与真实低级钩子链路** → 只能证明「逻辑写对了」，不能证明「真实输入下生效」。
  用户因此质疑「没有真正的修复」，质疑合理。**任何输入类改动都必须补真实验收。**
- 工具：`…\HoldDrag\work\inpute2e\InputE2E.cs`（+ 交付目录 `evidence\6-InputE2E.cs.txt`）。
  用 `SendInput` 注入真实输入（`dwExtraInfo=0`），目标是验收进程自己的窗体（对 HoldDrag 即外部窗口），
  每步注入 `MOUSEEVENTF_MOVE` 并逐帧记录窗口位移。
  用法：`InputE2E.exe <HoldDrag.exe> <simult|sequ|leftfirst|rightfirst|nomove|tap|taphold|fastmove|all|order|user>`；
  exe 传 `-` = 直连"已经在跑"的实例（不另起进程）。
  ⚠️ 本程序有**单实例互斥**（`Mutex Local\HoldDrag.SingleInstance.v1`，只对 `--selftest`/`--windowhost` 等分支放行）：
  桌面旧实例还活着时，新起的进程会**启动即退出**（日志「已有实例在运行，通知它把设置窗口显示出来」），
  且旧实例排在钩子链更前面会吞掉 Enter 按下 → 量到的全是旧实例的行为。**真实链路验收前必须先让旧实例退出。**
- ⚠️ **工具自身最大的坑**：**`SetCursorPos` 不产生鼠标输入事件**，低级钩子看不到 → 第一版读数全是 `+0,+0`（假阴性）。
  移动光标必须用 `SendInput(MOUSEEVENTF_MOVE)`。另：注入抬起时不要带新坐标，否则"抬起本身"会把窗口带到位、污染判定。
- **实测结论（期望位移 +150,+60）**：
  | 场景 | 老版 | 修复版 / 交付版 exe |
  |---|---|---|
  | `simult`/`rightfirst`（**触发键先落**，左键落在 HoldMs 阈值窗口内） | **+125,+50**（`move1=+0,+0`，第一步被吞、整段慢一步） | **+150,+60 ✔ 1:1 跟手** |
  | `sequ`（先过阈值再按左键） | +150,+60 | +150,+60 ✔ |
  | `leftfirst`（左键早于触发键 25ms） | +125,+50 | +125,+50（**固有边界**） |
  | `nomove`（按下后完全不动） | 不动 | 不动 |
- **`leftfirst` 滞后一步原理上不可消除**：左键按下时触发键还没按下，HoldDrag 无从判断，该按下已真实投递给下层窗口；
  补起时若把起点回溯到按下点会让窗口跳变，设计上选择"不跳变"→ 丢掉起段位移。与「先选文字再按触发键」的语义保护同一取舍。
  **建议用户：先把触发键落稳再按左键。**
- 老版缺陷的精确表述（可用于对照）：① 按下后先不动 → 外框亮着但完全没有会话；② 一开始移动 → 第一次移动被吞，整段比手慢一步。
- **部署**：修复版已写入 **`C:\Users\64381\Desktop\HoldDrag.exe`**（639,655 B，与交付目录 exe 逐字节一致）。

## 第六轮（2026-10-16 晚）：真凶 = `GetAsyncKeyState` 漏报；判据改为「钩子事件账本」
- 报障：**「按 Enter → 很快按左键 → 立刻移动」窗口拖不动（外框正常亮）**；「按住左键等一会儿再移动」正常。
  唯一差别 = 触发键到 200ms 阈值之前，鼠标有没有已经在动。
- 复现：`InputE2E` 新增 `fastmove`（Enter → 20ms → 左键 → 不等直接逐帧移动）= **+0,+0**；`taphold`/`simult`/`sequ` = +150,+60。
- **真凶**：`AdoptBufferedButtonsNow` / `AdoptHeldButtonIfAny` 里的 `IsButtonPhysicallyDown()`
  （= `GetAsyncKeyState(vk) & 0x8000`）。现场日志 `缓冲[True,…] 物理按下[False,False,False]`：
  左键按住 600ms、缓冲账里确有那条按下，系统查询却说"没按着" ⇒
  `if (!IsButtonPhysicallyDown(i)) { _bufferedDown[i]=false; continue; }` 把好账当坏账丢掉，
  之后每次移动又被同一查询挡回 → 拖动永远建不起来。
  **⚠️ 判定"键是否还按着"只能用钩子事件账本（收到按下 + 未收到配对抬起），永不用该 API。**
- 修复：两处判据改成只看三本账；`FlushBufferedClicks` 的 `keepDown` 改按账本（恒 true，补"按下"不补"抬起"）；
  `OnTriggerDeactivated` 若仍在拖动立刻 `_drag.End()`（抬起丢失兜底）。
- 诊断长期保留：`[诊断] 鼠标键按下/抬起`、`[诊断] 激活瞬间清点缓冲`、`FindTarget 没找到目标` 改无条件记录。
- 验证：自检 **Debug 694/694、发布版 694/694**（新增 T88 两组断言）；真实链路 `fastmove` **+0,+0 → +200,+80** 逐帧 +25,+10 跟手。
  发布体积 **643,751 B**（sha256 `06988f8cb1ca5751…`）；补丁 **50 块 / 65,569 B**；源码 **695,409 B / 24,370 行 / 纯 CRLF**。
- ⚠️ `InputE2E` 的抬起注入**不能带 `MOUSEEVENTF_MOVE`**，否则光标被拽回、窗口位移读数被自己污染。
- ⚠️ 被测 exe 开 `--verbose` 会拖慢钩子回调（激活从 200ms 漂到 ~420ms），量位移时不要开。
- 交付目录新增 `evidence\7-*`（8 份），报告新增**第十二章**。
- **待用户确认**：`tap`（点一下即松开）在"按住才拖"语义下不会拖动；若用户习惯其实是"点一下抓住"，
  需另加"点一下抓 / 再点一下放"的粘滞模式。

## 交付/排查时的操作注意
- 交付目录 exe 若正在运行会**锁住文件无法覆盖**；先用 `Get-Process` 找 `HoldDrag*` 再关闭。
- 日志：每个进程写 `holddrag.log`；**若 `holddrag.log` 已被别的实例占用，会退化成 `holddrag.<pid>.log`** ——
  可用这个差异判断"当时有几个实例在跑"。`holddrag.log` 里也混有 `--selftest` 的 verbose 输出。
- 判断某次运行是哪个构建：看日志里有没有该轮新增的诊断串（如第三轮的 `激活瞬间补起拖动`）。
- 本机 `PowerShell Add-Type` 被安全策略禁用 → 查窗口/取像素改用 **Python ctypes**（`user32`/`gdi32`）。
- 本机 `schtasks.exe` 被安全策略列入黑名单 → HoldDrag 的"计划任务提权"路径必然失败（日志会打「查询计划任务未成功」），属环境限制。

- 简体中文输出；要求根因定位（含行号）+ 可直接落地的修复代码，不接受泛泛建议。
- 改动正式工程源码前需先获得明确指示（本次已获「直接做」授权，故改动落在**反编译重建源码**上，未触碰 `src\` 与线上 exe）。
