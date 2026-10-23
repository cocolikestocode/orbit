namespace Orbit.App.Services;

using System;
using System.Windows;
using System.Windows.Media;
using Orbit.Core.Models;

public static class ThemeManager
{
    public static void ApplyTheme(OrbitSettings settings)
    {
        if (Application.Current == null || settings == null) return;

        var resources = Application.Current.Resources;

        // 1. Theme Mode (Background, Surface, Text & Border)
        Color bgColor, surfaceColor, surfaceHoverColor, borderColor;

        string themeName = settings.Theme ?? "Dark Modern";
        if (themeName.Contains("OLED", StringComparison.OrdinalIgnoreCase))
        {
            bgColor = Color.FromRgb(0x00, 0x00, 0x00);
            surfaceColor = Color.FromRgb(0x10, 0x10, 0x14);
            surfaceHoverColor = Color.FromRgb(0x1B, 0x1C, 0x24);
            borderColor = Color.FromRgb(0x27, 0x2A, 0x36);
        }
        else if (themeName.Contains("Slate", StringComparison.OrdinalIgnoreCase) || themeName.Contains("Midnight", StringComparison.OrdinalIgnoreCase))
        {
            bgColor = Color.FromRgb(0x0B, 0x11, 0x20);
            surfaceColor = Color.FromRgb(0x1E, 0x29, 0x3B);
            surfaceHoverColor = Color.FromRgb(0x28, 0x35, 0x48);
            borderColor = Color.FromRgb(0x3B, 0x49, 0x60);
        }
        else // Dark Modern (#181920)
        {
            bgColor = Color.FromRgb(0x18, 0x19, 0x20);
            surfaceColor = Color.FromRgb(0x23, 0x25, 0x30);
            surfaceHoverColor = Color.FromRgb(0x2C, 0x2F, 0x3E);
            borderColor = Color.FromRgb(0x35, 0x37, 0x45);
        }

        // 2. Accent Color
        Color accentColor;
        try
        {
            accentColor = (Color)ColorConverter.ConvertFromString(settings.AccentColor ?? "#FF7A1A");
        }
        catch
        {
            accentColor = Color.FromRgb(0xFF, 0x7A, 0x1A);
        }

        Color accentHover = Color.FromArgb(accentColor.A,
            (byte)Math.Min(255, accentColor.R + 25),
            (byte)Math.Min(255, accentColor.G + 25),
            (byte)Math.Min(255, accentColor.B + 25));

        Color accentPressed = Color.FromArgb(accentColor.A,
            (byte)Math.Max(0, accentColor.R - 30),
            (byte)Math.Max(0, accentColor.G - 30),
            (byte)Math.Max(0, accentColor.B - 30));

        Color accentSubtle = Color.FromArgb(40, accentColor.R, accentColor.G, accentColor.B);

        // Update Dynamic Resources on Application level
        resources["OrbitBackgroundBrush"] = new SolidColorBrush(bgColor);
        resources["OrbitSurfaceBrush"] = new SolidColorBrush(surfaceColor);
        resources["OrbitSurfaceHoverBrush"] = new SolidColorBrush(surfaceHoverColor);
        resources["OrbitBorderBrush"] = new SolidColorBrush(borderColor);
        resources["OrbitPrimaryBrush"] = new SolidColorBrush(accentColor);
        resources["OrbitAccentBrush"] = new SolidColorBrush(accentColor);
        resources["OrbitAccentHoverBrush"] = new SolidColorBrush(accentHover);
        resources["OrbitAccentPressedBrush"] = new SolidColorBrush(accentPressed);
        resources["OrbitAccentSubtleBrush"] = new SolidColorBrush(accentSubtle);
    }
}
