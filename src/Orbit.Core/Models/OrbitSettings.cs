namespace Orbit.Core.Models;

public class OrbitSettings
{
    // Activation & Gesture
    public string ActivationModifier { get; set; } = "Shift";
    public int ActivationDelayMs { get; set; } = 200;
    public double DeadzoneRadius { get; set; } = 48.0;
    public double MenuScale { get; set; } = 1.0;

    // Radial Category Shortcuts
    public string ShortcutConvert { get; set; } = "C";
    public string ShortcutEdit { get; set; } = "E";
    public string ShortcutAnnotate { get; set; } = "A";
    public string ShortcutOptimize { get; set; } = "O";
    public string ShortcutOrganize { get; set; } = "G";
    public string ShortcutExtract { get; set; } = "X";
    public string ShortcutAnalyze { get; set; } = "I";
    public string ShortcutCancel { get; set; } = "Esc";

    // General
    public bool LaunchAtStartup { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool PlaySounds { get; set; } = false;

    // Appearance
    public string Theme { get; set; } = "Dark Modern";
    public string AccentColor { get; set; } = "#FF7A1A";

    // File Operations
    public bool KeepOriginalFiles { get; set; } = true;
    public string OutputDestinationMode { get; set; } = "Same folder";
    public string CustomOutputDirectory { get; set; } = "";
    public int MaxConcurrentThreads { get; set; } = 4;
}
