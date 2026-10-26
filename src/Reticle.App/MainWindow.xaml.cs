using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayIcon;
    private IntPtr handle;
    private bool overlayEnabled, ready, allowSave = true, zoomed;
    private int backgroundIndex;
    private record MonitorOption(string Device, string Label);

    public MainWindow(SettingsStore? settingsStore = null)
    {
        store = settingsStore ?? new SettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReticleStudio", "settings.json"));
        settings = store.Load(out string? warning);
        InitializeComponent();
        DataContext = settings;
        Preview.PhysicalPixels = true;
        ColorInput.Text = settings.Color;
        overlay = new OverlayWindow(settings);
        PopulateMonitors();
        settings.PropertyChanged += Settings_Changed;
        saveTimer.Tick += (_, _) => Save();
        if (warning is not null) { SaveStatus.Text = warning; allowSave = !warning.Contains("备份失败"); }

        trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Reticle Studio · 准星工具", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 隐藏准星", null, (_, _) => Dispatcher.Invoke(ToggleOverlay));
        menu.Items.Add("打开设置", null, (_, _) => Dispatcher.Invoke(Restore));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Restore);
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
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            settings.PropertyChanged -= Settings_Changed;
            NativeMethods.UnregisterHotKey(handle, 1); NativeMethods.UnregisterHotKey(handle, 2);
            overlay.Close(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); trayIcon.Dispose();
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
        overlayEnabled = !overlayEnabled;
        overlay.SetVisible(overlayEnabled);
        ToggleButton.Content = overlayEnabled ? "关闭桌面准星" : "开启桌面准星";
        OverlayStatus.Text = overlayEnabled ? "桌面准星已开启" : "桌面准星已关闭";
        StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(overlayEnabled ? "#67E8B0" : "#657082"));
        tray.Text = overlayEnabled ? "Reticle Studio · 准星已开启" : "Reticle Studio · 准星已关闭";
    }
    private void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
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
