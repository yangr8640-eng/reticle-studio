using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Gaming.XboxGameBar;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Reticle.GameBar
{
    public sealed partial class WidgetPage : Page, IDisposable
    {
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly WidgetDiagnostics diagnostics = new WidgetDiagnostics();
        private XboxGameBarWidget widget;
        private DisplayInformation display;
        private CrosshairModel settings = new CrosshairModel();
        private bool reading, resizing, disposed, paused, enabled = true, hasLiveState, hasSeenLiveFile, lastSync = true;
        private DateTimeOffset lastUpdate;
        private string previousText;
        private Size? lastRequestedSize;
        private double DpiScale { get { return Math.Max(0.5, display == null ? 1 : display.RawPixelsPerViewPixel); } }

        public WidgetPage()
        {
            InitializeComponent();
            timer.Tick += Timer_Tick;
            Reticle.SizeChanged += Reticle_SizeChanged;
            Loaded += Page_Loaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs args)
        {
            widget = (XboxGameBarWidget)args.Parameter;
            display = DisplayInformation.GetForCurrentView();
            display.DpiChanged += Display_DpiChanged;
            widget.GameBarDisplayModeChanged += Widget_Changed;
            widget.PinnedChanged += Widget_Changed;
            widget.ClickThroughEnabledChanged += Widget_Changed;
            widget.RequestedOpacityChanged += Widget_Changed;
            widget.WindowBoundsChanged += Widget_BoundsChanged;
            widget.VisibleChanged += Widget_Changed;
            widget.HorizontalResizeSupported = false;
            widget.VerticalResizeSupported = false;
            widget.PinningSupported = true;
            lastSync = ConfigurationReader.UseDesktopSync;
            timer.Start();
            UpdateInstructions();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs args)
        {
            await RefreshAsync();
            await ResizeAndCenterAsync(true);
            Draw();
        }

        private async void Timer_Tick(object sender, object args) { await RefreshAsync(); }

        private async Task RefreshAsync()
        {
            if (reading || disposed || paused) return;
            reading = true;
            try
            {
                bool sync = ConfigurationReader.UseDesktopSync;
                if (lastSync != sync)
                {
                    lastSync = sync; previousText = null; hasLiveState = false; hasSeenLiveFile = false;
                    settings = new CrosshairModel(); enabled = true;
                }
                var item = await ApplicationData.Current.LocalFolder.TryGetItemAsync(sync ? ConfigurationReader.LiveFileName : ConfigurationReader.ImportFileName);
                if (disposed || paused) return;
                if (item is StorageFile)
                {
                    if (sync) hasSeenLiveFile = true;
                    string text = await ConfigurationReader.ReadBoundedAsync((StorageFile)item);
                    if (disposed || paused) return;
                    // An import or mode switch in the launcher may finish while this read is pending.
                    if (sync != ConfigurationReader.UseDesktopSync) return;
                    if (text != previousText)
                    {
                        if (sync)
                        {
                            var state = ConfigurationReader.ParseLive(text);
                            settings = state.Settings; enabled = state.Enabled;
                            lastUpdate = state.UpdatedAtUtc; hasLiveState = true;
                        }
                        else { settings = ConfigurationReader.ParseImported(text); enabled = true; }
                        previousText = text;
                        await ResizeAndCenterAsync(false);
                    }
                    ReadStatus.Text = sync ? "桌面同步已连接。" : "使用独立导入配置。";
                }
                else ReadStatus.Text = sync
                    ? (hasSeenLiveFile ? "桌面同步文件暂不可用，等待重新连接。" : "等待桌面同步，当前显示默认准星。")
                    : "未找到导入配置，保留当前准星。";
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Runtime.InteropServices.COMException || error is ArgumentException || error is FormatException)
            {
                // Atomic replacements can briefly make the file unavailable; retry without losing the last valid configuration.
                ReadStatus.Text = "同步暂不可用，正在重试：" + error.Message;
            }
            finally
            {
                reading = false;
                if (!disposed && !paused) Draw();
            }
        }

        private void Draw()
        {
            if (disposed || widget == null) return;
            double age = (DateTimeOffset.UtcNow - lastUpdate).TotalSeconds;
            bool expired = hasLiveState && (age > 15 || age < -5);
            if (expired) ReadStatus.Text = "桌面同步已超时，准星已隐藏。请重新开启编辑器中的 Game Bar 模式。";
            bool invalidInitialState = hasSeenLiveFile && !hasLiveState;
            double requestedOpacity = widget.RequestedOpacity;
            bool drawEnabled = enabled && !expired && !invalidInitialState;
            Reticle.Draw(settings, DpiScale, drawEnabled, requestedOpacity);
            if (!expired && !invalidInitialState && !enabled) ReadStatus.Text = "桌面同步已连接；编辑器已关闭准星。";
            else if (drawEnabled && Reticle.LastEffectiveOpacity == 0) ReadStatus.Text = "准星不透明度为 0，请检查编辑器与 Game Bar 的透明度设置。";
            var snapshot = new JsonObject
            {
                ["capturedAtUtc"] = JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("O")),
                ["version"] = JsonValue.CreateStringValue("0.3.0-preview.2"),
                ["enabled"] = JsonValue.CreateBooleanValue(enabled),
                ["expired"] = JsonValue.CreateBooleanValue(expired),
                ["invalidInitialState"] = JsonValue.CreateBooleanValue(invalidInitialState),
                ["drawEnabled"] = JsonValue.CreateBooleanValue(drawEnabled),
                ["gameBarDisplayMode"] = JsonValue.CreateStringValue(widget.GameBarDisplayMode.ToString()),
                ["pinned"] = JsonValue.CreateBooleanValue(widget.Pinned),
                ["clickThroughEnabled"] = JsonValue.CreateBooleanValue(widget.ClickThroughEnabled),
                ["canvasWidth"] = Number(Reticle.ActualWidth),
                ["canvasHeight"] = Number(Reticle.ActualHeight),
                ["dpiScale"] = Number(DpiScale),
                ["requestedOpacity"] = Number(requestedOpacity),
                ["editorOpacity"] = Number(settings.Opacity),
                ["effectiveOpacity"] = Number(Reticle.LastEffectiveOpacity),
                ["primitiveCount"] = Number(Reticle.DrawnPrimitiveCount),
                ["heartbeatAgeSeconds"] = Number(Math.Round(age, 2))
            };
            diagnostics.Record(snapshot, Reticle);
        }

        private static JsonValue Number(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? JsonValue.Parse("null") : JsonValue.CreateNumberValue(value);
        }

        private async Task ResizeAndCenterAsync(bool force)
        {
            if (resizing || disposed || widget == null) return;
            var desired = new Size(settings.RequiredWidth(DpiScale), settings.RequiredHeight(DpiScale));
            if (!force && lastRequestedSize.HasValue && lastRequestedSize.Value.Equals(desired)) return;
            resizing = true;
            lastRequestedSize = desired;
            try
            {
                // Default 400 DIP. Large physical-pixel offsets need a larger transparent content area.
                widget.MaxWindowSize = new Size(Math.Max(400, desired.Width), Math.Max(400, desired.Height));
                bool resized = await widget.TryResizeWindowAsync(desired);
                if (disposed) return;
                await widget.CenterWindowAsync();
                if (disposed) return;
                PositionStatus.Text = resized ? "" : "Game Bar 未接受所需尺寸，大偏移可能被裁切。请减小偏移，或点击重新居中重试。";
            }
            catch (Exception error)
            {
                if (!disposed) PositionStatus.Text = "无法自动调整位置，可点击重新居中重试：" + error.Message;
            }
            finally { resizing = false; }
        }

        private async void Center_Click(object sender, RoutedEventArgs args) { await ResizeAndCenterAsync(true); Draw(); }
        private void Reticle_SizeChanged(object sender, SizeChangedEventArgs args) { Draw(); }
        private async void Display_DpiChanged(DisplayInformation sender, object args)
        {
            await OnUiAsync(async () => { await ResizeAndCenterAsync(true); Draw(); });
        }
        private async void Widget_Changed(XboxGameBarWidget sender, object args)
        {
            await OnUiAsync(() => { UpdateInstructions(); Draw(); });
        }
        private async void Widget_BoundsChanged(XboxGameBarWidget sender, object args)
        {
            await OnUiAsync(Draw);
        }
        private async Task OnUiAsync(Action action)
        {
            if (disposed) return;
            try { await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { if (!disposed) action(); }); }
            // Game Bar can deliver a final event while the view dispatcher is shutting down.
            catch (COMException) { }
            catch (TaskCanceledException) { }
            catch (ObjectDisposedException) { }
        }
        private void UpdateInstructions()
        {
            Instructions.Visibility = widget.GameBarDisplayMode == XboxGameBarDisplayMode.Foreground ? Visibility.Visible : Visibility.Collapsed;
            WidgetStatus.Text = (widget.Pinned ? "已固定" : "未固定：请点击标题栏图钉") + " · "
                + (widget.ClickThroughEnabled ? "鼠标穿透已开启" : "请在 Game Bar 开启鼠标穿透");
        }

        public void Pause() { if (disposed) return; paused = true; timer.Stop(); }
        public async void Resume() { if (disposed) return; paused = false; timer.Start(); await RefreshAsync(); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; timer.Stop(); timer.Tick -= Timer_Tick;
            Loaded -= Page_Loaded; Reticle.SizeChanged -= Reticle_SizeChanged;
            if (display != null) display.DpiChanged -= Display_DpiChanged;
            if (widget == null) return;
            widget.GameBarDisplayModeChanged -= Widget_Changed;
            widget.PinnedChanged -= Widget_Changed;
            widget.ClickThroughEnabledChanged -= Widget_Changed;
            widget.RequestedOpacityChanged -= Widget_Changed;
            widget.WindowBoundsChanged -= Widget_BoundsChanged;
            widget.VisibleChanged -= Widget_Changed;
            widget = null;
        }
    }
}
