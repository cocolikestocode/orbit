namespace Orbit.App.Views;

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Orbit.App.Services;
using Orbit.Core.Models;
using Orbit.Core.Services;

public partial class SettingsWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly DispatcherTimer _bannerTimer = new();
    private string _selectedAccent = "#FF7A1A";
    private string _selectedTheme = "Dark Modern (#181920)";

    public SettingsWindow(ISettingsService settingsService)
    {
        InitializeComponent();

        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

        _bannerTimer.Interval = TimeSpan.FromSeconds(3);
        _bannerTimer.Tick += (s, e) =>
        {
            SaveNotificationBanner.Visibility = Visibility.Collapsed;
            _bannerTimer.Stop();
        };

        Loaded += OnSettingsLoaded;
    }

    private void OnSettingsLoaded(object sender, RoutedEventArgs e)
    {
        LoadSettingsIntoUI();
        RenderMiniatureRadialMenu();
    }

    private void LoadSettingsIntoUI()
    {
        var s = _settingsService.CurrentSettings;

        // Modifier
        switch (s.ActivationModifier.ToLowerInvariant())
        {
            case "ctrl":
                CmbActivationModifier.SelectedIndex = 1;
                break;
            case "alt":
                CmbActivationModifier.SelectedIndex = 2;
                break;
            default:
                CmbActivationModifier.SelectedIndex = 0;
                break;
        }

        // Delay & Scale
        SliderHoldDelay.Value = s.ActivationDelayMs;
        TxtHoldDelayValue.Text = $"{s.ActivationDelayMs} ms";

        SliderMenuScale.Value = s.MenuScale;
        UpdateMenuScaleLabel(s.MenuScale);

        // Shortcuts
        TxtShortcutConvert.Text = s.ShortcutConvert;
        TxtShortcutEdit.Text = s.ShortcutEdit;
        TxtShortcutAnnotate.Text = s.ShortcutAnnotate;
        TxtShortcutOptimize.Text = s.ShortcutOptimize;
        TxtShortcutOrganize.Text = s.ShortcutOrganize;
        TxtShortcutExtract.Text = s.ShortcutExtract;
        TxtShortcutAnalyze.Text = s.ShortcutAnalyze;
        TxtShortcutCancel.Text = s.ShortcutCancel;

        // General
        ChkStartup.IsChecked = s.LaunchAtStartup;
        ChkMinimizeToTray.IsChecked = s.MinimizeToTray;
        ChkNotifications.IsChecked = s.ShowNotifications;
        ChkSounds.IsChecked = s.PlaySounds;

        // Appearance
        _selectedTheme = s.Theme ?? "Dark Modern (#181920)";
        if (_selectedTheme.Contains("OLED", StringComparison.OrdinalIgnoreCase))
        {
            CmbTheme.SelectedIndex = 1;
        }
        else if (_selectedTheme.Contains("Slate", StringComparison.OrdinalIgnoreCase) || _selectedTheme.Contains("Midnight", StringComparison.OrdinalIgnoreCase))
        {
            CmbTheme.SelectedIndex = 2;
        }
        else
        {
            CmbTheme.SelectedIndex = 0;
        }

        _selectedAccent = !string.IsNullOrWhiteSpace(s.AccentColor) ? s.AccentColor : "#FF7A1A";
        UpdateAccentDotBorders(_selectedAccent);

        // File Ops
        ChkPreserveOriginals.IsChecked = s.KeepOriginalFiles;
        if (s.OutputDestinationMode.Contains("Subfolder", StringComparison.OrdinalIgnoreCase))
        {
            CmbDefaultOutputDir.SelectedIndex = 1;
            PnlCustomDir.Visibility = Visibility.Collapsed;
        }
        else if (s.OutputDestinationMode.Contains("Custom", StringComparison.OrdinalIgnoreCase))
        {
            CmbDefaultOutputDir.SelectedIndex = 2;
            PnlCustomDir.Visibility = Visibility.Visible;
            TxtCustomOutputDir.Text = s.CustomOutputDirectory;
        }
        else
        {
            CmbDefaultOutputDir.SelectedIndex = 0;
            PnlCustomDir.Visibility = Visibility.Collapsed;
        }

        // Advanced
        switch (s.MaxConcurrentThreads)
        {
            case 2: CmbWorkerThreads.SelectedIndex = 1; break;
            case 4: CmbWorkerThreads.SelectedIndex = 2; break;
            case 8: CmbWorkerThreads.SelectedIndex = 3; break;
            case 16: CmbWorkerThreads.SelectedIndex = 4; break;
            default: CmbWorkerThreads.SelectedIndex = 0; break;
        }
    }

    #region Tab Navigation

    private void OnNavGeneralClicked(object sender, RoutedEventArgs e) => SwitchTab(NavGeneral, PanelGeneral);
    private void OnNavShortcutsClicked(object sender, RoutedEventArgs e) => SwitchTab(NavShortcuts, PanelShortcuts);
    private void OnNavAppearanceClicked(object sender, RoutedEventArgs e) => SwitchTab(NavAppearance, PanelAppearance);
    private void OnNavFileOpsClicked(object sender, RoutedEventArgs e) => SwitchTab(NavFileOps, PanelFileOps);
    private void OnNavIntegrationsClicked(object sender, RoutedEventArgs e) => SwitchTab(NavIntegrations, PanelIntegrations);
    private void OnNavAdvancedClicked(object sender, RoutedEventArgs e) => SwitchTab(NavAdvanced, PanelAdvanced);

    private void SwitchTab(Button activeBtn, StackPanel activePanel)
    {
        // Clear all active tags
        NavGeneral.Tag = null;
        NavShortcuts.Tag = null;
        NavAppearance.Tag = null;
        NavFileOps.Tag = null;
        NavIntegrations.Tag = null;
        NavAdvanced.Tag = null;

        activeBtn.Tag = "Active";

        // Toggle panel visibilities
        PanelGeneral.Visibility = Visibility.Collapsed;
        PanelShortcuts.Visibility = Visibility.Collapsed;
        PanelAppearance.Visibility = Visibility.Collapsed;
        PanelFileOps.Visibility = Visibility.Collapsed;
        PanelIntegrations.Visibility = Visibility.Collapsed;
        PanelAdvanced.Visibility = Visibility.Collapsed;

        activePanel.Visibility = Visibility.Visible;
    }

    #endregion

    #region Appearance & Customization Events

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbTheme?.SelectedItem is ComboBoxItem item)
        {
            _selectedTheme = item.Content.ToString() ?? "Dark Modern (#181920)";
            ThemeManager.ApplyTheme(GetDraftSettings());
        }
    }

    private void OnColorDotClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag != null)
        {
            _selectedAccent = border.Tag.ToString() ?? "#FF7A1A";
            UpdateAccentDotBorders(_selectedAccent);
            ThemeManager.ApplyTheme(GetDraftSettings());
            RenderMiniatureRadialMenu();
        }
    }

    private void UpdateAccentDotBorders(string activeColor)
    {
        ColorDotOrange.BorderBrush = string.Equals(activeColor, "#FF7A1A", StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent;
        ColorDotIndigo.BorderBrush = string.Equals(activeColor, "#6366F1", StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent;
        ColorDotCyan.BorderBrush = string.Equals(activeColor, "#06B6D4", StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent;
        ColorDotEmerald.BorderBrush = string.Equals(activeColor, "#10B981", StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent;
    }

    private void OnDefaultOutputDirChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PnlCustomDir == null) return;
        PnlCustomDir.Visibility = CmbDefaultOutputDir.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBrowseCustomDirClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Default Orbit Output Folder"
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            TxtCustomOutputDir.Text = dialog.FolderName;
        }
    }

    private OrbitSettings GetDraftSettings()
    {
        return new OrbitSettings
        {
            Theme = _selectedTheme,
            AccentColor = _selectedAccent
        };
    }

    #endregion

    #region Radial Menu Miniature Preview

    public void RenderMiniatureRadialMenu()
    {
        if (MiniPreviewCanvas == null) return;

        MiniPreviewCanvas.Children.Clear();

        double cx = 120;
        double cy = 120;
        double innerR = 40;
        double outerR = 106;

        Color userAccent;
        try { userAccent = (Color)ColorConverter.ConvertFromString(_selectedAccent); }
        catch { userAccent = Color.FromRgb(0xFF, 0x7A, 0x1A); }

        var categories = new[]
        {
            ("Convert", TxtShortcutConvert?.Text ?? "C", userAccent),
            ("Edit", TxtShortcutEdit?.Text ?? "E", Color.FromRgb(0x63, 0x66, 0xF1)),
            ("Annotate", TxtShortcutAnnotate?.Text ?? "A", Color.FromRgb(0xEC, 0x48, 0x99)),
            ("Optimize", TxtShortcutOptimize?.Text ?? "O", Color.FromRgb(0x10, 0xB9, 0x81)),
            ("Organize", TxtShortcutOrganize?.Text ?? "G", Color.FromRgb(0x3B, 0x82, 0xF6)),
            ("Extract", TxtShortcutExtract?.Text ?? "X", Color.FromRgb(0xF5, 0x9E, 0x0B)),
            ("Analyze", TxtShortcutAnalyze?.Text ?? "I", Color.FromRgb(0x06, 0xB6, 0xD4))
        };

        double step = 360.0 / categories.Length;

        for (int i = 0; i < categories.Length; i++)
        {
            double startAngle = i * step - 90;
            double endAngle = startAngle + step;
            var (name, key, accentColor) = categories[i];

            var wedgePath = CreateWedgePath(cx, cy, innerR, outerR, startAngle, endAngle);
            wedgePath.Fill = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
            wedgePath.Stroke = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
            wedgePath.StrokeThickness = 1;

            MiniPreviewCanvas.Children.Add(wedgePath);

            double midAngle = (startAngle + endAngle) / 2.0;
            double rad = midAngle * Math.PI / 180.0;
            double labelR = (innerR + outerR) / 2.0;
            double lx = cx + labelR * Math.Cos(rad);
            double ly = cy + labelR * Math.Sin(rad);

            var sp = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var txtName = new TextBlock
            {
                Text = name,
                Foreground = Brushes.White,
                FontSize = 7,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(60, accentColor.R, accentColor.G, accentColor.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3, 1, 3, 1),
                Margin = new Thickness(0, 1, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock
                {
                    Text = key,
                    Foreground = new SolidColorBrush(accentColor),
                    FontSize = 6.5,
                    FontWeight = FontWeights.Bold
                }
            };

            sp.Children.Add(txtName);
            sp.Children.Add(badge);

            sp.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(sp, lx - sp.DesiredSize.Width / 2.0);
            Canvas.SetTop(sp, ly - sp.DesiredSize.Height / 2.0);

            MiniPreviewCanvas.Children.Add(sp);
        }

        // Inner Deadzone Ring
        var deadzoneCircle = new Ellipse
        {
            Width = innerR * 2,
            Height = innerR * 2,
            Fill = new SolidColorBrush(Color.FromArgb(180, 24, 25, 32)),
            Stroke = new SolidColorBrush(userAccent),
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(deadzoneCircle, cx - innerR);
        Canvas.SetTop(deadzoneCircle, cy - innerR);
        MiniPreviewCanvas.Children.Add(deadzoneCircle);

        var cancelText = new TextBlock
        {
            Text = "✕\nCancel",
            Foreground = new SolidColorBrush(userAccent),
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center
        };
        cancelText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(cancelText, cx - cancelText.DesiredSize.Width / 2.0);
        Canvas.SetTop(cancelText, cy - cancelText.DesiredSize.Height / 2.0);
        MiniPreviewCanvas.Children.Add(cancelText);
    }

    private System.Windows.Shapes.Path CreateWedgePath(double cx, double cy, double rIn, double rOut, double startDeg, double endDeg)
    {
        double startRad = startDeg * Math.PI / 180.0;
        double endRad = endDeg * Math.PI / 180.0;

        Point p1 = new(cx + rOut * Math.Cos(startRad), cy + rOut * Math.Sin(startRad));
        Point p2 = new(cx + rOut * Math.Cos(endRad), cy + rOut * Math.Sin(endRad));
        Point p3 = new(cx + rIn * Math.Cos(endRad), cy + rIn * Math.Sin(endRad));
        Point p4 = new(cx + rIn * Math.Cos(startRad), cy + rIn * Math.Sin(startRad));

        bool isLarge = (endDeg - startDeg) > 180.0;

        var figure = new PathFigure { StartPoint = p1, IsClosed = true };
        figure.Segments.Add(new ArcSegment(p2, new Size(rOut, rOut), 0, isLarge, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(p3, true));
        figure.Segments.Add(new ArcSegment(p4, new Size(rIn, rIn), 0, isLarge, SweepDirection.Counterclockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return new System.Windows.Shapes.Path { Data = geometry };
    }

    #endregion

    #region Input Event Handlers & Save

    private void OnActivationModifierChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void OnHoldDelayChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtHoldDelayValue != null)
        {
            TxtHoldDelayValue.Text = $"{(int)e.NewValue} ms";
        }
    }

    private void OnShortcutTextChanged(object sender, TextChangedEventArgs e)
    {
        RenderMiniatureRadialMenu();
    }

    private void OnMenuScaleValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        double scale = e.NewValue;
        if (MiniatureMenuScaleTransform != null)
        {
            MiniatureMenuScaleTransform.ScaleX = scale;
            MiniatureMenuScaleTransform.ScaleY = scale;
        }

        UpdateMenuScaleLabel(scale);
    }

    private void UpdateMenuScaleLabel(double scale)
    {
        if (TxtMenuScaleLabel == null) return;

        if (scale < 0.9)
        {
            TxtMenuScaleLabel.Text = $"Small ({(scale * 100):0}%)";
        }
        else if (scale > 1.1)
        {
            TxtMenuScaleLabel.Text = $"Large ({(scale * 100):0}%)";
        }
        else
        {
            TxtMenuScaleLabel.Text = $"Medium ({(scale * 100):0}%)";
        }
    }

    private void OnSaveSettingsClicked(object sender, RoutedEventArgs e)
    {
        string modifier = "Shift";
        if (CmbActivationModifier.SelectedItem is ComboBoxItem modItem)
        {
            var content = modItem.Content.ToString() ?? "";
            if (content.StartsWith("Ctrl")) modifier = "Ctrl";
            else if (content.StartsWith("Alt")) modifier = "Alt";
        }

        string outputDirMode = "Same folder as source file";
        if (CmbDefaultOutputDir.SelectedItem is ComboBoxItem outItem)
        {
            outputDirMode = outItem.Content.ToString() ?? outputDirMode;
        }

        int threads = 4;
        if (CmbWorkerThreads.SelectedItem is ComboBoxItem threadItem)
        {
            var content = threadItem.Content.ToString() ?? "";
            if (content.Contains("2")) threads = 2;
            else if (content.Contains("8")) threads = 8;
            else if (content.Contains("16")) threads = 16;
            else threads = 4;
        }

        var settings = new OrbitSettings
        {
            ActivationModifier = modifier,
            ActivationDelayMs = (int)SliderHoldDelay.Value,
            MenuScale = Math.Round(SliderMenuScale.Value, 2),
            ShortcutConvert = TxtShortcutConvert.Text.Trim(),
            ShortcutEdit = TxtShortcutEdit.Text.Trim(),
            ShortcutAnnotate = TxtShortcutAnnotate.Text.Trim(),
            ShortcutOptimize = TxtShortcutOptimize.Text.Trim(),
            ShortcutOrganize = TxtShortcutOrganize.Text.Trim(),
            ShortcutExtract = TxtShortcutExtract.Text.Trim(),
            ShortcutAnalyze = TxtShortcutAnalyze.Text.Trim(),
            ShortcutCancel = TxtShortcutCancel.Text.Trim(),
            LaunchAtStartup = ChkStartup.IsChecked == true,
            MinimizeToTray = ChkMinimizeToTray.IsChecked == true,
            ShowNotifications = ChkNotifications.IsChecked == true,
            PlaySounds = ChkSounds.IsChecked == true,
            Theme = _selectedTheme,
            AccentColor = _selectedAccent,
            KeepOriginalFiles = ChkPreserveOriginals.IsChecked == true,
            OutputDestinationMode = outputDirMode,
            CustomOutputDirectory = TxtCustomOutputDir?.Text.Trim() ?? "",
            MaxConcurrentThreads = threads
        };

        _settingsService.Save(settings);
        ThemeManager.ApplyTheme(settings);
        UpdateStartupRegistration(settings.LaunchAtStartup);

        SaveNotificationBanner.Visibility = Visibility.Visible;
        _bannerTimer.Stop();
        _bannerTimer.Start();
    }

    private void OnResetDefaultsClicked(object sender, RoutedEventArgs e)
    {
        var defaultSettings = new OrbitSettings();
        _settingsService.Save(defaultSettings);
        ThemeManager.ApplyTheme(defaultSettings);
        UpdateStartupRegistration(defaultSettings.LaunchAtStartup);

        LoadSettingsIntoUI();
        RenderMiniatureRadialMenu();

        SaveNotificationBanner.Visibility = Visibility.Visible;
        _bannerTimer.Stop();
        _bannerTimer.Start();
    }

    private static void UpdateStartupRegistration(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (enable)
                {
                    var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue("Orbit", $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue("Orbit", false);
                }
            }
        }
        catch
        {
            // Ignore restricted registry environments
        }
    }

    #endregion
}
