using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Reticle.Core;

namespace Reticle.App;

internal static class Diagnostics
{
    public static int Run(string outputPath)
    {
        var results = new Dictionary<string, object>();
        string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(directory);
        var settings = new CrosshairSettings();
        var overlay = new OverlayWindow(settings);
        try
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            overlay.SetVisible(true);
            Pump();
            int style = NativeMethods.GetWindowLong(overlay.Handle, NativeMethods.GwlExStyle);
            Check("mouseTransparent", (style & NativeMethods.Transparent) != 0);
            Check("nonActivating", (style & NativeMethods.NoActivate) != 0);
            Check("layered", (style & NativeMethods.Layered) != 0);
            Check("topmost", (style & 8) != 0);
            Check("noFocusSteal", NativeMethods.GetForegroundWindow() == foreground);
            var bounds = overlay.SelectedScreen.Bounds;
            NativeMethods.GetWindowRect(overlay.Handle, out var rect);
            Check("centered", rect.Left + (rect.Right - rect.Left) / 2 == bounds.Left + bounds.Width / 2 && rect.Top + (rect.Bottom - rect.Top) / 2 == bounds.Top + bounds.Height / 2);
            settings.OffsetX = 37; settings.OffsetY = -23; overlay.Position();
            NativeMethods.GetWindowRect(overlay.Handle, out var shifted);
            Check("positionOffsets", shifted.Left - rect.Left == 37 && shifted.Top - rect.Top == -23);
            overlay.SetVisible(false);
            Check("hidden", !NativeMethods.IsWindowVisible(overlay.Handle));
            overlay.SetVisible(true);
            Check("shownAgain", NativeMethods.IsWindowVisible(overlay.Handle));
            var neverShown = new OverlayWindow(new());
            neverShown.Close();
            Check("neverShownOverlayClosesSafely", neverShown.Handle == IntPtr.Zero);
            CheckOverlayRecovery(overlay, Check);
            bool registered = NativeMethods.RegisterHotKey(overlay.Handle, 99, 0x4003, 0x48);
            results["hotkeyAvailable"] = registered;
            if (registered) NativeMethods.UnregisterHotKey(overlay.Handle, 99);
            overlay.Hide();

            OpacityDiagnostics.Run(directory, Check);
            foreach (var shape in Enum.GetValues<CrosshairShape>())
            {
                settings = new CrosshairSettings { Shape = shape, Outline = false };
                var view = new CrosshairView { Settings = settings, Width = 128, Height = 128 };
                view.Measure(new Size(128, 128)); view.Arrange(new Rect(0, 0, 128, 128)); view.UpdateLayout();
                var bitmap = Render(view, 128, 128);
                byte[] pixels = new byte[128 * 128 * 4]; bitmap.CopyPixels(pixels, 128 * 4, 0);
                int count = Enumerable.Range(0, 128 * 128).Count(i => pixels[i * 4 + 3] > 0);
                Check(shape + "VisiblePixels", count > 20);
                Check(shape + "OpenCenter", pixels[(64 * 128 + 64) * 4 + 3] == 0);
                WritePng(bitmap, Path.Combine(directory, shape + ".png"));
                settings.Opacity = 0; view.InvalidateVisual(); view.UpdateLayout();
                bitmap = Render(view, 128, 128); bitmap.CopyPixels(pixels, 128 * 4, 0);
                Check(shape + "ZeroOpacity", pixels.All(x => x == 0));
            }
            // Exercise the real editor layout through WPF without driving the user's desktop.
            var editorStore = new SettingsStore(Path.Combine(directory, "test-editor-settings.json"));
            editorStore.Save(new());
            var editor = new MainWindow(editorStore);
            editor.ShowActivated = false; editor.Show(); Pump(); editor.UpdateLayout();
            WritePng(Render(editor, (int)editor.ActualWidth, (int)editor.ActualHeight), Path.Combine(directory, "editor.png"));
            Check("editorLayout", editor.ActualWidth >= 950 && editor.ActualHeight >= 680);
            CheckTrayRestore(editor, directory, Check);
            CheckTrayExitWithoutDesktopOverlay(directory, Check);
            CheckIndependentStartup(directory, Check);
            CheckUpdateWindow(editor, directory, Check);
            var model = (CrosshairSettings)editor.DataContext;
            ((System.Windows.Controls.Button)editor.FindName("CircleButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump();
            Check("circleButtonChangesShape", model.Shape == CrosshairShape.Circle);
            Check("circleShowsRadius", ((UIElement)editor.FindName("CircleControls")).Visibility == Visibility.Visible && ((UIElement)editor.FindName("ArmsControls")).Visibility == Visibility.Collapsed);
            var colorInput = (System.Windows.Controls.TextBox)editor.FindName("ColorInput");
            colorInput.Text = "#FF7AA2";
            Check("colorInputUpdatesModel", model.Color == "#FF7AA2");
            colorInput.Text = "#ZZZZZZ";
            Check("invalidColorPreservesPrevious", model.Color == "#FF7AA2" && ((UIElement)editor.FindName("ColorError")).Visibility == Visibility.Visible);
            colorInput.Text = "#FF7AA2";
            var palette = (ColorPalette)editor.FindName("Palette");
            var expander = (System.Windows.Controls.Expander)editor.FindName("PaletteExpander");
            var hue = (System.Windows.Controls.Slider)palette.FindName("HueSlider");
            var saturation = (System.Windows.Controls.Slider)palette.FindName("SaturationSlider");
            var brightness = (System.Windows.Controls.Slider)palette.FindName("BrightnessSlider");
            var surface = (FrameworkElement)palette.FindName("ColorSurface");
            Check("paletteTracksHexInput", palette.SelectedColor == "#FF7AA2");
            expander.IsExpanded = true; Pump(); editor.UpdateLayout();
            hue.Value = 240; saturation.Value = 100; brightness.Value = 100;
            Check("paletteUpdatesHexAndModel", model.Color == "#0000FF" && colorInput.Text == "#0000FF");
            brightness.Value = 0; hue.Value = 120;
            Check("paletteBlackRetainsHue", model.Color == "#000000" && hue.Value == 120);
            brightness.Value = 100;
            Check("paletteRestoresHueFromBlack", model.Color == "#00FF00");
            saturation.Value = 0; hue.Value = 300;
            Check("paletteGrayRetainsHue", model.Color == "#FFFFFF" && hue.Value == 300);
            saturation.Value = 100;
            Check("paletteRestoresHueFromGray", model.Color == "#FF00FF");
            palette.SelectAt(new Point(surface.ActualWidth / 2, surface.ActualHeight / 2));
            Check("paletteSurfaceSelectsColor", model.Color == "#804080" && colorInput.Text == "#804080");
            palette.SelectAt(new Point(-10, -10));
            Check("paletteClampsOutsideTopLeft", model.Color == "#FFFFFF");
            palette.SelectAt(new Point(surface.ActualWidth + 10, surface.ActualHeight + 10));
            Check("paletteClampsOutsideBottomRight", model.Color == "#000000");
            colorInput.Text = "#3366CC";
            Check("hexUpdatesPaletteSliders", Math.Abs(hue.Value - 220) < 0.01 && Math.Abs(saturation.Value - 75) < 0.01 && Math.Abs(brightness.Value - 80) < 0.01);
            colorInput.Text = "#ZZZZZZ";
            Check("invalidHexDoesNotChangePalette", palette.SelectedColor == "#3366CC");
            model.Apply(CrosshairSettings.Preset("diagonal"), false);
            Check("presetUpdatesPalette", palette.SelectedColor == "#71C7FF" && colorInput.Text == "#71C7FF");
            hue.Value = 280; saturation.Value = 75; brightness.Value = 90;
            string paletteColor = model.Color;
            var imported = SettingsStore.Parse(JsonSerializer.Serialize(model, SettingsStore.JsonOptions));
            Check("paletteColorExportImport", imported.Color == paletteColor);
            Pump(); editor.UpdateLayout(); expander.BringIntoView(); Pump();
            WritePng(Render(editor, (int)editor.ActualWidth, (int)editor.ActualHeight), Path.Combine(directory, "editor-palette.png"));
            editor.Width = 950; editor.Height = 680; Pump(); editor.UpdateLayout(); expander.BringIntoView(); Pump();
            Check("paletteFitsNarrowWindow", surface.ActualWidth > 200 && palette.ActualWidth <= ((System.Windows.Controls.ScrollViewer)editor.FindName("SettingsScroller")).ViewportWidth);
            WritePng(Render(editor, (int)editor.ActualWidth, (int)editor.ActualHeight), Path.Combine(directory, "editor-palette-small.png"));
            editor.Width = 1120; editor.Height = 810;
            expander.IsExpanded = false;
            ((System.Windows.Controls.ScrollViewer)editor.FindName("SettingsScroller")).ScrollToTop();
            model.Shape = CrosshairShape.Circle;
            colorInput.Text = "#FF7AA2";
            model.CenterDot = true;
            Pump(); editor.UpdateLayout();
            WritePng(Render(editor, (int)editor.ActualWidth, (int)editor.ActualHeight), Path.Combine(directory, "editor-circle.png"));
            var toggle = (System.Windows.Controls.Button)editor.FindName("ToggleButton");
            toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check("toggleButtonEnablesOverlay", toggle.Content.ToString() == "关闭桌面准星");
            toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check("toggleButtonDisablesOverlay", toggle.Content.ToString() == "开启桌面准星");
            var modeBox = (System.Windows.Controls.ComboBox)editor.FindName("OverlayModeBox");
            modeBox.SelectedIndex = 1;
            Check("gameBarModeHasSeparateToggle", toggle.Content.ToString() == "开启全屏组件");
            Check("gameBarMonitorManagedByWidget", !((System.Windows.Controls.ComboBox)editor.FindName("MonitorBox")).IsEnabled);
            Check("gameBarPackageIdentityResolved", GameBarBridge.PackageFamilyName.StartsWith("ReticleStudio.GameBar_", StringComparison.Ordinal));
            modeBox.SelectedIndex = 2;
            Check("independentModeHasSeparateToggle", toggle.Content.ToString() == "开启独立准星");
            Check("independentModeSelectsMonitor", ((System.Windows.Controls.ComboBox)editor.FindName("MonitorBox")).IsEnabled);
            Check("independentModeDoesNotNeedGameBar", ((System.Windows.Controls.TextBlock)editor.FindName("ModeHint")).Text.Contains("无需 Game Bar"));
            Check("independentModePreferenceSaved", File.ReadAllText(editorStore.Path + ".display") == "2");
            if (!File.Exists(IndependentOverlayBridge.HostPath))
            {
                toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump();
                Check("missingIndependentHostFailsWithoutCrash", toggle.Content.ToString() == "开启独立准星" && toggle.IsEnabled &&
                    ((System.Windows.Controls.TextBlock)editor.FindName("OverlayStatus")).Text.Contains("安装"));
            }
            modeBox.SelectedIndex = 0;
            Check("switchingBackKeepsOverlayDisabled", toggle.Content.ToString() == "开启桌面准星");
            editor.Width = 950; editor.Height = 680; Pump(); editor.UpdateLayout();
            WritePng(Render(editor, (int)editor.ActualWidth, (int)editor.ActualHeight), Path.Combine(directory, "editor-small.png"));
            var togglePosition = toggle.TransformToAncestor(editor).Transform(new Point(0, toggle.ActualHeight));
            Check("smallWindowButtonFits", togglePosition.Y < editor.ActualHeight - 35);
            var updateButton = (FrameworkElement)editor.FindName("UpdateButton");
            var updatePosition = updateButton.TransformToAncestor(editor).Transform(new Point(updateButton.ActualWidth, updateButton.ActualHeight));
            Check("smallWindowUpdateButtonFits", updateButton.ActualWidth > 60 && updatePosition.X < editor.ActualWidth - 20 && updatePosition.Y < 120);
            ((System.Windows.Controls.ScrollViewer)editor.FindName("SettingsScroller")).ScrollToBottom();
            Pump(); editor.UpdateLayout();
            WritePng(Render(editor, (int)editor.ActualWidth, (int)editor.ActualHeight), Path.Combine(directory, "editor-position.png"));
            editor.Close();
            var restored = editorStore.Load(out var loadWarning);
            Check("editorSavesOnClose", loadWarning is null && restored.Shape == CrosshairShape.Circle && restored.Color == "#FF7AA2" && restored.CenterDot);
            results["passed"] = true;
        }
        catch (Exception e) { results["passed"] = false; results["error"] = e.ToString(); }
        finally
        {
            overlay.Close();
            File.WriteAllText(outputPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }
        return results.TryGetValue("passed", out var passed) && passed is true ? 0 : 1;

        void Check(string name, bool pass)
        {
            results[name] = pass;
            if (!pass) throw new InvalidOperationException("Diagnostic failed: " + name);
        }
    }

    private static void CheckTrayRestore(MainWindow editor, string directory, Action<string, bool> check)
    {
        // Production editors use ShowActivated=true; WPF rejects showing a maximized
        // window with the non-activating flag used by the earlier rendering checks.
        editor.ShowActivated = true;
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var tray = (System.Windows.Forms.NotifyIcon)typeof(MainWindow).GetField("tray", flags)!.GetValue(editor)!;
        var menu = (System.Windows.Controls.ContextMenu)typeof(MainWindow).GetField("trayMenu", flags)!.GetValue(editor)!;
        var mouseClick = typeof(System.Windows.Forms.NotifyIcon).GetMethod("OnMouseClick", flags)!;
        void Click(System.Windows.Forms.MouseButtons button) => mouseClick.Invoke(tray, [new System.Windows.Forms.MouseEventArgs(button, 1, 0, 0, 0)]);
        editor.Hide();
        var nativeWindow = (System.Windows.Forms.NativeWindow)typeof(System.Windows.Forms.NotifyIcon).GetField("_window", flags)!.GetValue(tray)!;
        var wndProc = typeof(System.Windows.Forms.NotifyIcon).GetMethod("WndProc", flags)!;
        void RightClick()
        {
            // Follow NotifyIcon's actual native callback path, not just OnMouseClick.
            foreach (int message in new[] { 0x204, 0x205 })
                wndProc.Invoke(tray, [System.Windows.Forms.Message.Create(nativeWindow.Handle, 0x800, IntPtr.Zero, new IntPtr(message))]);
            Pump();
        }
        var commands = menu.Items.OfType<System.Windows.Controls.MenuItem>().ToArray();
        try
        {
            RightClick();
            check("trayNativeRightClickOpensMenu", menu.IsOpen && menu.IsVisible);
            check("trayMenuContainsAllCommands", commands.Select(item => (string)item.Header).SequenceEqual(new[] { "显示 / 隐藏准星", "显示设置", "检查更新", "退出" }));
            check("trayMenuOpeningDoesNotRunCommand", tray.Visible && !editor.IsVisible && ((System.Windows.Controls.Button)editor.FindName("ToggleButton")).Content.ToString() == "开启桌面准星");
            menu.UpdateLayout();
            WritePng(Render(menu, (int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight)), Path.Combine(directory, "tray-menu.png"));
            // The release that opened a popup (or another right click) must not invoke Exit.
            var rightRelease = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Right)
            { RoutedEvent = UIElement.PreviewMouseUpEvent };
            commands[^1].RaiseEvent(rightRelease); Pump();
            check("trayMenuIgnoresRightReleaseOverExit", rightRelease.Handled && menu.IsOpen && tray.Visible);
            menu.IsOpen = false; Pump();
            check("trayMenuDismissKeepsEditorRunning", tray.Visible && !editor.IsVisible);
            RightClick();
            check("trayMenuCanReopenAfterDismiss", menu.IsOpen && menu.IsVisible);
            commands[1].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); Pump();
            check("trayMenuSettingsRestoresEditor", editor.IsVisible && !menu.IsOpen);
            editor.Hide(); RightClick();
            commands[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); Pump();
            check("trayMenuToggleEnablesOverlayWithoutOpeningSettings", !editor.IsVisible && ((System.Windows.Controls.Button)editor.FindName("ToggleButton")).Content.ToString() == "关闭桌面准星");
            RightClick();
            commands[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); Pump();
            check("trayMenuToggleDisablesOverlay", ((System.Windows.Controls.Button)editor.FindName("ToggleButton")).Content.ToString() == "开启桌面准星");
            RightClick();
            commands[2].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); Pump();
            var updates = editor.OwnedWindows.OfType<UpdateWindow>().SingleOrDefault();
            check("trayMenuOpensUpdates", editor.IsVisible && updates?.IsVisible == true && !menu.IsOpen);
            updates?.Close();
            editor.Hide();
            Click(System.Windows.Forms.MouseButtons.Right); Pump();
            check("trayRightClickDoesNotRestore", !editor.IsVisible);
            menu.IsOpen = false; Pump();
            Click(System.Windows.Forms.MouseButtons.Left); Pump();
            check("traySingleLeftClickRestores", editor.IsVisible && editor.WindowState == WindowState.Normal);
            editor.WindowState = WindowState.Minimized; Pump();
            Click(System.Windows.Forms.MouseButtons.Left); Pump();
            check("trayClickRestoresMinimizedWindow", editor.IsVisible && editor.WindowState == WindowState.Normal);
            editor.WindowState = WindowState.Maximized; editor.Hide();
            Click(System.Windows.Forms.MouseButtons.Left); Pump();
            check("trayRestorePreservesMaximizedState", editor.IsVisible && editor.WindowState == WindowState.Maximized);
            editor.WindowState = WindowState.Normal; Pump();
            check("trayHasUpdateEntry", commands.Any(item => (string)item.Header == "检查更新"));
            }
        finally { menu.IsOpen = false; }
    }

    private static void CheckTrayExitWithoutDesktopOverlay(string directory, Action<string, bool> check)
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        // Fresh sessions and Game Bar-only sessions never create the desktop overlay HWND.
        foreach (bool gameBarMode in new[] { false, true })
        {
            var editor = new MainWindow(new SettingsStore(Path.Combine(directory, "test-tray-exit-settings.json")));
            try
            {
                editor.Show(); Pump();
                if (gameBarMode) ((System.Windows.Controls.ComboBox)editor.FindName("OverlayModeBox")).SelectedIndex = 1;
                editor.Hide();
                bool closed = false;
                editor.Closed += (_, _) => closed = true;
                var menu = (System.Windows.Controls.ContextMenu)typeof(MainWindow).GetField("trayMenu", flags)!.GetValue(editor)!;
                var tray = (System.Windows.Forms.NotifyIcon)typeof(MainWindow).GetField("tray", flags)!.GetValue(editor)!;
                typeof(System.Windows.Forms.NotifyIcon).GetMethod("OnMouseClick", flags)!.Invoke(tray, [new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Right, 1, 0, 0, 0)]);
                Pump();
                check(gameBarMode ? "gameBarOnlySessionRightClickStaysOpen" : "freshSessionRightClickStaysOpen", menu.IsOpen && !closed && tray.Visible);
                menu.Items.OfType<System.Windows.Controls.MenuItem>().Single(item => (string)item.Header == "退出")
                    .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); Pump();
                check(gameBarMode ? "trayExitClosesGameBarOnlySessionSafely" : "trayExitClosesFreshSessionSafely", closed && !tray.Visible && !menu.IsOpen);
            }
            finally { editor.Close(); }
        }
    }

    private static void CheckIndependentStartup(string directory, Action<string, bool> check)
    {
        var editor = new MainWindow(new SettingsStore(Path.Combine(directory, "test-independent-startup-settings.json")), preferIndependent: true);
        try
        {
            editor.ShowActivated = false; editor.Show(); Pump();
            check("independentShortcutSelectsIndependentMode", ((System.Windows.Controls.ComboBox)editor.FindName("OverlayModeBox")).SelectedIndex == 2);
            check("independentStartupDoesNotEnableWithoutConsent", ((System.Windows.Controls.Button)editor.FindName("ToggleButton")).Content.ToString() == "开启独立准星");
            check("independentEditionHasDistinctWindowTitle", editor.Title.Contains("独立覆盖版"));
        }
        finally { editor.Close(); }
    }

    private static void CheckUpdateWindow(MainWindow owner, string directory, Action<string, bool> check)
    {
        var handler = new DiagnosticUpdateHandler { Version = AppVersion.IsIndependent ? "99.0.0-independent.1" : "99.0.0" };
        using var client = new System.Net.Http.HttpClient(handler);
        var window = new UpdateWindow(new UpdateChecker(client, AppVersion.IsIndependent ? ReleaseChannel.Independent : ReleaseChannel.Standard)) { Owner = owner };
        try
        {
            window.Show(); Pump(); window.UpdateLayout();
            var status = (System.Windows.Controls.TextBlock)window.FindName("StatusText");
            var button = (System.Windows.Controls.Button)window.FindName("CheckButton");
            var download = (System.Windows.Controls.Button)window.FindName("DownloadButton");
            check("updateDialogFindsNewVersion", WaitUntil(() => button.IsEnabled) && status.Text.Contains("99.0.0") && download.Content.ToString() == "前往下载");
            WritePng(Render(window, (int)window.ActualWidth, (int)window.ActualHeight), Path.Combine(directory, "update-available.png"));
            handler.Version = AppVersion.Current;
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            check("updateDialogHandlesCurrentVersion", WaitUntil(() => button.IsEnabled) && status.Text == "当前已是最新版本" && download.Content.ToString() == "打开发布页");
            handler.Status = System.Net.HttpStatusCode.ServiceUnavailable;
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            check("updateDialogHandlesFailureAndAllowsRetry", WaitUntil(() => button.IsEnabled) && status.Text == "暂时无法检查更新" && download.IsEnabled);
            WritePng(Render(window, (int)window.ActualWidth, (int)window.ActualHeight), Path.Combine(directory, "update-failed.png"));
            handler.Status = System.Net.HttpStatusCode.OK;
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            check("updateDialogRetryRecovers", WaitUntil(() => button.IsEnabled) && status.Text == "当前已是最新版本");
        }
        finally { window.Close(); }
    }

    private sealed class DiagnosticUpdateHandler : System.Net.Http.HttpMessageHandler
    {
        public string Version { get; set; } = "99.0.0";
        public System.Net.HttpStatusCode Status { get; set; } = System.Net.HttpStatusCode.OK;
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new System.Net.Http.HttpResponseMessage(Status)
            {
                Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(new[] { new { tag_name = "v" + Version, draft = false, prerelease = false } }))
            });
    }

    private static void CheckOverlayRecovery(OverlayWindow overlay, Action<string, bool> check)
    {
        // A real borderless, topmost HWND reproduces the desktop z-order failure.
        // This is not an exclusive-fullscreen DirectX or CS compatibility test.
        var game = new Window
        {
            Title = "Reticle diagnostics · fullscreen window", WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, Background = Brushes.Black,
            ShowInTaskbar = false, ShowActivated = false, Topmost = true
        };
        try
        {
            game.Show();
            IntPtr gameHandle = new WindowInteropHelper(game).Handle;
            var bounds = overlay.SelectedScreen.Bounds;
            check("fullscreenTestWindowPositioned", NativeMethods.SetWindowPos(gameHandle, new IntPtr(-1),
                bounds.Left, bounds.Top, bounds.Width, bounds.Height, NativeMethods.NoActivatePosition));
            check("fullscreenWindowInitiallyCoversOverlay", IsAbove(gameHandle, overlay.Handle));
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            check("overlayRecoversAboveFullscreenWindow", WaitUntil(() => IsAbove(overlay.Handle, gameHandle)));
            check("fullscreenRecoveryDoesNotStealFocus", NativeMethods.GetForegroundWindow() == foreground);

            // Losing the TOPMOST flag and having the HWND moved/resized must also recover.
            NativeMethods.GetWindowRect(overlay.Handle, out var expected);
            check("overlayDisplacedForTest", NativeMethods.SetWindowPos(overlay.Handle, new IntPtr(-2),
                expected.Left + 81, expected.Top + 49, 240, 240, NativeMethods.NoActivatePosition));
            check("overlayRecoversPositionAndTopmost", WaitUntil(() =>
            {
                NativeMethods.GetWindowRect(overlay.Handle, out var current);
                return current.Equals(expected) && IsAbove(overlay.Handle, gameHandle)
                    && (NativeMethods.GetWindowLong(overlay.Handle, NativeMethods.GwlExStyle) & 8) != 0;
            }));
            check("positionRecoveryDoesNotStealFocus", NativeMethods.GetForegroundWindow() == foreground);

            overlay.SetVisible(false);
            check("hiddenOverlayStaysHidden", !WaitUntil(() => NativeMethods.IsWindowVisible(overlay.Handle), 1100));
            overlay.SetVisible(true);
            NativeMethods.SetWindowPos(gameHandle, new IntPtr(-1), bounds.Left, bounds.Top,
                bounds.Width, bounds.Height, NativeMethods.NoActivatePosition);
            check("overlayRecoveryRestartsAfterShow", WaitUntil(() => IsAbove(overlay.Handle, gameHandle)));
        }
        finally { game.Close(); }

        static bool IsAbove(IntPtr upper, IntPtr lower)
        {
            // GW_HWNDPREV walks towards the top; cap the walk if other apps reorder windows.
            var current = lower;
            for (int i = 0; i < 4096 && current != IntPtr.Zero; i++)
            {
                current = NativeMethods.GetWindow(current, 3);
                if (current == upper) return true;
            }
            return false;
        }
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMilliseconds = 3000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            Pump();
            if (condition()) return true;
            Thread.Sleep(10);
        }
        return condition();
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static RenderTargetBitmap Render(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); return bitmap;
    }
    private static void WritePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
