# 2026-10-17 工作日志

## r71：参考 FlutterPicOrigin / PicImageSearch 全方面完善识图（已完成）

- 项目：`C:/Users/64381/WorkBuddy/2026-10-10-16-56-30/QuickSearch-Win`（Tauri 2，Rust + WebView2）
- 识图引擎 11 → 15 家：新增 trace.moe（Upload，本地结果页渲染 + Url 直链）、元宝、文心一言（AI Page）；AI_ENGINES 4→6
- 修 3 个真 bug：① SauceNAO 分支死代码（r70 起 img_upload 顶部无条件 JSON 解析使回 HTML 的 SauceNAO 永远走不到落盘分支）② 结果页固定文件名并行互相覆盖（新增 result_page_path：pid+自增序号，SauceNAO 自 r60 起就有同一隐患）③ trace.moe 空中文片名（chinese="" 不是字段缺失，优先级链要跳空串）
- fmt_ts 改截断（场景起点四舍五入会让用户错过开头半秒）
- glass 测试并行偶发：两个抓屏测试共享进程级 LAST_RECT，加 GLASS_TEST_LOCK 串行化
- 验证：cargo test 223/223（6 连跑）、r71-verify 15/15、自检 29/29、r71-tracemoe-e2e 10/10（Edge kiosk 放图 + 驱动 img_region 抓全屏真上传）
- 部署：桌面 exe MD5 C5564378FF24044D6B5F3DCB159C4BFA，备份 QuickSearch.exe.old-r70-20261017-194259

## 环境备忘（本项目反复用到）

- cargo 不在 PATH：`export PATH="/c/Users/64381/.cargo/bin:$PATH"`
- 沙箱代理：curl 访问 localhost/外网都要 `--noproxy '*'`
- Config 的 serde 用 Rust 字段名（snake_case）——探针写配置必须用 snake_case，camelCase 会被静默忽略（r70/r71 探针都踩过）
- CDP `Runtime.evaluate` returnByValue 直接给对象，不要再 JSON.parse（r70 探针踩过）
- Edge 路径：`C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe`（不在 PATH）
- **`QS_T_WV_ARGS` 是整体替换 WebView2 参数**：只写 `--remote-debugging-port` 会丢掉 `--disable-gpu-sandbox` → GPU 进程连崩、渲染进程失联，症状是"第一条 Runtime.evaluate 有回音、之后全超时"。探针里必须带全 flags。
- **探针调试端口要随机取空闲口**（写死会连到上一轮残留实例的死页面）；实例 tag 也要每轮唯一。

## r72：四件用户点名需求（已完成）

- 需求 A 引擎库四级目录：`renderAllList` 改成「先组 plan 再画目录+正文」，顶部四行吸顶目录（大类=筛子，中类/主题/小类=直达，小类 27 个全展开不折叠，行内横滚）。修两个真 bug：① 跳转偏移不能直接用目录自然位置（没吸顶时是 1147px，差 1095px），要用 `min(顶边相对距离,24)+高度`；② `behavior:'smooth'` 在不可见窗口被节流停在半路 → 改 `auto`。
- 需求 B 三家识图出册：必应/搜狗/360 用真实 Edge 复测全废（必应 302 到固定搜索页；搜狗只剩 #app 空壳；360 上传 302 回首页），两个参考项目也没有搜狗/360，必应真上传拿到 bcid 但 insightsToken 结果页丢会话。识图 15→12 家，`needs_file_pick` 只剩 AI 六家。
- 需求 C 横条识图升级一等成员：`@img` 成为 bar_list 哨兵，启动时 `ensure_img_bar_sentinel` 给老配置补一根；横条 `renderBar` 画进引擎区、固定位 #btnImg 让开；设置页那行有五键（↑↓/换图标/编辑/默认/移出）+ 拖拽；开关与哨兵双向同步。
- 需求 D OCR 凭据区：API Key 改密码框 + 眼睛切换；拉到的模型在模型行下方**陈列清单**（`.cred-models`，视觉徽标 + 点击回填 + 可收起），datalist 保留。
- 验证：cargo test 223/223、r72-lib-nav 20/20、r72-bar-img 15/15、r72-ocr-models 23/23、设置页自检 29/29、r71-verify 15/15。
- 部署：桌面 MD5 77DC34F8597397C4A98BCBAB42E69F45，备份 .old-r71-20261017-212414。

