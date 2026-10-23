namespace Orbit.App.Controls;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

public partial class GoogleColorPickerControl : UserControl
{
    private double _hue = 25.0;        // 0 to 360
    private double _saturation = 0.90; // 0 to 1
    private double _value = 1.0;       // 0 to 1
    private bool _isUpdatingInternally;
    private bool _isDraggingSv;
    private bool _isDraggingHue;

    public event Action<Color>? ColorChanged;
    public event Action<Color>? ColorApplied;
    public event Action? Closed;

    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(
            nameof(SelectedColor),
            typeof(Color),
            typeof(GoogleColorPickerControl),
            new FrameworkPropertyMetadata(Color.FromRgb(255, 122, 26), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorPropertyChanged));

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private static void OnSelectedColorPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GoogleColorPickerControl picker && !picker._isUpdatingInternally)
        {
            picker.SetColorInternal((Color)e.NewValue, updateHex: true, updateThumbs: true);
        }
    }

    public GoogleColorPickerControl()
    {
        InitializeComponent();
        Loaded += OnControlLoaded;
        SizeChanged += (s, e) => UpdateThumbPositions();
    }

    private void OnControlLoaded(object sender, RoutedEventArgs e)
    {
        SetColorInternal(SelectedColor, updateHex: true, updateThumbs: true);
    }

    public void SetColor(Color color)
    {
        SetColorInternal(color, updateHex: true, updateThumbs: true);
    }

    private void SetColorInternal(Color color, bool updateHex, bool updateThumbs)
    {
        _isUpdatingInternally = true;
        try
        {
            RgbToHsv(color.R, color.G, color.B, out _hue, out _saturation, out _value);
            UpdateUiFromHsv(color, updateHex, updateThumbs);
        }
        finally
        {
            _isUpdatingInternally = false;
        }
    }

    private void UpdateUiFromHsv(Color color, bool updateHex, bool updateThumbs)
    {
        // 1. Update Preview Swatch
        if (PreviewSwatch != null)
        {
            PreviewSwatch.Background = new SolidColorBrush(color);
        }

        // 2. Update base color of SV Canvas (pure color at full Saturation & Value for this Hue)
        var pureHueColor = HsvToRgb(_hue, 1.0, 1.0);
        if (SvCanvas != null)
        {
            SvCanvas.Background = new SolidColorBrush(pureHueColor);
        }

        // 3. Update Hue Thumb Color
        if (HueThumb != null)
        {
            HueThumb.Fill = new SolidColorBrush(pureHueColor);
        }

        // 4. Update Thumb Positions
        if (updateThumbs)
        {
            UpdateThumbPositions();
        }

        // 5. Update HEX
        if (updateHex && TbHex != null)
        {
            TbHex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        // 6. Update Readouts: RGB, HSV, CMYK, HSL
        if (TxtRgbVal != null) TxtRgbVal.Text = $"{color.R}, {color.G}, {color.B}";
        if (TxtHsvVal != null) TxtHsvVal.Text = $"{(int)_hue}°, {(int)(_saturation * 100)}%, {(int)(_value * 100)}%";

        RgbToCmyk(color.R, color.G, color.B, out double c, out double m, out double y, out double k);
        if (TxtCmykVal != null) TxtCmykVal.Text = $"{(int)(c * 100)}%, {(int)(m * 100)}%, {(int)(y * 100)}%, {(int)(k * 100)}%";

        RgbToHsl(color.R, color.G, color.B, out double hL, out double sL, out double lVal);
        if (TxtHslVal != null) TxtHslVal.Text = $"{(int)hL}°, {(int)(sL * 100)}%, {(int)(lVal * 100)}%";

        // 7. Update Bound DependencyProperty
        SetValue(SelectedColorProperty, color);
        ColorChanged?.Invoke(color);
    }

    private void UpdateThumbPositions()
    {
        if (SvCanvas != null && SvThumb != null && SvCanvas.ActualWidth > 0 && SvCanvas.ActualHeight > 0)
        {
            double svX = _saturation * SvCanvas.ActualWidth;
            double svY = (1.0 - _value) * SvCanvas.ActualHeight;

            Canvas.SetLeft(SvThumb, Math.Max(0, Math.Min(SvCanvas.ActualWidth - SvThumb.Width, svX - SvThumb.Width / 2)));
            Canvas.SetTop(SvThumb, Math.Max(0, Math.Min(SvCanvas.ActualHeight - SvThumb.Height, svY - SvThumb.Height / 2)));
        }

        if (HueCanvas != null && HueThumb != null && HueCanvas.ActualWidth > 0)
        {
            double hueRatio = Math.Max(0, Math.Min(1.0, _hue / 360.0));
            double hueX = hueRatio * HueCanvas.ActualWidth;

            Canvas.SetLeft(HueThumb, Math.Max(0, Math.Min(HueCanvas.ActualWidth - HueThumb.Width, hueX - HueThumb.Width / 2)));
            Canvas.SetTop(HueThumb, (HueCanvas.ActualHeight - HueThumb.Height) / 2);
        }
    }

    #region 2D SV Canvas Mouse Events

    private void OnSvCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSv = true;
        SvCanvas.CaptureMouse();
        UpdateSvFromMouse(e.GetPosition(SvCanvas));
    }

    private void OnSvCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingSv)
        {
            UpdateSvFromMouse(e.GetPosition(SvCanvas));
        }
    }

    private void OnSvCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingSv)
        {
            _isDraggingSv = false;
            SvCanvas.ReleaseMouseCapture();
        }
    }

    private void UpdateSvFromMouse(Point pos)
    {
        if (SvCanvas.ActualWidth <= 0 || SvCanvas.ActualHeight <= 0) return;

        double s = Math.Max(0.0, Math.Min(1.0, pos.X / SvCanvas.ActualWidth));
        double v = Math.Max(0.0, Math.Min(1.0, 1.0 - (pos.Y / SvCanvas.ActualHeight)));

        _saturation = s;
        _value = v;

        _isUpdatingInternally = true;
        try
        {
            var color = HsvToRgb(_hue, _saturation, _value);
            UpdateUiFromHsv(color, updateHex: true, updateThumbs: true);
        }
        finally
        {
            _isUpdatingInternally = false;
        }
    }

    #endregion

    #region Rainbow Hue Slider Mouse Events

    private void OnHueCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingHue = true;
        HueCanvas.CaptureMouse();
        UpdateHueFromMouse(e.GetPosition(HueCanvas));
    }

    private void OnHueCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingHue)
        {
            UpdateHueFromMouse(e.GetPosition(HueCanvas));
        }
    }

    private void OnHueCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingHue)
        {
            _isDraggingHue = false;
            HueCanvas.ReleaseMouseCapture();
        }
    }

    private void UpdateHueFromMouse(Point pos)
    {
        if (HueCanvas.ActualWidth <= 0) return;

        double ratio = Math.Max(0.0, Math.Min(1.0, pos.X / HueCanvas.ActualWidth));
        _hue = ratio * 360.0;
        if (_hue >= 360.0) _hue = 0.0;

        _isUpdatingInternally = true;
        try
        {
            var color = HsvToRgb(_hue, _saturation, _value);
            UpdateUiFromHsv(color, updateHex: true, updateThumbs: true);
        }
        finally
        {
            _isUpdatingInternally = false;
        }
    }

    #endregion

    #region HEX Text Input & Preset Clicks

    private void OnTbHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingInternally || TbHex == null) return;

        string hex = TbHex.Text.Trim();
        if (!hex.StartsWith("#")) hex = "#" + hex;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            SetColorInternal(color, updateHex: false, updateThumbs: true);
        }
        catch { }
    }

    private void OnCopyHexClicked(object sender, RoutedEventArgs e)
    {
        if (TbHex != null && !string.IsNullOrEmpty(TbHex.Text))
        {
            Clipboard.SetText(TbHex.Text);
            BtnCopyHex.Content = "✓ Copied";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (s, ev) =>
            {
                BtnCopyHex.Content = "📋 Copy";
                timer.Stop();
            };
            timer.Start();
        }
    }

    private void OnQuickSwatchClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Background is SolidColorBrush scb)
        {
            SetColor(scb.Color);
        }
    }

    private void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        var color = HsvToRgb(_hue, _saturation, _value);
        ColorApplied?.Invoke(color);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Closed?.Invoke();
    }

    #endregion

    #region Math Color Conversions (HSV, HSL, CMYK, RGB)

    public static Color HsvToRgb(double h, double s, double v)
    {
        h = h % 360.0;
        if (h < 0) h += 360.0;

        double c = v * s;
        double x = c * (1.0 - Math.Abs((h / 60.0 % 2.0) - 1.0));
        double m = v - c;

        double rPrime = 0, gPrime = 0, bPrime = 0;

        if (h < 60) { rPrime = c; gPrime = x; bPrime = 0; }
        else if (h < 120) { rPrime = x; gPrime = c; bPrime = 0; }
        else if (h < 180) { rPrime = 0; gPrime = c; bPrime = x; }
        else if (h < 240) { rPrime = 0; gPrime = x; bPrime = c; }
        else if (h < 300) { rPrime = x; gPrime = 0; bPrime = c; }
        else { rPrime = c; gPrime = 0; bPrime = x; }

        byte r = (byte)Math.Round((rPrime + m) * 255);
        byte g = (byte)Math.Round((gPrime + m) * 255);
        byte b = (byte)Math.Round((bPrime + m) * 255);

        return Color.FromRgb(r, g, b);
    }

    public static void RgbToHsv(byte rByte, byte gByte, byte bByte, out double h, out double s, out double v)
    {
        double r = rByte / 255.0;
        double g = gByte / 255.0;
        double b = bByte / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        v = max;
        s = max == 0 ? 0 : delta / max;

        if (delta == 0)
        {
            h = 0;
        }
        else if (max == r)
        {
            h = 60.0 * (((g - b) / delta) % 6.0);
        }
        else if (max == g)
        {
            h = 60.0 * (((b - r) / delta) + 2.0);
        }
        else
        {
            h = 60.0 * (((r - g) / delta) + 4.0);
        }

        if (h < 0) h += 360.0;
    }

    public static void RgbToCmyk(byte rByte, byte gByte, byte bByte, out double c, out double m, out double y, out double k)
    {
        double r = rByte / 255.0;
        double g = gByte / 255.0;
        double b = bByte / 255.0;

        k = 1.0 - Math.Max(r, Math.Max(g, b));
        if (Math.Abs(k - 1.0) < 0.0001)
        {
            c = m = y = 0;
            return;
        }

        c = (1.0 - r - k) / (1.0 - k);
        m = (1.0 - g - k) / (1.0 - k);
        y = (1.0 - b - k) / (1.0 - k);
    }

    public static void RgbToHsl(byte rByte, byte gByte, byte bByte, out double h, out double s, out double l)
    {
        double r = rByte / 255.0;
        double g = gByte / 255.0;
        double b = bByte / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        l = (max + min) / 2.0;

        if (delta == 0)
        {
            h = 0;
            s = 0;
        }
        else
        {
            s = l <= 0.5 ? delta / (max + min) : delta / (2.0 - max - min);

            if (max == r)
            {
                h = 60.0 * (((g - b) / delta) % 6.0);
            }
            else if (max == g)
            {
                h = 60.0 * (((b - r) / delta) + 2.0);
            }
            else
            {
                h = 60.0 * (((r - g) / delta) + 4.0);
            }

            if (h < 0) h += 360.0;
        }
    }

    #endregion
}
