// HoldDrag · UI/SettingsForm.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HoldDrag;

internal sealed partial class SettingsForm
{
	// ======================= 窗体字段 =======================

	private readonly ConfigStore _store;
	private readonly Coordinator _coord;
	private AppConfig _edit;
	private bool _recording;
	private bool _updateBusy;
	private bool _populating;

	private SideNav _nav;
	private Panel _content;
	private Panel _pager;
	private readonly List<Page> _pages = new List<Page>();
	private int _activePage;

	private Panel _titleStrip;
	private CaptionButton _btnMin;
	private CaptionButton _btnMax;
	private CaptionButton _btnClose;
	private Label _lblPageTitle;
	private Label _lblPageSub;

	private Label _lblStatus;
	private FlatButton _btnDefaultsRef;
	private FlatButton _btnSaveRef;
	private FlatButton _btnCancelRef;

	private TextBox _txtKey;
	private FlatButton _btnRecord;
	private Label _lblKeyHint;
	private FlatCheck _chkHold;
	private FlatNum _numHold;

	/// <summary>触发模式：按住 / 点按切换。</summary>
	private FlatCombo _cmbTriggerMode;

	private FlatCombo _cmbLeft;
	private FlatCombo _cmbRight;
	private FlatCombo _cmbMiddle;

	private FlatCheck _chkFrame;
	private FlatCombo _cmbFrameColor;
	private FlatCheck _chkFollowAccent;
	private FlatButton _btnCustomColor;
	private Label _lblCustomColor;
	private int _customColorArgb;
	private FlatCombo _cmbFrameStyle;
	private SwatchGrid _gridColor;
	private Label _lblColorName;
	private Label _lblStyleHeader;
	private Label _lblStyleHint;
	private StyleGallery _galleryStyle;
	private FlatNum _numFrameThick;
	private FlatNum _numFrameRadius;
	private FlatNum _numFrameOpacity;
	private Label _lblFrameTip;
	private FramePreviewBox _pnlPreview;

	private FlatNum _numCorner;
	private FlatCheck _chkDblLeft;
	private FlatCheck _chkDblRight;
	private FlatCheck _chkRestoreMax;
	private FlatCheck _chkFront;
	private FlatCheck _chkFullscreen;
	private FlatCheck _chkExitFullscreen;
	private FlatCheck _chkShake;
	private TextBox _txtBlacklist;
	private FlatButton _btnPickExe;
	private FlatButton _btnPickWindow;

	private FlatCombo _cmbRefreshMode;
	private FlatNum _numRefreshHz;
	private Label _lblRefreshHint;

	private FlatCombo _cmbAutoStart;

	/// <summary>界面语言下拉框（简体中文 / English）：选完立即保存并重建整个界面。</summary>
	private FlatCombo _cmbLanguage;
	private FlatCheck _chkRunAsAdmin;
	private FlatCheck _chkWarnNonAdmin;
	private Label _lblAdmin;
	private FlatButton _btnRestartAdmin;

	private HelpPanel _helpPanel;

	private FlatButton _btnGitHub;
	private FlatCheck _chkAutoUpdate;
	private FlatButton _btnCheckUpdate;
	private Label _lblUpdateStatus;
	// v1.7.0：GitHub 个人访问令牌
	private TextBox _txtToken;
	private FlatButton _btnSaveToken;
	private FlatButton _btnVerifyToken;
	private FlatButton _btnTokenHelp;
	private Label _lblTokenState;
	private Label _lblTokenVerify;
	private ToolTip _tipToken;

	private int _defaultClientH = 680;

	// ======================= 缩放与度量 =======================

	internal static float CurrentUiScale = 1f;

	internal static float UiScaleForTest => (CurrentUiScale > 0f) ? CurrentUiScale : 1f;

	internal static string AppVersionForTest => AppInfo.Version;

	private float _uiScale = 1f;

	private float UiScale
	{
		get
		{
			return _uiScale;
		}
		set
		{
			_uiScale = value;
			CurrentUiScale = value;
		}
	}

	internal static string DesignFontFamily = "Microsoft YaHei UI";

	private int S(int design)
	{
		return (int)Math.Round((float)design * ((UiScale > 0f) ? UiScale : 1f));
	}

	private static int Sc(int design)
	{
		return (int)Math.Round((float)design * ((CurrentUiScale > 0f) ? CurrentUiScale : 1f));
	}

