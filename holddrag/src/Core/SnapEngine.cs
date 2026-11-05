// HoldDrag · Core/SnapEngine.cs
//
// 拖动时的边缘吸附（纯函数，便于自检）。
//
// 只做"屏幕/工作区边缘"这一层：拖到离边 8px 内自动贴上去，并给出引导线的坐标。
// 不做"其它窗口边缘"的吸附 —— 那需要每帧枚举全桌面的窗口，代价和误吸都太高。

using System;

namespace HoldDrag;

internal static class SnapEngine
{
	internal struct Result
	{
		public Native.RECT Rect;

		public bool SnappedX;

		public bool SnappedY;

		/// <summary>竖引导线的屏幕 x；-1 表示没有。</summary>
		public int GuideX;

		/// <summary>横引导线的屏幕 y；-1 表示没有。</summary>
		public int GuideY;
	}

	/// <summary>移动吸附：把窗口的左/右/上/下边贴到工作区边缘或水平/垂直居中。</summary>
	public static Result SnapMove(Native.RECT proposed, Native.RECT work, int threshold)
	{
		Result result = new Result
		{
			Rect = proposed,
			GuideX = -1,
			GuideY = -1
		};
		if (threshold <= 0 || work.Width <= 0 || work.Height <= 0)
		{
			return result;
		}
		int width = proposed.Width;
		int height = proposed.Height;
		int bestDx = int.MaxValue;
		int left = proposed.Left;
		int guideX = -1;
		ConsiderX(work.Left, work.Left);
		ConsiderX(work.Right - width, work.Right);
		ConsiderX(work.Left + (work.Width - width) / 2, work.Left + work.Width / 2);
		if (guideX >= 0)
		{
			result.Rect.Left = left;
			result.Rect.Right = left + width;
			result.SnappedX = true;
			result.GuideX = guideX;
		}
		int bestDy = int.MaxValue;
		int top = proposed.Top;
		int guideY = -1;
		ConsiderY(work.Top, work.Top);
		ConsiderY(work.Bottom - height, work.Bottom);
		ConsiderY(work.Top + (work.Height - height) / 2, work.Top + work.Height / 2);
		if (guideY >= 0)
		{
			result.Rect.Top = top;
			result.Rect.Bottom = top + height;
			result.SnappedY = true;
			result.GuideY = guideY;
		}
		return result;

		void ConsiderX(int candidateLeft, int guide)
		{
			int num = Math.Abs(candidateLeft - proposed.Left);
			if (num <= threshold && num < bestDx)
			{
				bestDx = num;
				left = candidateLeft;
				guideX = guide;
			}
		}

		void ConsiderY(int candidateTop, int guide)
		{
			int num = Math.Abs(candidateTop - proposed.Top);
			if (num <= threshold && num < bestDy)
			{
				bestDy = num;
				top = candidateTop;
				guideY = guide;
			}
		}
	}

	/// <summary>缩放吸附：只吸附真正在拖的那条边（另一个方向保持不动）。</summary>
	public static Result SnapResize(Native.RECT proposed, bool dragLeft, bool dragRight, bool dragTop, bool dragBottom, Native.RECT work, int threshold)
	{
		Result result = new Result
		{
			Rect = proposed,
			GuideX = -1,
			GuideY = -1
		};
		if (threshold <= 0)
		{
			return result;
		}
		if (dragLeft)
		{
			int num = Math.Abs(proposed.Left - work.Left);
			if (num <= threshold)
			{
				result.Rect.Left = work.Left;
				result.SnappedX = true;
				result.GuideX = work.Left;
			}
		}
		else if (dragRight)
		{
			int num2 = Math.Abs(proposed.Right - work.Right);
			if (num2 <= threshold)
			{
				result.Rect.Right = work.Right;
				result.SnappedX = true;
				result.GuideX = work.Right;
			}
		}
		if (dragTop)
		{
			int num3 = Math.Abs(proposed.Top - work.Top);
			if (num3 <= threshold)
			{
				result.Rect.Top = work.Top;
				result.SnappedY = true;
				result.GuideY = work.Top;
			}
		}
		else if (dragBottom)
		{
			int num4 = Math.Abs(proposed.Bottom - work.Bottom);
			if (num4 <= threshold)
			{
				result.Rect.Bottom = work.Bottom;
				result.SnappedY = true;
				result.GuideY = work.Bottom;
			}
		}
		return result;
	}
}
