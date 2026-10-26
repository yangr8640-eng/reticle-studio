using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Reticle.Core;
using Forms = System.Windows.Forms;

namespace Reticle.App;

public partial class MainWindow : Window
{
    private readonly CrosshairSettings settings;
    private readonly SettingsStore store;
    private readonly OverlayWindow overlay;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly GameBarBridge gameBar = new();
    private readonly DispatcherTimer gameBarTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer gameBarSyncTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool gameBarMode;
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private readonly ContextMenu trayMenu;
    private UpdateWindow? updateWindow;
    private IntPtr handle;
    private bool overlayEnabled, ready, allowSave = true, zoomed;
    private int backgroundIndex;
    private record MonitorOption(string Device, string Label);

    public MainWindow(SettingsStore? settingsStore = null)
    {
        store = settingsStore ?? new SettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReticleStudio", "settings.json"));
        settings = store.Load(out string? warning);
        InitializeComponent();
        VersionText.Text = "v" + AppVersion.Current;
        DataContext = settings;
        Preview.PhysicalPixels = true;
        ColorInput.Text = settings.Color;
        overlay = new OverlayWindow(settings);
        PopulateMonitors();
        settings.PropertyChanged += Settings_Changed;
        saveTimer.Tick += (_, _) => Save();
        gameBarTimer.Tick += (_, _) => SendGameBarState();
        gameBarSyncTimer.Tick += (_, _) => { gameBarSyncTimer.Stop(); SendGameBarState(); };
        if (warning is not null) { SaveStatus.Text = warning; allowSave = !warning.Contains("备份失败"); }

        trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Reticle Studio · 准星工具", Visible = true };
        // Use WPF input handling for the popup too. Opening a menu must never
        // dispatch the right-button release to a command under the cursor.
        trayMenu = new ContextMenu { Placement = PlacementMode.MousePoint, StaysOpen = false };
        AddTrayCommand("显示 / 隐藏准星", ToggleOverlay);
        AddTrayCommand("显示设置", Restore);
        AddTrayCommand("检查更新", ShowUpdates);
        trayMenu.Items.Add(new Separator());
        AddTrayCommand("退出", Close);
        trayMenu.Opened += (_, _) =>
        {
            // The editor can be hidden. Foreground the popup's HWND so clicking
            // elsewhere dismisses it without showing or activating the editor.
            if (PresentationSource.FromVisual(trayMenu) is HwndSource source)
                NativeMethods.SetForegroundWindow(source.Handle);
        };
        trayMenu.PreviewMouseDown += (_, e) => { if (e.ChangedButton == System.Windows.Input.MouseButton.Right) e.Handled = true; };
        trayMenu.PreviewMouseUp += (_, e) => { if (e.ChangedButton == System.Windows.Input.MouseButton.Right) e.Handled = true; };
        tray.MouseClick += Tray_MouseClick;
        tray.MouseDoubleClick += Tray_MouseClick;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(Hook);
            bool toggle = NativeMethods.RegisterHotKey(handle, 1, 0x4003, 0x48);
            bool show = NativeMethods.RegisterHotKey(handle, 2, 0x4003, 0x53);
            HotkeyWarning.Text = (toggle ? "" : "Ctrl+Alt+H 被占用，请使用按钮或托盘。 ") + (show ? "" : "Ctrl+Alt+S 被占用，请从托盘打开设置。");
        };
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        ready = true;
        UpdateShape();
        Closed += (_, _) =>
        {
            ready = false; saveTimer.Stop(); Save();
            gameBarTimer.Stop(); gameBarSyncTimer.Stop();
            if (gameBarMode && overlayEnabled) gameBar.TrySend(settings, false, out _);
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            settings.PropertyChanged -= Settings_Changed;
            NativeMethods.UnregisterHotKey(handle, 1); NativeMethods.UnregisterHotKey(handle, 2);
            trayMenu.IsOpen = false;
            tray.Visible = false; tray.Dispose(); trayIcon.Dispose();
            overlay.Close();
        };
    }
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { if (!ready) return; PopulateMonitors(); if (overlayEnabled) overlay.Position(); });
    private void PopulateMonitors()
    {
        var options = Forms.Screen.AllScreens.Select((s, i) => new MonitorOption(s.DeviceName, $"显示器 {i + 1} · {s.Bounds.Width} × {s.Bounds.Height}{(s.Primary ? " · 主屏" : "")}")).ToList();
        MonitorBox.ItemsSource = options;
        MonitorBox.SelectedValue = options.Any(x => x.Device == settings.Monitor) ? settings.Monitor : (Forms.Screen.PrimaryScreen?.DeviceName ?? options[0].Device);
    }
    private void Settings_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (!ready) return;
        if (e.PropertyName == nameof(CrosshairSettings.Color) && ColorInput.Text != settings.Color) ColorInput.Text = settings.Color;
        if (e.PropertyName == nameof(CrosshairSettings.Shape)) UpdateShape();
        if (overlayEnabled && e.PropertyName is nameof(CrosshairSettings.Monitor) or nameof(CrosshairSettings.OffsetX) or nameof(CrosshairSettings.OffsetY)) overlay.Position();
        if (gameBarMode && overlayEnabled) { gameBarSyncTimer.Stop(); gameBarSyncTimer.Start(); }
        if (allowSave) { SaveStatus.Text = "正在保存…"; saveTimer.Stop(); saveTimer.Start(); }
    }
    private void Save()
    {
        saveTimer.Stop(); if (!allowSave) return;
        try { store.Save(settings); SaveStatus.Text = "已自动保存到本机"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SaveStatus.Text = "保存失败，请使用导出配置：" + e.Message; }
    }
    private void UpdateShape()
    {
        foreach (var b in new[] { CrossButton, DiagonalButton, CircleButton })
        {
            bool selected = (string)b.Tag == settings.Shape.ToString();
            b.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(selected ? "#234337" : "#232D3C"));
            b.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(selected ? "#67E8B0" : "#344052"));
        }
        ArmsControls.Visibility = settings.Shape == CrosshairShape.Circle ? Visibility.Collapsed : Visibility.Visible;
        CircleControls.Visibility = settings.Shape == CrosshairShape.Circle ? Visibility.Visible : Visibility.Collapsed;
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey)
        {
            if (wParam.ToInt32() == 1) ToggleOverlay();
            if (wParam.ToInt32() == 2) Restore();
            handled = true;
        }
        return IntPtr.Zero;
    }
    private void ToggleOverlay()
    {
        if (gameBarMode && !overlayEnabled && !gameBar.TryConnect(out var message))
        {
            OverlayStatus.Text = message;
            return;
        }
        overlayEnabled = !overlayEnabled;
        UpdateOverlayControls();
        if (gameBarMode)
        {
            SendGameBarState();
            if (overlayEnabled) gameBarTimer.Start(); else gameBarTimer.Stop();
        }
        else overlay.SetVisible(overlayEnabled);
    }
    private void UpdateOverlayControls()
    {
        string mode = gameBarMode ? "全屏组件" : "桌面准星";
        ToggleButton.Content = (overlayEnabled ? "关闭" : "开启") + mode;
        OverlayStatus.Text = gameBarMode && overlayEnabled ? "已发送到组件，请在 Game Bar 中固定" : mode + (overlayEnabled ? "已开启" : "已关闭");
        StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(overlayEnabled ? "#67E8B0" : "#657082"));
        tray.Text = overlayEnabled ? "Reticle Studio · 准星已开启" : "Reticle Studio · 准星已关闭";
    }
    private void SendGameBarState()
    {
        if (!gameBarMode) return;
        if (!gameBar.TrySend(settings, overlayEnabled, out string? error))
        {
            // StorageFile readers can briefly hold the previous snapshot during replacement.
            // Keep the heartbeat active so a transient sharing failure does not disable the crosshair forever.
            OverlayStatus.Text = error + (overlayEnabled ? "；正在重试。" : "；组件将在心跳超时后隐藏。");
        }
        else if (overlayEnabled) OverlayStatus.Text = "已发送到组件，请在 Game Bar 中固定";
    }
    private void OverlayMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        if (overlayEnabled) ToggleOverlay();
        gameBarTimer.Stop(); gameBarSyncTimer.Stop();
        gameBarMode = OverlayModeBox.SelectedIndex == 1;
        MonitorBox.IsEnabled = !gameBarMode;
        MonitorBox.ToolTip = gameBarMode ? "全屏组件所在显示器请在 Game Bar 中调整。" : null;
        ModeHint.Text = gameBarMode
            ? "保留 CS 全屏；需安装并固定 Game Bar 组件。"
            : "桌面模式适用于全屏窗口化 / 无边框。";
        UpdateOverlayControls();
    }
    private void GameBarHelp_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "全屏组件为实验支持，需要另行安装 Reticle Game Bar 预览包。\n\n" +
        "1. 保留 CS 的全屏显示模式。按 Win+G，确认 Game Bar 能显示在游戏画面上。\n" +
        "2. 在 Game Bar 的小组件菜单打开 Reticle 准星，点击固定，并启用鼠标穿透。\n" +
        "3. 在本工具选择「全屏 · Game Bar（实验）」，点击开启。外观与 Ctrl+Alt+H 开关会同步；如果组件曾导入独立配置，请先在其启动页点「使用桌面同步」。\n" +
        "4. 在组件中点居中；多屏位置请在 Game Bar 中调整。\n\n" +
        "需要 Windows / Game Bar 对当前游戏的覆盖支持。若 Win+G 切出或黑屏，请在 cs2.exe 属性的兼容性页取消「禁用全屏优化」，重启游戏再验证。CS 游戏内兼容性尚待实测。",
        "全屏准星 · Game Bar", MessageBoxButton.OK, MessageBoxImage.Information);
    private void Tray_MouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
            // Let the shell finish handling the tray flyout before activating the editor.
            Dispatcher.BeginInvoke(() => { if (ready) Restore(); });
        else if (e.Button == Forms.MouseButtons.Right)
            Dispatcher.BeginInvoke(() => { if (ready) trayMenu.IsOpen = true; }, DispatcherPriority.ContextIdle);
    }
    private void AddTrayCommand(string text, Action command)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) =>
        {
            trayMenu.IsOpen = false;
            // Finish closing the popup before restoring windows or disposing the tray.
            Dispatcher.BeginInvoke(() => { if (ready) command(); });
        };
        trayMenu.Items.Add(item);
    }
    private void Restore() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
    private void ShowUpdates()
    {
        Restore();
        if (updateWindow is not null) { updateWindow.Activate(); return; }
        updateWindow = new UpdateWindow { Owner = this };
        updateWindow.Closed += (_, _) => updateWindow = null;
        updateWindow.Show();
    }
    private void Update_Click(object sender, RoutedEventArgs e) => ShowUpdates();
    private void ToggleOverlay_Click(object sender, RoutedEventArgs e) => ToggleOverlay();
    private void MinimizeToTray_Click(object sender, RoutedEventArgs e) { Save(); Hide(); }
    private void Shape_Click(object sender, RoutedEventArgs e) => settings.Shape = Enum.Parse<CrosshairShape>((string)((Button)sender).Tag);
    private void Preset_Click(object sender, RoutedEventArgs e) => settings.Apply(CrosshairSettings.Preset((string)((Button)sender).Tag), false);
    private void Reset_Click(object sender, RoutedEventArgs e) => settings.Apply(new(), false);
    private void Color_Click(object sender, RoutedEventArgs e) => settings.Color = (string)((Button)sender).Tag;
    private void Palette_Expanded(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(() => { if (PaletteExpander.IsExpanded) PaletteExpander.BringIntoView(); }, DispatcherPriority.Loaded);
    private void Center_Click(object sender, RoutedEventArgs e) { settings.OffsetX = 0; settings.OffsetY = 0; }
    private void ColorInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (ColorError is null) return;
        bool valid = CrosshairSettings.IsColor(ColorInput.Text);
        ColorError.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        if (valid && ready) settings.Color = ColorInput.Text;
    }
    private void Monitor_Changed(object sender, SelectionChangedEventArgs e) { if (ready && MonitorBox.SelectedValue is string device) settings.Monitor = device; }
    private void Zoom_Click(object sender, RoutedEventArgs e)
    {
        zoomed = !zoomed; Preview.Zoom = zoomed ? 4 : 1; Preview.InvalidateVisual();
        PreviewCaption.Text = zoomed ? "4× 放大预览 · 桌面尺寸不变" : "1× 实际像素";
    }
    private void Background_Click(object sender, RoutedEventArgs e)
    {
        string[] colors = ["#151F2C", "#ACA99B", "#4D624D"];
        PreviewPlate.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[++backgroundIndex % colors.Length]));
    }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "导入准星配置", Filter = "准星配置 (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { var imported = SettingsStore.Parse(File.ReadAllText(dialog.FileName)); settings.Apply(imported); PopulateMonitors(); SaveStatus.Text = "已导入配置"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException) { MessageBox.Show(this, error.Message, "无法导入配置", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "导出准星配置", Filter = "准星配置 (*.json)|*.json", FileName = "my-reticle.json", DefaultExt = ".json" };
        if (dialog.ShowDialog(this) != true) return;
        try { new SettingsStore(dialog.FileName).Save(settings); SaveStatus.Text = "配置已导出"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { MessageBox.Show(this, error.Message, "无法导出配置", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