	// 与自测 T40 同一把尺子：GDI+ 排版高度 ×1.28，用来给标签留够高度（高 DPI 不裁字）。
	private static int TextHeightFor(Control c)
	{
		try
		{
			if (string.IsNullOrEmpty(c.Text))
			{
				return 0;
			}
			using Graphics graphics = c.CreateGraphics();
			return (int)Math.Ceiling(graphics.MeasureString(c.Text, c.Font, int.MaxValue, StringFormat.GenericTypographic).Height * 1.28f);
		}
		catch
		{
			return 0;
		}
	}

	private static int TextLines(string text, Control c, int avail)
	{
		if (string.IsNullOrEmpty(text) || avail <= 10)
		{
			return 1;
		}
		try
		{
			float num = c.Font.SizeInPoints * 96f / 72f * ((CurrentUiScale > 0f) ? CurrentUiScale : 1f);
			if (num < 6f)
			{
				num = 6f;
			}
			using Font font = new Font(c.Font.FontFamily, num, c.Font.Style, GraphicsUnit.Pixel);
			Size size = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
			Size size2 = TextRenderer.MeasureText(text, font, new Size(avail, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
			if (size.Height <= 0)
			{
				return 1;
			}
			int num2 = (int)Math.Ceiling((double)size2.Height / (double)size.Height);
			if (num2 < 1)
			{
				num2 = 1;
			}
			return num2;
		}
		catch
		{
			return 1;
		}
	}

	private static Color Mix(Color a, Color b, float t)
	{
		if (t < 0f)
		{
			t = 0f;
		}
		if (t > 1f)
		{
			t = 1f;
		}
		return Color.FromArgb((int)Math.Round((float)a.A + (float)(b.A - a.A) * t), (int)Math.Round((float)a.R + (float)(b.R - a.R) * t), (int)Math.Round((float)a.G + (float)(b.G - a.G) * t), (int)Math.Round((float)a.B + (float)(b.B - a.B) * t));
	}

	private static GraphicsPath RoundedPath(Rectangle r, int radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		if (radius <= 0)
		{
			graphicsPath.AddRectangle(r);
			return graphicsPath;
		}
		int num = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
		if (num < 1)
		{
			num = 1;
		}
		graphicsPath.AddArc(r.X, r.Y, num * 2, num * 2, 180f, 90f);
		graphicsPath.AddArc(r.Right - num * 2, r.Y, num * 2, num * 2, 270f, 90f);
		graphicsPath.AddArc(r.Right - num * 2, r.Bottom - num * 2, num * 2, num * 2, 0f, 90f);
		graphicsPath.AddArc(r.X, r.Bottom - num * 2, num * 2, num * 2, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	// ======================= 构造 =======================

	public SettingsForm(ConfigStore store, Coordinator coord)
	{
		_store = store;
		_coord = coord;
		_edit = store.Current.Clone();
		try
		{
			DesignFontFamily = PickDesignFont();
		}
		catch
		{
		}
		// 先探一次系统主题，再建界面：控件构造时读到的就是当前该用的那套颜色。
		Theme.Refresh();
		BuildUi();
		Populate();
		// 文案是 Populate 才填进去的（比如「固定值」下面那段实测说明），
		// 而行高要按文字折行数算 —— 填完必须重排一次，否则首帧用的是空文字的高度。
		LayoutAllPages(ContentWidth());
	}

	private static string PickDesignFont()
	{
		// 按界面语言选字体族：
		//   · 中文界面优先「微软雅黑 UI」——专为界面设计，字重均匀，小字号下最稳；
		//   · 英文界面优先 Segoe UI——Win10/11 自带，字形比雅黑的拉丁部分更现代。
		// 刻意**不选** "Segoe UI Variable*"：它是可变字体，WinForms 控件走 GDI 渲染，
		// GDI 只能取默认实例、中文还会逐字回退到别的字体，两套字形混排就是"文字发毛/锯齿"的主因。
		string[] array = Loc.IsEnglish
			? new string[2] { "Segoe UI", "Microsoft YaHei UI" }
			: new string[3] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" };
		try
		{
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			using (System.Drawing.Text.InstalledFontCollection installedFontCollection = new System.Drawing.Text.InstalledFontCollection())
			{
				for (int i = 0; i < installedFontCollection.Families.Length; i++)
				{
					hashSet.Add(installedFontCollection.Families[i].Name);
				}
			}
			for (int j = 0; j < array.Length; j++)
			{
				if (hashSet.Contains(array[j]))
				{
					return array[j];
				}
			}
		}
		catch
		{
		}
		return "Microsoft YaHei UI";
	}
}
