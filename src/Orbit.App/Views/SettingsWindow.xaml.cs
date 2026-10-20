namespace Orbit.App.Views;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Orbit.Core.Models;
using Orbit.Core.Services;

public partial class SettingsWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly DispatcherTimer _bannerTimer = new();

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

        // File Ops
        ChkPreserveOriginals.IsChecked = s.KeepOriginalFiles;
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

    #region Radial Menu Miniature Preview

    public void RenderMiniatureRadialMenu()
    {
        if (MiniPreviewCanvas == null) return;

        MiniPreviewCanvas.Children.Clear();

        double cx = 120;
        double cy = 120;
        double innerR = 40;
        double outerR = 106;

        var categories = new[]
        {
            ("Convert", TxtShortcutConvert?.Text ?? "C", Color.FromRgb(0xFF, 0x7A, 0x1A)),
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

            // Sector Path
            var sector = CreateArcSector(cx, cy, innerR, outerR, startAngle, endAngle);
            sector.Fill = new SolidColorBrush(Color.FromArgb(0xDD, 0x1D, 0x1F, 0x2B));
            sector.Stroke = new SolidColorBrush(Color.FromArgb(0x55, 0x45, 0x4A, 0x5C));
            sector.StrokeThickness = 1.0;

            MiniPreviewCanvas.Children.Add(sector);

            // Label at mid-angle
            double midAngle = (startAngle + endAngle) / 2.0;
            double rad = midAngle * Math.PI / 180.0;
            double labelR = (innerR + outerR) / 2.0;
            double lx = cx + labelR * Math.Cos(rad);
            double ly = cy + labelR * Math.Sin(rad);

            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var nameBlock = new TextBlock
            {
                Text = name,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDF, 0xEE)),
                FontSize = 8,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var keyBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x40, accentColor.R, accentColor.G, accentColor.B)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3, 1, 3, 1),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            };
            var keyBlock = new TextBlock
            {
                Text = key,
                Foreground = new SolidColorBrush(accentColor),
                FontSize = 7,
                FontWeight = FontWeights.Bold
            };
            keyBadge.Child = keyBlock;

            panel.Children.Add(nameBlock);
            panel.Children.Add(keyBadge);

            panel.Measure(new Size(100, 100));
            Canvas.SetLeft(panel, lx - (panel.DesiredSize.Width / 2.0));
            Canvas.SetTop(panel, ly - (panel.DesiredSize.Height / 2.0));

            MiniPreviewCanvas.Children.Add(panel);
        }
    }

    private static Path CreateArcSector(double cx, double cy, double innerR, double outerR, double startAngle, double endAngle)
    {
        double startRad = startAngle * Math.PI / 180.0;
        double endRad = endAngle * Math.PI / 180.0;

        Point p0 = new(cx + innerR * Math.Cos(startRad), cy + innerR * Math.Sin(startRad));
        Point p1 = new(cx + outerR * Math.Cos(startRad), cy + outerR * Math.Sin(startRad));
        Point p2 = new(cx + outerR * Math.Cos(endRad), cy + outerR * Math.Sin(endRad));
        Point p3 = new(cx + innerR * Math.Cos(endRad), cy + innerR * Math.Sin(endRad));

        bool isLargeArc = (endAngle - startAngle) > 180.0;

        var figure = new PathFigure { StartPoint = p0, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(p1, true));
        figure.Segments.Add(new ArcSegment(p2, new Size(outerR, outerR), 0, isLargeArc, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(p3, true));
        figure.Segments.Add(new ArcSegment(p0, new Size(innerR, innerR), 0, isLargeArc, SweepDirection.Counterclockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        return new Path { Data = geometry };
    }

    #endregion

    #region Form Events & Handlers

    private void OnShortcutTextChanged(object sender, TextChangedEventArgs e)
    {
        RenderMiniatureRadialMenu();
    }

    private void OnActivationModifierChanged(object sender, SelectionChangedEventArgs e)
    {
        // Visual feedback if needed
    }

    private void OnHoldDelayChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtHoldDelayValue != null)
        {
            TxtHoldDelayValue.Text = $"{(int)e.NewValue} ms";
        }
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
        if (CmbActivationModifier.SelectedItem is ComboBoxItem item)
        {
            var content = item.Content.ToString() ?? "";
            if (content.StartsWith("Ctrl")) modifier = "Ctrl";
            else if (content.StartsWith("Alt")) modifier = "Alt";
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
            KeepOriginalFiles = ChkPreserveOriginals.IsChecked == true
        };

        _settingsService.Save(settings);

        SaveNotificationBanner.Visibility = Visibility.Visible;
        _bannerTimer.Stop();
        _bannerTimer.Start();
    }

    private void OnResetDefaultsClicked(object sender, RoutedEventArgs e)
    {
        var defaultSettings = new OrbitSettings();
        _settingsService.Save(defaultSettings);
        LoadSettingsIntoUI();
        RenderMiniatureRadialMenu();

        SaveNotificationBanner.Visibility = Visibility.Visible;
        _bannerTimer.Stop();
        _bannerTimer.Start();
    }

    #endregion
}
