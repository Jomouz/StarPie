using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.CompilerServices;
using StarPie.Plugin;
using WinPieGestures;
using WinPieGestures.Plugins;

internal static class Program
{
    private static int _passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        _passed++; Console.WriteLine("PASS: " + name);
    }
    private static async Task Denied(Func<Task> action, PluginCapability capability, string name)
    {
        try { await action(); Check(false, name); }
        catch (PluginCapabilityDeniedException exception) { Check(exception.Capability == capability, name); }
    }
    private static async Task Main(string[] args)
    {
        string sandbox = Path.Combine(Path.GetTempPath(), "StarPie-qr-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        PluginPaths.OverrideRootsForTesting(Path.Combine(sandbox, "managed"), Path.Combine(sandbox, "scan"));
        Check(ImageCaptureGeometry.AroundPointer(-1900, -100, 400, new Rectangle(-1920, -200, 3840, 1280)) ==
            new Rectangle(-1920, -200, 420, 500), "Negative desktop origin and edge clipping");
        Check(ImageCaptureGeometry.AroundPointer(1919, 1079, 64, new Rectangle(0, 0, 1920, 1080)) ==
            new Rectangle(1855, 1015, 65, 65), "Bottom/right clipping");
        try { new CapturedImage(1, 1, 0, 0, new byte[3]); Check(false, "Length guard"); }
        catch (ArgumentException) { Check(true, "Length guard"); }
        try { new CapturedImage(4096, 4096, 0, 0, Array.Empty<byte>()); Check(false, "Pixel budget guard"); }
        catch (ArgumentException) { Check(true, "Pixel budget guard"); }
        using (var bitmap = new Bitmap(3, 2, PixelFormat.Format24bppRgb))
        {
            bitmap.SetPixel(0, 0, Color.Red); bitmap.SetPixel(2, 1, Color.Blue);
            CapturedImage image = PluginImageCaptureService.ToImage(bitmap, -5, 10);
            Check(image.Width == 3 && image.Height == 2 && image.ScreenX == -5 && image.ScreenY == 10, "Capture geometry retained");
            Check(image.Bgra32.Length == 24 && image.Bgra32[2] == 255 && image.Bgra32[20] == 255,
                "24-bit padded rows convert to tightly packed BGRA");
        }
        var capture = new PluginImageCaptureService("probe", PluginCapability.Ui);
        await Denied(() => capture.SelectRegionAsync(default), PluginCapability.ScreenCapture, "Selection requires ScreenCapture before UI");
        await Denied(() => capture.CaptureAroundPointerAsync(400, default), PluginCapability.ScreenCapture, "Pointer requires ScreenCapture before Win32");
        var results = new PluginTextResultService("probe", PluginCapability.None);
        await Denied(() => results.ShowAsync("", Array.Empty<TextResultItem>(), default), PluginCapability.Ui, "Results require Ui before UI");
        await Denied(() => results.CopyAsync("", default), PluginCapability.Clipboard, "Copy requires Clipboard before clipboard");
        await Denied(() => results.OpenWebUrlAsync("", default), PluginCapability.Process, "Open requires Process before shell");
        var permitted = new PluginTextResultService("probe", PluginCapability.Process);
        Check(!await permitted.OpenWebUrlAsync("file:///C:/Windows", default), "Invalid protocol rejected without opening");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { await new PluginImageCaptureService("probe", PluginCapability.ScreenCapture).SelectRegionAsync(cancellation.Token); Check(false, "Cancelled selection"); }
            catch (OperationCanceledException) { Check(true, "Cancelled selection exits before UI"); }
        }
        if (args.Length is < 1 or > 2) throw new ArgumentException("Pass the QR plugin output directory and optional SDK 1.11 fixture DLL.");
        WeakReference weak = LoadAndUnload(Path.GetFullPath(args[0]));
        for (int i = 0; i < 20 && weak.IsAlive; i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Task.Delay(100);
        }
        Check(!weak.IsAlive, "Real plugin + private ZXing ALC reclaimed");
        if (args.Length == 2)
        {
            WeakReference legacy = LoadAndUnloadLegacy(Path.GetFullPath(args[1]));
            for (int i = 0; i < 20 && legacy.IsAlive; i++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                await Task.Delay(100);
            }
            Check(!legacy.IsAlive, "SDK 1.11 binary plugin ALC reclaimed on the new host");
        }
        Console.WriteLine($"{_passed} assertions passed. Sandbox retained: {sandbox}. No live actions or GUI.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndUnload(string output)
    {
        PluginScanResult scan = PluginScanner.ScanInstalledPlugin(output, allowReservedIdPrefix: true);
        Check(scan.Accepted, "Static candidate scan: " + scan.DescribeFailure());
        Check(scan.Manifest!.ApiVersion == "1.12", "Manifest requires only the image-service SDK");
        var catalogModule = new OfficialPluginModule { Version = scan.Manifest.Version, ApiVersion = scan.Manifest.ApiVersion,
            TargetFramework = scan.Manifest.TargetFramework, MinHostVersion = scan.Manifest.MinHostVersion };
        var olderHost = new OfficialPluginEnvironment("1.8.0-beta.7", "1.11", "net8.0-windows10.0.19041.0");
        var minimumHost = olderHost with { ApiVersion = "1.12" };
        Check(OfficialPluginVersionSelector.Evaluate(catalogModule, olderHost).Kind == OfficialPluginCompatibilityKind.ApiUnsupported,
            "Catalog still rejects QR on SDK 1.11");
        Check(OfficialPluginVersionSelector.Evaluate(catalogModule, minimumHost).IsCompatible,
            "Catalog accepts the declared SDK 1.12 minimum");
        Check(OfficialPluginVersionSelector.Evaluate(catalogModule).IsCompatible,
            "Catalog accepts current SDK 1.13");
        var entry = new PluginRegistryEntry { Id = scan.Manifest.Id, Name = scan.Manifest.Name, Enabled = true, Official = true,
            ExternalPath = scan.DllPath, Source = "DeveloperPath" };
        var instance = new PluginInstance(entry.Id, entry, scan);
        Check(instance.Load(out string failure), "Real Load -> Initialize -> Commit -> gate: " + failure);
        Check(instance.ActionCount == 2 && PluginHost.Catalog.SnapshotActions().Count(a => a.PluginId == entry.Id) == 2,
            "Both actions committed");
        foreach (LanguageCode language in Enum.GetValues<LanguageCode>())
        {
            I18n.CurrentLanguage = language;
            string expectedName = language switch { LanguageCode.En => "QR Scanner", LanguageCode.ZhTw => "QR 碼掃描",
                LanguageCode.Ja => "QRコードスキャナー", _ => "二维码扫描" };
            string expectedAction = language switch { LanguageCode.En => "Scan a selected region", LanguageCode.ZhTw => "框選掃碼",
                LanguageCode.Ja => "範囲を選択してスキャン", _ => "框选扫码" };
            Check(PluginListItem.Build(instance).DisplayName == expectedName, "Plugin card translated without loading again");
            PluginDetailModel detail = PluginDetailModelBuilder.FromLocal(instance);
            Check(detail.DisplayName == expectedName && detail.Features[0].Name == expectedAction,
                "Plugin details and feature names translated from static manifest");
            PluginActionRegistration region = PluginHost.Catalog.SnapshotActions().Single(a => a.PluginId == entry.Id && a.ShortId == "scanRegion");
            Check(region.CurrentDisplayName == expectedAction && region.CurrentDescription != "scan.region.description",
                "Committed action labels resolve current language");
            var panel = PluginActionPanelText.Registered(region, expectedName);
            Check(panel.Title == "🔌 " + expectedAction && panel.Detail.Contains(region.CurrentDescription!),
                "Settings panel uses current action language");
            var fresh = new ActionItem { Name = "" };
            Check(PluginActionBinding.Apply(fresh, region.FullId) && fresh.Name == expectedAction,
                "New bindings use current language");
            var custom = new ActionItem { Name = "My custom name" };
            Check(PluginActionBinding.Apply(custom, region.FullId) && custom.Name == "My custom name",
                "Custom binding names preserved");
            PluginActionRegistration pointer = PluginHost.Catalog.SnapshotActions().Single(a => a.PluginId == entry.Id && a.ShortId == "scanPointer");
            Check(PluginActionBinding.Apply(fresh, pointer.FullId) && fresh.Name == pointer.CurrentDisplayName,
                "Current automatic name follows a newly chosen action");
        }
        I18n.CurrentLanguage = LanguageCode.ZhCn;
        Check(PluginHost.Catalog.SnapshotActions().Where(a => a.PluginId == entry.Id)
            .All(a => a.Kind == ActionKind.Background && a.TimeoutSeconds == 300),
            "Real registrations preserve the five-minute background timeout used by Invoker");
        Check(instance.TryAcquireInvocation(PluginCallKind.ActionExecution, out var lease, out _), "Real invocation gate opened");
        lease!.Dispose();
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        object context = typeof(PluginInstance).GetField("_pluginContext", fields)!.GetValue(instance)!;
        Check(context is IImageCapturePluginContext && context is ITextResultPluginContext, "Real context supplies optional services");
        Check(((ITextResultPluginContext)context).TextResults is IHostLocalizedTextResultService,
            "Current host advertises live translated result titles");
        var alc = (System.Runtime.Loader.AssemblyLoadContext)typeof(PluginInstance).GetField("_loadContext", fields)!.GetValue(instance)!;
        Assembly assembly = alc.Assemblies.Single(a => a.GetName().Name == "StarPie.Plugin.QrScanner");
        Type decoderType = assembly.GetType("StarPie.Plugin.QrScanner.QrDecoder")!;
        object decoder = Activator.CreateInstance(decoderType)!;
        decoderType.GetMethod("Decode", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(decoder,
            [new CapturedImage(32, 32, 0, 0, new byte[32 * 32 * 4]), CancellationToken.None]);
        Check(alc.Assemblies.Any(a => a.GetName().Name == "zxing"), "Decoder dependency resolves privately");
        var weak = new WeakReference(alc);
        instance.Unload();
        Check(!instance.TryAcquireInvocation(PluginCallKind.ActionExecution, out _, out _), "Stopping revokes invocation gate");
        Check(PluginHost.Catalog.SnapshotActions().All(a => a.PluginId != entry.Id), "Shutdown removes actions");
        return weak;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndUnloadLegacy(string dll)
    {
        PluginScanResult scan = PluginScanner.ScanSelectedDll(dll);
        Check(scan.Accepted, "Actual SDK 1.11 fixture passes static scan: " + scan.DescribeFailure());
        var entry = new PluginRegistryEntry { Id = "user.qrcompat.legacy", Name = "SDK 1.11 fixture", Enabled = true,
            ExternalPath = dll, Source = "DeveloperPath" };
        var instance = new PluginInstance(entry.Id, entry, scan);
        Check(instance.Load(out string failure), "SDK 1.11 plugin loads and initializes on the current host: " + failure);
        Check(instance.ActionCount == 1 && PluginHost.Catalog.SnapshotActions().Any(a => a.PluginId == entry.Id && a.ShortId == "noop"),
            "Old SDK action registration remains available without invoking it");
        var alc = (System.Runtime.Loader.AssemblyLoadContext)typeof(PluginInstance).GetField("_loadContext",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
        var weak = new WeakReference(alc);
        instance.Unload();
        Check(PluginHost.Catalog.SnapshotActions().All(a => a.PluginId != entry.Id),
            "Old SDK plugin shutdown removes its registration");
        return weak;
    }
}