## r73：引擎库重做 + 识图剪贴板开关（已完成）

- 引擎库按用户要求整个重做：**删掉主题层**；默认只显示 4 个大类（不铺引擎）；选大类才出中类、选中类才出小类；「全部引擎」按 A→Z 分组（中文用 `Intl.Collator('zh')` + 23 个边界字推拼音首字母，170 个中文首字全命中），顶部 27 个字母索引一次铺开、点字母跳转；目录**不再吸顶**（用户嫌"浮在页面上"）。
- 踩坑：字母遍历一开始用拼音的 23 个首字母（无 I/U/V），漏掉 Instagram/Udemy 等 33 个英文名 → 改完整 26 字母 + #。
- 识图新增开关 `img_keep_clip`（默认关）：开 = 搜完不还原剪贴板（图可继续 Ctrl+V）；关 = 用完还原。
- 验证：cargo 223/223、r73-lib 17/17、r72-bar-img 15/15、r72-ocr 23/23、自检 29/29、r71-verify 15/15。
- 部署：桌面 MD5 505592B0AEAF923964A90BDC00068BB7，备份 .old-r72-20261017-214949。
- 收尾：冒烟点遍 11 种状态无控制台错误；删掉 `.eng-theme-h` 死样式；**大类胶囊故意不带数量**（页内自检/探针按 `textContent === '全部引擎'` 找按钮，挂数字会全断）。最终部署 MD5 **D5C3440A1AA00039024E4226DE6EB5C1**（21:56），备份 .old-r72b-20261017-215630。
- ⚠ 自检脚本在 ui/settings.js 里（6f 段），它依赖引擎库列表有行 —— 默认视图改空后必须先去点「全部引擎」。

## r74 完成（QuickSearch-Win，项目根 C:/Users/64381/WorkBuddy/2026-10-10-16-56-30/QuickSearch-Win）

四条需求全部落地并部署到桌面（MD5 2106E1C3…，回退副本 old-r73-20261017-233000）：

1. **小类目录汇总 + 跳转**（settings.js renderAllNav/renderAllDrill/renderAllList）：
   下钻目录三级，小类行始终给当前范围全部小类；点小类 chip = 滚到该节
   （data-jump 认领），过滤收进节标题 `.eng-cat-only`「只看」按钮（r72 直达
   按钮不丢）。目录与正文共用同一张 sections 顺序表（锚点 allCat-N）。
2. **首字母聚合索引（Win11 式）**（settings.js idx* 模块 + settings.css
   .idx-fab/.idx-pop*）：letters 模式右下角浮动钮 + 27 字母聚合面板（无引擎
   字母 .off 变灰占位）；点字母跳转且面板不关可连跳；Esc/外点/再点钮收起；
   滚动监听（document capture scroll + rAF 节流 idxSpyRun）同步点亮目录
   chip、面板字母、FAB 徽章；bindTabs 挂钩：切走引擎页 idxFabHide、切回且
   allGroupFilter==='*' 时 idxReshow（不重画列表，防弹回顶部）。
3. **更多引擎面板遮挡修复**（bar.js renderPanel + qs://bar-room）：引擎面板只
   按屏幕 78% 封顶，不再 capByRoom；放不下由 Rust layout_bar 包围盒钳制整体
   上移；收起自动回位（±0px）。收藏面板老策略未动。
4. **死代码清理**：删 renderEngineLib 家族（JS −383 / CSS −165 行）；扫描
   166 个 function 零调用=无、266 个 CSS 类孤儿=无真孤儿。

验证：cargo test 223/223；r73-lib-probe 24/24（新增 L18-L23 索引面板七项）；
r74-panel-shift-probe 12/12（新）；自检 29/29；r72-bar-img 15/15；r72-ocr
23/23；r71-verify 15/15。

工程经验：
- 探针在真实机器上跑必须防用户碰鼠标：SetCursorPos 后回读校验 + 重试循环
  （本次 P1 曾因鼠标被挪动读到 y=1207 而误报）。
- bar-probe 的 SNAP 表达式直接 returnByValue 对象，不能 JSON.parse。
- cargo 不在 PATH，用 /c/Users/64381/.cargo/bin/cargo.exe。
- 项目实际根目录在 C:/Users/64381/WorkBuddy/2026-10-10-16-56-30/QuickSearch-Win
  （不是 D:/Workbuddy 下的会话目录）。
