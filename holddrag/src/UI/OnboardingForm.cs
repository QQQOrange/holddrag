// HoldDrag · UI/OnboardingForm.cs
//
// 首次启动的三步引导。目标只有一个：让新用户在两分钟内成功拖动一次窗口。
// 刻意做成三步、每步只有一个重点，避免"设置页那么大一坨，不知道从哪开始"。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HoldDrag;

internal sealed class OnboardingForm : Form
{
	private readonly ConfigStore _store;

	private readonly Coordinator _coord;

	private readonly Panel _body;

	private readonly Label _stepLabel;

	private readonly AuroraButton _back;

	private readonly AuroraButton _next;

	private readonly AuroraButton _close;

	private int _step;

	private CheckBox _chkEnable;

	private ComboBoxLike _keyList;

	public OnboardingForm(ConfigStore store, Coordinator coord)
	{
		_store = store;
		_coord = coord;
		Text = Loc.T("欢迎使用长按拖窗");
		FormBorderStyle = FormBorderStyle.FixedDialog;
		StartPosition = FormStartPosition.CenterScreen;
		MaximizeBox = false;
		MinimizeBox = false;
		ShowInTaskbar = true;
		AutoScaleMode = AutoScaleMode.None;
		ClientSize = new Size(560, 420);
		BackColor = AuroraTheme.Surface;
		ForeColor = AuroraTheme.TextPrimary;
		Font = AuroraTheme.Font(13f);
		try
		{
			Icon icon = Icon.ExtractAssociatedIcon(AppPaths.ExePath);
			if (icon != null)
			{
				Icon = icon;
			}
		}
		catch
		{
		}
		AuroraTheme.Refresh();
		_stepLabel = new Label
		{
			AutoSize = false,
			Bounds = new Rectangle(28, 20, 300, 22),
			Text = "",
			ForeColor = AuroraTheme.TextTertiary,
			Font = AuroraTheme.Font(12f),
			BackColor = Color.Transparent
		};
		_body = new Panel
		{
			Bounds = new Rectangle(28, 48, 504, 296),
			BackColor = Color.Transparent
		};
		_back = new AuroraButton
		{
			Text = Loc.T("上一步"),
			Bounds = new Rectangle(28, 360, 100, 36)
		};
		_back.Click += delegate
		{
			if (_step > 0)
			{
				_step--;
				RenderStep();
			}
		};
		_next = new AuroraButton
		{
			Text = Loc.T("下一步"),
			Primary = true,
			Bounds = new Rectangle(340, 360, 110, 36)
		};
		_next.Click += delegate
		{
			if (_step >= 2)
			{
				Finish();
				return;
			}
			_step++;
			RenderStep();
		};
		_close = new AuroraButton
		{
			Text = Loc.T("跳过"),
			Bounds = new Rectangle(458, 360, 74, 36)
		};
		_close.Click += delegate
		{
			Finish();
		};
		Controls.Add(_stepLabel);
		Controls.Add(_body);
		Controls.Add(_back);
		Controls.Add(_next);
		Controls.Add(_close);
		RenderStep();
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		try
		{
			Native.SetDarkTitleBar(Handle, AuroraTheme.IsDark);
		}
		catch
		{
		}
	}

