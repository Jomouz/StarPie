using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace WinPieGestures;

public partial class ScreenSnipWindow : Window
{
	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int nIndex);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteObject(IntPtr hObject);

	private const int SM_XVIRTUALSCREEN = 76;
	private const int SM_YVIRTUALSCREEN = 77;
	private const int SM_CXVIRTUALSCREEN = 78;
	private const int SM_CYVIRTUALSCREEN = 79;
	private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
	private const uint SWP_NOACTIVATE = 0x0010;
	private const uint SWP_NOZORDER = 0x0004;

	private System.Windows.Point _startPoint;
	private bool _isSelecting;
	private readonly Action<Bitmap?> _onCaptured;
	private Bitmap? _fullScreenBmp;
	private bool _resourcesReleased;
	private bool _completing;
	private readonly int _maxSelectionPixels;
	private readonly int _screenX;
	private readonly int _screenY;
	private readonly string? _guide;
	private readonly string? _guideKey;
	internal int CapturedScreenX { get; private set; }
	internal int CapturedScreenY { get; private set; }
	internal bool SelectionTooLarge { get; private set; }

	public ScreenSnipWindow(Action<Bitmap?> onCaptured, string? guide = null,
		int maxSnapshotPixels = int.MaxValue, int maxSelectionPixels = int.MaxValue, string? guideKey = null)
	{
		InitializeComponent();
		_onCaptured = onCaptured;
		_maxSelectionPixels = maxSelectionPixels;
		_guide = guide;
		_guideKey = guideKey;
		RefreshCaptureLanguage();
		AppThemeManager.ApplyTheme(this, ConfigManager.CurrentConfig?.AppTheme ?? "System");

		// 1. 获取真实多显示器全景物理边界
		int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
		int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
		int vw = Math.Max(100, GetSystemMetrics(SM_CXVIRTUALSCREEN));
		int vh = Math.Max(100, GetSystemMetrics(SM_CYVIRTUALSCREEN));
		_screenX = vx;
		_screenY = vy;
		if ((long)vw * vh > maxSnapshotPixels)
			throw new InvalidOperationException("Desktop snapshot exceeds the capture memory budget.");

		// 2. 瞬间冻结屏幕，抓取 24bpp 纯 RGB 全景物理快照（杜绝任何 Alpha 透明通道污染）
		try
		{
			_fullScreenBmp = new Bitmap(vw, vh, PixelFormat.Format24bppRgb);
			using (Graphics g = Graphics.FromImage(_fullScreenBmp))
			{
				g.CopyFromScreen(vx, vy, 0, 0, new System.Drawing.Size(vw, vh), CopyPixelOperation.SourceCopy);
			}
			BackgroundImage.Source = BitmapToBitmapSource(_fullScreenBmp);
		}
		catch (Exception ex)
		{
			AppLogger.LogError("Failed to capture freeze full screen", ex);
			_fullScreenBmp?.Dispose();
			_fullScreenBmp = null;
			if (maxSnapshotPixels != int.MaxValue) throw;
		}

		// 3. WPF 逻辑尺寸对齐虚拟桌面
		Left = SystemParameters.VirtualScreenLeft;
		Top = SystemParameters.VirtualScreenTop;
		Width = SystemParameters.VirtualScreenWidth;
		Height = SystemParameters.VirtualScreenHeight;

		SourceInitialized += (s, e) =>
		{
			IntPtr handle = new WindowInteropHelper(this).Handle;
			if (handle != IntPtr.Zero)
			{
				SetWindowPos(handle, HWND_TOPMOST, vx, vy, vw, vh, SWP_NOACTIVATE | SWP_NOZORDER);
			}
		};
		I18n.LanguageChanged += RefreshCaptureLanguage;
	}

	private void RefreshCaptureLanguage()
	{
		Title = I18n.T("ImageCaptureTitle");
		GuideText.Text = _guideKey != null ? I18n.T(_guideKey) : _guide ?? I18n.T("ImageCaptureOcrGuide");
		CancelText.Text = I18n.T("ImageCaptureCancel");
		SelectionHint.Text = I18n.T("ImageCaptureHint");
		if (IsLoaded) PositionGuide();
	}

	private void PositionGuide()
	{
		double available = Math.Max(1, ActualWidth - 32);
		GuideBadge.MaxWidth = available;
		GuideBadge.Measure(new System.Windows.Size(available, double.PositiveInfinity));
		Canvas.SetLeft(GuideBadge, Math.Max(16, (ActualWidth - GuideBadge.DesiredSize.Width) / 2));
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		Focus();
		CaptureMouse();
		double w = Math.Max(1.0, ActualWidth);
		double h = Math.Max(1.0, ActualHeight);
		ScreenGeometry.Rect = new Rect(0, 0, w, h);
		CutoutGeometry.Rect = Rect.Empty;
		PositionGuide();
	}

	private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			ReleaseMouseCapture();
			CleanupAndClose(null);
		}
	}

	private void Window_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		ReleaseMouseCapture();
		CleanupAndClose(null);
	}

	private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		_startPoint = e.GetPosition(this);
		_isSelecting = true;

		SelectionBorder.Visibility = Visibility.Visible;
		InfoBadge.Visibility = Visibility.Visible;

		Canvas.SetLeft(SelectionBorder, _startPoint.X);
		Canvas.SetTop(SelectionBorder, _startPoint.Y);
		SelectionBorder.Width = 0;
		SelectionBorder.Height = 0;

		CutoutGeometry.Rect = new Rect(_startPoint.X, _startPoint.Y, 0, 0);
	}

	private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (!_isSelecting)
		{
			return;
		}

		System.Windows.Point currentPoint = e.GetPosition(this);
		double x = Math.Min(_startPoint.X, currentPoint.X);
		double y = Math.Min(_startPoint.Y, currentPoint.Y);
		double w = Math.Abs(currentPoint.X - _startPoint.X);
		double h = Math.Abs(currentPoint.Y - _startPoint.Y);

		CutoutGeometry.Rect = new Rect(x, y, w, h);

		Canvas.SetLeft(SelectionBorder, x);
		Canvas.SetTop(SelectionBorder, y);
		SelectionBorder.Width = w;
		SelectionBorder.Height = h;

		double scaleX = _fullScreenBmp == null ? 1 : _fullScreenBmp.Width / Math.Max(1, ActualWidth);
		double scaleY = _fullScreenBmp == null ? 1 : _fullScreenBmp.Height / Math.Max(1, ActualHeight);
		SizeTextBlock.Text = I18n.TF("ImageCaptureSize", (int)Math.Round(w * scaleX), (int)Math.Round(h * scaleY));
		InfoBadge.MaxWidth = Math.Max(1, ActualWidth - 20);
		InfoBadge.Measure(new System.Windows.Size(InfoBadge.MaxWidth, double.PositiveInfinity));
		Canvas.SetLeft(InfoBadge, Math.Clamp(x, 10, Math.Max(10, ActualWidth - InfoBadge.DesiredSize.Width - 10)));
		double badgeY = y >= InfoBadge.DesiredSize.Height + 12 ? y - InfoBadge.DesiredSize.Height - 8 : y + h + 8;
		Canvas.SetTop(InfoBadge, Math.Clamp(badgeY, 10, Math.Max(10, ActualHeight - InfoBadge.DesiredSize.Height - 10)));
	}

	private void Window_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
	{
		if (!_isSelecting)
		{
			return;
		}
		_isSelecting = false;
		ReleaseMouseCapture();

		System.Windows.Point endPoint = e.GetPosition(this);
		double x = Math.Min(_startPoint.X, endPoint.X);
		double y = Math.Min(_startPoint.Y, endPoint.Y);
		double w = Math.Abs(endPoint.X - _startPoint.X);
		double h = Math.Abs(endPoint.Y - _startPoint.Y);

		Bitmap? capturedSnippet = null;

		if (w > 5 && h > 5 && _fullScreenBmp != null)
		{
			try
			{
				double actualW = Math.Max(1.0, ActualWidth);
				double actualH = Math.Max(1.0, ActualHeight);

				// 1:1 精确投影缩放比 (无论何种 DPI 缩放、单双屏分辨率均 100% 精确映射到物理大图)
				double ratioX = (double)_fullScreenBmp.Width / actualW;
				double ratioY = (double)_fullScreenBmp.Height / actualH;

				int cropX = (int)Math.Round(x * ratioX);
				int cropY = (int)Math.Round(y * ratioY);
				int cropW = (int)Math.Round(w * ratioX);
				int cropH = (int)Math.Round(h * ratioY);

				// 边界安全钳位
				cropX = Math.Clamp(cropX, 0, _fullScreenBmp.Width - 1);
				cropY = Math.Clamp(cropY, 0, _fullScreenBmp.Height - 1);
				cropW = Math.Clamp(cropW, 1, _fullScreenBmp.Width - cropX);
				cropH = Math.Clamp(cropH, 1, _fullScreenBmp.Height - cropY);

				if (cropW > 3 && cropH > 3)
				{
					CapturedScreenX = _screenX + cropX;
					CapturedScreenY = _screenY + cropY;
					SelectionTooLarge = (long)cropW * cropH > _maxSelectionPixels ||
						(_maxSelectionPixels != int.MaxValue && (cropW > 4096 || cropH > 4096));
					if (SelectionTooLarge) { CleanupAndClose(null); return; }
					// 直接从纯净全景物理快照中裁出选区，零位移、零残影、零透明通道黑化
					capturedSnippet = _fullScreenBmp.Clone(
						new System.Drawing.Rectangle(cropX, cropY, cropW, cropH),
						PixelFormat.Format24bppRgb);
				}
			}
			catch (Exception ex)
			{
				AppLogger.LogError("Failed to crop snippet rectangle", ex);
			}
		}

		CleanupAndClose(capturedSnippet);
	}

	private void CleanupAndClose(Bitmap? result)
	{
		_completing = true;
		ReleaseCaptureResources();

		try
		{
			Close();
			_onCaptured(result);
		}
		catch
		{
			// 回调未能接管选区位图时，由截屏窗口兜底释放。
			result?.Dispose();
			throw;
		}
	}

	private void ReleaseCaptureResources()
	{
		if (_resourcesReleased)
		{
			return;
		}

		_resourcesReleased = true;

		try { ReleaseMouseCapture(); } catch { }
		try { BackgroundImage.Source = null; } catch { }

		try
		{
			_fullScreenBmp?.Dispose();
		}
		catch
		{
		}
		finally
		{
			_fullScreenBmp = null;
		}

		try { CutoutGeometry.Rect = Rect.Empty; } catch { }
	}

	protected override void OnClosed(EventArgs e)
	{
		I18n.LanguageChanged -= RefreshCaptureLanguage;
		// Alt+F4、应用退出等非标准关闭路径同样必须释放全屏快照和 WPF 图像引用。
		ReleaseCaptureResources();
		base.OnClosed(e);
		if (!_completing)
		{
			_completing = true;
			_onCaptured(null);
		}
	}

	private static BitmapSource BitmapToBitmapSource(Bitmap bitmap)
	{
		IntPtr hBitmap = bitmap.GetHbitmap();
		try
		{
			BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
				hBitmap,
				IntPtr.Zero,
				Int32Rect.Empty,
				BitmapSizeOptions.FromEmptyOptions());
			source.Freeze();
			return source;
		}
		finally
		{
			DeleteObject(hBitmap);
		}
	}
}

