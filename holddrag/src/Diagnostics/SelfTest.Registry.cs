// HoldDrag · Diagnostics/SelfTest.Registry.cs
//
// 用例清单集中放在这里：加一条用例只改这个文件，不用在 Runner 里翻几百行。
// 顺序就是执行顺序（同 http:// 里的 HookCost 统计也按这个顺序累加）。

using System;

namespace HoldDrag;

internal static partial class SelfTest
{
	/// <summary>注册全部用例。名字里带 ★ 的是最不能回归的几条。</summary>
	private static void RegisterAllTests(string dir)
	{
		Safe("T01 默认配置", TestDefaults);
		Safe("T02 按键标识", TestKeyIds);
		Safe("T03 触发键匹配", TestKeyMatching);
		Safe("T04 九宫格判定", TestZones9);
		Safe("T05 最近角判定", TestNearestCorner);
		Safe("T07 九宫格矩形换算", TestSnapRects);
		Safe("T08 九宫格→屏幕区域", TestZoneMapping);
		Safe("T09 配置越界归一", TestNormalize);
		Safe("T10 配置往返", () =>
		{
			TestConfigRoundTrip(dir);
		});
		Safe("T11 配置备份恢复", () =>
		{
			TestConfigRecovery(dir);
		});
		Safe("T12 长按状态机（超阈值）", TestTriggerHold);
		Safe("T13 长按状态机（短按补发）", TestTriggerShortPress);
		Safe("T14 关闭长按阈值", TestTriggerNoHold);
		Safe("T15 全局钩子装卸", TestHooks);
		Safe("T16 窗口排除表", TestExclusions);
		Safe("T17 exe 内嵌图标", TestIconEmbedded);
		Safe("T18 端到端拖动（移动）", TestDragMoveE2E);
		Safe("T19 端到端拖动（缩放）", TestDragResizeE2E);
		Safe("T20 拖动期间绝不吞鼠标移动（光标冻结回归）", TestDragNeverSwallowsMouseMove);
		Safe("T21 未激活时鼠标键不触发", TestNoDragWhenInactive);
		Safe("T22 设置窗口所有分组都已挂到窗体上（设置空白回归）", TestSettingsLayout);
		Safe("T23 触发键激活态稳定保持（看门狗误判回归）", TestTriggerStaysActive);
		Safe("T24 拖动激活外框 显示/隐藏 与自选矩形一致", TestActivationFrameOverlay);
		Safe("T26 帧率节流算出的间隔合理，手动值不被吞", TestFrameInterval);
		Safe("T27 全屏窗口默认被跳过，开关打开后可拖", TestFullscreenOptIn);
		Safe("T28 钩子看门狗能判定「钩子已死」", TestHookWatchdog);
		Safe("T29 删除的功能已彻底下线（无残留配置项）", TestRemovedFeaturesGone);
		Safe("T30 外框外观（颜色 / 样式 / 粗细 / 圆角）可配置且越界会被拉回", TestFrameTheme);
		Safe("T31 外框在会话结束之后不会残留（显框前二次校验）", TestFrameNeverSticks);
		Safe("T32 拖动期间用区域环、结束后换成抗锯齿框（拖动残影回归）", TestDragRingFrameSwap);
		Safe("T33 按住触发键执行一次性动作后外框继续保留（外框消失回归）", TestFramePersistsAfterAction);
		Safe("T34 设置窗口可调整大小，且分组带左右锚定（设置尺寸回归）", TestSettingsResizable);
		Safe("T35 主题变更立刻反映到浮层像素上，不会等到下次移动", TestThemeAppliesImmediately);
		Safe("T36 在设置里选主题后，预览与保存结果都是选的那个（选主题失效回归）", TestSettingsThemeSelectionRoundTrip);
		Safe("T37 设置窗口拉宽后真的多装东西（1 列变 2 列、输入框跟着变宽）", TestSettingsResizeIsMeaningful);
		Safe("T38 设置窗口所有控件可见可点（美化改成自绘后功能失效回归）", TestSettingsControlsReachable);
		Safe("T39 外框亮着时左键/右键依然能接管窗口（浮层挡掉拖动回归）", TestHoverFrameDoesNotBlockDrag);
		Safe("T40 高DPI 下文字不被裁切（字号被放大两次回归）", TestNoTextClippedByHeight);
		Safe("T41 所有下拉框都有可见选中项（外框样式显示为空回归）", TestKeyCombosHaveSelection);
		Safe("T42 拖动写窗口必须异步提交（移动/变形不跟手回归）", TestMoveFlagsAreAsync);
		Safe("T43 真实鼠标事件能走完整钩子链路（触发后左键右键失效回归）", TestRealMouseThroughHook);
		Safe("T44 重启后配置必须完整保留（配置持久化回归）", TestConfigSurvivesRestart);
		Safe("T45 命中测试必须有兜底、Z 序方向必须正确（核心功能失效回归）", TestHitTestHasFallback);
		Safe("T46 物理坐标必须换算进进程坐标空间（DPI不感知导致核心功能失效回归）", TestCoordinateSpace);
		Safe("T47 黑名单能用进程名匹配 + 两个挑选入口都在（黑名单易用性回归）", TestBlacklistPickers);
		Safe("T48 全链路真实拖动：窗口必须真的跟着光标动（核心功能端到端）", TestRealDragMovesWindow);
		Safe("T50 真实长按触发键能激活（钩子线程化后 WinForms Timer 失效回归）", TestRealKeyboardHoldActivates);
		Safe("T52 视觉反馈组几何：窄/宽各档下样式预览框必须留在分组内且不被遮", TestVisualGroupGeometry);
		Safe("T53 晃窗手势：真晃才触发、匀速/小幅抖动/拖太慢都不触发（#39）", TestShakeGesture);
		Safe("T54 松开触发键后外框立即消失，pin 机制已下线（外框残留）", TestFrameGoesAwayOnRelease);
		Safe("T55 外框调色板/样式已扩充且每一种都画得出来（#37）", TestFramePaletteExpanded);
		Safe("T56 设置页默认尺寸够用就好、不会被内容撑到满屏（#33）", TestSettingsDefaultSizeCompact);
		Safe("T57 拖动过程的视觉反馈必须真的画在屏幕上并持续跟随（拖拽反馈失效）", TestDragVisualFeedback);
		Safe("T58 视觉反馈：进入即见全部选项、选中态清晰、全宽下不消失", TestFramePickersAlwaysVisible);
		Safe("T59 整体视觉（Win11）：层次/动效到位、无黑块、强调色与外框色都画出来了", TestVisualPolish);
		Safe("T60 最小窗口下任何分组的控件都不得越出分组框（全屏选项被遮挡）", TestNoControlClippedAtMinSize);
		Safe("T61 拖边框改尺寸：布局幂等、不抖动、不溢出、列数不来回翻", TestResizeIsStable);
		Safe("T62 设置页按显示器真实 DPI 渲染，且 DPI 变化时能整体重建", TestDpiRendering);
		Safe("T63 触发后左键/右键在普通·最大化·全屏窗口上都必须生效", TestTriggerOnBigWindows);
		Safe("T64 滚轮扫过设置页不得改掉任何选项（右键被悄悄改成最小化的根因）", TestWheelDoesNotChangeSettings);
		Safe("T65 图标字体可用且每个字形都真的渲染出墨水（缺字形是空白，肉眼难发现）", TestIconGlyphs);
		Safe("T66 触发时鼠标键已按住：必须补起拖动（外框亮着但窗口不动）", TestAdoptHeldButton);
		Safe("T67 拖动全程外框必须紧跟窗口：绝不出现滞后（错位/掉帧）", TestFrameTracksWindow);
		Safe("T68 贴边/出屏拖动时外框几何必须恒定（不许缩小、不许跳变）", TestFrameGeometryStableAtEdge);
		Safe("T69 外框不透明度：改数值必须真的改变渲染结果", TestFrameOpacity);
		Safe("T70 帮助面板：点标题能展开、内容不越界", TestHelpPanel);
		Safe("T71 关于信息：开发者/版本/更新时间/开发工具必须真的显示在界面上", TestAboutInfo);
		Safe("T72 每个分组都必须在可视树里、侧栏条目齐全（「新增了却看不见」回归）", TestAllGroupsReachable);
		Safe("T73 分组内任意两个控件不得互相遮挡", TestNoControlOverlap);
		Safe("T74 滚动页面时侧栏高亮必须跟随（程序化滚动）", TestSidebarFollowsScroll);
		Safe("T75 用真实滚轮消息滚动时，侧栏高亮必须跟随（事件接线）", TestSidebarFollowsRealWheel);
		Safe("T76 全屏窗口触发后降级为窗口：外框必须重新出现在新窗口周围", TestFrameAfterFullscreenDowngrade);
		Safe("T77 调色板必须整体柔和（饱和/明度都在舒适区，不刺眼）", TestPaletteIsSoft);
		Safe("T78 每个外框样式都要有名字、都真的画得出来", TestAllStylesRender);
		Safe("T79 外框绝不残留：兜底自检 + 退出路径都必须收干净", TestFrameNeverResidues);
		Safe("T80 全屏转窗口：用【外部进程】的真实全屏窗口走真实链路", TestFullscreenExternalWindow);
		Safe("T81 窗口操作不得干扰页面内容：取消拖拽模态 + 鼠标接管必须成对", TestNoContentInterference);
		Safe("T82 选中文本后按 Enter：先取消选中再补起拖动，左键恢复移动窗口", TestPhantomDownAdoption);
		Safe("T83 补发的配对抬起：wParam 如实报告其余按键、消息类型对应、lParam 为客户区坐标", TestPairedUpMaskReport);
		Safe("T84 激活时抢占钩子链头：重装后真实事件链路仍然可用（wgesture 抢占右键回归）", TestChainPriorityReinstall);
		Safe("T85 钩子线程路径无同步跨进程写：阻塞目标上 Begin/移动/松手/降级/置顶必须立即返回", TestHookThreadNeverBlocks);
		Safe("T86 自有窗口内控件不被抢输入：滑块上放行、标题栏上仍可拖", TestOwnInteractiveControlPassthrough);
		Safe("T87 分组框不是可交互控件：按住触发键时设置页的成片分组区仍可整体拖走", TestGroupBoxNotInteractive);
		Safe("T88 阈值内按下 + 激活前已在移动：激活后仍必须补起拖动（用户『外框出现却拖不动』复现）", TestAdoptAfterEarlyMove);
		Safe("T89 关于页 GitHub 链接 + 自动更新开关 + 检查更新按钮（v1.1 更新功能）", TestUpdateFeatures);
		Safe("T90 更新重启的单实例竞态：新版本必须等旧进程释放互斥体再接管", TestUpdateRestartSingleInstance);
		Safe("T91 单实例：同一时间只能开一个（重复启动必须自己退出并唤起已有窗口）", TestSingleInstanceOnlyOne);
		Safe("T92 外框颜色：跟随系统强调色 / 自定义颜色都要真的生效且存得进配置", TestFrameColorAccentAndCustom);
		Safe("T93 设置页跟随系统深浅色：深浅两套都要够清楚（对比度/层次）", TestSettingsThemeFollowsSystem);
		Safe("T94 设置窗口是无边框圆角卡片窗：无系统标题栏、标题条与页面同色、三个窗口按钮在位", TestFramelessCardWindow);
		Safe("T96 设置页重排性能：换宽度一次 < 60ms、同宽度重复调用被去重", TestSettingsRelayoutPerformance);
		Safe("T97 侧栏高亮只跟着当前页（点过就留胶囊的回归）", TestNavHighlightFollowsSelection);
		Safe("T95 圆角参数必须真的改变渲染结果（圆角设置失效回归）", TestFrameRadiusTakesEffect);
		Safe("T98 录制守卫与配置不变量：\"K:0x00\" 必被 Normalize 兜底、鼠标侧键/Shift 不被误伤、QuoteArg 按需包引号", TestRecordedKeyAndConfigInvariants);
		Safe("T99 保存失败不再静默：Save/Apply 返回 false 并带回错误信息（设置页据此红字早退）", TestConfigSaveFailureIsVisible);
		Safe("T100 GitHub 令牌：整理/鉴权头/失效与限额判定/配置往返/无令牌只提示不下载", TestGitHubTokenHandling);
		Safe("T101 点按切换模式：点一下抓住、再点一下放下，出厂默认仍是长按", TestToggleMode);
		Safe("T102 合成按键欠账：注入过的「只按下」必须在抬起丢失时被补发，绝不留下系统级卡键", TestInjectedDownLedger);
		Safe("T103 抢占链头的就地重装必须保持静默（否则激活瞬间被复位，核心拖动失效）", TestChainPriorityReinstallIsSilent);
		Safe("T104 外框换颜色：点色板 / 下拉框弹层都必须真的换掉并同步预览", TestFrameColorSwitch);
		Safe("T105 边缘吸附：靠近工作区边缘时贴齐并给出引导线，远离时不动", TestSnapEngine);
		Safe("T106 键盘分屏：半屏 / 九宫格 / 居中 的矩形换算", TestTiling);
		Safe("T107 撤销：Ctrl+Z 能还原上一次拖动，且只能用一次", TestUndo);
		Safe("T108 诊断包：生成的 zip 里包含 system.txt 与版本/显示器信息", TestDiagnosticsBundle);
		Safe("T109 新配置项默认值与越界归一（HUD / 吸附 / 分屏 / 撤销 / 引导）", TestNewConfigDefaults);
		Safe("T110 尺寸 HUD 与吸附引导线：分层窗口能创建、显示、隐藏", TestHudOverlay);
		Safe("T111 按程序规则：解析 / 匹配 / 非法条目丢弃", TestAppRules);
		Safe("T112 工作区布局：保存 / 读取 / 恢复 / 删除", TestLayoutStore);
		Safe("T113 脚本接口：命名管道一条命令一来一回", TestIpcRoundTrip);
		Safe("T114 设置搜索：关键字命中与状态提示", TestSettingsSearch);
		Safe("T115 便携模式：数据目录判定", TestPortableMode);
		Safe("T116 规则与布局的界面行真的存在", TestRulesAndLayoutUi);
		Safe("T117 引导线位置：必须画在吸附位置（之前在屏幕左上角）", TestGuideLineGeometry);
		Safe("T118 吸附：窗口比工作区大时不做右侧/底部吸附", TestSnapWideWindow);
		Safe("T119 规则通配符：* 与 ? 都要能匹配", TestRuleWildcards);
		Safe("T120 设置搜索：输入即定位（不用按回车）", TestSearchLive);
		Safe("T121 触发提示浮层：能显示、能隐藏、内容覆盖主操作", TestTriggerHint);
		Safe("T122 Shift 临时关吸附：状态读取与默认值", TestShiftBypassSnap);
		Safe("T123 外部语言包：加载 / 匹配 / 缺词条回退 / 不许覆盖内置语言", TestLanguagePacks);
		Safe("T124 语言包模板：能导出全部词条", TestLanguageTemplate);
	}
}
