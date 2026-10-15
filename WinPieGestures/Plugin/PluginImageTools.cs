using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

internal static class ImageCaptureGeometry
{
    internal static Rectangle AroundPointer(int x, int y, int radius, Rectangle desktop)
    {
        if (radius is < 64 or > 1024) throw new ArgumentOutOfRangeException(nameof(radius));
        Rectangle area = Rectangle.Intersect(new Rectangle(checked(x - radius), checked(y - radius),
            radius * 2, radius * 2), desktop);
        if (area.Width < 1 || area.Height < 1) throw new InvalidOperationException("Pointer is outside the desktop.");
        return area;
    }
}

internal sealed class PluginImageCaptureService : PluginGatedService, IHostImageCaptureService
{
    // A single selector owns mouse capture; concurrent plugins must not stack overlays.
    private static readonly SemaphoreSlim CaptureGate = new(1, 1);

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }

    internal PluginImageCaptureService(string pluginId, PluginCapability capabilities)
        : base(pluginId, capabilities, PluginCapability.ScreenCapture, nameof(IHostImageCaptureService)) { }

    public async Task<CapturedImage?> SelectRegionAsync(CancellationToken cancellationToken)
    {
        RequireCapability();
        cancellationToken.ThrowIfCancellationRequested();
        if (!await CaptureGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Another capture is already active.");
        try
        {
            // The wheel must have left the compositor before the desktop is frozen.
            await Task.Delay(35, cancellationToken).ConfigureAwait(false);
            Dispatcher dispatcher = GetUiDispatcher();
            var completion = new TaskCompletionSource<Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
            ScreenSnipWindow? window = null;
            await dispatcher.InvokeAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                window = new ScreenSnipWindow(bitmap => completion.TrySetResult(bitmap),
                    maxSnapshotPixels: 12 * 1024 * 1024, maxSelectionPixels: CapturedImage.MaxPixels, guideKey: "ImageCaptureGuide");
                window.Show();
            });
            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
                dispatcher.BeginInvoke(new Action(() => window?.Close())));
            using Bitmap? bitmap = await completion.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (window!.SelectionTooLarge) throw new ArgumentException("Selected image is too large.");
            if (bitmap == null) return null;
            int x = window!.CapturedScreenX, y = window.CapturedScreenY;
            return await Task.Run(() => ToImage(bitmap, x, y), cancellationToken).ConfigureAwait(false);
        }
        finally { CaptureGate.Release(); }
    }

    public async Task<CapturedImage> CaptureAroundPointerAsync(int radius, CancellationToken cancellationToken)
    {
        RequireCapability();
        cancellationToken.ThrowIfCancellationRequested();
        if (radius is < 64 or > 1024) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!await CaptureGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Another capture is already active.");
        try
        {
            if (!GetCursorPos(out NativePoint point)) throw new InvalidOperationException("Cannot read pointer position.");
            var desktop = new Rectangle(GetSystemMetrics(76), GetSystemMetrics(77),
                GetSystemMetrics(78), GetSystemMetrics(79));
            Rectangle area = ImageCaptureGeometry.AroundPointer(point.X, point.Y, radius, desktop);
            await Task.Delay(35, cancellationToken).ConfigureAwait(false);
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(area.Location, System.Drawing.Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
                cancellationToken.ThrowIfCancellationRequested();
                return ToImage(bitmap, area.X, area.Y);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { CaptureGate.Release(); }
    }

    internal static CapturedImage ToImage(Bitmap bitmap, int x, int y)
    {
        if (bitmap.Width > CapturedImage.MaxDimension || bitmap.Height > CapturedImage.MaxDimension ||
            (long)bitmap.Width * bitmap.Height > CapturedImage.MaxPixels)
            throw new ArgumentException("Selected image is too large.");
        using Bitmap converted = bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), PixelFormat.Format32bppArgb);
        BitmapData data = converted.LockBits(new Rectangle(0, 0, converted.Width, converted.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = converted.Width * 4;
            byte[] pixels = new byte[rowBytes * converted.Height];
            for (int row = 0; row < converted.Height; row++)
                Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), pixels, row * rowBytes, rowBytes);
            return new CapturedImage(converted.Width, converted.Height, x, y, pixels);
        }
        finally { converted.UnlockBits(data); }
    }

    internal static Dispatcher GetUiDispatcher()
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
            throw new InvalidOperationException("Host UI is unavailable.");
        return dispatcher;
    }
}

internal sealed class PluginTextResultService : PluginGatedService, IHostLocalizedTextResultService
{
    private readonly string _pluginId;
    private readonly PluginCapability _capabilities;
    internal PluginTextResultService(string pluginId, PluginCapability capabilities)
        : base(pluginId, capabilities, PluginCapability.Ui, nameof(IHostTextResultService))
    {
        _pluginId = pluginId;
        _capabilities = capabilities;
    }

    public Task<bool> CopyAsync(string text, CancellationToken cancellationToken)
    {
        RequireOperation(PluginCapability.Clipboard);
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 8192) throw new ArgumentException("Text is too long.");
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Guard(nameof(CopyAsync), () => ActionExecutor.TrySetClipboardText(text));
        }, cancellationToken);
    }

    public Task<bool> OpenWebUrlAsync(string text, CancellationToken cancellationToken)
    {
        RequireOperation(PluginCapability.Process);
        if (!TextResultUrl.TryGetWebUrl(text, out string url)) return Task.FromResult(false);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Guard(nameof(OpenWebUrlAsync), () =>
            {
                ActionExecutor.ExecuteWebUrl(url, "Default", null, privateResult: true);
                return true;
            });
        }, cancellationToken);
    }

    private void RequireOperation(PluginCapability required)
    {
        if ((_capabilities & required) != required)
            throw new PluginCapabilityDeniedException(required, nameof(IHostTextResultService), _pluginId);
    }

    public Task<TextResultSelection> ShowAsync(string title, System.Collections.Generic.IReadOnlyList<TextResultItem> items,
        CancellationToken cancellationToken) => ShowCoreAsync(title, null, items, cancellationToken);

    public Task<TextResultSelection> ShowLocalizedAsync(string titleKey, System.Collections.Generic.IReadOnlyList<TextResultItem> items,
        CancellationToken cancellationToken) => ShowCoreAsync(I18n.T(titleKey), titleKey, items, cancellationToken);

    private async Task<TextResultSelection> ShowCoreAsync(string title, string? titleKey,
        System.Collections.Generic.IReadOnlyList<TextResultItem> items, CancellationToken cancellationToken)
    {
        RequireCapability();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(title);
        if (items.Count is < 1 or > 32 || title.Length > 256)
            throw new ArgumentException("Invalid text result collection.");
        var snapshot = new TextResultItem[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            TextResultItem item = items[i];
            if (item == null || item.Text == null || item.Text.Length > 8192 || item.Format == null || item.Format.Length > 128)
                throw new ArgumentException("Invalid text result.");
            snapshot[i] = new TextResultItem { Text = item.Text, Format = item.Format };
        }
        Dispatcher dispatcher = PluginImageCaptureService.GetUiDispatcher();
        var completion = new TaskCompletionSource<TextResultSelection>(TaskCreationOptions.RunContinuationsAsynchronously);
        TextResultWindow? window = null;
        await dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            window = new TextResultWindow(title, snapshot, titleKey);
            window.Closed += (_, _) => completion.TrySetResult(window.Selection);
            window.Show();
        });
        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            dispatcher.BeginInvoke(new Action(() => window?.Close())));
        TextResultSelection selection = await completion.Task.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return selection;
    }
}
