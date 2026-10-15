using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StarPie.Plugin;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static class Program
{
    private static int _passed;
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + name);
        _passed++;
    }
    private static TextResultItem[] Items(bool multiple) => multiple ?
        [new() { Text = "https://example.com/articles/qr?language=en", Format = "QR Code" },
         new() { Text = "StarPie 中文測試 日本語\nLocal text, copied only after choosing Copy content.", Format = "QR Code" }] :
        [new() { Text = "https://example.com/articles/qr?language=en", Format = "QR Code" }];

    [STAThread]
    private static void Main(string[] args)
    {
        string output = args.Length == 1 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "StarPie-qr-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (string theme in new[] { "Light", "ObsidianDark", "TitaniumGray" })
        foreach (LanguageCode language in new[] { LanguageCode.ZhCn, LanguageCode.ZhTw, LanguageCode.En, LanguageCode.Ja })
        foreach (bool multiple in new[] { false, true })
        {
            ConfigManager.CurrentConfig = new AppConfig { AppTheme = theme, SelectedArtStyleId = "" };
            I18n.CurrentLanguage = language;
            int before = LanguageSubscriptionCount();
            var window = new TextResultWindow(I18n.T("TextResultListHeading"), Items(multiple), "TextResultListHeading");
            int width = multiple ? 820 : 640;
            window.ApplyLayout(width);
            Render(window, width, 520, Path.Combine(output, $"{theme}-{language}-{(multiple ? "multi" : "single")}.png"));
            Check(!window.IsVisible, "Never shown");
            Check(window.ResultSidebar.Visibility == (multiple ? Visibility.Visible : Visibility.Collapsed), "Single/multiple layout");
            Check(window.ResultContent.Text == Items(multiple)[0].Text && window.OpenButton.IsEnabled, "Selected payload and link action");
            Check(window.FormatText.Text == I18n.T("TextResultFormatQr"), "Localized format");
            Check(window.CopyButton.Content?.ToString() == I18n.T("TextResultCopy"), "Localized copy");
            Check(window.ResultContent.ActualHeight >= 60, "Content remains readable");
            Check(Contrast(window, "TextPrimaryBrush", "InputBackgroundBrush") >= 4.5, "Primary content contrast");
            Check(Contrast(window, "TextSecondaryBrush", "WindowBackgroundBrush") >= 4.5, "Secondary contrast");
            Check(Contrast(window.CopyButton.Foreground, window.CopyButton.Background) >= 4.5, "Primary button contrast");
            window.ApplyLayout(window.MinWidth);
            Render(window, (int)window.MinWidth, (int)window.MinHeight,
                Path.Combine(output, $"{theme}-{language}-{(multiple ? "multi" : "single")}-minimum.png"));
            Check(window.ResultContent.ActualHeight >= 30, "Declared minimum keeps readable content");
            if (multiple)
            {
                window.ApplyLayout(480);
                Render(window, 480, 420, Path.Combine(output, $"{theme}-{language}-narrow.png"));
                Check(window.ResultSelector.Visibility == Visibility.Visible && window.ResultSidebar.Visibility == Visibility.Collapsed,
                    "Narrow mode uses selector instead of sidebars");
                Check(window.ResultContent.ActualHeight >= 30, "Narrow content remains readable");
                window.ResultSelector.SelectedIndex = 1;
                Check(window.ResultList.SelectedIndex == 1 && !window.OpenButton.IsEnabled && window.ResultContent.Text == Items(true)[1].Text,
                    "Narrow selector synchronizes selected result");
                LanguageCode next = language == LanguageCode.En ? LanguageCode.Ja : LanguageCode.En;
                I18n.CurrentLanguage = next;
                Check(window.Title == I18n.T("TextResultListHeading") && window.FormatText.Text == I18n.T("TextResultFormatQr") &&
                    window.ResultList.SelectedIndex == 1 && window.ResultContent.Text == Items(true)[1].Text,
                    "Hot language switch preserves selected payload");
                I18n.CurrentLanguage = language;
            }
            window.Close();
            Check(LanguageSubscriptionCount() == before, "Closed window releases language subscriptions");
        }
        I18n.CurrentLanguage = LanguageCode.En;
        var copy = new TextResultWindow("test", Items(false));
        copy.CopyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(copy.Selection.Index == 0 && copy.Selection.Operation == TextResultOperation.Copy, "Copy returns choice only");
        var close = new TextResultWindow("test", Items(false));
        close.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(close.Selection.Operation == TextResultOperation.Close && close.Selection.Index == -1, "Close returns no operation");
        foreach (LanguageCode language in Enum.GetValues<LanguageCode>())
        {
            I18n.CurrentLanguage = language;
            string expected = language switch { LanguageCode.En => "QR Scanner", LanguageCode.Ja => "QRコードスキャナー",
                LanguageCode.ZhTw => "QR 碼掃描", _ => "二维码扫描" };
            var translations = new Dictionary<string, string> { ["zh-CN"] = "二维码扫描", ["zh-TW"] = "QR 碼掃描",
                ["en-US"] = "QR Scanner", ["ja-JP"] = "QRコードスキャナー" };
            Check(PluginDisplayText.Resolve(translations, "fallback") == expected, "Static translations accept locale aliases");
            Check(PluginDisplayText.Resolve(null, "legacy name") == "legacy name", "Legacy labels preserved");
        }
        application.Shutdown();
        Console.WriteLine($"{_passed} presentation assertions passed. Rendered previews: {output}. No windows shown, screen capture, clipboard or browser.");
    }

    private static void Render(TextResultWindow window, int width, int height, string file)
    {
        FrameworkElement view = (FrameworkElement)window.Content;
        view.Width = width; view.Height = height;
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using FileStream stream = File.Create(file);
        encoder.Save(stream);
    }

    private static int LanguageSubscriptionCount() => ((Delegate?)typeof(I18n)
        .GetField("LanguageChanged", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null))?.GetInvocationList().Length ?? 0;

    private static double Contrast(Window window, string foreground, string background)
        => Contrast((Brush)window.Resources[foreground], (Brush)window.Resources[background]);

    private static double Contrast(Brush foreground, Brush background)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte value) { double c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        double left = Luminance(((SolidColorBrush)foreground).Color);
        double right = Luminance(((SolidColorBrush)background).Color);
        return (Math.Max(left, right) + .05) / (Math.Min(left, right) + .05);
    }
}