	private void RenderStep()
	{
		_body.Controls.Clear();
		_stepLabel.Text = Loc.T("第 ") + (_step + 1) + Loc.T(" / 3 步");
		_back.Visible = _step > 0;
		_next.Text = (_step >= 2) ? Loc.T("开始使用") : Loc.T("下一步");
		switch (_step)
		{
		case 0:
			AddTitle(Loc.T("长按一个键，就能拖动任何窗口"));
			AddParagraph(Loc.T("按住触发键不要松手 —— 光标下的窗口会出现一圈提示框； 这时按住左键拖动 = 移动窗口，按住右键拖动 = 缩放窗口。"));
			AddParagraph(Loc.T("松开触发键就退出，鼠标行为立刻恢复正常。"));
			break;
		case 1:
		{
			AddTitle(Loc.T("选一个顺手的触发键"));
			AddParagraph(Loc.T("默认是 Enter 回车。选一个你平时不会用来打字的键最合适。"));
			_keyList = new ComboBoxLike();
			_keyList.SetItems(new string[7]
			{
				"Enter 回车", "Space 空格", "CapsLock 大写锁定", "右 Ctrl", "右 Alt", "F8", "F9"
			}, new string[7]
			{
				"K:0x0D", "K:0x20", "K:0x14", "K:0xA3", "K:0xA5", "K:0x77", "K:0x78"
			}, _store.Current.TriggerKeyId);
			_keyList.Bounds = new Rectangle(0, 92, 260, 34);
			_body.Controls.Add(_keyList);
			break;
		}
		default:
			AddTitle(Loc.T("可以开始了"));
			AddParagraph(Loc.T("按住触发键 → 拖动窗口试试。"));
			AddParagraph(Loc.T("想改颜色 / 外框样式 / 灵敏度，右键托盘图标 →「设置…」。 出问题时可以在这里一键导出诊断包。"));
			_chkEnable = new CheckBox
			{
				Text = Loc.T("开机自动启动"),
				Checked = _store.Current.AutoStart != "None",
				Bounds = new Rectangle(0, 150, 320, 30),
				ForeColor = AuroraTheme.TextPrimary,
				BackColor = AuroraTheme.Surface,
				FlatStyle = FlatStyle.Flat,
				Font = AuroraTheme.Font(13f)
			};
			_body.Controls.Add(_chkEnable);
			break;
		}
	}

	private void AddTitle(string text)
	{
		Label label = new Label
		{
			AutoSize = false,
			Bounds = new Rectangle(0, 0, 504, 40),
			Text = text,
			Font = AuroraTheme.Font(19f, bold: true),
			ForeColor = AuroraTheme.TextPrimary,
			BackColor = Color.Transparent
		};
		_body.Controls.Add(label);
	}

	private void AddParagraph(string text)
	{
		Label label = new Label
		{
			AutoSize = false,
			Bounds = new Rectangle(0, 52 + _body.Controls.Count * 4, 504, 76),
			Text = text,
			Font = AuroraTheme.Font(13f),
			ForeColor = AuroraTheme.TextSecondary,
			BackColor = Color.Transparent
		};
		_body.Controls.Add(label);
	}

	private void Finish()
	{
		try
		{
			AppConfig appConfig = _store.Current;
			if (_keyList != null && !string.IsNullOrEmpty(_keyList.SelectedId))
			{
				appConfig.TriggerKeyId = _keyList.SelectedId;
			}
			if (_chkEnable != null)
			{
				appConfig.AutoStart = (_chkEnable.Checked ? "TaskScheduler" : "None");
			}
			appConfig.OnboardingDone = true;
			string error;
			_store.Apply(appConfig, out error);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		Close();
	}

	/// <summary>
	/// 引导里用的极简下拉：复用 FlatCombo 太重（它是 SettingsForm 的私有嵌套类），
	/// 这里用系统 ComboBox 配上 Aurora 字体与颜色，够用且不会再引入一套自绘逻辑。
	/// </summary>
	private sealed class ComboBoxLike : ComboBox
	{
		private readonly List<string> _ids = new List<string>();

		public string SelectedId
		{
			get
			{
				int num = SelectedIndex;
				if (num < 0 || num >= _ids.Count)
				{
					return null;
				}
				return _ids[num];
			}
		}

		public ComboBoxLike()
		{
			DropDownStyle = ComboBoxStyle.DropDownList;
			FlatStyle = FlatStyle.Flat;
			Font = AuroraTheme.Font(13f);
			BackColor = AuroraTheme.Surface;
			ForeColor = AuroraTheme.TextPrimary;
		}

		public void SetItems(string[] names, string[] ids, string current)
		{
			Items.Clear();
			_ids.Clear();
			for (int i = 0; i < names.Length; i++)
			{
				Items.Add(names[i]);
				_ids.Add(ids[i]);
			}
			int num = _ids.IndexOf(current);
			SelectedIndex = ((num >= 0) ? num : 0);
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			try
			{
				Native.SetWindowTheme(Handle, " ", " ");
			}
			catch
			{
			}
		}
	}
}
