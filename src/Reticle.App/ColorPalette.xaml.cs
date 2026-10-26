using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Reticle.Core;

namespace Reticle.App;

public partial class ColorPalette : UserControl
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(string), typeof(ColorPalette),
        new FrameworkPropertyMetadata("#67E8B0", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, ColorChanged),
        value => value is string color && CrosshairSettings.IsColor(color));

    public string SelectedColor { get => (string)GetValue(SelectedColorProperty); set => SetValue(SelectedColorProperty, value); }
    private double hue, saturation, brightness;
    private bool ready, refreshing, committing;

    public ColorPalette()
    {
        InitializeComponent();
        ready = true;
        ReadColor();
    }

    private static void ColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPalette)d;
        if (picker.ready && !picker.committing) picker.ReadColor();
    }

    private void ReadColor()
    {
        var hsv = ColorMath.HexToHsv(SelectedColor);
        // Gray has no hue; black has neither hue nor saturation. Retain these
        // choices so moving away from either edge keeps the user's selected hue.
        if (hsv.Saturation > 0) hue = hsv.Hue;
        if (hsv.Value > 0) saturation = hsv.Saturation;
        brightness = hsv.Value;
        Refresh();
    }

    private void Commit()
    {
        committing = true;
        try { SetCurrentValue(SelectedColorProperty, ColorMath.HsvToHex(hue, saturation, brightness)); }
        finally { committing = false; }
        Refresh();
    }

    private void Refresh()
    {
        refreshing = true;
        try
        {
            HueSlider.Value = hue;
            SaturationSlider.Value = saturation * 100;
            BrightnessSlider.Value = brightness * 100;
            HueLabel.Text = $"{hue:0}°";
            SaturationLabel.Text = $"{saturation * 100:0}%";
            BrightnessLabel.Text = $"{brightness * 100:0}%";
            ColorSurface.Background = Brush(ColorMath.HsvToHex(hue, 1, 1));
            ColorChip.Background = Brush(SelectedColor);
            ColorLabel.Text = SelectedColor.ToUpperInvariant();
            PositionMarker();
        }
        finally { refreshing = false; }
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze(); return brush;
    }

    private void PositionMarker()
    {
        Canvas.SetLeft(SelectionMarker, saturation * ColorSurface.ActualWidth - SelectionMarker.Width / 2);
        Canvas.SetTop(SelectionMarker, (1 - brightness) * ColorSurface.ActualHeight - SelectionMarker.Height / 2);
    }

    // Shared by pointer down/move; capture lets a drag finish outside the panel.
    internal void SelectAt(Point point)
    {
        if (ColorSurface.ActualWidth <= 0 || ColorSurface.ActualHeight <= 0) return;
        saturation = Math.Clamp(point.X / ColorSurface.ActualWidth, 0, 1);
        brightness = 1 - Math.Clamp(point.Y / ColorSurface.ActualHeight, 0, 1);
        Commit();
    }

    private void Surface_MouseDown(object sender, MouseButtonEventArgs e)
    {
        ColorSurface.Focus(); ColorSurface.CaptureMouse(); SelectAt(e.GetPosition(ColorSurface)); e.Handled = true;
    }
    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        if (ColorSurface.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) SelectAt(e.GetPosition(ColorSurface));
    }
    private void Surface_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!ColorSurface.IsMouseCaptured) return;
        SelectAt(e.GetPosition(ColorSurface)); ColorSurface.ReleaseMouseCapture(); e.Handled = true;
    }
    private void Surface_KeyDown(object sender, KeyEventArgs e)
    {
        double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 0.1 : 0.01;
        switch (e.Key)
        {
            case Key.Left: saturation = Math.Max(0, saturation - step); break;
            case Key.Right: saturation = Math.Min(1, saturation + step); break;
            case Key.Up: brightness = Math.Min(1, brightness + step); break;
            case Key.Down: brightness = Math.Max(0, brightness - step); break;
            default: return;
        }
        Commit(); e.Handled = true;
    }
    private void Surface_SizeChanged(object sender, SizeChangedEventArgs e) { if (ready) PositionMarker(); }
    private void Hue_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (ready && !refreshing) { hue = e.NewValue; Commit(); } }
    private void Saturation_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (ready && !refreshing) { saturation = e.NewValue / 100; Commit(); } }
    private void Brightness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (ready && !refreshing) { brightness = e.NewValue / 100; Commit(); } }
}
