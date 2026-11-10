// HoldDrag · UI/SettingsForm.Probe.Access.cs
//
// 探针面：控件字段的直接访问器（测试要拿到具体控件）。

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
	internal void ProbeCaptureInto(AppConfig target)
	{
		CaptureInto(target);
	}
	internal string ProbeBlacklistText => _txtBlacklist.Text;
	internal Control ProbeComboLeft => _cmbLeft;
	internal Control ProbeComboRight => _cmbRight;
	internal Control ProbeNumHold => _numHold;
	internal static Control ProbeCardByTitle(Form f, string title)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return null;
		}
		foreach (Card item in AllCards(settingsForm))
		{
			if (item.Text == title)
			{
				return item;
			}
		}
		return null;
	}
	internal HelpPanel ProbeHelpPanel => _helpPanel;
	internal string ProbeAboutText
	{
		get
		{
			try
			{
				Card card = ProbeCardByTitle(this, Loc.T("关于")) as Card;
				if (card == null)
				{
					return "";
				}
				StringBuilder stringBuilder = new StringBuilder();
				foreach (Control control in card.Controls)
				{
					if (!string.IsNullOrEmpty(control.Text))
					{
						stringBuilder.Append(control.Text).Append(' ');
					}
				}
				return stringBuilder.ToString();
			}
			catch
			{
				return "";
			}
		}
	}
	internal Button ProbeGitHubButton => _btnGitHub;
	internal Button ProbeCheckUpdateButton => _btnCheckUpdate;
	internal CheckBox ProbeAutoUpdateCheck => _chkAutoUpdate;
	/// <summary>自检/调试：语言下拉框（用来模拟用户切换语言）。</summary>
	internal FlatCombo ProbeLanguageCombo => _cmbLanguage;
	internal Label ProbeUpdateStatusLabel => _lblUpdateStatus;
	// v1.7.0：令牌相关的探针（自检用来断言对齐与无障碍，不需要真的弹对话框）
	internal TextBox ProbeTokenBox => _txtToken;
	internal Button ProbeTokenHelpButton => _btnTokenHelp;
	internal Button ProbeSaveTokenButton => _btnSaveToken;
	internal Button ProbeVerifyTokenButton => _btnVerifyToken;
	internal Label ProbeTokenVerifyLabel => _lblTokenVerify;
	internal Label ProbeTokenStateLabel => _lblTokenState;
}
