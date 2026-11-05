// HoldDrag · Core/Tiling.cs
//
// 触发键按住期间的键盘布局（纯函数）。
//
// 约定：激活后用方向键 / 数字小键盘布局当前窗口：
//   ← 左半屏   → 右半屏   ↑ 最大化   ↓ 还原
//   1..9     九宫格（1 = 左下，9 = 右上，和数字小键盘一致）
//   0        保持大小、居中

using System;

namespace HoldDrag;

internal static class Tiling
{
	public static Native.RECT Half(Native.RECT work, bool left)
	{
		int num = work.Left + work.Width / 2;
		if (left)
		{
			return new Native.RECT(work.Left, work.Top, num, work.Bottom);
		}
		return new Native.RECT(num, work.Top, work.Right, work.Bottom);
	}

	/// <summary>九宫格：digit 1..9，按数字小键盘排布（1 在左下）。</summary>
	public static Native.RECT Zone(int digit, Native.RECT work)
	{
		if (digit < 1 || digit > 9)
		{
			return work;
		}
		int num = digit - 1;
		int col = num % 3;
		int rowFromBottom = num / 3;
		int rowFromTop = 2 - rowFromBottom;
		int x0 = work.Left + work.Width * col / 3;
		int x1 = work.Left + work.Width * (col + 1) / 3;
		int y0 = work.Top + work.Height * rowFromTop / 3;
		int y1 = work.Top + work.Height * (rowFromTop + 1) / 3;
		return new Native.RECT(x0, y0, x1, y1);
	}

	/// <summary>保持当前尺寸，在工作区里居中。</summary>
	public static Native.RECT Centered(Native.RECT work, Native.RECT current)
	{
		int width = Math.Min(current.Width, work.Width);
		int height = Math.Min(current.Height, work.Height);
		int left = work.Left + (work.Width - width) / 2;
		int top = work.Top + (work.Height - height) / 2;
		return new Native.RECT(left, top, left + width, top + height);
	}

	/// <summary>把目标可见矩形换算成 SetWindowPos 需要的窗口矩形（补上不可见的边框）。</summary>
	public static Native.RECT InflateToWindowRect(Native.RECT visible, Native.RECT windowRect, Native.RECT visibleRect)
	{
		int num = visibleRect.Left - windowRect.Left;
		int num2 = visibleRect.Top - windowRect.Top;
		int num3 = windowRect.Right - visibleRect.Right;
		int num4 = windowRect.Bottom - visibleRect.Bottom;
		if (num < 0 || num2 < 0 || num3 < 0 || num4 < 0 || num > 40 || num2 > 40 || num3 > 40 || num4 > 40)
		{
			return visible;
		}
		return new Native.RECT(visible.Left - num, visible.Top - num2, visible.Right + num3, visible.Bottom + num4);
	}
}
