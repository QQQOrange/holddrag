// HoldDrag · Core/ShakeDetector.cs

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

internal sealed class ShakeDetector
{
	private struct Sample
	{
		public int X;

		public int Y;

		public long T;
	}

	public const int MinSpanLogical = 32;

	public const int MaxSpanLogical = 220;

	public const int GestureWindowMs = 800;

	public const int ReversalsNeeded = 4;

	public const int MaxNetDriftLogical = 80;

	public const int MinPathLogical = 150;

	public const double HorizontalDominance = 0.72;

	public const int CooldownMs = 900;

	private const int SampleCap = 768;

	private readonly Sample[] _buf = new Sample[768];

	private int _n;

	private int _head;

	private bool _armed;

	private long _coolUntil;

	private double _scale = 1.0;

	internal int SampleCountForTest => _n;

	internal bool CoolingForTest => _coolUntil != 0;

	public void SetScale(double scale)
	{
		_scale = ((scale > 0.1 && scale < 8.0) ? scale : 1.0);
	}

	public void Reset(int x, int y, long nowMs)
	{
		_n = 0;
		_head = 0;
		_coolUntil = 0L;
		_armed = true;
		Push(x, y, nowMs);
	}

	public void Invalidate()
	{
		_armed = false;
		_n = 0;
		_head = 0;
	}

	public bool Feed(int x, int y, long nowMs)
	{
		if (!_armed)
		{
			return false;
		}
		if (nowMs < _coolUntil)
		{
			return false;
		}
		Push(x, y, nowMs);
		while (_n > 0 && nowMs - Oldest().T > 800)
		{
			DropOldest();
		}
		if (_n < 6)
		{
			return false;
		}
		if (!Evaluate())
		{
			return false;
		}
		_coolUntil = nowMs + 900;
		_n = 0;
		_head = 0;
		return true;
	}

	private bool Evaluate()
	{
		int num = (int)Math.Round(32.0 * _scale);
		int num2 = (int)Math.Round(220.0 * _scale);
		int num3 = (int)Math.Round(150.0 * _scale);
		int num4 = (int)Math.Round(80.0 * _scale);
		if (num < 8)
		{
			num = 8;
		}
		if (num4 < num)
		{
			num4 = num;
		}
		int num5 = 0;
		int num6 = 0;
		int num7 = 0;
		int num8 = 0;
		int num9 = 0;
		bool flag = false;
		Sample sample = At(0);
		for (int i = 1; i < _n; i++)
		{
			Sample sample2 = At(i);
			int num10 = sample2.X - sample.X;
			int value = sample2.Y - sample.Y;
			sample = sample2;
			num6 += Math.Abs(num10);
			num7 += Math.Abs(value);
			int num11 = Math.Abs(num10);
			if (num11 == 0)
			{
				continue;
			}
			int num12 = ((num10 > 0) ? 1 : (-1));
			if (num8 == 0)
			{
				num8 = num12;
				num9 = num11;
				continue;
			}
			if (num12 == num8)
			{
				num9 += num11;
				if (num9 > num2)
				{
					flag = true;
				}
				continue;
			}
			if (num9 >= num)
			{
				num5++;
			}
			num8 = num12;
			num9 = num11;
		}
		if (flag)
		{
			return false;
		}
		if (num5 < 4)
		{
			return false;
		}
		if (num6 < num3)
		{
			return false;
		}
		int num13 = num6 + num7;
		if (num13 <= 0)
		{
			return false;
		}
		if ((double)num6 < (double)num13 * 0.72)
		{
			return false;
		}
		Sample sample3 = At(0);
		Sample sample4 = At(_n - 1);
		if (Math.Abs(sample4.X - sample3.X) > num4)
		{
			return false;
		}
		if (Math.Abs(sample4.Y - sample3.Y) > num4)
		{
			return false;
		}
		return true;
	}

	private void Push(int x, int y, long nowMs)
	{
		_buf[_head] = new Sample
		{
			X = x,
			Y = y,
			T = nowMs
		};
		_head = (_head + 1) % 768;
		if (_n < 768)
		{
			_n++;
		}
	}

	private Sample Oldest()
	{
		int num = (_head - _n + 768) % 768;
		return _buf[num];
	}

	private void DropOldest()
	{
		if (_n > 0)
		{
			_n--;
		}
	}

	private Sample At(int i)
	{
		int num = (_head - _n + i + 1536) % 768;
		return _buf[num];
	}
}
