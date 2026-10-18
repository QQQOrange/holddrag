# 2026-10-15

## QuickSearch-Win：百度 OCR 在本机慢、在别的电脑快的根因定位

结论：与 API、密钥、带宽无关。根因是本机 IPv6「半配置」——
- 网卡有全局 IPv6 地址 `2408:8256:dc85:991::/64`（联通下发）+ IPv6 默认路由 `::/0 → fe80::5`
- 但对外 IPv6 是黑洞：`ping -6 2408:8756:c52:1a85:0:ff:b040:90d1` 100% 丢包，TCP 直连挂满 20s 超时
- DNS 正确返回 AAAA（`aip.baidubce.com` → `2408:8756:c52:1a85:0:ff:b040:90d1` + `163.177.17.163`）
- 前缀策略 `::/0` 优先级 40 > `::ffff:0:0/96` 的 35 → getaddrinfo 把 IPv6 排在前面

叠加 `src/ocr.rs` 的实现放大：
- `agent()` 用 ureq 2.12.1，`.timeout_connect(6s)`；ureq `stream.rs::connect_host` 在 `multiple_addrs` 时把
  总超时 `div(2)`，故第一个地址（IPv6）独占 3s 才失败回退 IPv4
- `agent()` 每次请求都新建 Agent（调用点：`access_token` 289 行、`call_endpoint` 437 行）→ 连接池失效，
  每个 HTTP 请求都要重付这 3s + 重做 TLS 握手
- `ocr.rs` 完全不读代理，系统代理 `ProxyEnable=0`，代理因素已排除

实测：复现 ureq 算法得 IPv6 失败 3.012s + IPv4 成功 0.014s；纯 IPv4 对照 0.021s。
即每次 OCR 请求白等约 3.0s。别的电脑没这问题，是因为其 IPv6 要么正常、要么直接不可达（无全局地址时
connect 立即返回 WERUNREACH），两种情况都不会等满 3s。

修复方向：
- 系统层（最快）：`netsh interface ipv6 set prefixpolicy ::ffff:0:0/96 60 4`（原 35 4，需管理员），
  让 IPv4 排在前面；回滚即改回 35。根因仍是路由器 IPv6 上游不通。
- 应用层（根治）：给 ureq 传 IPv4 优先的 `.resolver()`，并把 `timeout_connect` 从 6s 降到 2s；
  复用同一个 Agent 让连接池生效。

产出：诊断脚本 `D:/Workbuddy/2026-10-15-22-17-44/ocr-net-diag.py`（复现 ureq 连接顺序与耗时，可复跑验证）。

## 已落地的代码改动（用户选择「只改代码」）

文件：`C:/Users/64381/WorkBuddy/2026-10-10-16-56-30/QuickSearch-Win/src/ocr.rs`
1. import 增加 `use std::net::{SocketAddr, ToSocketAddrs};`
2. 新增 `fn ipv4_first(netloc: &str) -> std::io::Result<Vec<SocketAddr>>`，
   用 `sort_by_key(|a| a.is_ipv6())` 把 IPv4 排到 IPv6 前面（稳定排序，纯 IPv6 主机行为不变）。
3. `agent()` 里加 `.resolver(ipv4_first)`。
4. 新增单测 `ocr::tests::ipv4_is_preferred_over_ipv6`。

**刻意没做的两件事**（重要，别被后人「顺手优化」掉）：
- `timeout_connect` 保持 6s 不变。原打算降到 2s，实测推理后放弃：ureq 有两个地址时把总超时
  一分为二（每个 3s），而 Windows SYN 首包超时重传间隔就约 3s；预算压到 1.5s 会让「首包丢了靠
  重传才连上」的弱网直接失败。
- `agent()` 故意每次新建 Agent，不复用连接池。ureq 只对「幂等方法 + 空 body」重试
  （unit.rs::is_retryable），而识别接口是带 body 的 POST —— 复用到服务器已关的空闲连接时
  错误会直接抛给用户。实测握手仅约 60ms，不值得冒这个险。

验证：`cargo check` 通过（5.34s，无新增警告）；`cargo test --bin QuickSearch ocr::` 12 passed / 0 failed。
（未跑全量测试：其余 105 个测试涉及窗口/剪贴板等，会弹真实 UI，不适合在本机非预期时机触发。）

**遗留同类问题**：`src/webdav.rs:467` 也是 `ureq::AgentBuilder::new()` 且未配 resolver，
WebDAV 备份的每个请求同样要白等 3s IPv6 超时。已告知用户，等其确认后再改。

## 补充证据：拓扑与 IPv6 断点（用户补充"慢的是有线，快的是同路由器的 WiFi"）

用户补充关键拓扑：慢的这台是**网线**接路由器，快的那台是**同一路由器的 WiFi**。据此补测：

- 本机 IPv6 配置其实是"完整"的：`netsh interface ipv6 show addresses` 显示以太网 2 有 3 个全局地址
  （1 公用 + 2 临时，前缀 `2408:8256:dc85:991::/64`），且有 `::/0 → fe80::5`（metric 256）默认路由。
  本机 WLAN 适配器为 disconnected，只有 fe80:: 链路本地地址。
- **整条 IPv6 线路不通，不是只有百度**：`2400:3200::1`（阿里）、`240c::6666`（CNGI）、
  百度 AAAA `2408:8756:c52:...` 三个不同网络的目标全部超时。
- **`tracert -6 2400:3200::1` 定位到断点**：
  第 1 跳 `2408:8256:dc85:991:1620:4ff:fe7f:5be8`（路由器自身）1ms 通 →
  第 2 跳 `2408:8256:dc85:58cf:56b2:9dff:fefd:7a08`（联通接入设备）1ms 通 →
  第 3 跳起 `* * *` 无响应。
  → 本地段（PC → 路由器 → 运营商接入）完好，断点在本地网络之外。

推论（用于回答用户）：两台电脑连同一路由器 = 共享同一条 IPv6 上行；上行既坏，"另一台快"只能说明
**那台电脑没有在用的全局 IPv6 地址**（没有 → connect 立即返回网络不可达 → 0 等待）。
有线/无线、信号强弱、带宽大小都与那 3s 无关（它是等待超时，不是传输耗时）。
给用户的 10 秒验证法：在那台电脑上 `ipconfig` 看有没有 `2408:`/`240e:` 开头的全局 IPv6，或
`ping -6 aip.baidubce.com` 看是"立刻不可达"还是"卡几秒超时"。
根治方向：在路由器上关闭 IPv6 下发（两台电脑同时恢复正常），或联系联通修 IPv6 上行。
