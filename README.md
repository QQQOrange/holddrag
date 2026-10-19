# holddrag — 长按拖窗 HoldDrag

[![Platform](https://img.shields.io/badge/platform-Windows%207%2B-0078D6)](#)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](#)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue)](#许可证)

**长按一个自定义按键，随手拖动 / 缩放 / 操作光标下的任意窗口。**

按 `Enter`（可改成任意键或鼠标侧键）不要松手，光标底下那扇窗四周就会亮起一圈外框；
这时按住鼠标左键就能把它拖走 —— **不用去找标题栏，全屏窗口也能拖**。

> 上游来源：本仓库是 [stefansundin/altdrag](https://github.com/stefansundin/altdrag)（AltDrag）的 fork。
> 上游的 C 源码按原样保留在仓库根目录；本产品是**独立的 C# / .NET 10 实现**，位于 [`holddrag/`](holddrag/)。

---

## 目录结构

| 路径 | 内容 |
| --- | --- |
| [`holddrag/src/`](holddrag/src/) | **产品源码**（`HoldDrag.cs` / `HoldDrag.csproj` / 图标 / 清单 / 程序集信息） |
| [`holddrag/dist/`](holddrag/dist/) | **交付产物**：框架依赖单文件 `HoldDrag.exe`（+ pdb） |
| [`holddrag/docs/PRD.md`](holddrag/docs/PRD.md) | **产品需求文档**：产品目标 / 页面结构 / 功能边界 / 验收标准（唯一基准） |
| [`holddrag/docs/修复实施报告.md`](holddrag/docs/修复实施报告.md) | 三轮缺陷的根因、坐标（行号）、修复方案与验证结论 |
| [`holddrag/docs/patches/`](holddrag/docs/patches/) | 可 `git apply` 的历史修复补丁 |
| [`holddrag/evidence/`](holddrag/evidence/) | 自检报告、真实输入链路取证、截图与原始日志 |
| [`holddrag/probes/`](holddrag/probes/) | 探针工程（样式墨量、浮层渲染、表单几何、真实输入 E2E 工具） |
| [`holddrag/reference/`](holddrag/reference/) | 反编译基准源码 + 修复前的基线 exe（可做 A/B） |
| [`holddrag/legacy/`](holddrag/legacy/) | 历史多文件工程快照、各阶段构建产物、设计截图 |
| [`holddrag/notes/`](holddrag/notes/) | 开发期长期笔记与会话记录（踩坑、约定、决策） |
| `altdrag.c` / `hooks.c` / `build.bat` … | 上游 AltDrag 的原始文件，未改动 |

---

## 快速开始

1. 到 **[Releases](https://github.com/QQQOrange/holddrag/releases/latest)** 下载最新的 `HoldDrag.exe`，双击运行。
   **会弹一次 UAC 提权确认**（原因见下），点「是」。
2. 托盘区出现图标，首次运行会有气泡提示。
3. **长按 `Enter` 约 0.6 秒不要松手** —— 光标底下的窗口立刻亮起一圈圆角外框，说明它已被接管。
   接着按住左键拖它即可。
4. **双击托盘图标**打开设置窗口。

> 换触发键：设置窗口里点「重新录制」→ 直接按下你想用的键。
> 左 / 右 / 中键已被鼠标动作占用，不能当触发键；鼠标侧键（前进 / 后退）可以。

> **需要 .NET 10 桌面运行时**（框架依赖单文件发布）。没有的话装一个
> [Windows Desktop Runtime 10](https://dotnet.microsoft.com/download/dotnet/10.0) 即可。

---

## 单实例（v1.2 冻结为需求）

**同一时间只允许存在一个 HoldDrag 进程，不能开出好几个。**

- 第二次启动**不会**再驻留成第二个进程，也不会"没反应"：它会把**已在运行的那个实例的设置窗口弹出来**，然后自己退出（退出码 0）。
- 实现：互斥体 `Local\HoldDrag.SingleInstance.v1` + 命名事件 `Local\HoldDrag.ShowSettings.v1`。
- 这两个名字**是契约，不能改**：一改，新旧版本就互认不出来，直接退化成"能同时开出两个"。
- 例外（有意为之）：`--selftest` / `--stress` / `--dragbench` / `--probe` / `--windowhost` 这些**诊断分支**会绕过互斥，否则在应用运行时没法做检测。
- 更新接续是特例：就地替换时新版本先启动、旧进程后退出，带 `--updated-from` 的实例必须**等**旧进程释放互斥体再接管，而不是让位（否则更新完一个进程都不剩）。
- 回归保护：自检 **T91** 会**真实启动第二个 exe**，断言它自己退出并唤起已有实例（该用例已做反向验证：禁用让位分支后必然报 2 项失败）。

## 自动更新（v1.1 新增）

「关于」页里有 **GitHub 项目主页**链接、**自动更新**开关和**检查更新**按钮：

- **自动更新默认开启**：每次启动后台查一次最新发布，**只在真有新版本时**用托盘气泡提示，不打断你正在做的事。
- 点「检查更新」会立刻检查并在按钮右侧实时回显状态（检查中 / 已是最新 / 发现新版 / 下载进度）。
- 发现新版本后确认即**在设置页里直接下载**，下载完成自动替换旧文件并重启。
- 同一个新版本**只提示一次**，不会每次开机都弹。
- 网络不通时如实报出具体原因并给出发布页链接，**绝不**谎报"已是最新"。
- 更换机制不用批处理脚本：Windows 允许重命名正在运行的 exe，因此按
  「旧 exe → `.old`，新文件 → 正式名，新版启动后删 `.old`」三步完成；
  任一步失败都会把旧文件改回来，**保证始终有一份能跑的 exe**。

托盘右键菜单里也有「检查更新…」和「项目主页（GitHub）」两个入口。

### 默认绑定（对齐 AltSnap 出厂绑定，只是把修饰键换成了长按 Enter）

| 操作 | 行为 |
| --- | --- |
| 长按 `Enter` 0.6 秒 → **左键拖动** | 移动窗口 |
| 长按 `Enter` 0.6 秒 → **右键拖动** | 调整大小（九宫格就近角 / 边） |
| 长按 `Enter` 0.6 秒 → **中键** | 最大化 / 还原 |
| 长按 `Enter` 0.6 秒 → **左键双击** | 最大化 / 还原 |
| 长按 `Enter` 0.6 秒 → **右键双击** | 按九宫格吸附到屏幕对应位置 |
| 长按到阈值、还没按鼠标键 | 光标下那扇窗先亮起外框，告诉你"接管的是它" |

---

## 为什么需要管理员权限

- 全局低级键鼠钩子只有在**同级或更高权限**下，才能看到"以管理员身份运行的窗口"上的输入。
  不提权时，你在任务管理器上按的键它根本收不到。
- 要移动 / 缩放提权窗口，本进程也必须是管理员。

所以默认开启，但设置里可以关掉。为了不每次开机都弹 UAC，开机自启默认走
**计划任务（最高权限 + 登录时静默启动）**，而不是注册表 `Run` 项。

**UAC 上点了「否」不会启动失败** —— 程序降级成普通权限继续跑，功能照常，
只是拖不动任务管理器这类提权窗口，随后弹一次"是否以管理员重启"，取消后不再打扰。

临时不提权运行：

```
HoldDrag.exe --no-elevate
```

---

## 触发键的两个关键行为

本程序的触发键**不是普通修饰键**，而是「长按触发」，这样才能用 `Enter` 这种日常高频按键：

1. **短按 = 原样透传。** 按下瞬间程序吞掉这次按键并开始计时；在阈值内松手，就用 `SendInput`
   合成一次完整的「按下 + 抬起」补发出去。正常打字、回车换行、回车提交都不受影响。
2. **超过阈值 = 进入激活态。** 此时不再补发任何按键，鼠标左 / 右 / 中键触发对应动作；松开触发键即退出。

**必须知道的代价：** 要区分「长按」和「连发」，就必须吞掉键盘的自动重复事件，
因此**按住 Enter 不放连续换行会被压成一次**。这是原理上的取舍，无法两头兼顾。

---

## 命令行参数

| 参数 | 作用 |
| --- | --- |
| （无） | 正常启动，需要时自动提权重启 |
| `--no-elevate` | 不提权启动，便于排查问题 |
| `--selftest` | 跑全部自检（T01–T91），结果写入 `%APPDATA%\HoldDrag\selftest.txt`；全通过返回 0 |
| `--stress` | 泄漏压力测试：40 次预热 + 320 次完整拖动，采样 GDI / USER / 句柄 / 私有内存 |
| `--smoke` | 冒烟模式：正常启动，2 秒后开设置窗口，9 秒后自动退出 |
| `--trace` | 手感类问题的取证开关：逐帧把光标 / 期望矩形 / 实际矩形 / 是否贴边写入日志 |
| `--updated-from <path>` | 更新接续：新版启动后删除该路径指向的 `.old` 旧版本备份 |
| `--checkupdate` | 只做一次更新检查并把结果打到控制台与日志（排查更新链路用） |
| `--dragbench` / `--probe` / `--windowhost` / `--visualcheck` / `--previewtest` | 基准、渲染探针、外部窗口宿主、视觉核对、样式预览导出 |

---

## 文件位置

| 内容 | 路径 |
| --- | --- |
| 配置 | `%APPDATA%\HoldDrag\config.json` |
| 配置备份 | 同目录 `config.json.bak` / `config.json.bak2`（两级轮换，任一损坏自动回退） |
| 日志 | `%APPDATA%\HoldDrag\holddrag.log`（超过 512 KB 自动清空） |
| 自检报告 | `%APPDATA%\HoldDrag\selftest.txt` |

托盘菜单里有「打开配置文件夹」和「查看日志」两个快捷入口。

---

## 从源码构建

需要 **.NET SDK 10**（`dotnet --list-sdks` 应能看到 `10.0.x`）。

```powershell
# 编译
dotnet build holddrag/src/HoldDrag.csproj -c Release

# 发布单文件（与仓库内交付产物同形态）
dotnet publish holddrag/src/HoldDrag.csproj -c Release -o out `
  --self-contained false -p:PublishSingleFile=true

# 自检（必须先确认桌面上没有旧实例在跑）
.\out\HoldDrag.exe --selftest      # 退出码 0 = 全通过

# 压力测试
.\out\HoldDrag.exe --stress
```

> ⚠️ **`%TEMP%` 必须可写**。自检里大量用例会在临时目录建夹具窗口，
> 若临时目录被沙箱 / 权限限制，会出现成片的 "Access to the path ... is denied" 假红。

> ⚠️ **自检必须在"没有旧实例、且桌面未被遮挡"的环境下独占运行**。
> 并发跑两份实例会争抢全局光标与前台窗口，产生"期望 90、实际 -1500"这类离谱读数；
> 若日志目录被宿主沙箱禁止写入，`%APPDATA%` 日志用例会自动跳过并标注环境限制。

构建产物期望（v1.2.0 交付态）：

| 项 | 值 |
| --- | --- |
| 输出目录内容 | 仅 `HoldDrag.exe` + `HoldDrag.pdb` |
| exe 字节数 | 671,399 |
| exe SHA-256 | `67C9967188B69A036E63B780915108357DF70A3513DE12EB722BBE1D9C2A6C02` |
| 文件版本 | `1.2.0.0` |

---

## 已知限制

1. **Win11 的 Snap Layouts 布局面板不会弹出。** 要实现双击左键 / 右键就不能用系统的原生拖动循环，
   边缘吸附是程序自己实现的（AltSnap 同样如此）；吸附落点与原生一致。
2. **按住触发键不会连续换行**（见上文取舍）。
3. 用 `Alt` / `Ctrl` / `Win` 作触发键时，按住期间该键的组合功能不可用（如 Win 作触发键时 `Win+E` 无效）。
4. `CapsLock` 作触发键时不会切换大小写（被程序压制）。
5. 极少数自绘界面 / 全屏游戏的窗口可能无法移动或移动异常。
6. 提权窗口只有在本程序也提权时才能操作。
7. **落键次序边界**：若左键按下**早于**触发键 25 ms 以上，那一次按下已真实投递给下层窗口，
   补起时若把起点回溯到按下点会让窗口跳变，故选择"不跳变"→ 丢掉起段位移。
   **建议：先把触发键落稳，再按左键。**
8. `tap`（点一下触发键即松开）在"按住才拖"语义下不会拖动；
   若需要"点一下抓住 / 再点一下放下"，暂未实现。

完整的边界定义与验收标准见 [`holddrag/docs/PRD.md`](holddrag/docs/PRD.md)。

---

## 仓库的提交内容说明

- 仓库根目录的上游 C 源码（`altdrag.c`、`hooks.c`、`installer.nsi`、`localization/` …）**原样保留**，
  与 `upstream` 远端一致，便于对照与合并。
- 本产品源码集中在 [`holddrag/`](holddrag/)，与上游文件不交叉。
- `holddrag/dist/HoldDrag.exe` 与 `holddrag/reference/baseline/HoldDrag.exe` 是**有意提交的二进制**
  （交付产物与修复前基线），因此 `.gitignore` 中对这两个目录做了反忽略。
- 上游 `.gitignore` 里的 `bin` / `*.dll` / `*.exe` 规则依然生效，构建中间产物不会被提交。

---

## 许可证

本仓库沿用上游 fork 的 **GNU General Public License v3.0**，全文见根目录 [`LICENSE`](LICENSE)。
