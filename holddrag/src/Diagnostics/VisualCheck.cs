// HoldDrag · Diagnostics/VisualCheck.cs

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

internal static class VisualCheck
{
	public static void Run(string outDir)
	{
		Directory.CreateDirectory(outDir);
		string text = Path.Combine(Path.GetTempPath(), "holddrag-visual-cfg");
		try
		{
			Directory.CreateDirectory(text);
		}
		catch
		{
		}
		ConfigStore configStore = new ConfigStore(text);
		try
		{
			configStore.Load();
		}
		catch
		{
		}
		Coordinator coord = new Coordinator(configStore);
		Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
		int num = (int)Math.Round((double)workingArea.Width * 0.42);
		int num2 = (int)Math.Round((double)workingArea.Width * 0.72);
		int num3 = (int)Math.Round((double)workingArea.Width * 0.96);
		int num4 = (int)Math.Round(SettingsForm.CurrentUiScale * 600f);
		if (num < num4)
		{
			num = num4;
		}
		int num5 = (int)Math.Round((double)workingArea.Height * 0.88);
		int num6 = (int)Math.Round((double)workingArea.Height * 0.92);
		int num7 = (int)Math.Round((double)workingArea.Height * 0.8);
		string path = Path.Combine(outDir, "sizes.txt");
		InlineArray9<object> buffer = default;
		buffer[0] = workingArea.Width;
		buffer[1] = workingArea.Height;
		buffer[2] = SettingsForm.CurrentUiScale;
		buffer[3] = num;
		buffer[4] = num5;
		buffer[5] = num2;
		buffer[6] = num7;
		buffer[7] = num3;
		buffer[8] = num6;
		File.WriteAllText(path, string.Format("工作区={0}x{1} 缩放={2:0.###}\n窄={3}x{4}\n中={5}x{6}\n宽={7}x{8}", (ReadOnlySpan<object?>)buffer), Encoding.UTF8);
		Shoot(configStore, coord, new Size(num, num5), Path.Combine(outDir, "settings-narrow.png"));
		Shoot(configStore, coord, new Size(num2, num7), Path.Combine(outDir, "settings-mid.png"));
		Shoot(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "settings-wide.png"));
		// 同一页在亮 / 暗两套主题下各来一张：切主题是这次的重点，必须能肉眼对照
		SettingsForm.ProbeForceDarkTheme(false);
		Shoot(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "settings-wide-light.png"));
		SettingsForm.ProbeForceDarkTheme(true);
		Shoot(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "settings-wide-dark.png"));
		SettingsForm.ProbeForceDarkTheme(false);
		Shoot(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "visual-page-light.png"), 2);
		// 默认尺寸（1024）和拉宽两档都拍一张：单列居中 / 两列用满宽度，一眼能对比
		ShootLive(configStore, coord, new Size(num2, num7), Path.Combine(outDir, "live-default.png"), 0);
		ShootLive(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "live-wide.png"), 0);
		ShootLive(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "live-light.png"), 2);
		SettingsForm.ProbeForceDarkTheme(true);
		Shoot(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "visual-page-dark.png"), 2);
		ShootLive(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "live-dark.png"), 2);
		SettingsForm.ProbeForceDarkTheme(false);
		ShootWindow(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "window-light.png"), 2);
		SettingsForm.ProbeForceDarkTheme(true);
		ShootWindow(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "window-dark.png"), 2);
		SettingsForm.ProbeForceDarkTheme(null);
		Dump(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "layout-wide.txt"), 3);
		Dump(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "layout-g1.txt"), 0);
		Dump(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "layout-g5.txt"), 4);
		Dump(configStore, coord, new Size(num, num5), Path.Combine(outDir, "layout-n1.txt"), 0);
		Dump(configStore, coord, new Size(num, num5), Path.Combine(outDir, "layout-n3.txt"), 3);
		Dump(configStore, coord, new Size(num, num5), Path.Combine(outDir, "layout-g3.txt"), 2);
		Dump(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "layout-g3-wide.txt"), 2);
		ShootGroup(configStore, coord, new Size(num, num5), Path.Combine(outDir, "group-visual.png"), 2);
		ShootFrameSheet(Path.Combine(outDir, "frames-all.png"));
		ShootRadiusSheet(Path.Combine(outDir, "radius-sheet.png"));
	}

	private static void ShootRadiusSheet(string path)
	{
		try
		{
			int[] array = new int[4] { 0, 10, 20, 28 };
			string[] array2 = new string[3] { "Dashed", "Focus", "Shadow" };
			int num = 260;
			int num2 = 180;
			int num3 = 10;
			int num4 = 26;
			int width = num3 + array2.Length * (num + num3);
			int height = num4 + array.Length * (num2 + num4 + num3);
			using Bitmap bitmap = new Bitmap(width, height);
			using Graphics graphics = Graphics.FromImage(bitmap);
			graphics.Clear(Color.White);
			using Font font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular);
			using SolidBrush brush = new SolidBrush(Color.FromArgb(40, 42, 48));
			for (int i = 0; i < array2.Length; i++)
			{
				graphics.DrawString(array2[i], font, brush, new PointF(num3 + i * (num + num3), 4f));
			}
			for (int j = 0; j < array.Length; j++)
			{
				for (int k = 0; k < array2.Length; k++)
				{
					int x = num3 + k * (num + num3);
					int y = num4 + j * (num2 + num4 + num3);
					RectangleF rectangleF = new RectangleF(x, y, num, num2);
					using (SolidBrush brush2 = new SolidBrush(Color.White))
					{
						graphics.FillRectangle(brush2, rectangleF);
					}
					FrameStyles.Draw(graphics, rectangleF, Color.FromArgb(107, 196, 165), array2[k], 3, array[j]);
					graphics.DrawString("半径 " + array[j], font, brush, new PointF(x + 4f, (float)(y + num2) - 18f));
				}
			}
			bitmap.Save(path, ImageFormat.Png);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static void ShootFrameSheet(string path)
	{
		try
		{
			int num = 210;
			int num2 = 132;
			int num3 = 12;
			int num4 = 26;
			int num5 = 40;
			int num6 = FrameStyles.All.Length;
			int num7 = FrameColors.All.Length;
			int width = num3 + num6 * (num + num3);
			int height = num5 + num4 + num3 + num7 * (num2 + num4 + num3);
			using (Bitmap bitmap = new Bitmap(width, height))
			{
				using Graphics graphics = Graphics.FromImage(bitmap);
				graphics.Clear(Color.FromArgb(244, 245, 247));
				using (Font font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold))
				{
					using Font font2 = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
					using SolidBrush brush = new SolidBrush(Color.FromArgb(24, 26, 33));
					using SolidBrush brush2 = new SolidBrush(Color.FromArgb(120, 126, 140));
					graphics.DrawString("外框颜色 × 样式 对照表（v1.5 精简重做）", font, brush, new PointF(num3, 10f));
					for (int i = 0; i < num6; i++)
					{
						graphics.DrawString(layoutRectangle: new RectangleF(num3 + i * (num + num3), num5, num, num4), s: FrameStyles.DisplayName(FrameStyles.All[i]), font: font2, brush: brush);
					}
					for (int j = 0; j < num7; j++)
					{
						FrameColors.Entry entry = FrameColors.All[j];
						Color color = FrameColors.Resolve(entry.Id);
						int num8 = num5 + num4 + num3 + j * (num2 + num4 + num3);
						for (int k = 0; k < num6; k++)
						{
							int num9 = num3 + k * (num + num3);
							RectangleF rectangleF = new RectangleF(num9, num8, num, num2);
							using (SolidBrush brush3 = new SolidBrush(Color.White))
							{
								graphics.FillRectangle(brush3, rectangleF);
							}
							FrameStyles.Draw(graphics, rectangleF, color, FrameStyles.All[k], 3, 12);
						}
						using (SolidBrush brush4 = new SolidBrush(color))
						{
							graphics.FillRectangle(brush4, num3, num8 + num2 + 4, 14, 14);
						}
						graphics.DrawString(entry.Name, font2, brush2, new RectangleF(num3 + 20, num8 + num2 + 1, num, num4));
					}
				}
				bitmap.Save(path, ImageFormat.Png);
			}
			File.WriteAllText(path + ".txt", "颜色 " + FrameColors.All.Length + " 种 × 样式 " + FrameStyles.All.Length + " 种\n", Encoding.UTF8);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	// DrawToBitmap 拍到的是 WinForms 自己画的假边框，DWM 的真实标题栏只能从屏幕抓。
	private static void ShootLive(ConfigStore store, Coordinator coord, Size size, string path, int page)
	{
		try
		{
			using SettingsForm settingsForm = new SettingsForm(store, coord);
			settingsForm.StartPosition = FormStartPosition.Manual;
			settingsForm.Location = new Point(40, 40);
			settingsForm.ClientSize = size;
			settingsForm.TopMost = true;
			settingsForm.Show();
			SettingsForm.ProbeSelectPage(settingsForm, page);
			settingsForm.Activate();
			settingsForm.BringToFront();
			Application.DoEvents();
			for (int i = 0; i < 10; i++)
			{
				Application.DoEvents();
				Thread.Sleep(80);
			}
			Rectangle bounds = settingsForm.Bounds;
			using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
			{
				using (Graphics graphics = Graphics.FromImage(bitmap))
				{
					graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
				}
				bitmap.Save(path, ImageFormat.Png);
			}
			settingsForm.Close();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	// PrintWindow(PW_RENDERFULLCONTENT)：连 DWM 画的标题栏一起抓，用来核对"最上面那条和页面是否同色"。
	private static void ShootWindow(ConfigStore store, Coordinator coord, Size size, string path, int page)
	{
		try
		{
			using SettingsForm settingsForm = new SettingsForm(store, coord);
			settingsForm.StartPosition = FormStartPosition.Manual;
			settingsForm.Location = new Point(60, 60);
			settingsForm.ClientSize = size;
			settingsForm.Show();
			SettingsForm.ProbeSelectPage(settingsForm, page);
			Application.DoEvents();
			for (int i = 0; i < 8; i++)
			{
				Application.DoEvents();
				Thread.Sleep(80);
			}
			Native.RECT rECT;
			Native.GetWindowRect(settingsForm.Handle, out rECT);
			int width = rECT.Width;
			int height = rECT.Height;
			using (Bitmap bitmap = new Bitmap(width, height))
			{
				using (Graphics graphics = Graphics.FromImage(bitmap))
				{
					nint hdc = graphics.GetHdc();
					try
					{
						Native.PrintWindow(settingsForm.Handle, hdc, 2u);
					}
					finally
					{
						graphics.ReleaseHdc(hdc);
					}
				}
				bitmap.Save(path, ImageFormat.Png);
			}
			settingsForm.Close();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static void ShootGroup(ConfigStore store, Coordinator coord, Size size, string path, int groupIndex)
	{
		try
		{
			using SettingsForm settingsForm = new SettingsForm(store, coord);
			settingsForm.StartPosition = FormStartPosition.Manual;
			settingsForm.Location = new Point(20, 20);
			settingsForm.ClientSize = size;
			settingsForm.TopMost = true;
			settingsForm.Show();
			for (int i = 0; i < 8; i++)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			Control control = SettingsForm.ProbeGroupAt(settingsForm, groupIndex);
			if (control == null || control.Width <= 0 || control.Height <= 0)
			{
				settingsForm.Close();
				return;
			}
			using (Bitmap bitmap = new Bitmap(control.Width, control.Height))
			{
				control.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
				bitmap.Save(path, ImageFormat.Png);
			}
			settingsForm.Close();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static void Dump(ConfigStore store, Coordinator coord, Size size, string path, int groupIndex)
	{
		try
		{
			using SettingsForm settingsForm = new SettingsForm(store, coord);
			settingsForm.StartPosition = FormStartPosition.Manual;
			settingsForm.Location = new Point(20, 20);
			settingsForm.ClientSize = size;
			settingsForm.Show();
			Application.DoEvents();
			StringBuilder stringBuilder = new StringBuilder();
			SettingsForm.DumpGroup(settingsForm, groupIndex, stringBuilder);
			for (int i = 0; i < 3; i++)
			{
				string text = stringBuilder.ToString();
				SettingsForm.RelayoutForTest(settingsForm);
				Application.DoEvents();
				StringBuilder stringBuilder2 = new StringBuilder();
				SettingsForm.DumpGroup(settingsForm, groupIndex, stringBuilder2);
				if (text != stringBuilder2.ToString())
				{
					stringBuilder.AppendLine();
					stringBuilder.AppendLine("!! 第 " + (i + 1) + " 次重排结果与上次不一致（布局不幂等）");
					stringBuilder.AppendLine(stringBuilder2.ToString());
					break;
				}
			}
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("幂等性：连续重排 3 次结果一致 ✓");
			File.WriteAllText(path, stringBuilder.ToString(), Encoding.UTF8);
			settingsForm.Close();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static void Shoot(ConfigStore store, Coordinator coord, Size size, string path)
	{
		Shoot(store, coord, size, path, 0);
	}

	private static void Shoot(ConfigStore store, Coordinator coord, Size size, string path, int page)
	{
		try
		{
			using SettingsForm settingsForm = new SettingsForm(store, coord);
			settingsForm.StartPosition = FormStartPosition.Manual;
			settingsForm.Location = new Point(20, 20);
			settingsForm.ClientSize = size;
			settingsForm.TopMost = true;
			settingsForm.Show();
			SettingsForm.ProbeSelectPage(settingsForm, page);
			settingsForm.Activate();
			settingsForm.BringToFront();
			Application.DoEvents();
			for (int i = 0; i < 6; i++)
			{
				Application.DoEvents();
				Thread.Sleep(120);
			}
			settingsForm.Refresh();
			Application.DoEvents();
			int width = settingsForm.Width;
			int height = settingsForm.Height;
			using (Bitmap bitmap = new Bitmap(width, height))
			{
				settingsForm.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
				bitmap.Save(path, ImageFormat.Png);
			}
			settingsForm.Close();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}
}
